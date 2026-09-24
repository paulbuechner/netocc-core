// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Emit;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>What a package's classes become, as its config picks them.</summary>
[TestFixture]
public class ClassRulesTests
{
    [Test]
    public void IsListed_PicksTheInstancesAPackageOwns()
    {
        // Arrange (classes lists the package's own classes; an instance it owns serves every package's signatures)
        var config = new PackageConfig { Classes = ["Demo_Driver"] };
        var traits = new ClassTraits(IsTransient: false, IsAbstract: false, HasPublicDestructor: true, IsCopyable: true);
        var instance = new ClassModel("Demo_Tree_float", "Demo_Tree.hxx", [], traits, [], [],
            Instance: new ClassInstance("Demo_Tree<float>", ["Demo_Tree.hxx"], Template("Demo_Tree", Builtin("float"))));

        // Act
        var listed = ClassRules.IsListed(instance, config);

        // Assert
        Assert.That(listed, Is.True);
    }
}
