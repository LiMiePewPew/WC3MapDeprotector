using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.ClearScript.V8;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacCompatibilitySpike;

internal static class Program
{
    private sealed record ProbeTarget(string Name, string RelativePath, bool Critical = true);

    private sealed class ProbeLoadContext : AssemblyLoadContext
    {
        private readonly IReadOnlyList<string> _searchDirectories;

        public ProbeLoadContext(IReadOnlyList<string> searchDirectories)
            : base(isCollectible: true)
        {
            _searchDirectories = searchDirectories;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (var directory in _searchDirectories)
            {
                var candidate = Path.Combine(directory, $"{assemblyName.Name}.dll");
                if (File.Exists(candidate))
                {
                    return LoadFromAssemblyPath(Path.GetFullPath(candidate));
                }
            }

            return null;
        }
    }

    private static readonly ProbeTarget[] ManagedAssemblyTargets =
    [
        new("War3Net.Common", "War3Net/Binaries/War3Net.Common.dll"),
        new("War3Net.IO.Compression", "War3Net/Binaries/War3Net.IO.Compression.dll"),
        new("War3Net.IO.Mpq", "War3Net/Binaries/War3Net.IO.Mpq.dll"),
        new("War3Net.IO.Slk", "War3Net/Binaries/War3Net.IO.Slk.dll"),
        new("War3Net.Build.Core", "War3Net/Binaries/War3Net.Build.Core.dll"),
        new("War3Net.Build", "War3Net/Binaries/War3Net.Build.dll"),
        new("War3Net.CodeAnalysis", "War3Net/Binaries/War3Net.CodeAnalysis.dll"),
        new("War3Net.CodeAnalysis.Jass", "War3Net/Binaries/War3Net.CodeAnalysis.Jass.dll"),
        new("War3Net.CodeAnalysis.Decompilers", "War3Net/Binaries/War3Net.CodeAnalysis.Decompilers.dll"),
        new("War3Net.CodeAnalysis.Transpilers", "War3Net/Binaries/War3Net.CodeAnalysis.Transpilers.dll"),
        new("CSharp.lua", "War3Net/Binaries/CSharp.lua.dll"),
        new("DotNetZip", "War3Net/Binaries/DotNetZip.dll"),
        new("Pidgin", "War3Net/Binaries/Pidgin.dll"),
        new("Microsoft.CodeAnalysis", "War3Net/Binaries/Microsoft.CodeAnalysis.dll"),
        new("Jass2Lua", "Jass2Lua/Jass2Lua.dll"),
        new("FastMDX", "WC3MapDeprotector/FastMDX.dll"),
        new("MdxLib", "WC3MapDeprotector/MdxLib.dll"),

        // These are informational. The macOS port should not depend on the
        // Windows audio backends even if the managed assemblies happen to load.
        new("NAudio.Core", "NAudio/NAudio.Core.dll", Critical: false),
        new("NAudio", "NAudio/NAudio.dll", Critical: false),
        new("NAudio.WinMM", "NAudio/NAudio.WinMM.dll", Critical: false),
        new("NAudio.Wasapi", "NAudio/NAudio.Wasapi.dll", Critical: false),
        new("NAudio.WinForms", "NAudio/NAudio.WinForms.dll", Critical: false),
    ];

    public static int Main()
    {
        Console.WriteLine("WC3MapDeprotector macOS compatibility spike");
        Console.WriteLine(new string('=', 52));
        Console.WriteLine($"Framework:     {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS:            {RuntimeInformation.OSDescription}");
        Console.WriteLine($"OS arch:       {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"Process arch:  {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine();

        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("FAIL Could not locate WC3MapDeprotector.sln. Run this tool from inside the repository checkout.");
            return 2;
        }

        Console.WriteLine($"Repository:    {repoRoot}");
        Console.WriteLine();

        var searchDirectories = new[]
        {
            Path.Combine(repoRoot, "War3Net", "Binaries"),
            Path.Combine(repoRoot, "Jass2Lua"),
            Path.Combine(repoRoot, "WC3MapDeprotector"),
            Path.Combine(repoRoot, "NAudio"),
        };

        var failures = 0;
        var warnings = 0;

        Console.WriteLine("Managed assembly probes");
        Console.WriteLine("-----------------------");
        foreach (var target in ManagedAssemblyTargets)
        {
            var result = ProbeManagedAssembly(repoRoot, searchDirectories, target);
            if (!result)
            {
                if (target.Critical)
                {
                    failures++;
                }
                else
                {
                    warnings++;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Cross-platform package runtime probes");
        Console.WriteLine("-------------------------------------");

        failures += RunRuntimeProbe("NLua / native Lua", () =>
        {
            using var lua = new NLua.Lua();
            var result = lua.DoString("return 6 * 7");
            if (result is null || result.Length == 0 || Convert.ToInt64(result[0]) != 42)
            {
                throw new InvalidOperationException("Lua executed but returned an unexpected result.");
            }
        });

        failures += RunRuntimeProbe("ClearScript V8 native runtime", () =>
        {
            using var engine = new V8ScriptEngine();
            var result = engine.Evaluate("6 * 7");
            if (Convert.ToInt32(result) != 42)
            {
                throw new InvalidOperationException("V8 executed but returned an unexpected result.");
            }
        });

        failures += RunRuntimeProbe("ImageSharp encode", () =>
        {
            using var image = new Image<Rgba32>(1, 1);
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            if (stream.Length == 0)
            {
                throw new InvalidOperationException("ImageSharp produced an empty PNG.");
            }
        });

        Console.WriteLine();
        Console.WriteLine("Summary");
        Console.WriteLine("-------");
        Console.WriteLine($"Critical failures: {failures}");
        Console.WriteLine($"Informational warnings: {warnings}");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Console.WriteLine("NOTE This spike is intended to be run on macOS; current results are not a macOS compatibility verdict.");
        }

        if (failures == 0)
        {
            Console.WriteLine("GO: managed dependency layer is compatible enough to proceed to the StormLib macOS spike.");
            return 0;
        }

        Console.WriteLine("STOP: resolve the critical dependency failures before refactoring the deprotector core.");
        return 1;
    }

    private static bool ProbeManagedAssembly(string repoRoot, IReadOnlyList<string> searchDirectories, ProbeTarget target)
    {
        var fullPath = Path.Combine(repoRoot, target.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"FAIL {target.Name,-38} missing: {target.RelativePath}");
            return false;
        }

        var loadContext = new ProbeLoadContext(searchDirectories);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(fullPath));
            var version = assembly.GetName().Version?.ToString() ?? "unknown";

            try
            {
                var exportedTypeCount = assembly.GetExportedTypes().Length;
                Console.WriteLine($"PASS {target.Name,-38} v{version}, exported types: {exportedTypeCount}");
                return true;
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaderErrors = ex.LoaderExceptions
                    .Where(error => error is not null)
                    .Select(error => error!.Message)
                    .Distinct()
                    .Take(3)
                    .ToArray();

                Console.WriteLine($"FAIL {target.Name,-38} assembly loaded, type resolution failed");
                foreach (var error in loaderErrors)
                {
                    Console.WriteLine($"     {error}");
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL {target.Name,-38} {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static int RunRuntimeProbe(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"PASS {name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(startingPath);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "WC3MapDeprotector.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}
