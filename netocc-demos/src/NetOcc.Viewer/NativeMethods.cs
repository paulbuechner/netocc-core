// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace NetOcc.Viewer;

// the user32 calls ViewerWindow needs
internal static class NativeMethods
{
    public const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000;
    public const uint CS_VREDRAW = 0x0001, CS_HREDRAW = 0x0002, CS_OWNDC = 0x0020;
    public const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_MOUSEMOVE = 0x0200,
        WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207,
        WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A, WM_CAPTURECHANGED = 0x0215;
    public const uint MK_LBUTTON = 0x0001, MK_RBUTTON = 0x0002, MK_SHIFT = 0x0004, MK_CONTROL = 0x0008, MK_MBUTTON = 0x0010;
    public const int VK_MENU = 0x12, IDC_ARROW = 32512, WHEEL_DELTA = 120;

    public delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public int left, top, right, bottom;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr window, out PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr window, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr window, ref POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? module);
}
