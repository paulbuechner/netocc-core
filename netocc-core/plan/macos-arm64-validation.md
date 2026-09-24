# macOS arm64 validation

Handoff to the Claude session on the Mac. **Goal:** close Phase 0 on `osx-arm64`: OCCT 8.0.1 via vcpkg, native shim, whole NUnit suite green on every .NET target (net10.0, net8.0, net6.0). Then report back through the remote.

**Status: done 2026-09-25**, no source changes (`findings/osx-arm64.md`):
- **Phase 0:** 77/77 on an M1 Pro for net10.0, net8.0 and net6.0.
- **Re-run** with the 30 generated modules, deployment target 14.0, the flat stage and the packages: 79/79 on all three, package tests 2/2.

Its two open questions are fixed on `main`: `PackagePath`s use forward slashes (a Linux pack now writes no `//` entries), and the rebase recipe under Rules starts from the root commit. A next re-run can confirm with `unzip -l` on the osx runtime package. Keep this guide for re-runs.

Windows x64 and x86 are green on every test target (net35 on CLR 2 included), and linux-x64 in Docker on the three .NET ones (OCCT built in ~25 min). Every run has 79 tests. Read `CLAUDE.md` first: wrapper contract, rules, gotchas.

## Rules for this task

These override the Git section of `CLAUDE.md` for this task only.

- **Branch.** Work on `validate/osx-arm64` and push only that branch. Never push `main`.
- **`main` gets rewritten.** The Windows side keeps `main` as one parentless commit, amends it and force-pushes after every change. Your branch's root commit is therefore always the `main` it started from; rebase from there:

  ```bash
  git fetch
  git rebase --onto origin/main "$(git rev-list --max-parents=0 HEAD)"
  ```

  - This works however `origin/main` moved, even when a background fetch updated it first.
  - It also crosses the monorepo move (2026-09-26: the repo is github.com/paulbuechner/netocc, this tree now `netocc-core/`): git follows the renames. Work in `netocc-core/`.
  - Commits already folded into `main` become empty and drop out.
  - Don't use `--fork-point`: a fresh clone has no reflog for `origin/main`.

  Then push with `git push --force-with-lease origin validate/osx-arm64`. Force-push only your own branch.
- **Commits.** Use `<type>(<scope>): <description>`, for example `fix(macos): resolve TK dylibs through @loader_path`. Commit with the default git identity and never add Claude as author or co-author. Never bypass signing; if signing fails, stop and ask the user.
- **Fix scope.** Only fix what macOS needs, and guard it:
  - CMake: `if(APPLE)`;
  - Python: `sys.platform == "darwin"`;
  - C#: `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)`.
- **Shared changes.** Some changes affect every platform: typemaps, the `.i` files, `build.py`, and `CMakeLists.txt` outside `if(APPLE)`. Mark any of these in the findings as *needs Windows/Linux re-run*.
- **The rest of `CLAUDE.md` applies:**
  - C# style, NUnit + Arrange/Act/Assert, SPDX headers on new source files.
  - No OCCT doc text, no code from other OCCT bindings.
- **Tests.** Don't weaken tests to get green. A test that can't pass on macOS gets a finding, not an edit.

## Setup

