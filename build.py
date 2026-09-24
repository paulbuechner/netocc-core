#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""netocc-core build driver.

    python build.py tools                       # project-local SWIG (pip venv in .tools/)
    python build.py occt   --triplet x64-windows    # OCCT via vcpkg manifest, repo-local
    python build.py generate                    # SWIG: src/SWIG_files/wrapper/*.i -> build/generated
    python build.py native --triplet x64-windows    # CMake: NetOccNative + OCCT libs -> artifacts/runtimes/<rid>/native
    python build.py test   --arch x64 [--framework net10.0|net8.0|net6.0|net48|net472|net462|net35]
                                                # no --framework on Windows: every framework, net35 on CLR 2,
                                                # plus the net45/net452 library builds
    python build.py all    --triplet x64-windows
    python build.py pack   [--version 1.2.3] [--rids win-x64,win-x86]  # NuGet: NetOcc + NetOcc.runtime.<rid>
    python build.py test-package [--arch x64]  # the packed NetOcc, consumed like an app would
    python build.py changelog [--version 1.2.3]  # that version's CHANGELOG.md section (release notes)

Everything vcpkg touches stays under .vcpkg/ (install trees, build trees,
downloads, binary cache, registry cache); nothing is installed globally.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
TOOLS = ROOT / ".tools"
VCPKG_STATE = ROOT / ".vcpkg"
BUILD = ROOT / "build"
GENERATED = BUILD / "generated"
ARTIFACTS = ROOT / "artifacts"
SWIG_FILES = ROOT / "src" / "SWIG_files"

SWIG_VERSION = "4.5.0"

# keep in sync with VCPKG_OSX_DEPLOYMENT_TARGET in vcpkg/triplets/arm64-osx-dynamic.cmake
MACOS_DEPLOYMENT_TARGET = "14.0"

PACKAGES = ARTIFACTS / "packages"
NUGET_ORG = "https://api.nuget.org/v3/index.json"

# triplet -> (.NET RID, vcvars arch)
TRIPLETS = {
    "x64-windows": ("win-x64", "x64"),
    "x86-windows": ("win-x86", "x86"),
    "x64-linux-dynamic": ("linux-x64", None),
    "arm64-osx-dynamic": ("osx-arm64", None),
}

# SWIG modules in dependency order (each module %imports only earlier ones), written by netocc-gen generate
MODULES = json.loads((SWIG_FILES / "modules.json").read_text(encoding="utf-8"))


def run(cmd: list[str], env: dict[str, str] | None = None, cwd: Path | None = None) -> None:
    print("+", " ".join(str(c) for c in cmd), flush=True)
    subprocess.run([str(c) for c in cmd], check=True, env=env, cwd=cwd)


def host_is_windows() -> bool:
    return os.name == "nt"


# --------------------------------------------------------------------------- tools
def venv_python() -> Path:
    for rel in ("Scripts/python.exe", "bin/python.exe", "bin/python"):
        cand = TOOLS / "venv" / rel
        if cand.exists():
            return cand
    return TOOLS / "venv" / ("Scripts/python.exe" if host_is_windows() else "bin/python")


def swig_executable() -> Path:
    """Real swig binary inside the pip 'swig' package (not the console-script shim).

    The shim only sets SWIG_LIB before exec'ing the binary; swig_env() does the same."""
    if not venv_python().exists():
        sys.exit("project-local SWIG missing; run: python build.py tools")
    out = subprocess.run(
        [str(venv_python()), "-c",
         "import swig, os; d = os.path.dirname(swig.__file__); "
         "print(os.path.join(d, 'data', 'bin', 'swig.exe' if os.name == 'nt' else 'swig'))"],
        check=True, capture_output=True, text=True).stdout.strip()
    exe = Path(out)
    if not exe.exists():
        sys.exit(f"swig binary not found at {exe}; run: python build.py tools")
    return exe


def swig_env() -> dict[str, str]:
    env = dict(os.environ)
    share = swig_executable().parent.parent / "share" / "swig"
    env["SWIG_LIB"] = str(next(share.iterdir()))
    return env


