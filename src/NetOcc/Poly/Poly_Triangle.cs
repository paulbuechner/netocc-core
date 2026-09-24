// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace OCC.Core.Poly;

/// <summary>OCCT <c>Poly_Triangle</c>: three 1-based node indices, same layout as C++.</summary>
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct Poly_Triangle(int theN1, int theN2, int theN3) : IEquatable<Poly_Triangle>
{
    [FieldOffset(0)] private int n1 = theN1;
    [FieldOffset(4)] private int n2 = theN2;
    [FieldOffset(8)] private int n3 = theN3;

    public void Set(int theN1, int theN2, int theN3)
    {
        n1 = theN1;
        n2 = theN2;
        n3 = theN3;
    }

    public readonly void Get(ref int theN1, ref int theN2, ref int theN3)
    {
        theN1 = n1;
        theN2 = n2;
        theN3 = n3;
    }

    /// <summary>1-based, like OCCT.</summary>
    public readonly int Value(int theIndex) => theIndex switch
    {
        1 => n1,
        2 => n2,
        3 => n3,
        _ => throw new OcctException("Standard_OutOfRange", "Poly_Triangle::Value() - index is out of range"),
    };

    public readonly bool Equals(Poly_Triangle other) => n1 == other.n1 && n2 == other.n2 && n3 == other.n3;
    public override readonly bool Equals(object obj) => obj is Poly_Triangle other && Equals(other);
    public override readonly int GetHashCode() => unchecked((n1 * 397 ^ n2) * 397 ^ n3);
    public override readonly string ToString() => $"({n1}, {n2}, {n3})";
}
