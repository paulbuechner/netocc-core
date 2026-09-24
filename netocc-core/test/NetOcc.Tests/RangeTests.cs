// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRepGraph;
using OCC.Core.BRepPrimAPI;
using OCC.Core.TopExp;
using OCC.Core.TopoDS;

// static usings
using static OCC.Core.TopAbs.TopAbs_ShapeEnum;

namespace NetOcc.Tests;

/// <summary>OCCT 8's range-for iterators (begin/end over More/Next/Value) are IEnumerable: foreach advances the iterator itself.</summary>
[TestFixture]
public class RangeTests
{
    [Test]
    public void Explorer_EnumeratesTheFacesOfABox()
    {
        // Arrange
        var explorer = new TopExp_Explorer(Shapes.Box(), TopAbs_FACE);
        List<TopoDS_Shape> faces = [];

        // Act
        foreach (var face in explorer)
        {
            faces.Add(face);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(faces, Has.Count.EqualTo(6));
            Assert.That(faces.All(f => f.ShapeType() == TopAbs_FACE), Is.True);
            Assert.That(explorer.More(), Is.False, "foreach advanced the explorer to its end");
        }
    }

    [Test]
    public void Iterator_EnumeratesTheSubShapes()
    {
        // Arrange (a box is a solid of one shell)
        var solid = Shapes.Box();

        // Act
        var children = new TopoDS_Iterator(solid).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0].ShapeType(), Is.EqualTo(TopAbs_SHELL));
        }
    }

    [Test]
    public void GraphIterator_EnumeratesTheDefinitions()
    {
        // Arrange
        var graph = new BRepGraph();
        graph.Shapes().Add(new BRepPrimAPI_MakeBox(10, 20, 30).Shape());

        // Act (the iterator refers to the graph, which C# keeps alive: C++ references don't reach the GC)
        var faces = new BRepGraph_FaceIterator(graph).Count();
        GC.KeepAlive(graph);

        // Assert
        Assert.That(faces, Is.EqualTo(6));
    }
}