def cmd_tools(_: argparse.Namespace) -> None:
    import sysconfig
    if sysconfig.get_platform().startswith("mingw"):
        sys.exit("MSYS2/MinGW Python can't install the swig wheel (platform "
                 f"{sysconfig.get_platform()}); run this step with a CPython from python.org or conda")
    if not venv_python().exists():
        run([sys.executable, "-m", "venv", TOOLS / "venv"])
    run([venv_python(), "-m", "pip", "install", "--disable-pip-version-check", f"swig=={SWIG_VERSION}"])
    swig = swig_executable()
    run([swig, "-version"], env=swig_env())


# --------------------------------------------------------------------------- occt
def vcpkg_exe() -> str:
    root = os.environ.get("VCPKG_ROOT")
    if root:
        cand = Path(root) / ("vcpkg.exe" if host_is_windows() else "vcpkg")
        if cand.exists():
            return str(cand)
    found = shutil.which("vcpkg")
    if not found:
        sys.exit("vcpkg not found (set VCPKG_ROOT or put vcpkg on PATH)")
    return found


def vcpkg_env() -> dict[str, str]:
    env = dict(os.environ)
    archives = VCPKG_STATE / "archives"
    archives.mkdir(parents=True, exist_ok=True)
    env["VCPKG_BINARY_SOURCES"] = f"clear;files,{archives},readwrite"
    env["X_VCPKG_REGISTRIES_CACHE"] = str(VCPKG_STATE / "registries")
    (VCPKG_STATE / "registries").mkdir(parents=True, exist_ok=True)
    env["VCPKG_DOWNLOADS"] = str(VCPKG_STATE / "downloads")
    (VCPKG_STATE / "downloads").mkdir(parents=True, exist_ok=True)
    env["VCPKG_DISABLE_METRICS"] = "1"
    return env


def vcpkg_installed(triplet: str) -> Path:
    return VCPKG_STATE / f"installed-{triplet}"


def cmd_occt(args: argparse.Namespace) -> None:
    host = "x64-windows" if host_is_windows() else args.triplet
    run([vcpkg_exe(), "install",
         f"--triplet={args.triplet}", f"--host-triplet={host}",
         f"--x-install-root={vcpkg_installed(args.triplet)}",
         f"--x-buildtrees-root={VCPKG_STATE / 'buildtrees'}",
         f"--x-packages-root={VCPKG_STATE / 'packages'}",
         f"--overlay-triplets={ROOT / 'vcpkg' / 'triplets'}"],
        env=vcpkg_env(), cwd=ROOT)


# --------------------------------------------------------------------------- generate
def cmd_generate(_: argparse.Namespace) -> None:
    swig = swig_executable()
    env = swig_env()
    cxx_dir = GENERATED / "cxx"
    cs_root = GENERATED / "cs"
    if GENERATED.exists():
        shutil.rmtree(GENERATED)
    cxx_dir.mkdir(parents=True)
    for module in MODULES:
        cs_dir = cs_root / module
        cs_dir.mkdir(parents=True)
        run([swig, "-csharp", "-c++",
             "-namespace", f"OCC.Core.{module}",
             "-dllimport", "NetOccNative",
             "-I" + str(SWIG_FILES / "common"),
             "-I" + str(SWIG_FILES / "wrapper"),
             "-I" + str(SWIG_FILES),  # %include "extras/<Pkg>.i"
             "-outdir", cs_dir,
             "-o", cxx_dir / f"{module}_wrap.cxx",
             SWIG_FILES / "wrapper" / f"{module}.i"], env=env)


# --------------------------------------------------------------------------- native
def msvc_env(arch: str) -> dict[str, str]:
    """Environment of vcvarsall.bat for the newest Visual Studio with C++ tools."""
    vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / \
        "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    install = subprocess.run(
        [str(vswhere), "-latest", "-products", "*",
         "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
         "-property", "installationPath"],
        check=True, capture_output=True, text=True).stdout.strip()
    vcvars = Path(install) / "VC" / "Auxiliary" / "Build" / "vcvarsall.bat"
    out = subprocess.run(f'"{vcvars}" {arch} >nul && set', shell=True, check=True,
                         capture_output=True, text=True).stdout
    env = {}
    for line in out.splitlines():
        key, sep, value = line.partition("=")
        if sep:
            env[key] = value
    return env


