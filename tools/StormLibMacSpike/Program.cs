using System.Runtime.InteropServices;

namespace StormLibMacSpike;

internal static class Program
{
    private const uint MpqOpenReadOnly = 0x00000100;

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

    private sealed class StormApi : IDisposable
    {
        private readonly IntPtr _libraryHandle;

        public SFileOpenArchiveDelegate OpenArchive { get; }
        public SFileCloseArchiveDelegate CloseArchive { get; }
        public SFileGetFileInfoDelegate GetFileInfo { get; }
        public SFileHasFileDelegate HasFile { get; }

        public StormApi(string libraryPath)
        {
            _libraryHandle = NativeLibrary.Load(libraryPath);
            OpenArchive = Load<SFileOpenArchiveDelegate>("SFileOpenArchive");
            CloseArchive = Load<SFileCloseArchiveDelegate>("SFileCloseArchive");
            GetFileInfo = Load<SFileGetFileInfoDelegate>("SFileGetFileInfo");
            HasFile = Load<SFileHasFileDelegate>("SFileHasFile");
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
            Console.WriteLine("PASS Required exports resolved: SFileOpenArchive, SFileCloseArchive, SFileGetFileInfo, SFileHasFile");

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

                foreach (var fileName in new[]
                         {
                             "war3map.w3i",
                             "war3map.j",
                             "war3map.lua",
                             "war3map.w3u",
                             "war3map.w3a",
                             "war3map.w3t",
                             "(listfile)",
                         })
                {
                    Console.WriteLine($"{(HasFile(storm, archiveHandle, fileName) ? "YES" : " no")}  {fileName}");
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
