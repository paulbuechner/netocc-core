// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace OCC.Core.gp;

/// <summary>OCCT <c>gp_Ax1</c>: location + direction, same layout as C++.</summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct gp_Ax1
{
    [FieldOffset(0)] private gp_Pnt loc;
    [FieldOffset(24)] private gp_Dir vdir;

    /// <summary>Origin, Z direction, like OCCT.</summary>
    public gp_Ax1() => gpModule.NetOcc_gp_Ax1_Init(ref this);

    public gp_Ax1(in gp_Pnt theP, in gp_Dir theV)
    {
        loc = theP;
        vdir = theV;
    }

    public readonly gp_Pnt Location() => loc;
    public readonly gp_Dir Direction() => vdir;
    public void SetLocation(in gp_Pnt theP) => loc = theP;
    public void SetDirection(in gp_Dir theV) => vdir = theV;
    public readonly gp_Ax1 Reversed() => new(loc, vdir.Reversed());
}