def cmd_native(args: argparse.Namespace) -> None:
    rid, vc_arch = TRIPLETS[args.triplet]
    build_dir = BUILD / f"native-{args.triplet}"
    stage = ARTIFACTS / "runtimes" / rid / "native"
    env = msvc_env(vc_arch) if vc_arch else dict(os.environ)
    prefix = vcpkg_installed(args.triplet) / args.triplet
    configure = ["cmake", "-S", ROOT, "-B", build_dir, "-G", "Ninja",
                 "-DCMAKE_BUILD_TYPE=Release",
                 f"-DCMAKE_PREFIX_PATH={prefix}",
                 f"-DNETOCC_GENERATED_DIR={GENERATED / 'cxx'}",
                 f"-DNETOCC_RID={rid}",
                 f"-DNETOCC_OCCT_RUNTIME_DIR={prefix / ('bin' if host_is_windows() else 'lib')}",
                 f"-DCMAKE_INSTALL_PREFIX={stage}"]
    if sys.platform == "darwin":
        configure.append(f"-DCMAKE_OSX_DEPLOYMENT_TARGET={MACOS_DEPLOYMENT_TARGET}")
    run(configure, env=env)
    run(["cmake", "--build", build_dir, "--parallel"], env=env)
    if stage.exists():
        shutil.rmtree(stage)
    run(["cmake", "--install", build_dir], env=env)
    if not host_is_windows():
        flatten_stage(stage)
    write_notices(prefix, stage.parent / "THIRD-PARTY-NOTICES.txt")


def loader_name(library: Path) -> str | None:
    """The file name the dynamic loader asks for: the SONAME (Linux) or the install name (macOS)."""
    if sys.platform == "darwin":
        lines = subprocess.run(["otool", "-D", str(library)], check=True, capture_output=True, text=True).stdout.splitlines()
        return Path(lines[1].strip()).name if len(lines) > 1 else None
    out = subprocess.run(["readelf", "-d", str(library)], check=True, capture_output=True, text=True).stdout
    match = re.search(r"\(SONAME\)\s+Library soname: \[(.+?)\]", out)
    return match.group(1) if match else None


def flatten_stage(stage: Path) -> None:
    """One file per library, under the name the loader asks for.

    The install keeps the symlink chains (libTKernel.so -> .so.8.0 -> .so.8.0.1); a copy or a NuGet package
    stores every link as a full file (237 MB instead of 79 MB on macOS). Also drops pkgconfig/."""
    for entry in sorted(stage.iterdir()):
        if entry.is_symlink():
            entry.unlink()
        elif entry.is_dir():
            shutil.rmtree(entry)
    for library in sorted(stage.iterdir()):
        name = loader_name(library)
        if name and name != library.name:
            library.rename(stage / name)


def write_notices(prefix: Path, target: Path) -> None:
    """NetOcc's license, NOTICE and the license of every vcpkg port in the prefix, for the runtime package."""
    parts = [("NetOcc", (ROOT / "LICENSE").read_text(encoding="utf-8")),
             ("NetOcc NOTICE", (ROOT / "NOTICE").read_text(encoding="utf-8"))]
    for copyright_file in sorted((prefix / "share").glob("*/copyright")):
        if copyright_file.parent.name.startswith("vcpkg-"):
            continue  # vcpkg's build helpers, not shipped
        parts.append((copyright_file.parent.name, copyright_file.read_text(encoding="utf-8", errors="replace")))
    rule = "=" * 78
    target.write_text("".join(f"{rule}\n{name}\n{rule}\n\n{text.strip()}\n\n" for name, text in parts), encoding="utf-8")


