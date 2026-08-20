# macOS Port

WC3MapDeprotector is being ported incrementally while keeping the existing Windows application working.

## Scope

The first macOS release targets the deprotection pipeline itself:

- open `.w3m` and `.w3x` archives
- recover and extract map files
- recover JASS, Lua, SLK and object data where the existing core supports it
- rebuild a deprotected map
- provide a command line interface

The following Windows integrations are deliberately out of scope for the first macOS release:

- WinForms UI
- World Editor automation through FlaUI and `user32.dll`
- ETW based live game file access scanning
- Windows Registry integration

## M0: preserve a reference

Before changing the deprotection core, keep a small local fixture set in `test-maps-local/`. This directory is gitignored intentionally.

Recommended fixture categories:

1. normal unprotected map
2. simple protected map
3. heavily obfuscated JASS map
4. Lua map
5. map with custom object data
6. map with unknown or hidden archive file names, if available

For each fixture, record the current Windows application's result before judging a later macOS result. Binary identical output is not required. Compare behavior and recovered content.

Useful baseline fields:

- deprotection completes or fails
- number of warnings and critical warnings
- recovered JASS or Lua
- recovered object data
- recovered archive files
- remaining unknown files
- output map can be reopened by the normal Windows workflow

## M1A: managed dependency compatibility spike

Run on the Mac from the repository checkout:

```bash
bash scripts/mac/run-compat-spike.sh
```

The spike intentionally does not reference the Windows Forms application. It tests:

- existing War3Net assemblies
- Jass2Lua
- FastMDX and MdxLib
- optional NAudio assemblies as informational probes
- NLua with real Lua execution
- ClearScript V8 with real JavaScript execution
- ImageSharp with a real PNG encode

### Status: PASS on Apple Silicon

Observed on an `osx-arm64` Mac with .NET 8:

```text
Critical failures: 0
Informational warnings: 2
GO: managed dependency layer is compatible enough to proceed to the StormLib macOS spike.
```

The two informational failures are expected for the first Mac release:

- `FastMDX` does not load, but the current recovery path also uses `MdxLib` and regex scanning.
- `NAudio.WinForms` requires Windows Forms and is not part of the cross-platform core.

NLua is backed by a locally built, architecture-correct Lua 5.3.6 shared library. ClearScript V8, War3Net, Jass2Lua, MdxLib and ImageSharp all passed their probes.

## M1B: StormLib macOS spike

StormLib is pinned to upstream version 9.30.0 at commit:

```text
c430a0c7ffc13b5d8fdaf0d7574be9e826a890af
```

The build uses StormLib's bundled dependencies and produces a local shared library under `native/<rid>/`. Generated native sources, build caches and binaries are gitignored.

### Native-load test

Requirements:

- .NET 8 SDK/runtime
- CMake 3.25 or newer
- the macOS command line developer tools

If CMake is missing:

```bash
brew install cmake
```

Then run:

```bash
bash scripts/mac/run-stormlib-spike.sh
```

This first builds StormLib for the current Mac architecture and verifies that the required exports can be loaded from .NET.

### Real Warcraft III map test

Pass a local `.w3x` or `.w3m` path:

```bash
bash scripts/mac/run-stormlib-spike.sh "/path/to/map.w3x"
```

The spike validates:

- `SFileOpenArchive`
- `SFileCloseArchive`
- `SFileGetFileInfo`
- `SFileHasFile`
- archive size
- file count
- file table size
- MPQ sector size
- presence of common Warcraft III map files such as `war3map.w3i`, JASS/Lua scripts and object data

### Gate

M1B passes when a real `.w3x` or `.w3m` reports:

```text
PASS SFileOpenArchive
...
GO: native StormLib can open and inspect this Warcraft III map on macOS.
```

After this gate, the next step is to move the proven native-loading approach into a platform-aware StormLib wrapper and start extracting the deprotection core from WinForms.

## Architecture rule

Cross-platform core code must not reference:

- `System.Windows.Forms`
- `Microsoft.Win32.Registry`
- FlaUI
- ETW `TraceEventSession`
- `user32.dll` or `kernel32.dll`
- `Warcraft III.exe`
- `World Editor.exe`

Platform integrations will live behind interfaces or in platform-specific projects after the core split.
