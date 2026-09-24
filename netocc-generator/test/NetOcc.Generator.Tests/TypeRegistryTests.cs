// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>Collection instantiations: an OCCT alias names one, or a name of its own in the package of its first user.</summary>
[TestFixture]
public class TypeRegistryTests
{
    [Test]
    public void Instantiation_WithAnAlias_IsTheAliases()
    {
        // Arrange
        var registry = Registry();

        // Act
        var instantiation = registry.Instantiation(Template("NCollection_Array1", Builtin("double")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(instantiation?.Alias, Is.EqualTo("TColStd_Array1OfReal"));
            Assert.That(instantiation?.IsSynthesized, Is.False);
        }
    }

    [TestCase("NCollection_Array1", "gp_Pnt", "NCollection_Array1_gp_Pnt")]
    [TestCase("NCollection_List", "Handle(Geom_Surface)", "NCollection_List_Handle_Geom_Surface")]
    [TestCase("NCollection_Sequence", "TColStd_Array1OfReal", "NCollection_Sequence_TColStd_Array1OfReal")]
    public void Instantiation_WithoutAlias_IsNamedAfterItsArguments(string template, string element, string name)
    {
        // Arrange
        var registry = Registry();
        CppType argument = element switch
        {
            "Handle(Geom_Surface)" => Handle("Geom_Surface"),
            "TColStd_Array1OfReal" => Template("NCollection_Array1", Builtin("double")),
            _ => Class(element),
        };

        // Act
        var instantiation = registry.Instantiation(Template(template, argument));

        // Assert
        Assert.That(instantiation?.Alias, Is.EqualTo(name));
    }

    [Test]
    public void Instantiation_OfAMapWithTheDefaultHasher_LeavesTheHasherOut()
    {
        // Arrange
        var registry = Registry();
        var map = Template("NCollection_Map", Class("gp_Pnt"), Template("NCollection_DefaultHasher", Class("gp_Pnt")));

        // Act
        var instantiation = registry.Instantiation(map);

        // Assert
        Assert.That(instantiation?.Alias, Is.EqualTo("NCollection_Map_gp_Pnt"));
    }

    [Test]
    public void AssignOwners_GivesTheFirstUserTheInstantiationAndItsBase()
    {
        // Arrange (a handle-managed array of points, used by two packages; its base array too)
        var registry = Registry();
        var handleManaged = registry.Instantiation(Template("NCollection_HArray1", Class("gp_Pnt")))!;

        // Act
        registry.AssignOwners([("GeomAPI", [handleManaged]), ("BRepFill", [handleManaged])]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Instantiation(Template("NCollection_HArray1", Class("gp_Pnt")))?.Package, Is.EqualTo("GeomAPI"));
            Assert.That(registry.BaseOf(handleManaged)?.Package, Is.EqualTo("GeomAPI"), "its base goes with it");
        }
    }
}
