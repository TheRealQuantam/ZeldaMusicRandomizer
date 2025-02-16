using FtRandoLib;
using FtRandoLib.Importer;
using FtRandoLib.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ZeldaMusicRandomizer;

public class MusicImporter : Importer
{
    protected override int BankSize => 0x4000;
    protected override List<int> FreeBanks { get; } = new(ExRange(7, 0xf));

    protected override int PrimarySquareChan => 1;
    protected override IstringSet Uses { get; } = new("Overworld Dungeon LastDungeon Ending".Split(' '));
    protected override IstringSet DefaultUses { get; } = new("Dungeon LastDungeon".Split(' '));
    protected override bool DefaultStreamingSafe => true;

    protected override int SongMapOffs => 0 + 0x10;
    protected override int SongModAddrTblOffs => 0x80 + 0x10;

    protected override HashSet<int> BuiltinSongIdcs { get; } = new(ExRange(0, 11));
    protected override List<int> FreeSongIdcs { get; } = new(ExRange(0x12, 0x40));
    protected override int NumSongs => 0x40;
    protected override IReadOnlyDictionary<string, SongMapInfo> SongMapInfos { get; } = new Dictionary<string, SongMapInfo>();

    protected override int NumFtChannels => 5;
    protected override int DefaultFtStartAddr => 0;
    protected override int DefaultFtPrimarySquareChan => 0;

    static readonly Dictionary<string, BankLayout> BankLayouts = new()
    {
        {
            "ft", 
            new(0x8000, 0x4000, [new(0x1800, 0x4000)], 7)
        }
    };

    static readonly BuiltinSong[] BuiltinSongs = [
        new(0, "Overworld", Uses: new(["Overworld"])),
        new(4, "Ending", Uses: new(["Ending"])),
        new(5, "Last Dungeon", Uses: new(["LastDungeon"])),
        new(6, "Dungeon", Uses: new(["Dungeon"])),
    ];

    static readonly IstringDictionary<int[]> UsageTrackIdcs = new()
    {
        { "Overworld", [0] },
        { "Ending", [4] },
        { "LastDungeon", [5] },
        { "Dungeon", [6, 11, 12, 13, 14, 15, 16, 17] },
    };

    RandomShuffler _shuffler;

    public MusicImporter(byte[] rom, Random rng)
        : base(BankLayouts, new SimpleRomAccess(rom))
    {
        _shuffler = new(rng);
    }

    public static IEnumerable<BuiltinSong> GetBuiltinSongs()
    {
        foreach (var song in BuiltinSongs)
            yield return new(
                song.Number,
                $"Legend of Zelda - {song.Title}",
                "Koji Kondo",
                true,
                song.Uses,
                1);
    }

    public void Import(
        IReadOnlyList<ISong> allSongs,
        out HashSet<int> freeBanks)
    {
        freeBanks = new(FreeBanks);

        var usesSongs = SplitSongsByUsage(allSongs);
        var selUsesSongs = SelectUsesSongs(
            usesSongs,
            UsageTrackIdcs.ToDictionary(kv => kv.Key, kv => kv.Value.Length),
            _shuffler);

        Log.WriteLine("Selected songs:");

        Dictionary<int, ISong?> songMap = new();
        foreach (var (usage, usageSongs) in selUsesSongs)
        {
            if (usageSongs.Count == 0)
                continue;

            var songIdcs = UsageTrackIdcs[usage];

            Debug.Assert(usageSongs.Count == songIdcs.Length);

            for (int entryIdx = 0; entryIdx < songIdcs.Length; entryIdx++)
            {
                int songIdx = songIdcs[entryIdx];
                ISong song = usageSongs[entryIdx];

                songMap[songIdx] = usageSongs[entryIdx];

                string numStr = (songIdcs.Length > 1)
                    ? $" {entryIdx + 1}" : "";
                Log.WriteLine($"\t{usage}{numStr}: {song.Title}");
            }
        }

        Import(songMap, null, out freeBanks);
    }
}
