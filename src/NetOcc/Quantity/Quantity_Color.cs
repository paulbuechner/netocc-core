// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace OCC.Core.Quantity;

/// <summary>
/// OCCT <c>Quantity_Color</c>: linear RGB as three floats, same layout as C++. Constructors run
/// in OCCT (color-space conversion, <c>Standard_OutOfRange</c> for components outside [0, 1]).
/// The parameterless constructor is OCCT's default color, yellow; <c>default(Quantity_Color)</c> is black.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct Quantity_Color : IEquatable<Quantity_Color>
{
    [FieldOffset(0)] private float r;
    [FieldOffset(4)] private float g;
    [FieldOffset(8)] private float b;

    /// <summary>Yellow, like OCCT.</summary>
    public Quantity_Color() => QuantityModule.NetOcc_Quantity_Color_Init(ref this);

    public Quantity_Color(double theC1, double theC2, double theC3, Quantity_TypeOfColor theType) =>
        QuantityModule.NetOcc_Quantity_Color_InitValues(ref this, theC1, theC2, theC3, theType);

    /// <summary>Linear red component.</summary>
    public readonly double Red() => r;

    /// <summary>Linear green component.</summary>
    public readonly double Green() => g;

    /// <summary>Linear blue component.</summary>
    public readonly double Blue() => b;

    /// <summary>The components in the color space <paramref name="theType"/> (conversion runs in OCCT).</summary>
    public readonly void Values(ref double theC1, ref double theC2, ref double theC3, Quantity_TypeOfColor theType) =>
        QuantityModule.NetOcc_Quantity_Color_Values(in this, ref theC1, ref theC2, ref theC3, theType);

    public readonly double SquareDistance(in Quantity_Color theColor)
    {
        double dr = (double)r - theColor.r, dg = (double)g - theColor.g, db = (double)b - theColor.b;
        return dr * dr + dg * dg + db * db;
    }

    public readonly double Distance(in Quantity_Color theColor) => Math.Sqrt(SquareDistance(theColor));

    /// <summary>OCCT's tolerant comparison (distance within the global <c>Quantity_Color::Epsilon()</c>); <see cref="Equals(Quantity_Color)"/> is exact.</summary>
    public readonly bool IsEqual(in Quantity_Color theOther) => QuantityModule.NetOcc_Quantity_Color_IsEqual(in this, in theOther);

    public readonly bool Equals(Quantity_Color other) => r == other.r && g == other.g && b == other.b;
    public override readonly bool Equals(object obj) => obj is Quantity_Color other && Equals(other);
    public override readonly int GetHashCode() => unchecked((r.GetHashCode() * 397 ^ g.GetHashCode()) * 397 ^ b.GetHashCode());
    public override readonly string ToString() => $"({r}, {g}, {b})";
}
