using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using BpsFormat;
using FtRandoLib.Importer;
using FtRandoLib.Library;
using ReactiveUI;
using System;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FtRandoLib.Utility;

namespace ZeldaMusicRandomizer.ViewModels;

public class MainViewModel : ViewModelBase
{
    public string VersionLine => $"v{Assembly.GetExecutingAssembly().GetName().Version}";
    public string AuthorLine => $"By {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>()!.Company}";
    public string RepositoryUrl => Assembly
        .GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(a => a.Key == "RepositoryUrl").First()
        .Value!;

    public bool CannotUsePaths => _isBrowser;

    public bool IsRomSelected => _isRomSel;

    public string StatusString 
    {
        get => _statusStr; 
        set => this.RaiseAndSetIfChanged(ref _statusStr, value);
    }

    public string RomPath
    {
        get => _romPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _romPath, value);
            this.RaiseAndSetIfChanged(ref _isRomSel, value.Length != 0, "IsRomSelected");
        }
    }

    public string MusicDirPath
    {
        get => _musicDirPath;
        set => this.RaiseAndSetIfChanged(ref _musicDirPath, value);
    }

    public string SeedString
    {
        get => _seedStr;
        set => this.RaiseAndSetIfChanged(ref _seedStr, value);
    }

    public bool IsRandomizerRom
    {
        get => _isRandomizerRom;
        set => this.RaiseAndSetIfChanged(ref _isRandomizerRom, value);
    }

    public bool IncludeOriginalTracks
    {
        get => _includeOrigTracks;
        set => this.RaiseAndSetIfChanged(ref _includeOrigTracks, value);
    }

    public bool IncludeStandardLibraryTracks
    {
        get => _includeStdLibTracks;
        set => this.RaiseAndSetIfChanged(ref _includeStdLibTracks, value);
    }

    public bool ExcludeUnsafeTracks
    {
        get => _excludeUnsafeTracks;
        set => this.RaiseAndSetIfChanged(ref _excludeUnsafeTracks, value);
    }

    public string Log
    {
        get => _log;
        set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    // Used to scroll to the bottom automatically
    public int LogCaretIndex
    {
        get => _logCaretIdx;
        set => this.RaiseAndSetIfChanged(ref _logCaretIdx, value);
    }

    const int VanillaRomSize = 0x20010;
    static string[] VanillaSha256s = 
    [
        "8f72dc2e98572eb4ba7c3a902bca5f69c448fc4391837e5f8f0d4556280440ac", // Revision 0
        "89232edf4f9b52e3cb872094bc78973de080befca2ddea893b6e936066514d4e", // Revision 1
    ];

    readonly TopLevel _topLevel;
    readonly bool _isBrowser;

    string _statusStr = "Select path to ROM to get started";
    bool _isRomSel = false;
    string _romPath = "";
    string _musicDirPath = "";
    IStorageFile? _romFile = null;
    IStorageFolder? _musicDir = null;
    string _seedStr = "";

    bool _isRandomizerRom = false;
    bool _includeOrigTracks = true;
    bool _includeStdLibTracks = true;
    bool _excludeUnsafeTracks = true;
    string _log = "";
    int _logCaretIdx = 0;

    static byte[] ParseHexString(string str)
    {
        if (str.Length % 2 != 0)
            str = "0" + str;
        int size = str.Length / 2;
        byte[] bytes = new byte[size];
        for (int i = 0; i < size; i++)
            bytes[i] = byte.Parse(str.Substring(i * 2, 2), NumberStyles.HexNumber);

        return bytes;
    }

    public MainViewModel(TopLevel topLevel, bool isBrowser)
    {
        _topLevel = topLevel;
        _isBrowser = isBrowser;

        GenerateSeed();
    }

    /*public async void RepositoryUrl_Clicked()
    {
        if (OperatingSystem.IsBrowser())
        {
            var jsRuntime = AvaloniaLocator.Current.GetService<IJSRuntime>();
            jsRuntime.InvokeVoidAsync("open", RepositoryUrl, "_blank");
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RepositoryUrl,
                UseShellExecute = true,
            });
        }
    }*/

    public async void SelectRomPath_Clicked()
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Select Base ROM...",
            FileTypeFilter = [
                new("NES Roms") { Patterns = ["*.nes"] },
                new("All Files") { Patterns = ["*.*"] },
            ],
            AllowMultiple = false,
        });

        if (files.Count != 1)
            return;

        var file = files[0];
        bool isVanilla = false;

        try
        {
            isVanilla = await VerifyRom(file);
        }
        catch (Exception e)
        {
            Log = $"ERROR: {e.Message}";

            return;
        }

        if (isVanilla)
        {
            IsRandomizerRom = false;
            Log = "Vanilla ROM selected";
        }
        else
            Log = "Non-vanilla ROM selected";

        RomPath = file.TryGetLocalPath() ?? file.Name;
        _romFile = file;
    }

    public async void SelectMusicDirPath_Clicked()
    {
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "Select Music Folder...",
            AllowMultiple = false,
        });

        if (folders.Count != 1)
            return;

        MusicDirPath = folders[0].TryGetLocalPath() ?? folders[0].Name;
        _musicDir = folders[0];
    }

    public async void RandomSeed_Clicked()
    {
        GenerateSeed();
    }

    public async void Randomize_Clicked()
    {
        try
        {
            LogCaretIndex = 0;
            Log = "";

            int seed;
            if (SeedString.Length == 0 || !int.TryParse(SeedString, out seed))
                throw new Exception($"Seed value must be between 0 and {int.MaxValue}");

            // Find and verify ROM
            if (!_isBrowser)
            {
                _romFile = await _topLevel.StorageProvider.TryGetFileFromPathAsync(_romPath);
            }

            if (_romFile is null)
                return;

            bool isVanilla = await VerifyRom(_romFile);

            // Load ROM
            var srcRom = new byte[VanillaRomSize];
            using (var stream = await _romFile.OpenReadAsync())
                await stream.ReadExactlyAsync(srcRom, 0, VanillaRomSize);

            // Load and apply the BPS
            var assembly = Assembly.GetExecutingAssembly();
            BpsFile? bps = null;
            using (var stream = assembly.GetManifestResourceStream("ZeldaMusicRandomizer.Assets.lozft.bps"))
            {
                Debug.Assert(stream is not null);

                var data = new byte[stream.Length];
                await stream.ReadExactlyAsync(data, 0, data.Length);

                bps = new BpsFile(data);
            }

            Debug.Assert((ulong)srcRom.LongLength == bps.SourceSize);

            var tgtRom = bps.Apply(srcRom);

            // Load the songs
            Random rng = new(seed);

            MusicImporter imptr = new(tgtRom, rng);

            await TestLibraries(imptr);

            LibraryParserOptions opts = new() 
            {
                SafeOnly = _excludeUnsafeTracks
            };
            List<ISong> songs = new(await LoadTracks(
                imptr, 
                _includeStdLibTracks,
                _includeOrigTracks,
                opts));

            if (songs.Count == 0)
            {
                Log = "No songs were selected and no ROM was generated";
                return;
            }

            // Get output path
            string defFilename = $"{Path.GetFileNameWithoutExtension(_romFile.Name)} Music {seed}{Path.GetExtension(_romFile.Name)}";
            var tgtFile = await _topLevel.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Save ROM As...",
                DefaultExtension = "nes",
                FileTypeChoices = [new("NES Roms") { Patterns = ["*.nes"] }],
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await _romFile.GetParentAsync(),
                SuggestedFileName = defFilename,
            });

            if (tgtFile is null)
                return;

            // Do randomization
            HashSet<int> freeBanks;
            StringWriter logStream = new();
            TextLogger logger = new(logStream);
            FtRandoLib.Utility.Log.Push(logger);

            try
            {
                imptr.Import(songs, out freeBanks);
            }
            catch (Exception)
            {
                Logger popLogger = FtRandoLib.Utility.Log.Pop();
                Debug.Assert(object.ReferenceEquals(popLogger, logger));

                throw;
            }
            finally
            {
                Log = logStream.ToString();
            }

            // Write it out
            using (var stream = await tgtFile.OpenWriteAsync())
                await stream.WriteAsync(tgtRom, 0, tgtRom.Length);

            Log += $"\r\n✔️ Successfully wrote {tgtFile.Name}\r\n";
        }
        catch (Exception e)
        {
            if (Log.Length != 0)
                Log += "\r\n";

            Log += $"❌ ERROR: {e.Message}";
        }
        finally
        {
            LogCaretIndex = Log.Length;
        }
    }

    void GenerateSeed()
    {
        int seed = new Random().Next();
        SeedString = seed.ToString();
    }

    async Task<bool> VerifyRom(IStorageFile file)
    {
        string errMsg = "ROM is not Legend of Zelda";
        var props = await file.GetBasicPropertiesAsync();
        if (props.Size != VanillaRomSize)
            throw new Exception(errMsg);

        var data = new byte[(int)props.Size];
        using (var stream = await file.OpenReadAsync())
            await stream.ReadExactlyAsync(data, 0, (int)props.Size);

        if (!data.Take(4).SequenceEqual(Encoding.ASCII.GetBytes("NES\x1a")))
            throw new Exception(errMsg);

        var hashBytes = SHA256.HashData(data);
        bool isVanilla = false;
        foreach (string hashStr in VanillaSha256s)
            isVanilla = isVanilla 
                || ParseHexString(hashStr).SequenceEqual(hashBytes);

        return isVanilla;
    }

    async Task<IEnumerable<ISong>> LoadTracks(
        MusicImporter imptr,
        bool incStdLib,
        bool incOrigTracks,
        LibraryParserOptions? opts)
    {
        List<ISong> songs = new();

        if (!_isBrowser)
        {
            if (_musicDirPath.Length != 0)
                _musicDir = await _topLevel.StorageProvider.TryGetFolderFromPathAsync(_musicDirPath);
            else
                _musicDir = null;
        }

        if (_musicDir is not null)
            songs.AddRange(await LoadFolderTracks(imptr, _musicDir, opts));

        if (incStdLib)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string jsonData = "";
                using (var stream = assembly.GetManifestResourceStream("ZeldaMusicRandomizer.Assets.StandardLibrary.json5"))
                {
                    Debug.Assert(stream is not null);

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        jsonData = await reader.ReadToEndAsync();
                }

                songs.AddRange(imptr.LoadFtJsonLibrarySongs(jsonData, opts));
            }
            catch (Exception e)
            {
                TranslateParseError("Standard Library", e);
            }
        }

        if (songs.Count != 0 && incOrigTracks)
            songs.AddRange(MusicImporter.GetBuiltinSongs());

        return songs;
    }

    static async Task<IEnumerable<ISong>> LoadFolderTracks(
        MusicImporter imptr,
        IStorageFolder parent,
        LibraryParserOptions? opts)
    {
        const int MaxLibSize = 64 << 20;

        await foreach (var item in parent.GetItemsAsync())
        {
            if (item is IStorageFile file)
            {
                if (Path.GetExtension(file.Name).ToLowerInvariant() != ".json5")
                    continue;

                if ((await file.GetBasicPropertiesAsync()).Size > MaxLibSize)
                    throw new Exception($"File \"{file.Name}\" is too large");

                string jsonData = "";
                using (var stream = await file.OpenReadAsync())
                {
                    using (StreamReader reader = new(stream, Encoding.UTF8))
                        jsonData = await reader.ReadToEndAsync();
                }

                try
                {
                    return imptr.LoadFtJsonLibrarySongs(jsonData, opts);
                }
                catch (Exception e)
                {
                    TranslateParseError(file.TryGetLocalPath() ?? file.Name, e);
                }
            }
            else if (item is IStorageFolder folder)
                return await LoadFolderTracks(imptr, folder, opts);
        }

        return [];
    }

    static void TranslateParseError(string filename, Exception e)
    {
        string basicMsg = $"In '{filename}': {e.Message}";
        if (e is ParsingError pe)
        {
            StringBuilder sb = new();
            sb.AppendLine(basicMsg);
            if (!string.IsNullOrEmpty(pe.Submessage))
                sb.AppendLine(pe.Submessage);
            if (!string.IsNullOrEmpty(pe.AtString))
            {
                sb.AppendLine();
                sb.AppendLine(pe.AtString);
            }

            throw new Exception(sb.ToString(), e);
        }
        else
            throw new Exception(basicMsg, e);
    }

    async Task<bool> TestLibraries(MusicImporter imptr)
    {
        LibraryParserOptions opts = new()
        {
            EnabledOnly = false,
            SafeOnly = false,
        };
        var songs = (await LoadTracks(
            imptr, 
            true,
            false,
            opts)).ToList();
        imptr.TestRebase(songs);

        return true;
    }
}