# --------------------------------------------------------------------------- test
# test project frameworks; the .NET Framework ones build everywhere but run on Windows only
FRAMEWORKS = ["net10.0", "net8.0", "net6.0", "net48", "net472", "net462", "net35"]
MODERN_FRAMEWORKS = [f for f in FRAMEWORKS if f.startswith("net") and "." in f]
# library builds no test framework picks by itself (NUnit 4 needs 4.6.2): the net48 tests run against each
LEGACY_LIBRARY_BUILDS = ["net45", "net452"]
TESTS = ROOT / "test" / "NetOcc.Tests"


def clr2_installed() -> bool:
    """.NET Framework 3.5 (CLR 2), an optional Windows feature."""
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5") as key:
            return winreg.QueryValueEx(key, "Install")[0] == 1
    except OSError:
        return False


def run_net35(arch: str) -> None:
    """The net35 target: an NUnitLite exe on CLR 2, which VSTest can't host. Its exit code counts the failures."""
    if not clr2_installed():
        message = "net35 tests skipped: .NET Framework 3.5 (CLR 2) is not installed"
        print(f"::warning::{message}" if os.environ.get("GITHUB_ACTIONS") else f"warning: {message}", flush=True)
        return
    run(["dotnet", "build", TESTS / "NetOcc.Tests.csproj", "-c", "Release", "--framework", "net35", "--arch", arch])
    run([TESTS / "bin" / "Release" / "net35" / f"win-{arch}" / "NetOcc.Tests.exe", "--noresult"])


def cmd_test(args: argparse.Namespace) -> None:
    cmd = ["dotnet", "test", TESTS / "NetOcc.Tests.csproj", "-c", "Release", "--arch", args.arch]
    if args.framework == "net35":
        run_net35(args.arch)
    elif args.framework:
        run(cmd + ["--framework", args.framework])
    elif not host_is_windows():
        for framework in MODERN_FRAMEWORKS:
            run(cmd + ["--framework", framework])
    else:
        for build in LEGACY_LIBRARY_BUILDS:
            run(cmd + ["--framework", "net48", f"-p:NetOccTarget={build}"])
        run(cmd)  # last, so bin/ ends up with every framework's own library build; net35 builds but isn't a VSTest target
        if args.arch in ("x64", "x86"):
            run_net35(args.arch)


# --------------------------------------------------------------------------- pack
PACKAGE_TESTS = ROOT / "test" / "NetOcc.PackageTests"


def changelog_section(version: str | None) -> str | None:
    """The body of a version's section in CHANGELOG.md (None: [Unreleased]); None if there is no such section."""
    text = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    heading = re.escape(version) if version else "Unreleased"
    match = re.search(rf"^## \[{heading}\][^\n]*\n(.*?)(?=^## \[|^\[[^\]\n]+\]: |\Z)", text, re.S | re.M)
    return match.group(1).strip() if match else None


def cmd_changelog(args: argparse.Namespace) -> None:
    section = changelog_section(args.version)
    if section is None:
        sys.exit(f"CHANGELOG.md has no section [{args.version or 'Unreleased'}]")
    if args.output:
        Path(args.output).write_text(section + "\n", encoding="utf-8")
    else:
        sys.stdout.buffer.write((section + "\n").encode("utf-8"))  # UTF-8 on any console code page


