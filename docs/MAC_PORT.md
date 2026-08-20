# macOS Port

This branch ports WC3MapDeprotector incrementally while keeping the existing Windows application working.

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

### Gate

Proceed to the StormLib macOS work when the spike reports:

```text
Critical failures: 0
GO: managed dependency layer is compatible enough to proceed to the StormLib macOS spike.
```

A failure is not a reason to abandon the port. It identifies the exact dependency that should be replaced or rebuilt before the core is refactored.

## M1B: StormLib macOS spike

After M1A passes or its blockers are understood:

1. build StormLib for `osx-arm64`
2. replace the fixed `StormLib.dll` lookup with a platform aware native library resolver
3. remove the `kernel32.dll` dependency from the StormLib wrapper
4. open one real `.w3x` on macOS
5. enumerate archive entries and query the MPQ metadata required by `StormMPQArchive`
6. compare results with the Windows implementation

This is the first major GO/STOP gate for the full port.

## Architecture rule

Cross platform core code must not reference:

- `System.Windows.Forms`
- `Microsoft.Win32.Registry`
- FlaUI
- ETW `TraceEventSession`
- `user32.dll` or `kernel32.dll`
- `Warcraft III.exe`
- `World Editor.exe`

Platform integrations will live behind interfaces or in platform specific projects after the core split.
