// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

// NUnit
using NUnit.Framework;

//
using OCC.Core.gp;
using OCC.Core.Poly;
using OCC.Core.Quantity;

// static usings
using static OCC.Core.Quantity.Quantity_TypeOfColor;

namespace NetOcc.Tests;

/// <summary>C# struct layouts and semantics must match the C++ value types on every RID.</summary>
[TestFixture]
public class ValueTypeTests
{
    // Marshal.SizeOf(Type): the generic overload needs .NET Framework 4.5.1
    private static readonly Dictionary<string, SizePair> Sizes = new()
    {
        ["gp_XYZ"] = new(gpModule.NetOcc_SizeOf_gp_XYZ, () => Marshal.SizeOf(typeof(gp_XYZ))),
        ["gp_Pnt"] = new(gpModule.NetOcc_SizeOf_gp_Pnt, () => Marshal.SizeOf(typeof(gp_Pnt))),
        ["gp_Dir"] = new(gpModule.NetOcc_SizeOf_gp_Dir, () => Marshal.SizeOf(typeof(gp_Dir))),
        ["gp_Vec"] = new(gpModule.NetOcc_SizeOf_gp_Vec, () => Marshal.SizeOf(typeof(gp_Vec))),
        ["gp_Ax1"] = new(gpModule.NetOcc_SizeOf_gp_Ax1, () => Marshal.SizeOf(typeof(gp_Ax1))),
        ["gp_Ax2"] = new(gpModule.NetOcc_SizeOf_gp_Ax2, () => Marshal.SizeOf(typeof(gp_Ax2))),
        ["gp_Mat"] = new(gpModule.NetOcc_SizeOf_gp_Mat, () => Marshal.SizeOf(typeof(gp_Mat))),
        ["gp_Trsf"] = new(gpModule.NetOcc_SizeOf_gp_Trsf, () => Marshal.SizeOf(typeof(gp_Trsf))),
        ["Poly_Triangle"] = new(PolyModule.NetOcc_SizeOf_Poly_Triangle, () => Marshal.SizeOf(typeof(Poly_Triangle))),
        ["Quantity_Color"] = new(QuantityModule.NetOcc_SizeOf_Quantity_Color, () => Marshal.SizeOf(typeof(Quantity_Color))),
    };

    private static IEnumerable<string> ValueTypes() => Sizes.Keys;

    [TestCaseSource(nameof(ValueTypes))]
    public void Size_MatchesNative(string type)
    {
        // Arrange
        var sizes = Sizes[type];

        // Act
        var nativeSize = sizes.Native();
        var managedSize = sizes.Managed();

        // Assert
        Assert.That(managedSize, Is.EqualTo(nativeSize));
    }

