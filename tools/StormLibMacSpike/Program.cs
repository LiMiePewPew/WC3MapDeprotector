using System.Runtime.InteropServices;
using System.Text.Json;

namespace StormLibMacSpike;

internal static class Program
{
    private const uint MpqOpenReadOnly = 0x00000100;
    private const uint SFileOpenFromMpq = 0x00000000;

    private static readonly string[] KnownWarcraftFiles =
    [
        "war3map.w3i",
        "war3map.j",
        "war3map.lua",
        "war3map.wts",
        "war3map.wtg",
        "war3map.wct",
        "war3map.w3e",
        "war3map.doo",
        "war3mapUnits.doo",
        "war3map.w3u",
        "war3map.w3a",
        "war3map.w3t",
        "war3map.w3b",
        "war3map.w3d",
        "war3map.w3h",
        "war3map.w3q",
        "war3map.wpm",
        "war3map.mmp",
        "war3map.shd",
        "war3map.imp",
        "war3mapMap.blp",
        "war3mapPreview.tga",
        "war3mapSkin.txt",
        "war3mapExtra.txt",
        "war3mapMisc.txt",
        "(listfile)",
        "(attributes)",
        "(signature)",
    ];

    private static readonly HashSet<string> ResearchTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".j", ".lua", ".ai", ".txt", ".wts", ".slk", ".ini", ".fdf", ".toc",
    };

    private static readonly HashSet<string> ObjectDataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".w3u", ".w3a", ".w3t", ".w3b", ".w3d", ".w3h", ".w3q",
    };

    private enum SFileInfoClass
    {
        SFileMpqFileName,
        SFileMpqStreamBitmap,
        SFileMpqUserDataOffset,
        SFileMpqUserDataHeader,
        SFileMpqUserData,
        SFileMpqHeaderOffset,
        SFileMpqHeaderSize,
        SFileMpqHeader,
        SFileMpqHetTableOffset,
        SFileMpqHetTableSize,
        SFileMpqHetHeader,
        SFileMpqHetTable,
        SFileMpqBetTableOffset,
        SFileMpqBetTableSize,
        SFileMpqBetHeader,
        SFileMpqBetTable,
        SFileMpqHashTableOffset,
        SFileMpqHashTableSize64,
        SFileMpqHashTableSize,
        SFileMpqHashTable,
        SFileMpqBlockTableOffset,
        SFileMpqBlockTableSize64,
        SFileMpqBlockTableSize,
        SFileMpqBlockTable,
        SFileMpqHiBlockTableOffset,
        SFileMpqHiBlockTableSize64,
        SFileMpqHiBlockTable,
        SFileMpqSignatures,
        SFileMpqStrongSignatureOffset,
        SFileMpqStrongSignatureSize,
        SFileMpqStrongSignature,
        SFileMpqArchiveSize64,
        SFileMpqArchiveSize,
        SFileMpqMaxFileCount,
        SFileMpqFileTableSize,
        SFileMpqSectorSize,
        SFileMpqNumberOfFiles,
        SFileMpqRawChunkSize,
        SFileMpqStreamFlags,
        SFileMpqFlags,
    }

    private sealed record ExtractedFile(
        string ArchiveName,
        string RelativePath,
        long Size,
        bool ResearchText,
        bool ObjectData);

    private sealed record ResearchManifest(
        string SourceMap,
        ulong ArchiveSize,
        uint ArchiveFileCount,
        int NamedFileCount,
        int ExtractedFileCount,
        int UnnamedOrUnrecoveredCount,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyList<string> ScriptCandidates,
        IReadOnlyList<string> ObjectDataFiles,
        IReadOnlyList<ExtractedFile> Files);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SFileOpenArchiveDelegate(IntPtr archiveName, uint priority, uint flags, out IntPtr archiveHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SFileCloseArchiveDelegate(IntPtr archiveHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SFileGetFileInfoDelegate(
        IntPtr archiveOrFileHandle,
        SFileInfoClass infoClass,
        IntPtr fileInfo,
        uint fileInfoSize,
        out uint lengthNeeded);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SFileHasFileDelegate(IntPtr archiveHandle, IntPtr archivedName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SFileExtractFileDelegate(
        IntPtr archiveHandle,
        IntPtr archivedName,
        IntPtr localName,
        uint searchScope);

    private sealed class StormApi : IDisposable
    {
        private readonly IntPtr _libraryHandle;

        public SFileOpenArchiveDelegate OpenArchive { get; }
        public SFileCloseArchiveDelegate CloseArchive { get; }
        public SFileGetFileInfoDelegate GetFileInfo { get; }
        public SFileHasFileDelegate HasFile { get; }
        public SFileExtractFileDelegate ExtractFile { get; }

        public StormApi(string libraryPath)
        {
            _libraryHandle = NativeLibrary.Load(libraryPath);
            OpenArchive = Load<SFileOpenArchiveDelegate>("SFileOpenArchive");
            CloseArchive = Load<SFileCloseArchiveDelegate>("SFileCloseArchive");
            GetFileInfo = Load<SFileGetFileInfoDelegate>("SFileGetFileInfo");
            HasFile = Load<SFileHasFileDelegate>("SFileHasFile");
            ExtractFile = Load<SFileExtractFileDelegate>("SFileExtractFile");
        }

        private T Load<T>(string exportName) where T : Delegate
        {
            var address = NativeLibrary.GetExport(_libraryHandle, exportName);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        public void Dispose()
        {
            NativeLibrary.Free(_libraryHandle);
        }
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("WC3MapDeprotector StormLib macOS spike");
        Console.WriteLine(new string('=', 48));
        Console.WriteLine($"Framework:    {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS:           {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Console.Error.WriteLine("FAIL This spike is intended to validate the native macOS StormLib build.");
            return 2;
        }

        var libraryPath = Environment.GetEnvironmentVariable("WC3_STORMLIB_PATH");
        if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
        {
            Console.Error.WriteLine("FAIL WC3_STORMLIB_PATH does not point to a built StormLib shared library.");
            return 2;
        }

        try
        {
            using var storm = new StormApi(Path.GetFullPath(libraryPath));
            Console.WriteLine($"PASS Native library loaded: {Path.GetFullPath(libraryPath)}");
            Console.WriteLine("PASS Required exports resolved: SFileOpenArchive, SFileCloseArchive, SFileGetFileInfo, SFileHasFile, SFileExtractFile");

            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Native StormLib load gate passed.");
                Console.WriteLine("Run again with a .w3x or .w3m path to validate a real Warcraft III map archive.");
                return 0;
            }

            var mapPath = Path.GetFullPath(args[0]);
            if (!File.Exists(mapPath))
            {
                Console.Error.WriteLine($"FAIL Map does not exist: {mapPath}");
                return 2;
            }

            var extractIndex = Array.FindIndex(args, 1, value => string.Equals(value, "--extract", StringComparison.OrdinalIgnoreCase));
            var extractRequested = extractIndex >= 0;
            var outputDirectory = extractRequested && extractIndex + 1 < args.Length
                ? Path.GetFullPath(args[extractIndex + 1])
                : Path.GetFullPath(Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "mac-port-results",
                    $"{Path.GetFileNameWithoutExtension(mapPath)}-research"));

            Console.WriteLine();
            Console.WriteLine($"Map: {mapPath}");

            var mapPathUtf8 = Marshal.StringToCoTaskMemUTF8(mapPath);
            IntPtr archiveHandle;
            try
            {
                if (!storm.OpenArchive(mapPathUtf8, 0, MpqOpenReadOnly, out archiveHandle) || archiveHandle == IntPtr.Zero)
                {
                    Console.Error.WriteLine("FAIL StormLib could not open the map archive.");
                    return 3;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(mapPathUtf8);
            }

            try
            {
                Console.WriteLine("PASS SFileOpenArchive");

                var archiveSize = QueryUInt64(storm, archiveHandle, SFileInfoClass.SFileMpqArchiveSize64);
                var maxFileCount = QueryUInt32(storm, archiveHandle, SFileInfoClass.SFileMpqMaxFileCount);
                var fileTableSize = QueryUInt32(storm, archiveHandle, SFileInfoClass.SFileMpqFileTableSize);
                var numberOfFiles = QueryUInt32(storm, archiveHandle, SFileInfoClass.SFileMpqNumberOfFiles);
                var sectorSize = QueryUInt32(storm, archiveHandle, SFileInfoClass.SFileMpqSectorSize);

                Console.WriteLine();
                Console.WriteLine("Archive metadata");
                Console.WriteLine("----------------");
                Console.WriteLine($"Archive size:       {archiveSize:N0} bytes");
                Console.WriteLine($"Number of files:    {numberOfFiles:N0}");
                Console.WriteLine($"File table entries: {fileTableSize:N0}");
                Console.WriteLine($"Max file count:     {maxFileCount:N0}");
                Console.WriteLine($"Sector size:        {sectorSize:N0} bytes");

                Console.WriteLine();
                Console.WriteLine("Known Warcraft III files");
                Console.WriteLine("-------------------------");

                foreach (var fileName in KnownWarcraftFiles.Where(name => !name.StartsWith('(') || name == "(listfile)"))
                {
                    Console.WriteLine($"{(HasFile(storm, archiveHandle, fileName) ? "YES" : " no")}  {fileName}");
                }

                if (extractRequested)
                {
                    ExtractResearchPackage(storm, archiveHandle, mapPath, outputDirectory, archiveSize, numberOfFiles);
                }

                Console.WriteLine();
                Console.WriteLine("GO: native StormLib can open and inspect this Warcraft III map on macOS.");
                return 0;
            }
            finally
            {
                if (!storm.CloseArchive(archiveHandle))
                {
                    Console.Error.WriteLine("WARN SFileCloseArchive reported failure.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void ExtractResearchPackage(
        StormApi storm,
        IntPtr archiveHandle,
        string mapPath,
        string outputDirectory,
        ulong archiveSize,
        uint archiveFileCount)
    {
        var rawDirectory = Path.Combine(outputDirectory, "raw");
        var metadataDirectory = Path.Combine(outputDirectory, "metadata");
        Directory.CreateDirectory(rawDirectory);
        Directory.CreateDirectory(metadataDirectory);

        var namedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listfilePath = Path.Combine(metadataDirectory, "listfile.txt");

        if (HasFile(storm, archiveHandle, "(listfile)") && ExtractFile(storm, archiveHandle, "(listfile)", listfilePath))
        {
            foreach (var line in File.ReadLines(listfilePath))
            {
                var name = line.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    namedFiles.Add(name);
                }
            }

            Console.WriteLine();
            Console.WriteLine($"PASS Parsed (listfile): {namedFiles.Count:N0} named entries");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("WARN Could not extract (listfile); continuing with standard Warcraft III file names only.");
        }

        foreach (var knownName in KnownWarcraftFiles)
        {
            if (HasFile(storm, archiveHandle, knownName))
            {
                namedFiles.Add(knownName);
            }
        }

        var extractedFiles = new List<ExtractedFile>();
        var failedNames = new List<string>();

        foreach (var archiveName in namedFiles.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryGetSafeOutputPath(rawDirectory, archiveName, out var localPath, out var relativePath))
            {
                failedNames.Add($"{archiveName} [unsafe path]");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            if (!ExtractFile(storm, archiveHandle, archiveName, localPath))
            {
                failedNames.Add(archiveName);
                continue;
            }

            var extension = Path.GetExtension(archiveName);
            extractedFiles.Add(new ExtractedFile(
                archiveName,
                relativePath,
                new FileInfo(localPath).Length,
                IsResearchTextCandidate(archiveName),
                ObjectDataExtensions.Contains(extension)));
        }

        var scriptCandidates = extractedFiles
            .Where(file => file.ResearchText)
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var objectDataFiles = extractedFiles
            .Where(file => file.ObjectData)
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var unrecoveredCount = Math.Max(0, checked((int)archiveFileCount) - extractedFiles.Count);
        var manifest = new ResearchManifest(
            mapPath,
            archiveSize,
            archiveFileCount,
            namedFiles.Count,
            extractedFiles.Count,
            unrecoveredCount,
            DateTimeOffset.UtcNow,
            scriptCandidates,
            objectDataFiles,
            extractedFiles);

        File.WriteAllText(
            Path.Combine(metadataDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllLines(Path.Combine(metadataDirectory, "script-candidates.txt"), scriptCandidates);
        File.WriteAllLines(Path.Combine(metadataDirectory, "object-data-files.txt"), objectDataFiles);
        File.WriteAllLines(Path.Combine(metadataDirectory, "failed-extractions.txt"), failedNames);

        Console.WriteLine();
        Console.WriteLine("Research extraction");
        Console.WriteLine("-------------------");
        Console.WriteLine($"Output:                {outputDirectory}");
        Console.WriteLine($"Named archive entries: {namedFiles.Count:N0}");
        Console.WriteLine($"Extracted files:       {extractedFiles.Count:N0}");
        Console.WriteLine($"Script/text candidates:{scriptCandidates.Length,6:N0}");
        Console.WriteLine($"Object-data files:     {objectDataFiles.Length,6:N0}");
        Console.WriteLine($"Failed named extracts: {failedNames.Count,6:N0}");
        Console.WriteLine($"Unnamed/unrecovered:   {unrecoveredCount,6:N0}");

        if (scriptCandidates.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Research text candidates");
            Console.WriteLine("------------------------");
            foreach (var candidate in scriptCandidates.Take(40))
            {
                Console.WriteLine(candidate);
            }

            if (scriptCandidates.Length > 40)
            {
                Console.WriteLine($"... plus {scriptCandidates.Length - 40:N0} more (see metadata/script-candidates.txt)");
            }
        }

        if (unrecoveredCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("NOTE Some MPQ entries are not covered by the internal listfile or failed extraction.");
            Console.WriteLine("     The next recovery stage will inspect unnamed file-table entries instead of guessing names.");
        }
    }

    private static bool IsResearchTextCandidate(string archiveName)
    {
        var extension = Path.GetExtension(archiveName);
        if (ResearchTextExtensions.Contains(extension))
        {
            return true;
        }

        return archiveName.Contains("script", StringComparison.OrdinalIgnoreCase)
               || archiveName.Contains("trigger", StringComparison.OrdinalIgnoreCase)
               || archiveName.Contains("code", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetSafeOutputPath(
        string rootDirectory,
        string archiveName,
        out string fullPath,
        out string relativePath)
    {
        var normalized = archiveName.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            fullPath = string.Empty;
            relativePath = string.Empty;
            return false;
        }

        relativePath = Path.Combine(segments);
        var root = Path.GetFullPath(rootDirectory) + Path.DirectorySeparatorChar;
        fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));

        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
        {
            fullPath = string.Empty;
            relativePath = string.Empty;
            return false;
        }

        return true;
    }

    private static bool ExtractFile(StormApi storm, IntPtr archiveHandle, string archiveName, string localPath)
    {
        var archiveNameUtf8 = Marshal.StringToCoTaskMemUTF8(archiveName);
        var localPathUtf8 = Marshal.StringToCoTaskMemUTF8(localPath);
        try
        {
            return storm.ExtractFile(archiveHandle, archiveNameUtf8, localPathUtf8, SFileOpenFromMpq);
        }
        finally
        {
            Marshal.FreeCoTaskMem(archiveNameUtf8);
            Marshal.FreeCoTaskMem(localPathUtf8);
        }
    }

    private static uint QueryUInt32(StormApi storm, IntPtr handle, SFileInfoClass infoClass)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (!storm.GetFileInfo(handle, infoClass, buffer, sizeof(uint), out _))
            {
                throw new InvalidOperationException($"SFileGetFileInfo failed for {infoClass}.");
            }

            return unchecked((uint)Marshal.ReadInt32(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ulong QueryUInt64(StormApi storm, IntPtr handle, SFileInfoClass infoClass)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            if (!storm.GetFileInfo(handle, infoClass, buffer, sizeof(long), out _))
            {
                throw new InvalidOperationException($"SFileGetFileInfo failed for {infoClass}.");
            }

            return unchecked((ulong)Marshal.ReadInt64(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool HasFile(StormApi storm, IntPtr archiveHandle, string archivedName)
    {
        var archivedNameUtf8 = Marshal.StringToCoTaskMemUTF8(archivedName);
        try
        {
            return storm.HasFile(archiveHandle, archivedNameUtf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(archivedNameUtf8);
        }
    }
}
