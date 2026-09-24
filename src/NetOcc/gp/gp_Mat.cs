// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace OCC.Core.gp;

/// <summary>OCCT <c>gp_Mat</c>: 3x3 matrix <c>double myMat[3][3]</c>, row-major, same layout as C++.</summary>
[StructLayout(LayoutKind.Explicit, Size = 72)]
public struct gp_Mat
{
    [FieldOffset(0)] private double m11;
    [FieldOffset(8)] private double m12;
    [FieldOffset(16)] private double m13;
    [FieldOffset(24)] private double m21;
    [FieldOffset(32)] private double m22;
    [FieldOffset(40)] private double m23;
    [FieldOffset(48)] private double m31;
    [FieldOffset(56)] private double m32;
    [FieldOffset(64)] private double m33;

    /// <summary>1-based element access, like OCCT.</summary>
    public readonly double Value(int theRow, int theCol) => theRow switch
    {
        1 => theCol switch { 1 => m11, 2 => m12, 3 => m13, _ => OutOfRange() },
        2 => theCol switch { 1 => m21, 2 => m22, 3 => m23, _ => OutOfRange() },
        3 => theCol switch { 1 => m31, 2 => m32, 3 => m33, _ => OutOfRange() },
        _ => OutOfRange(),
    };

    // no tuple patterns: the net462 build has no System.ValueTuple
    private static double OutOfRange() => throw new OcctException("Standard_OutOfRange", "gp_Mat::Value() - index is out of range");
}
