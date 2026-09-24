// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Ax2</c>: right-handed coordinate system, same layout as C++.
/// Constructors run in OCCT (X direction computed or validated).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 96)]
public struct gp_Ax2
{
    [FieldOffset(0)] private gp_Ax1 axis;
    [FieldOffset(48)] private gp_Dir vydir;
    [FieldOffset(72)] private gp_Dir vxdir;

    /// <summary>Origin, main direction Z, X direction X, like OCCT.</summary>
    public gp_Ax2() => gpModule.NetOcc_gp_Ax2_Init(ref this);

    public gp_Ax2(in gp_Pnt theP, in gp_Dir theN) => gpModule.NetOcc_gp_Ax2_InitPN(ref this, in theP, in theN);

    public gp_Ax2(in gp_Pnt theP, in gp_Dir theN, in gp_Dir theVx) => gpModule.NetOcc_gp_Ax2_InitPNV(ref this, in theP, in theN, in theVx);

    public readonly gp_Ax1 Axis() => axis;
    public readonly gp_Pnt Location() => axis.Location();
    public readonly gp_Dir Direction() => axis.Direction();
    public readonly gp_Dir XDirection() => vxdir;
    public readonly gp_Dir YDirection() => vydir;
    public void SetLocation(in gp_Pnt theP) => axis.SetLocation(theP);
}
