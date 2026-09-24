// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// static usings
using static NetOcc.Viewer.NativeMethods;

namespace NetOcc.Viewer;

/// <summary>
/// A Win32 child window that shows an <see cref="OcctViewer"/>: its class owns a device context, which OpenGL needs, and its
/// messages drive the viewer (size, paint, mouse). WPF (<c>HwndHost</c>) and Avalonia (<c>NativeControlHost</c>) host it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ViewerWindow : IDisposable
{
    private const string ClassName = "NetOccViewerWindow";

    // native code calls the procedure: a static delegate the GC never collects
    private static readonly WindowProcedure Procedure = Receive;
    private static readonly Dictionary<IntPtr, ViewerWindow> Windows = [];
    private static bool _registered;

    private readonly OcctViewer _viewer;

    private ViewerWindow(IntPtr handle, OcctViewer viewer)
    {
        Handle = handle;
        _viewer = viewer;
    }

    public IntPtr Handle { get; private set; }

    /// <summary>A child window of <paramref name="parent"/> showing <paramref name="viewer"/>.</summary>
    public static ViewerWindow Create(IntPtr parent, OcctViewer viewer)
    {
        var instance = GetModuleHandleW(null);
        if (!_registered)
        {
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = CS_OWNDC | CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
                hInstance = instance,
                hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW),
                lpszClassName = ClassName,
            };
            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new Win32Exception();
            }

            _registered = true;
        }

        var handle = CreateWindowExW(0, ClassName, "", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN, 0, 0, 1, 1, parent, IntPtr.Zero,
            instance, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception();
        }

        var window = new ViewerWindow(handle, viewer);
        Windows[handle] = window;
        viewer.Attach(handle);
        return window;
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
    }

    private static IntPtr Receive(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        // messages during CreateWindowEx come before the window is known
        if (!Windows.TryGetValue(handle, out var window))
        {
            return DefWindowProcW(handle, message, wParam, lParam);
        }

        switch (message)
        {
            case WM_SIZE:
                window._viewer.Resize();
                return IntPtr.Zero;
            case WM_PAINT:
                BeginPaint(handle, out var paint);
                window._viewer.Redraw();
                EndPaint(handle, ref paint);
                return IntPtr.Zero;
            case WM_ERASEBKGND:
                // OpenGL paints every pixel
                return (IntPtr)1;
            case WM_MOUSEMOVE:
                window._viewer.MouseMove(X(lParam), Y(lParam), Buttons(wParam), Modifiers(wParam));
                return IntPtr.Zero;
            case WM_LBUTTONDOWN or WM_MBUTTONDOWN or WM_RBUTTONDOWN:
                SetFocus(handle);
                SetCapture(handle);
                window._viewer.MouseButtons(X(lParam), Y(lParam), Buttons(wParam), Modifiers(wParam));
                return IntPtr.Zero;
            case WM_LBUTTONUP or WM_MBUTTONUP or WM_RBUTTONUP:
                if (Buttons(wParam) == ViewerButtons.None)
                {
                    ReleaseCapture();
                }

                window._viewer.MouseButtons(X(lParam), Y(lParam), Buttons(wParam), Modifiers(wParam));
                return IntPtr.Zero;
            case WM_MOUSEWHEEL:
                // in screen coordinates
                var point = new POINT { X = X(lParam), Y = Y(lParam) };
                ScreenToClient(handle, ref point);
                window._viewer.MouseWheel(point.X, point.Y, (short)((long)wParam >> 16) / (double)WHEEL_DELTA);
                return IntPtr.Zero;
            case WM_DESTROY:
                Windows.Remove(handle);
                return IntPtr.Zero;
            default:
                return DefWindowProcW(handle, message, wParam, lParam);
        }
    }

    // the signed coordinates in lParam's words
    private static int X(IntPtr lParam) => (short)((long)lParam & 0xFFFF);

    private static int Y(IntPtr lParam) => (short)(((long)lParam >> 16) & 0xFFFF);

    private static ViewerButtons Buttons(IntPtr wParam)
    {
        var keys = (uint)(long)wParam;
        return ((keys & MK_LBUTTON) != 0 ? ViewerButtons.Left : 0) | ((keys & MK_MBUTTON) != 0 ? ViewerButtons.Middle : 0)
            | ((keys & MK_RBUTTON) != 0 ? ViewerButtons.Right : 0);
    }

    private static ViewerModifiers Modifiers(IntPtr wParam)
    {
        var keys = (uint)(long)wParam;
        return ((keys & MK_SHIFT) != 0 ? ViewerModifiers.Shift : 0) | ((keys & MK_CONTROL) != 0 ? ViewerModifiers.Control : 0)
            | (GetKeyState(VK_MENU) < 0 ? ViewerModifiers.Alt : 0);
    }
}
