// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Emit;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>The member rules both writers share.</summary>
[TestFixture]
public class MemberRulesTests
{
    [Test]
    public void HasConstTwin_CountsATwinReturningTheValue()
    {
        // Arrange (Image_ColorRGB::r() returns uint8_t&, r() const the value; the lists are the overloads' own)
        var mutableParameters = new List<ParameterModel>();
        var constParameters = new List<ParameterModel>();
        CppType reference = Ref(Builtin("unsigned char"));

        // Act
        var covered = MemberRules.HasConstTwin(reference, mutableParameters,
            [(reference, mutableParameters), (Builtin("unsigned char"), constParameters)]);

        // Assert
        Assert.That(covered, Is.True);
    }

    [Test]
    public void Declarable_LogsTheCallsBelowAnAmbiguousOne()
    {
        // Arrange (Fit(a, b) takes two arguments too, so Fit(a, b = 1, c = 2) declares all three: its call with one is lost)
        List<ParameterModel> fit = [new("theA", Builtin("int"), null), new("theB", Builtin("int"), "1"), new("theC", Builtin("int"), "2")];
        List<ParameterModel> pair = [new("theA", Builtin("int"), null), new("theB", Builtin("int"), null)];
        List<string> skipped = [];

        // Act
        var declared = MemberRules.Declarable(new(), "Demo_Fit::Fit", fit, [fit, pair], [fit, pair], null, null, skipped);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(declared, Has.Count.EqualTo(3));
            Assert.That(declared, Has.All.Matches<ParameterModel>(p => p.Default is null));
            Assert.That(skipped, Is.EqualTo(new[] { "Demo_Fit::Fit: its calls with 1 argument are left out: C++ takes them, but not the one with 2, "
                + "and a declaration's defaults run to its end" }));
        }
    }
}
