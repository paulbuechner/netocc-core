# From C++ to C#

NetOcc is generated from OCCT's headers: what a signature looks like in C++ decides what it looks like in C#. The [class index](../classes/index.md) links every type to OCCT's reference manual, which then applies as it is.

## Names

| C++ | C# |
|---|---|
| package `gp`, class `gp_Pnt` | namespace `OCC.Core.gp`, `gp_Pnt` |
| nested `Geom_Curve::ResD1` | `Geom_Curve_ResD1`: scopes joined by `_` |
| namespace functions `TopoDS::Face(shape)` | a static class: `TopoDS.Face(shape)` |
| `GeomAbs_CurveType` enum | a C# enum with OCCT's values and underlying type |
| `typedef NCollection_Array1<gp_Pnt> TColgp_Array1OfPnt` | class `TColgp_Array1OfPnt` |

## Handles

`Handle(T)` is `T`: the proxy holds one reference on the object. A cast goes through `DownCast`, since a C# cast never reaches the C++ object:

```csharp
Geom_Curve curve = new Geom_Circle(new gp_Ax2(), 10);
var circle = Geom_Circle.DownCast(curve);   // null if it isn't one
```

## Value types

All of `gp` (points, vectors, directions, axes, transformations, lines, circles, planes), `Bnd_Box`, `Quantity_Color` and a few more are C# structs with OCCT's memory layout. Their methods run OCCT's code, and OCCT's operators are C# operators:

```csharp
var p = new gp_Pnt(1, 2, 3);
var moved = p.Translated(new gp_Vec(10, 0, 0));
var sum = new gp_Vec(1, 0, 0) + new gp_Vec(0, 1, 0);
```

Plain data (OCCT 8's evaluation results) is a struct with public fields: `curve.EvalD1(u).Point`.

## Parameters

| C++ | C# |
|---|---|
| `T&` of a number, enum or struct | `ref T` (never `out`: OCCT may read it first) |
| `const T&` of a struct | `in T` |
| `T&` or `const T&` of a class | the proxy |
| `Handle(T)&` | `ref T`: the variable gets the object OCCT puts there |
| `T*` of numbers or structs | a C# array, pinned for the call (`double[]`, `gp_Pnt[]`) |
| `T*` of a class | the proxy, `null` for `nullptr` |
| `void*`, function pointers, pointers OCCT keeps | `IntPtr` |
| `int (&)[3]` | an array of exactly three |
| `Message_ProgressRange` at the end, defaulted | left out |

A member returning `T&` of a number, enum or struct is a C# `ref` return, read and written in place:

```csharp
var matrix = new math_Matrix(1, 2, 1, 2, 0.0);
matrix.Value(1, 2) = 5.0;
```

A member returning a class by reference gives a proxy that borrows the object and keeps its owner alive. A member returning its own object for chaining returns `void`.

## Collections

OCCT's arrays, lists, sequences and maps are C# classes named by OCCT's aliases. Indexers use OCCT's bounds (sequences from 1), and they are `IEnumerable<T>`:

```csharp
var points = new TColgp_Array1OfPnt([new gp_Pnt(0, 0, 0), new gp_Pnt(10, 0, 0), new gp_Pnt(10, 10, 0)]);
var spline = new GeomAPI_PointsToBSpline(points).Curve();
gp_Pnt[] back = points.ToArray();
```

The handle-managed variants (`TColgp_HArray1OfPnt`) derive from their collection. Maps have indexers by key or number and enumerate keys or `KeyValuePair`s. A bad index throws `OcctException` (`Standard_OutOfRange`).

## Strings and the standard library

| C++ | C# |
|---|---|
| `const char*`, `TCollection_AsciiString`, `std::string`, `std::string_view` | `string` (UTF-8) |
| `const char16_t*`, `TCollection_ExtendedString` | `string` (UTF-16) |
| `Standard_GUID` | `System.Guid` |
| `std::array<double, 3>` | `double[]` |
| `std::optional<double>` | `double?` |
| `std::pair<A, B>` | a class with `First` and `Second` |
| `std::bitset<N>` | `ulong` |
| `std::complex<double>` | `OCC.Core.Complex` |
| `char` | `byte` (its bits, not a character) |
| `char32_t` | `uint` (a code point) |
| `size_t` | `ulong`; on x86 a larger value throws `OcctException` |

## Streams

`std::ostream&` and `std::istream&` are a `System.IO.Stream`, lent for the call. OCCT writes into a native memory stream whose bytes are copied to the C# stream afterwards; readers read the stream's remaining bytes in place and leave a seekable stream where they stopped:

```csharp
using var file = File.Create("shape.brep");
BRepTools.Write(shape, file);
```

## Errors

OCCT's `Standard_Failure` and its subclasses arrive as `OcctException`, with `OcctType`, `OcctMessage` and `Is<T>()`. OCCT's exception classes are ordinary classes too (`new Standard_OutOfRange("bad index")`).

## Deprecated and left out

What OCCT marks deprecated is wrapped and `[Obsolete]`. What can't map is left out, each member with its reason in the generator's skip logs: members OCCT declares but never exports, raw pointers into structs, streams OCCT would keep beyond a call.
