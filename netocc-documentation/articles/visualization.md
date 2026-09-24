# 3D views

OCCT draws into a native window through its OpenGL driver. The window comes from the UI framework: an HWND on Windows, an X11 window on Linux, an NSView on macOS. `Aspect_Window.FromNativeHandle` wraps it for the view.

```csharp
using OCC.Core.AIS;
using OCC.Core.Aspect;
using OCC.Core.OpenGl;
using OCC.Core.V3d;

var display = new Aspect_DisplayConnection();
var driver = new OpenGl_GraphicDriver(display);
var viewer = new V3d_Viewer(driver);
viewer.SetDefaultLights();
viewer.SetLightOn();
var context = new AIS_InteractiveContext(viewer);

var view = viewer.CreateView();
view.SetWindow(Aspect_Window.FromNativeHandle(hwnd, display));
view.MustBeResized();

context.Display(new AIS_Shape(shape), (int)AIS_DisplayMode.AIS_Shaded, 0, true);
view.FitAll();
```

On a resize, call `view.MustBeResized()`; on a paint, `view.Redraw()`.

## Mouse input

`AIS_ViewController` turns mouse input into rotation, panning, zoom and selection. Positions are in the window's pixels; buttons and modifier keys are OCCT's bit masks, which C# repeats since OCCT declares them in anonymous enums (`Aspect_VKeyMouse_LeftButton = 1 << 13`, middle `1 << 14`, right `1 << 15`; `Aspect_VKeyFlags_SHIFT = 1 << 8`, control `1 << 9`, alt `1 << 10`):

```csharp
var controller = new AIS_ViewController();

// on a mouse event
using var point = new BVH_Vec2i(x, y);
if (controller.UpdateMousePosition(point, buttons, modifiers, false))
{
    controller.FlushViewEvents(context, view, true);   // applies the input and redraws
}

// on the wheel
using var delta = new Aspect_ScrollDelta(point, notches);
if (controller.UpdateZoom(delta))
{
    controller.FlushViewEvents(context, view, true);
}
```

## Hosting the window

The view needs a child window of its own, which also receives the mouse:

- **Windows:** register a window class with `CS_OWNDC` (OpenGL needs the device context) and a window procedure that feeds size, paint and mouse messages to the view.
- **WPF:** an `HwndHost` creates that window in `BuildWindowCore`.
- **Avalonia:** a `NativeControlHost` creates it in `CreateNativeControlCore`, once the window shows.

The [demos](https://github.com/paulbuechner/netocc/tree/main/netocc-demos) do all of this in a WPF and an Avalonia 12 viewer that open STEP files: `NetOcc.Viewer` holds the view, the window and the STEP loading both share.
