// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Dir</c>: a unit vector, same layout as C++. Constructors run in OCCT
/// (normalization, <c>Standard_ConstructionError</c> for a null vector).
/// <c>default(gp_Dir)</c> is all-zero and invalid; use a constructor.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct gp_Dir : IEquatable<gp_Dir>
{
    [FieldOffset(0)] private gp_XYZ coord;

    /// <summary>Z direction (0, 0, 1), like OCCT.</summary>
    public gp_Dir() => gpModule.NetOcc_gp_Dir_Init(ref this);

    public gp_Dir(double theXv, double theYv, double theZv) => gpModule.NetOcc_gp_Dir_InitXYZ(ref this, theXv, theYv, theZv);

    public gp_Dir(in gp_Vec theV) => gpModule.NetOcc_gp_Dir_InitVec(ref this, in theV);

    // already unit length: no normalization, no native call
    private gp_Dir(in gp_XYZ theUnitCoord, bool _) => coord = theUnitCoord;

    public readonly double X() => coord.X();
    public readonly double Y() => coord.Y();
    public readonly double Z() => coord.Z();
    public readonly void Coord(ref double theXv, ref double theYv, ref double theZv) => coord.Coord(ref theXv, ref theYv, ref theZv);
    public readonly double Coord(int theIndex) => coord.Coord(theIndex);
    public readonly gp_XYZ XYZ() => coord;

    public readonly double Dot(in gp_Dir theOther) => coord.Dot(theOther.coord);
    public readonly gp_Dir Reversed() => new(coord.Reversed(), true);

    public readonly gp_Dir Crossed(in gp_Dir theRight) => gpModule.NetOcc_gp_Dir_Crossed(in this, in theRight);
    public readonly double Angle(in gp_Dir theOther) => gpModule.NetOcc_gp_Dir_Angle(in this, in theOther);
    public readonly bool IsParallel(in gp_Dir theOther, double theAngularTolerance) => gpModule.NetOcc_gp_Dir_IsParallel(in this, in theOther, theAngularTolerance);
    public readonly gp_Dir Transformed(in gp_Trsf theT) => gpModule.NetOcc_gp_Dir_Transformed(in this, in theT);

    public readonly bool Equals(gp_Dir other) => coord.Equals(other.coord);
    public override readonly bool Equals(object obj) => obj is gp_Dir other && Equals(other);
    public override readonly int GetHashCode() => coord.GetHashCode();
    public override readonly string ToString() => coord.ToString();
}