def cmd_pack(args: argparse.Namespace) -> None:
    version = args.version or f"0.0.0-local.{datetime.now(timezone.utc):%Y%m%d%H%M%S}"
    rids = args.rids.split(",") if args.rids else [rid for rid, _ in TRIPLETS.values()]
    missing = [rid for rid in rids if not (ARTIFACTS / "runtimes" / rid / "native").is_dir()]
    if missing:
        sys.exit(f"natives missing for {', '.join(missing)}: run build.py native there (or pass --rids)")
    if not (GENERATED / "cs").is_dir():
        sys.exit("SWIG output missing: run build.py generate first")
    notes = changelog_section(version if "-local." not in version and "-ci." not in version else None)
    if notes is None:
        sys.exit(f"CHANGELOG.md has no section [{version}]: add it before packing a release")
    if PACKAGES.exists():
        shutil.rmtree(PACKAGES)
    PACKAGES.mkdir(parents=True)
    notes_file = BUILD / "release-notes.txt"
    notes_file.parent.mkdir(parents=True, exist_ok=True)
    notes_file.write_text(notes + "\n", encoding="utf-8")
    ci = ["-p:ContinuousIntegrationBuild=true"] if os.environ.get("GITHUB_ACTIONS") else []
    for rid in rids:
        run(["dotnet", "pack", ROOT / "pack" / "NetOcc.runtime.csproj", "-c", "Release", "-o", PACKAGES,
             f"-p:NetOccRid={rid}", f"-p:Version={version}", *ci])
    managed = [f"-p:Version={version}", f"-p:NetOccReleaseNotesFile={notes_file}", *ci]
    # the RID list as an environment variable: -p: splits values at ';' and ','
    env = dict(os.environ, NetOccRuntimeRids=";".join(rids))
    # sources from a config file: right after the packs, restore with two --source options took nuget.org for a local path
    config = BUILD / "pack-nuget.config"
    config.write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="{PACKAGES}" />
    <add key="nuget.org" value="{NUGET_ORG}" />
  </packageSources>
</configuration>
""", encoding="utf-8")
    run(["dotnet", "restore", ROOT / "src" / "NetOcc" / "NetOcc.csproj", "--configfile", config, *managed], env=env)
    run(["dotnet", "pack", ROOT / "src" / "NetOcc" / "NetOcc.csproj", "-c", "Release", "--no-restore", "-o", PACKAGES, *managed], env=env)
    # test/NetOcc.PackageTests picks this version up
    (PACKAGES / "NetOcc.version.props").write_text(
        f"<Project>\n  <PropertyGroup>\n    <NetOccPackageVersion>{version}</NetOccPackageVersion>\n  </PropertyGroup>\n</Project>\n",
        encoding="utf-8")
    print(f"NetOcc {version}: {', '.join(sorted(p.name for p in PACKAGES.glob('*.*nupkg')))}")


def cmd_test_package(args: argparse.Namespace) -> None:
    if not (PACKAGES / "NetOcc.version.props").exists():
        sys.exit("no packages: run build.py pack first")
    frameworks = ["net8.0", "net48"] if host_is_windows() else ["net8.0"]
    for framework in frameworks:
        run(["dotnet", "test", PACKAGE_TESTS / "NetOcc.PackageTests.csproj", "-c", "Release", "--arch", args.arch,
             "--framework", framework])


def cmd_all(args: argparse.Namespace) -> None:
    cmd_tools(args)
    cmd_occt(args)
    cmd_generate(args)
    cmd_native(args)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("tools").set_defaults(fn=cmd_tools)
    for name, fn in (("occt", cmd_occt), ("native", cmd_native), ("all", cmd_all)):
        sp = sub.add_parser(name)
        sp.add_argument("--triplet", required=True, choices=sorted(TRIPLETS))
        sp.set_defaults(fn=fn)
    sub.add_parser("generate").set_defaults(fn=cmd_generate)
    t = sub.add_parser("test")
    t.add_argument("--arch", default="x64", choices=["x64", "x86", "arm64"])
    t.add_argument("--framework", choices=FRAMEWORKS, help="default: all (Windows), the .NET ones elsewhere")
    t.set_defaults(fn=cmd_test)
    pk = sub.add_parser("pack")
    pk.add_argument("--version", help="package version; default 0.0.0-local.<utc timestamp>")
    pk.add_argument("--rids", help="comma-separated; default all four (natives from artifacts/runtimes/<rid>)")
    pk.set_defaults(fn=cmd_pack)
    tp = sub.add_parser("test-package")
    tp.add_argument("--arch", default="x64", choices=["x64", "x86", "arm64"])
    tp.set_defaults(fn=cmd_test_package)
    c = sub.add_parser("changelog")
    c.add_argument("--version", help="default: [Unreleased]")
    c.add_argument("--output", help="write to this file instead of stdout")
    c.set_defaults(fn=cmd_changelog)
    args = p.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
