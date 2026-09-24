// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OCC.Core;

/// <summary>
/// Loads NetOccNative (and, through the OS loader, the OCCT libraries next to it)
/// before the first P/Invoke. Probed locations, relative to the app base directory:
/// <c>&lt;arch&gt;/</c> (AnyCPU .NET Framework layout), <c>runtimes/&lt;rid&gt;/native/</c>, and the base directory.
/// </summary>
internal static class NativeLibraryLoader
{
    internal const string LibraryName = "NetOccNative";

    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static readonly object Sync = new();
    private static IntPtr _handle;

    // CA2255: a library module initializer is the point here; it must run before any P/Invoke.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
#if NET5_0_OR_GREATER
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, static (name, _, _) =>
            name == LibraryName ? Load() : IntPtr.Zero);
#else
        // .NET Framework: DllImport resolves by module name, so a preloaded module wins.
        if (IsWindows)
        {
            Load();
        }
#endif
    }

#if NETFRAMEWORK
    // .NET Framework runs on Windows only. RuntimeInformation arrived in 4.7.1, Is64BitProcess and
    // AppContext in 4.0 and 4.6; the net35 build needs the older equivalents.
    private static bool IsWindows => true;

    private static string ArchName() => IntPtr.Size == 8 ? "x64" : "x86";

    private static string OsName() => "win";

    private static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
#else
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string ArchName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        var other => other.ToString().ToLowerInvariant(),
    };

    private static string OsName() => IsWindows ? "win" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";

    private static string BaseDirectory => AppContext.BaseDirectory;
#endif

    private static IntPtr Load()
    {
        lock (Sync)
        {
            if (_handle != IntPtr.Zero)
            {
                return _handle;
            }

            foreach (var path in Candidates())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                // Windows: search the library's own directory for its dependencies (TK*.dll).
                _handle = IsWindows ? LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath) : LoadUnix(path);
                if (_handle != IntPtr.Zero)
                {
                    break;
                }
            }

            return _handle; // IntPtr.Zero: fall back to default probing
        }
    }

    private static string[] Candidates()
    {
        var baseDir = BaseDirectory;
        var arch = ArchName();
        var os = OsName();
        var file = os switch
        {
            "win" => $"{LibraryName}.dll",
            "osx" => $"lib{LibraryName}.dylib",
            _ => $"lib{LibraryName}.so",
        };
        return
        [
            Combine(baseDir, arch, file),
            Combine(baseDir, "runtimes", $"{os}-{arch}", "native", file),
            Combine(baseDir, file),
        ];
    }

    // Path.Combine takes more than two parts only from .NET Framework 4.0 on.
    private static string Combine(string first, params string[] rest)
    {
        var path = first;
        foreach (var part in rest)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    private static IntPtr LoadUnix(string path)
    {
#if NET5_0_OR_GREATER
        return NativeLibrary.TryLoad(path, out var handle) ? handle : IntPtr.Zero;
#else
        return IntPtr.Zero;
#endif
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);
}
