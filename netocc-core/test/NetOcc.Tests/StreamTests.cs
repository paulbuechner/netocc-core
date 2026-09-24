// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BinTools;
using OCC.Core.BRep;
using OCC.Core.BRepPrimAPI;
using OCC.Core.BRepTools;
using OCC.Core.gp;
using OCC.Core.TopoDS;

namespace NetOcc.Tests;

/// <summary>C++ streams are C# streams, lent for the call: OCCT writes into and reads from memory.</summary>
[TestFixture]
public class StreamTests
{
    [Test]
    public void BRepTools_RoundTripsAShape()
    {
        // Arrange
        var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
        using var stream = new MemoryStream();

        // Act
        BRepTools.Write(box, stream);
        stream.Position = 0;
        var read = new TopoDS_Shape();
        BRepTools.Read(read, stream, new BRep_Builder());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stream.Length, Is.GreaterThan(0));
            Assert.That(Shapes.Volume(read), Is.EqualTo(6.0).Within(1e-9));
        }
    }

    [Test]
    public void BinTools_RoundTripsAShape()
    {
        // Arrange (a binary format)
        var cylinder = new BRepPrimAPI_MakeCylinder(1, 2).Shape();
        using var stream = new MemoryStream();

        // Act
        BinTools.Write(cylinder, stream);
        stream.Position = 0;
        var read = new TopoDS_Shape();
        BinTools.Read(read, stream);

        // Assert
        Assert.That(Shapes.Volume(read), Is.EqualTo(Shapes.Volume(cylinder)).Within(1e-9));
    }

    [Test]
    public void Read_LeavesTheStreamWhereOcctStopped()
    {
        // Arrange (two shapes, one after the other)
        using var stream = new MemoryStream();
        BRepTools.Write(new BRepPrimAPI_MakeBox(1, 1, 1).Shape(), stream);
        BRepTools.Write(new BRepPrimAPI_MakeBox(1, 1, 2).Shape(), stream);
        stream.Position = 0;
        var first = new TopoDS_Shape();
        var second = new TopoDS_Shape();

        // Act
        BRepTools.Read(first, stream, new BRep_Builder());
        BRepTools.Read(second, stream, new BRep_Builder());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Shapes.Volume(first), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(Shapes.Volume(second), Is.EqualTo(2.0).Within(1e-9), "the second read starts after the first shape");
        }
    }

    [Test]
    public void DumpJson_WritesText()
    {
        // Arrange
        var point = new gp_Pnt(1.5, 2, 3);
        using var stream = new MemoryStream();

        // Act
        point.DumpJson(stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());

        // Assert
        Assert.That(text, Does.Contain("gp_Pnt").And.Contain("1.5"));
    }

    [Test]
    public void Write_ToAReadOnlyStream_Throws()
    {
        // Arrange
        var box = new BRepPrimAPI_MakeBox(1, 2, 3).Shape();
        using var stream = new MemoryStream(new byte[16], writable: false);

        // Act
        void Write() => BRepTools.Write(box, stream);

        // Assert
        Assert.That(Write, Throws.TypeOf<ArgumentException>());
    }
}
