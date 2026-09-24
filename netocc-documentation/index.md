---
_layout: landing
---

# NetOcc

[Open CASCADE Technology](https://dev.opencascade.org) (OCCT) 8.0.1 for .NET: generated C# bindings with OCCT's names unchanged, one namespace per OCCT package (`OCC.Core.gp`, `OCC.Core.BRepPrimAPI`). One AnyCPU package for .NET Framework 3.5 and 4.5+, .NET 6, 8 and 10, with natives for win-x64, win-x86, linux-x64 and osx-arm64.

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

## Covered

All 362 packages of OCCT's FoundationClasses, ModelingData, ModelingAlgorithms, ApplicationFramework and DataExchange modules, and of Visualization's TKService, TKV3d, TKOpenGl (the driver) and TKMeshVS: geometry, topology, booleans, fillets, offsets, sweeps, shape healing, meshing, HLR, OCAF and XCAF documents, STEP, IGES, STL, glTF, OBJ, PLY and VRML, and 3D views.

## Where to go

- [Getting started](articles/getting-started.md): shapes, STEP files with colors, meshes, errors.
- [From C++ to C#](articles/mapping.md): how OCCT's types and signatures look in C#.
- [Object lifetimes](articles/lifetimes.md): handles, `Dispose`, what stays alive with what.
- [3D views](articles/visualization.md): an OCCT view in a WPF or Avalonia window.
- [Class index](classes/index.md): every type, linked to its page in OCCT's reference manual. C# keeps OCCT's names, so the manual applies as it is.
