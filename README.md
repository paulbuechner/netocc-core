# NetOcc

[Open CASCADE Technology](https://dev.opencascade.org) (OCCT) 8.0.1 for .NET: SWIG-generated C# bindings, laid out like [pythonocc](https://github.com/tpaviot/pythonocc): one namespace per OCCT package (`OCC.Core.<Package>`), OCCT's names unchanged. One AnyCPU package for .NET Framework 3.5 and 4.5+, .NET 6, .NET 8 and .NET 10, with natives for win-x64, win-x86, linux-x64 and osx-arm64.

```bash
dotnet add package NetOcc
```

```csharp
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;

var box = new BRepPrimAPI_MakeBox(10, 20, 30).Shape();
var cylinder = new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(5, 5, 0), new gp_Dir(0, 0, 1)), 3, 40).Shape();
var fused = new BRepAlgoAPI_Fuse(box, cylinder).Shape();
```

## Projects

| Folder | Content |
|---|---|
| [netocc-core](netocc-core) | typemap library, generated SWIG interfaces, native shims, the `NetOcc` assembly, tests, NuGet packages |
| [netocc-generator](netocc-generator) | `netocc-gen`: reads OCCT's headers and writes netocc-core's interface files |
| [netocc-documentation](netocc-documentation) | the documentation site (GitHub Pages) |
| [netocc-demos](netocc-demos) | WPF and Avalonia viewers that load STEP files |

Documentation: [paulbuechner.github.io/netocc](https://paulbuechner.github.io/netocc/). Build and layout: [netocc-core/README.md](netocc-core/README.md). Changes per release: [netocc-core/CHANGELOG.md](netocc-core/CHANGELOG.md).

## Demos

A WPF and an Avalonia 12 viewer that open STEP files: [netocc-demos](netocc-demos/README.md).

## License

MIT, see [LICENSE](LICENSE). OCCT is LGPL-2.1 with the Open CASCADE exception, see [netocc-core/NOTICE](netocc-core/NOTICE).
