# Object lifetimes

A proxy owns its native object (or, for a transient, one reference on it) and releases it in `Dispose` or its finalizer. The garbage collector doesn't see native memory, so dispose large or scarce objects when you're done: views, drivers, meshes, documents.

```csharp
using var mesh = new BRepMesh_IncrementalMesh(shape, 0.1);
```

## What stays alive with what

C++ keeps raw references the garbage collector doesn't see. NetOcc keeps the C# side of each alive as long as the native object may use it, so no `GC.KeepAlive` is needed:

| The native object refers to | Kept alive by |
|---|---|
| its owner, for a reference a member returned (`block.ChangeShapes()`) | the returned proxy |
| a constructor's argument (`new BRepGraph_FaceIterator(graph)`, a boolean's shared `BOPAlgo_PaveFiller`) | the new proxy |
| a member's argument (`extrema.Initialize(surface, ...)`) | the proxy, until the member's next call replaces it |
| the object it came from (`editor.Mut(vertex)` returns a guard into the graph) | the returned proxy |

What a constructor keeps, and the object behind a returned guard, also outlive the finalizer: finalizers run in any order, and a destructor may still use them. A guard dropped without `Dispose` is safe, though its item stays locked until the collector finalizes it; dispose guards with `using`:

```csharp
using (var guard = editor.Mut(vertex))
{
    editor.SetPoint(guard, new gp_Pnt(1, 2, 3));
}
```

## OCAF documents

- Labels, attributes and `TNaming_Builder` keep their document's data alive, so a proxy never points into a freed tree.
- Close documents through the application (`application.Close(document)`): OCCT keeps a raw pointer to the document that only `Close` clears.
- Undo needs `document.SetUndoLimit(n)` with `n > 0`; with 0, `OpenCommand` and `AbortCommand` do nothing.
- `XCAFApp_Application.GetApplication()` is one per process.

## Threads

Finalizers run on the collector's thread. Objects with a GL context (views, drivers) belong to the thread that made them: dispose them there.