- **Xcode Command Line Tools:** `xcode-select --install`.
- **Build tools:** `brew install cmake ninja pkg-config python`. On macOS, OCCT's 12 ports all build with CMake. The autotools are needed only on Linux, where fontconfig pulls in `gperf`.
- **.NET SDK 10:** it builds every target (`dotnet --list-sdks` shows 10.0.x).
- **.NET 8:** the net8.0 tests need `Microsoft.NETCore.App 8.x` in `dotnet --list-runtimes`. A newer SDK alone runs no net8.0 tests. Install it with `brew install --cask dotnet-sdk@8`, a pkg installer that needs sudo.
- **.NET 6 runtime (arm64):** the net6.0 tests need it in the same dotnet root. `dotnet --list-runtimes` must then list `Microsoft.NETCore.App 6.0.x`. `--skip-non-versioned-files` keeps the newer `dotnet` host:

  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
  sudo bash dotnet-install.sh --channel 6.0 --runtime dotnet --install-dir /usr/local/share/dotnet --skip-non-versioned-files
  ```

  Avoid the other routes:
  - **`brew install dotnet@6`** is a separate keg. The net6.0 test host then aborts with "Framework 'Microsoft.NETCore.App' 6.0.0 not found", and the keg takes over `/opt/homebrew/bin/dotnet` from the `dotnet-sdk` cask.
  - **Microsoft's .NET 6 SDK pkg** replaces the `dotnet` host with 6.0's. The newer SDKs still run, since they resolve through their own hostfxr, but the runtime-only install above changes nothing else.
- **vcpkg:** needs a clone, because Homebrew's `vcpkg` formula alone lacks the scripts:

  ```bash
  git clone https://github.com/microsoft/vcpkg ~/vcpkg
  ~/vcpkg/bootstrap-vcpkg.sh -disableMetrics
  export VCPKG_ROOT=~/vcpkg
  ```

## Run

```bash
git clone https://github.com/paulbuechner/netocc.git && cd netocc/netocc-core
git switch -c validate/osx-arm64
python3 build.py tools                                  # SWIG from conda-forge into .tools/swig
python3 build.py occt --triplet arm64-osx-dynamic       # OCCT from source, 1-2 h once, then cached in .vcpkg/archives
python3 build.py generate
python3 build.py native --triplet arm64-osx-dynamic     # -> artifacts/runtimes/osx-arm64/native
python3 build.py test --arch arm64                     # net10.0, net8.0, net6.0 (the .NET Framework targets are Windows-only)
```

- **Expected result:** every test passes on all three frameworks, 79 each at the time of writing. Take the count from the `main` you rebased onto.
- **Build state:** vcpkg keeps all state under `.vcpkg/`; `build.py` sets every root. Don't run `vcpkg install` directly.
- **Pick up fixes.** The OCCT build doesn't depend on this repo's sources, so start it right away. The Windows side keeps working, so `main` may move meanwhile. Before `generate`, rebase onto it as described under Rules.

## Where it may break

| Area | Check |
|---|---|
| OCCT build | A failing port leaves its log in `.vcpkg/buildtrees/<port>/*-err.log`. Visualization needs the OpenGL framework (deprecated but present) and FreeType. |
| Host triplet | `build.py occt` uses the target triplet as host triplet off Windows (`arm64-osx-dynamic`). If a host tool fails to build, try `--host-triplet=arm64-osx`. This is a `build.py` change. |
| OCCT checks on | `.vcpkg/buildtrees/opencascade/arm64-osx-dynamic-rel/CMakeCache.txt` must show `BUILD_RELEASE_DISABLE_EXCEPTIONS...=OFF`. Otherwise `ExceptionTests` crash instead of throwing. |
| Install names | `otool -L artifacts/runtimes/osx-arm64/native/libNetOccModelingData.dylib` (any NetOcc library) shows `@rpath/libTK*`. `otool -l <lib> \| grep -A2 LC_RPATH` shows `@loader_path` on the shim. Every TK dylib must resolve its siblings the same way. |
| Versioned dylibs | The TK libs come as `libTKernel.dylib -> libTKernel.8.0.dylib -> ...`. Check that `cmake --install` and the test project's copy keep the name the loader asks for. |
| Exports | The shim builds with `-fvisibility=hidden`, and every entry point relies on `SWIGEXPORT`. Both `nm -gU <shim> \| grep -c CSharp_` and `nm -gU <shim> \| grep NetOcc_TDF_` must list them; the second covers the hand-written OCAF keep-alive in `TDF.i`/`TNaming.i`. An `EntryPointNotFoundException` names the missing symbol. |
| Native loader | On .NET (net6.0 and later), `src/NetOcc/Runtime/NativeLibraryLoader.cs` resolves `runtimes/osx-arm64/native/libNetOcc<Module>.dylib`. `NetOcc.Tests.csproj` copies the artifacts there. `DllNotFoundException` means probing or dependencies; run `DYLD_PRINT_LIBRARIES=1 dotnet test ...` to see which. |
| Code signing | arm64 macOS rejects unsigned native code. The linker ad-hoc signs, and copying keeps the signature. `killed: 9` or "code signature invalid" means a copy step broke it; check with `codesign -v`. |
| Strings | `TCollection_ExtendedString` crosses as UTF-16 (`LPWStr`), not `wchar_t`, which is 32-bit on macOS. The OCAF Unicode tests and the non-ASCII temp paths (`netocc Ünïcödé ...`) cover this. Report any path mismatch (NFC/NFD). |
| Struct layouts | `static_assert`s in `gp.i`, `Poly.i`, `Quantity.i` and `Guid.i` fail the native build if clang lays out differently. `ValueTypeTests` checks the sizes at run time. |
| OCAF lifetime | A test-host crash in a finalizer (`delete_*` on the stack) is an OCAF lifetime bug. See the tree keep-alive comment in `TDF.i`, and report the stack. |

## Report back

1. **Commit the fixes.** One commit per fix, each with its reason in the message body.
2. **Write the findings.** Put them in `plan/findings/osx-arm64.md` using the template below, and keep log excerpts to the first error lines. Commit as `docs(macos): add osx-arm64 validation findings`.
3. **Push the branch.** Rebase onto the latest `main` first, then run `git push -u origin validate/osx-arm64` (`--force-with-lease` after a rebase), and tell the user it's pushed. The Windows session fetches the branch, folds the fixes into `main` and force-pushes. Your commits then drop out when you next rebase.

```markdown
# osx-arm64 findings

- **Base:** `main` at <commit>, validated <date>
- **Machine:** <model>, macOS <version>, Apple clang <version>, .NET SDK <version> (runtime 8.0.x), vcpkg <commit>
- **Result:** per framework (net10.0, net8.0, net6.0): <n> passed, <n> failed, <n> skipped (arm64); OCCT build <duration>

## Failures

| Test | First error line | Cause | Fixed by |
|---|---|---|---|

## Fixes

- `<hash>` <what>, because <why>. Shared files: <none, or list: needs Windows/Linux re-run>

## Open questions

- ...
```
