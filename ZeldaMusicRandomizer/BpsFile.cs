using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BpsFormat;

public enum BpsAction
{
    SourceRead = 0,
    TargetRead = 1,
    SourceCopy = 2,
    TargetCopy = 3,
}

public class BpsPatch
{
    public readonly BpsAction Action;
    public readonly ulong Offset;
    public readonly ulong Size;

    public BpsPatch(BpsAction action, ulong offset, ulong size)
    {
        Action = action;
        Offset = offset;
        Size = size;
    }
}

class BpsPatchState
{
    public long offs;
    public ulong outOffs = 0;
    public long srcRelOffs = 0;
    public long tgtRelOffs = 0;

    public BpsPatchState(long offs)
        => this.offs = offs;
}

public class BpsFile
{
    public readonly ulong SourceSize;
    public readonly ulong TargetSize;
    public byte[] Metadata => checked(_data.Skip((int)_metaOffs).Take((int)_metaSize).ToArray());

    public readonly uint SourceCrc;
    public readonly uint TargetCrc;

    public IReadOnlyList<BpsPatch> Patches => _patches;

    readonly IReadOnlyList<byte> _data;
    readonly ulong _metaOffs, _metaSize;
    readonly ulong _ftrOffs;
    readonly List<BpsPatch> _patches = new();

    public BpsFile(IReadOnlyList<byte> data)
    {
        checked
        {
            _data = data;

            Debug.Assert(data.Count >= 7 + 4 * 3);
            Debug.Assert(data.Take(4).SequenceEqual(Encoding.ASCII.GetBytes("BPS1")));

            long crcOffs = data.Count - 4;
            uint crc = ReadCrc(crcOffs);

            Debug.Assert(crc == Crc32.HashToUInt32(new ReadOnlySpan<byte>(data.Take((int)crcOffs).ToArray())));

            long offs = 4;
            SourceSize = ReadNumber(ref offs);
            TargetSize = ReadNumber(ref offs);
            _ftrOffs = (ulong)(data.Count - 12);

            _metaSize = ReadNumber(ref offs);
            _metaOffs = (ulong)offs;

            offs += (long)_metaSize;

            BpsPatchState state = new(offs);
            while ((ulong)state.offs < _ftrOffs)
                _patches.Add(ReadPatch(state));

            offs = state.offs;

            Debug.Assert((ulong)offs == _ftrOffs);

            SourceCrc = ReadCrc(offs);
            TargetCrc = ReadCrc(offs + 4);
        }
    }

    /*public IEnumerable<DiffRange> GetDiff()
    {
        checked
        {
            Debug.Assert(TargetSize <= int.MaxValue);

            int offs = 0, diffStartOffs = -1;
            foreach (var patch in _patches)
            {
                if (patch.Action == BpsAction.SourceRead
                    || (patch.Action == BpsAction.SourceCopy && (int)patch.Offset == offs))
                {
                    if (diffStartOffs >= 0)
                    {
                        yield return new(diffStartOffs, offs);

                        diffStartOffs = -1;
                    }
                }
                else
                {
                    if (diffStartOffs < 0)
                        diffStartOffs = offs;
                }

                offs += (int)patch.Size;
            }

            if (diffStartOffs >= 0)
                yield return new(diffStartOffs, offs);
        }
    }*/

    public byte[] Apply(IReadOnlyList<byte> source)
    {
        checked
        {
            Debug.Assert((uint)source.Count == SourceSize);

            var src = source as byte[];
            if (src is null)
                src = source.ToArray();

            //Debug.Assert(SourceCrc == Crc32.HashToUInt32(src));

            var data = _data as byte[];
            if (data is null)
                data = _data.ToArray();

            var tgt = new byte[TargetSize];
            long offs = 0;
            foreach (var patch in _patches)
            {
                switch (patch.Action)
                {
                    case BpsAction.SourceRead:
                        Array.Copy(src, offs, tgt, offs, (long)patch.Size);
                        break;

                    case BpsAction.TargetRead:
                        Array.Copy(data, (long)patch.Offset, tgt, offs, (long)patch.Size);
                        break;

                    case BpsAction.SourceCopy:
                        Array.Copy(src, (long)patch.Offset, tgt, offs, (long)patch.Size);
                        break;

                    case BpsAction.TargetCopy:
                        for (ulong i = 0; i < patch.Size; i++)
                            tgt[(ulong)offs + i] = tgt[patch.Offset + i];

                        break;
                }

                offs += (long)patch.Size;
            }

            Debug.Assert((ulong)offs == TargetSize);
            //Debug.Assert(TargetCrc == Crc32.HashToUInt32(tgt));

            return tgt;
        }
    }

    ulong ReadNumber(ref long offs)
    {
        checked
        {
            ulong num = 0, shift = 1;
            while (true)
            {
                byte b = _data[(int)offs++];
                num += (ulong)(b & 0x7f) * shift;
                if ((b & 0x80) != 0)
                    break;

                shift <<= 7;
                num += shift;
            }

            return num;
        }
    }

    long ReadSignedNumber(ref long offs)
    {
        ulong num = ReadNumber(ref offs);
        return ((num & 1) != 0 ? -1 : 1) * (long)(num >> 1);
    }

    uint ReadCrc(long offs)
    {
        checked
        {
            uint crc = 0;
            for (int i = 3; i >= 0; i--)
                crc = (crc << 8) | _data[(int)offs + i];

            return crc;
        }
    }

    BpsPatch ReadPatch(BpsPatchState state)
    {
        checked
        {
            ulong num = ReadNumber(ref state.offs);
            var action = (BpsAction)(num & 3);
            ulong dataSize = (num >> 2) + 1;
            ulong offset = 0;

            switch (action)
            {
                case BpsAction.SourceRead:
                    offset = state.outOffs;
                    break;

                case BpsAction.TargetRead:
                    offset = (ulong)state.offs;

                    state.offs += (long)dataSize;

                    break;

                case BpsAction.SourceCopy:
                    state.srcRelOffs += ReadSignedNumber(ref state.offs);
                    Debug.Assert(state.srcRelOffs >= 0);
                    Debug.Assert((ulong)(state.srcRelOffs + (long)dataSize) <= SourceSize);

                    offset = (ulong)state.srcRelOffs;

                    state.srcRelOffs += (long)dataSize;

                    break;

                case BpsAction.TargetCopy:
                    state.tgtRelOffs += ReadSignedNumber(ref state.offs);
                    Debug.Assert(state.tgtRelOffs >= 0);
                    Debug.Assert((ulong)state.tgtRelOffs < TargetSize);

                    offset = (ulong)state.tgtRelOffs;

                    state.tgtRelOffs += (long)dataSize;

                    break;

            }

            state.outOffs += dataSize;

            Debug.Assert((ulong)state.offs <= _ftrOffs);
            Debug.Assert(state.outOffs <= TargetSize);

            return new BpsPatch(action, offset, dataSize);
        }
    }
}
