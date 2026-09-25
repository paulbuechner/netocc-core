# osx-arm64 findings

- **Base:** `main` at `4240494`, validated 2026-09-25 (net8.0 first on `b52d51a`, same result)
- **Machine:** MacBookPro18,1 (M1 Pro, 32 GB), macOS 27.0 (26A428), Apple clang 21.0.0 (clang-2100.3.34.2), .NET SDK 10.0.400 (runtimes 10.0.11, 8.0.31, 6.0.36), vcpkg `6ade29bb` (tool 2026-07-27-98d7cb0c)
- **Result:** per framework (net10.0, net8.0, net6.0): 77 passed, 0 failed, 0 skipped (arm64); OCCT build 9.3 min (12 ports, 10 cores), cached for the re-run

## Failures

None. On `4240494`: 14 runs each of net10.0 and net8.0, 7 of net6.0, all 77/77.

## Fixes

None needed: no source change.

## Checks

| Area | Result |
|---|---|
| OCCT build | 12 ports, all CMake (no fontconfig/gperf on osx). Host triplet `arm64-osx-dynamic` worked. |
| OCCT checks on | `BUILD_RELEASE_DISABLE_EXCEPTIONS:UNINITIALIZED=OFF`; no `-DNo_Exception` in `build.ninja`. |
| Install names | Shim and all 59 real dylibs: deps `@rpath/...`, `LC_RPATH @loader_path`, no path into `.vcpkg/`. |
| Versioned dylibs | Ids carry the full version (`@rpath/libTKernel.8.0.1.dylib`); `cmake --install` and the test copy both keep that file. |
| Exports | 505 `CSharp_*`, 4 `NetOcc_TDF_*`, 30 `NetOccRegisterExceptionCallback_*`. |
| Native loader | `DYLD_PRINT_LIBRARIES`, per framework: all 56 NetOcc/TK/third-party libs load from `bin/Release/<tfm>/osx-arm64/runtimes/osx-arm64/native/`. |
| Runtimes | CLR 10.0.11, 8.0.31, 6.0.36; net6.0 on Microsoft.NET.Test.Sdk 17.13.0 + NUnit3TestAdapter 5.2.0, the others on 18.10.1 + 6.3.0. |
| Framework builds | `dotnet build NetOcc.slnx -c Release` builds all 8 library targets, the 6 test targets and the net35 smoke, 0 warnings. |
| Code signing | Ad-hoc linker-signed; `codesign -v` passes in the stage and in the test output. |
| Strings | Test literals are NFC; no NFC/NFD mismatch on APFS. |
| Struct layouts | `static_assert`s compile; `ValueTypeTests` pass. |
| OCAF lifetime | No test-host crash in any run. |

## Open questions

- **Empty `pkgconfig/`** in the stage: `install(DIRECTORY ... FILES_MATCHING)` still creates subdirectories. Harmless for the tests; `PATTERN "pkgconfig" EXCLUDE` drops it. Shared with Linux.
- **.NET 6 setup.** Two routes other than the plan's `dotnet-install.sh` came up here; its Setup could warn against both:
  - `brew install dotnet@6` (disabled upstream, but still pours a bottle) is a separate keg. The net6.0 test host of `build.py test` aborts ("Framework: 'Microsoft.NETCore.App', version '6.0.0' (arm64)" not found), and `RunConfiguration.DotnetHostPath` didn't redirect it. It also takes over `/opt/homebrew/bin/dotnet` from the `dotnet-sdk` cask. Its own `dotnet vstest` passed 77/77 on its source-built 6.0.36.
  - Microsoft's SDK 6.0.428 pkg, which ran the net6.0 results above: its `sharedhost` component replaced the `dotnet` host with 6.0.36's. SDK 10 still resolves through hostfxr 10.0.11; net10.0 and net8.0 passed on both hosts.
- **Answered:** deployment target and staged symlinks are deferred to Phase 3 (`CLAUDE.md`); the rebase and setup notes went into the plan.

## Re-run: generated modules, deployment target, flat stage, packages

- **Base:** `main` at `0a9af63`, validated 2026-09-25
- **Machine:** as above, but .NET SDK 10.0.401 (runtimes 10.0.12, 8.0.31, 6.0.36); the `dotnet` host is 10.0.12 again
- **Result:** per framework (net10.0, net8.0, net6.0): 79 passed, 0 failed, 0 skipped (arm64), 7 runs each; package tests 2/2 (net8.0); OCCT rebuild 7.8 min
- **Failures, fixes:** none; no source change.

| Check | Result |
|---|---|
| `minos` | 14.0 on all 59 stage files, `libTKernel.8.0.1.dylib` included (SDK 27.0). |
| Stage | 59 files, 0 symlinks, no `pkgconfig/` or other directory: 82.0 MiB (86,031,560 bytes). Every file carries its install name; every `@rpath` dependency resolves inside the stage. |
| Exports | 5055 `CSharp_*`, all 5055 defined in the generated `*_wrap.cxx`; 4 `NetOcc_TDF_*`, 30 `NetOccRegisterExceptionCallback_*`. Every C# `EntryPoint` resolves. |
| Test output | 59 files, 82 MB per framework (was 176 files, 237 MB). `DYLD_PRINT_LIBRARIES`: the natives load from there on all three frameworks. |
| Packages | `NetOcc.runtime.osx-arm64.0.0.0-local.20260925143814.nupkg`: 27,420,981 bytes (26.2 MiB), with the 59 natives, `lib/` placeholders and `THIRD-PARTY-NOTICES.txt` (NetOcc, NOTICE, brotli, bzip2, egl-registry, freetype, libpng, opencascade, opengl-registry, rapidjson, zlib). `NetOcc` 2,261,222 bytes, `.snupkg` 5,127,531 bytes. |
| Package tests | 2/2 through `build.py test-package` (RID output, natives next to the app) and 2/2 portable (`runtimes/osx-arm64/native/`). |
| Framework builds | All 8 library targets build against the generated code, 0 warnings. |
| Strings | `NonAscii.Text` is NFC; no combining marks in the tests. |

### Open questions

- **Double slashes when packing on Unix.** CI packs on ubuntu-24.04. With backslash `PackagePath`s (`runtimes\$(NetOccRid)\native\`, `lib\net35\;lib\netstandard2.0\`), a Unix pack writes entries like `runtimes/osx-arm64/native//libTKBO.8.0.1.dylib` and `lib/net35//_._` (61 in the osx package). NuGet normalizes them on extraction. A probe `win-x64` package packed here restores for net8.0 (`lib/netstandard2.0/_._`, no NU1202) and imports its `build/net35` targets for net48. Forward slashes in those two `PackagePath`s give clean entries; cosmetic only.
- **Rebase recipe.** `old=$(git rev-parse origin/main)` captured the new `main`: a background fetch had already moved `origin/main`. Since `main` is one parentless commit, the branch's root commit is always the old `main`: `git rebase --onto origin/main "$(git rev-list --max-parents=0 HEAD)"` doesn't depend on fetch timing.
- **Answered:** the flat stage drops `pkgconfig/`; the plan's Setup now warns against the other .NET 6 routes.
