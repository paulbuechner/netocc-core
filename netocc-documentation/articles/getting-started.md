# Getting started

## Install

```bash
dotnet add package NetOcc
```

The package brings the natives of every platform. Windows needs the Visual C++ 2015-2022 runtime, Linux glibc 2.35 or newer, macOS 14 or newer on Apple silicon. See [Platforms](platforms.md).

## Shapes

Namespaces are OCCT's packages, classes keep their names:

```csharp
using OCC.Core.BRepAlgoAPI;
using OCC.Core.BRepFilletAPI;
using OCC.Core.BRepPrimAPI;
using OCC.Core.gp;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;
using OCC.Core.TopTools;

var box = new BRepPrimAPI_MakeBox(60, 40, 20).Shape();
var hole = new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(30, 20, -1), new gp_Dir(0, 0, 1)), 8, 22).Shape();
var part = new BRepAlgoAPI_Cut(box, hole).Shape();

// round the edges: each once, from an indexed map
var edges = new TopTools_IndexedMapOfShape();
TopExp.MapShapes(part, TopAbs_ShapeEnum.TopAbs_EDGE, edges);
var fillet = new BRepFilletAPI_MakeFillet(part);
foreach (var edge in edges)
{
    fillet.Add(1.5, TopoDS.Edge(edge));
}

var rounded = fillet.Shape();
```

`foreach` works on OCCT's collections and on its range-for iterators:

```csharp
foreach (var face in new TopExp_Explorer(rounded, TopAbs_ShapeEnum.TopAbs_FACE))
{
    // face is a TopoDS_Shape; TopoDS.Face(face) gives the TopoDS_Face
}
```

## STEP files

Shapes only:

```csharp
using OCC.Core.IFSelect;
using OCC.Core.STEPControl;

var writer = new STEPControl_Writer();
writer.Transfer(rounded, STEPControl_StepModelType.STEPControl_AsIs);
writer.Write("part.step");

var reader = new STEPControl_Reader();
if (reader.ReadFile("part.step") == IFSelect_ReturnStatus.IFSelect_RetDone)
{
    reader.TransferRoots();
    var shape = reader.OneShape();
}
```

With colors, names and assemblies, through an XCAF document:

```csharp
using OCC.Core.IFSelect;
using OCC.Core.Quantity;
using OCC.Core.STEPCAFControl;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.XCAFApp;
using OCC.Core.XCAFDoc;

var application = XCAFApp_Application.GetApplication();
TDocStd_Document? document = null;
application.NewDocument("MDTV-XCAF", ref document);
var reader = new STEPCAFControl_Reader();
reader.SetColorMode(true);
reader.SetNameMode(true);
if (reader.ReadFile("assembly.step") == IFSelect_ReturnStatus.IFSelect_RetDone && reader.Transfer(document))
{
    using var roots = new TDF_LabelSequence();
    XCAFDoc_DocumentTool.ShapeTool(document!.Main()).GetFreeShapes(roots);
    foreach (var root in roots)
    {
        var shape = XCAFDoc_ShapeTool.GetShape(root);
        var color = new Quantity_Color();
        var colored = XCAFDoc_ColorTool.GetColor(root, XCAFDoc_ColorType.XCAFDoc_ColorSurf, ref color);
    }
}

application.Close(document);
```

Paths are UTF-8, so non-ASCII file names work on every platform.

## Meshes

```csharp
using OCC.Core.BRepMesh;
using OCC.Core.StlAPI;

new BRepMesh_IncrementalMesh(rounded, 0.1);   // triangulates the faces, 0.1 linear deflection
new StlAPI_Writer().Write(rounded, "part.stl");
```

## Errors

OCCT's exceptions arrive as `OcctException`. `Is<T>()` tells the OCCT class, subclasses included:

```csharp
using OCC.Core;
using OCC.Core.Standard;

try
{
    var tooSmall = new BRepPrimAPI_MakeBox(0, 10, 10).Shape();
}
catch (OcctException e) when (e.Is<Standard_DomainError>())
{
    Console.WriteLine($"{e.OcctType}: {e.OcctMessage}");
}
```