    [Test]
    public void Ax2_DefaultComesFromOcct()
    {
        // Arrange
        var z = new gp_Dir(0, 0, 1);
        var x = new gp_Dir(1, 0, 0);
        var y = new gp_Dir(0, 1, 0);

        // Act
        var ax2 = new gp_Ax2();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ax2.Direction(), Is.EqualTo(z));
            Assert.That(ax2.XDirection(), Is.EqualTo(x));
            Assert.That(ax2.YDirection(), Is.EqualTo(y));
        }
    }

    [Test]
    public void Trsf_DefaultIsIdentity()
    {
        // Arrange
        // (no inputs: the parameterless constructor calls OCCT)

        // Act
        var identity = new gp_Trsf();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.Form(), Is.EqualTo(gp_TrsfForm.gp_Identity));
            Assert.That(identity.ScaleFactor(), Is.EqualTo(1.0));
        }
    }

    [Test]
    public void Trsf_TranslationFieldsAreReadAtTheirOffsets()
    {
        // Arrange
        var trsf = new gp_Trsf();

        // Act
        trsf.SetTranslation(new gp_Vec(1, 2, 3)); // native setter fills the struct
        var moved = new gp_Pnt(1, 1, 1).Transformed(trsf);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(trsf.Form(), Is.EqualTo(gp_TrsfForm.gp_Translation));
            Assert.That(trsf.TranslationPart(), Is.EqualTo(new gp_XYZ(1, 2, 3)));
            Assert.That(moved, Is.EqualTo(new gp_Pnt(2, 3, 4)));
        }
    }

    [Test]
    public void Trsf_RotationMatrixMatchesNativeValue()
    {
        // Arrange
        var rotation = new gp_Trsf();
        rotation.SetRotation(new gp_Ax1(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)), Math.PI / 2);
        var cells = (from row in Enumerable.Range(1, 3) from col in Enumerable.Range(1, 3) select new { Row = row, Col = col }).ToList();

        // Act
        var native = cells.Select(cell => rotation.Value(cell.Row, cell.Col)).ToArray();
        var managed = cells.Select(cell => rotation.HVectorialPart().Value(cell.Row, cell.Col) * rotation.ScaleFactor()).ToArray();
        var rotated = new gp_Pnt(1, 0, 0).Transformed(rotation);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rotation.Form(), Is.EqualTo(gp_TrsfForm.gp_Rotation));
            Assert.That(managed, Is.EqualTo(native).Within(1e-12));
            Assert.That(rotated.X(), Is.EqualTo(0.0).Within(1e-12));
            Assert.That(rotated.Y(), Is.EqualTo(1.0).Within(1e-12));
        }
    }

    [Test]
    public void Distance_ManagedMatchesNative()
    {
        // Arrange
        var random = new Random(42);
        var a = new gp_Pnt[1000];
        var b = new gp_Pnt[1000];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = new gp_Pnt(random.NextDouble() * 1e3, random.NextDouble() * -1e3, random.NextDouble());
            b[i] = new gp_Pnt(random.NextDouble(), random.NextDouble() * 1e-3, random.NextDouble() * 1e6);
        }

        // Act
        var managed = Enumerable.Range(0, a.Length).Select(i => a[i].Distance(b[i])).ToArray();
        var native = Enumerable.Range(0, a.Length).Select(i => gpModule.NetOcc_gp_Pnt_Distance(in a[i], in b[i])).ToArray();

        // Assert
        Assert.That(managed, Is.EqualTo(native).Within(1e-9));
    }

    [Test]
    public void Coord_WritesRefParameters()
    {
        // Arrange
        var point = new gp_Pnt(1, 2, 3);
        double x = 0, y = 0, z = 0;

        // Act
        point.Coord(ref x, ref y, ref z);

        // Assert
        Assert.That(new[] { x, y, z }, Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));
    }

    [Test]
    public void Transform_MutatesTheStructInPlace()
    {
        // Arrange
        var point = new gp_Pnt(1, 0, 0);
        var trsf = new gp_Trsf();
        trsf.SetTranslation(new gp_Vec(0, 0, 5));

        // Act
        point.Transform(trsf); // native thunk writes through ref this

        // Assert
        Assert.That(point, Is.EqualTo(new gp_Pnt(1, 0, 5)));
    }

    [Test]
    public void PolyTriangle_GetWritesRefParameters()
    {
        // Arrange
        var triangle = new Poly_Triangle(4, 5, 6);
        int n1 = 0, n2 = 0, n3 = 0;

        // Act
        triangle.Get(ref n1, ref n2, ref n3);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new[] { n1, n2, n3 }, Is.EqualTo(new[] { 4, 5, 6 }));
            Assert.That(triangle.Value(2), Is.EqualTo(5));
        }
    }

    [Test]
    public void QuantityColor_DefaultComesFromOcct()
    {
        // Arrange
        // (no inputs: the parameterless constructor calls OCCT)

        // Act
        var color = new Quantity_Color();

        // Assert (OCCT's default color is yellow)
        Assert.That(new[] { color.Red(), color.Green(), color.Blue() }, Is.EqualTo(new[] { 1.0, 1.0, 0.0 }));
    }

    [Test]
    public void QuantityColor_StoresSrgbAsLinearRgb()
    {
        // Arrange
        double c1 = 0, c2 = 0, c3 = 0;

        // Act
        var color = new Quantity_Color(0.5, 0.5, 0.5, Quantity_TOC_sRGB);
        color.Values(ref c1, ref c2, ref c3, Quantity_TOC_sRGB);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.Red(), Is.EqualTo(0.2140).Within(1e-4), "sRGB 0.5 in linear light");
            Assert.That(new[] { c1, c2, c3 }, Is.All.EqualTo(0.5).Within(1e-5));
        }
    }

    [Test]
    public void QuantityColor_IsEqualIsTolerantWhileEqualsIsExact()
    {
        // Arrange
        var color = new Quantity_Color(0.25, 0.5, 0.75, Quantity_TOC_RGB);

        // Act
        var nearby = new Quantity_Color(0.25 + 1e-6, 0.5, 0.75, Quantity_TOC_RGB);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.IsEqual(nearby), Is.True, "within OCCT's Quantity_Color::Epsilon()");
            Assert.That(color.Equals(nearby), Is.False);
        }
    }

    private sealed class SizePair(Func<int> native, Func<int> managed)
    {
        public Func<int> Native { get; } = native;

        public Func<int> Managed { get; } = managed;
    }
}
