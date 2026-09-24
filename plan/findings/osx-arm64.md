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
