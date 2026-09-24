# netocc

Monorepo of NetOcc, the SWIG-generated C# bindings for OCCT 8.0.1. netocc-core and netocc-generator have their own `CLAUDE.md`; read it before working there. The documentation and the demos are below.

| Folder | Content |
|---|---|
| `netocc-core/` | typemap library, generated `.i` files, native shims, the `NetOcc` assembly, tests, NuGet packs (`build.py`) |
| `netocc-generator/` | `netocc-gen`: reads OCCT's headers, writes `netocc-core/src/SWIG_files` and the value-type structs |
| `netocc-documentation/` | DocFX site for GitHub Pages |
| `netocc-demos/` | WPF and Avalonia 12 viewers on the packed NetOcc |

- **Folders refer to each other by relative paths** (`--core ../netocc-core`), so each builds on its own; netocc-core builds without the generator.
- **Workflows** (`.github/workflows/`): every push to main runs all of them; pull requests are filtered to each folder's paths. No path filters on pushes: main's one commit is amended and force-pushed as a new root commit, which gives GitHub no diff, and it silently skips path-filtered workflows then (the first two monorepo pushes started nothing).
  - `build.yml`: netocc-core on four RIDs, pack, package tests, the demos on the packages (Windows); also on changes to `netocc-demos/`. A `v*` tag (tags are never path-filtered) publishes. Keep the file name: nuget.org's Trusted Publishing policy names it.
  - `generator.yml`: netocc-gen's unit and golden tests on three OSes.
  - `docs.yml`: builds the documentation site (warnings as errors); pushes to main deploy it to GitHub Pages (Settings > Pages > Source: GitHub Actions, which the user sets).
- **Git:** `main` is a single signed commit on `origin` (github.com/paulbuechner/netocc, renamed from netocc-core). Amend it with `git commit --amend --no-edit` at validated checkpoints; the user publishes with `git push --force-with-lease origin main`. Other sessions (the Mac) work on branches, see `netocc-core/plan/macos-arm64-validation.md`.
- **License:** MIT. The root `LICENSE` has copies in `netocc-core/` (`build.py` writes it into the package notices) and `netocc-generator/`. No OCCT doc text in committed files; never copy code or lists from other OCCT bindings (GPL, LGPL).
- **Releasing:** `/release <version>` (`.claude/skills/release/SKILL.md`). A `v<version>` tag releases the NetOcc package (nuget.org and a GitHub release, notes from `netocc-core/CHANGELOG.md`); other builds pack `0.0.0-ci.<run>`, local packs `0.0.0-local.<time>`. netocc-gen and the demos aren't published: the tag marks their state too, so they get no tags of their own.
- **Logs** go into each folder's gitignored `log/`.

## netocc-documentation

```bash
cd netocc-documentation
dotnet tool restore                 # docfx, pinned in dotnet-tools.json
python classes.py                   # classes/*.md from netocc-core's modules.json and classes.json (gitignored)
dotnet docfx docfx.json --serve     # _site/, http://localhost:8080
```

- **Content:** `index.md`, `articles/` (getting started, C++ to C#, lifetimes, 3D views, platforms, building) and the class index, which links every type to OCCT's 8.0.1 reference manual. Pages there are Doxygen's (`classgp___pnt.html`: a capital as `_` and its lower letter, `_` as `__`, `::` as `_1_1`); `classes.py` builds them from what netocc-gen records.
- **No generated API reference:** NetOcc has no doc comments (no OCCT doc text), so the manual is the reference and the site explains the mapping.
- **Snippets compile:** check an article's C# against the packed NetOcc before committing it (a scratch console project on `artifacts/packages`).

## netocc-demos

```bash
cd netocc-core && python build.py pack --rids win-x64,win-x86     # the packages, and NetOcc.version.props the demos import
dotnet test netocc-demos/NetOcc.Demos.slnx -c Release             # both viewers build; the viewer library's tests run
netocc-demos/src/NetOcc.Viewer.Wpf/bin/Release/net10.0-windows/NetOcc.Viewer.Wpf.exe --sample   # or a .step path
```

- **Layout:** `src/NetOcc.Viewer` (net10.0) holds what both share: `OcctViewer` (driver, viewer, context, `AIS_ViewController`), `StepDocument` (XCAF, STEPCAFControl), `SampleModel` (a bolted plate written as STEP), `ViewerWindow`. `NetOcc.Viewer.Wpf` hosts the window in an `HwndHost`, `NetOcc.Viewer.Avalonia` (12.1.3) in a `NativeControlHost`. `test/NetOcc.Viewer.Tests` covers the parts without a window.
- **Packages:** `Directory.Build.props` imports netocc-core's `artifacts/packages/NetOcc.version.props`; `nuget.config` maps `NetOcc*` to that folder, the rest to nuget.org. Without a pack, the build stops with a message.
- **The view's window** (`ViewerWindow`) is a Win32 child of our own class: `CS_OWNDC` (OpenGL needs its DC), and its procedure feeds size, paint and mouse messages to the viewer (capture while a button is down, wheel positions from screen coordinates). The host disposes the viewer before destroying the window.
- **Mouse masks:** OCCT's `Aspect_VKeyMouse`/`Aspect_VKeyFlags` are anonymous enums, which C# doesn't get: `ViewerButtons` and `ViewerModifiers` repeat their values.
- **Avalonia** creates the native control after the window shows, and again after the control moves: `ViewerHost.ViewerCreated` fires after that layout pass, and the command line's file opens then. Windows only: Linux and macOS would need an X11 window or NSView child.
- **Never drive the viewers** (no clicks, keystrokes or screenshots): launch them with `--sample` or a file and ask the user to look.
