// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Tests.Types;

namespace NetOcc.Generator.Tests;

/// <summary>C++ spellings and declarations of model types, as the .i writes them.</summary>
[TestFixture]
public class CppTypeTests
{
    private static readonly FunctionType Callback = new(Builtin("double"), [Builtin("double"), Pointer(Builtin("void"))]);

    [Test]
    public void Spelling_Pointers_StayTogether()
    {
        // Arrange
        var pointerToPointer = Pointer(Pointer(Const(Builtin("int"))));
        var constPointer = new PointerType(Class("TopoDS_Shape"), Const: true);
        var referenceToConstPointer = Ref(constPointer);

        // Act
        string[] spellings = [pointerToPointer.Spelling, constPointer.Spelling, referenceToConstPointer.Spelling];

        // Assert
        Assert.That(spellings, Is.EqualTo(new[] { "const int**", "TopoDS_Shape* const", "TopoDS_Shape* const&" }));
    }

    [Test]
    public void Spelling_FunctionPointer_IsAnAbstractDeclarator()
    {
        // Act
        var spelling = Pointer(Callback).Spelling;

        // Assert
        Assert.That(spelling, Is.EqualTo("double (*)(double, void*)"));
    }

    [Test]
    public void Declare_FunctionPointer_NamesInsideTheParentheses()
    {
        // Act
        var constant = new PointerType(Callback, Const: true).Declare("theF");
        var variadic = Pointer(new FunctionType(Builtin("int"), [], IsVariadic: true)).Spelling;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(constant, Is.EqualTo("double (* const theF)(double, void*)"));
            Assert.That(variadic, Is.EqualTo("int (*)(...)"));
        }
    }

    [Test]
    public void Declare_Arrays_PutTheLengthsAfterTheName()
    {
        // Arrange
        var matrix = new ArrayType(new ArrayType(Const(Builtin("double")), 3), 3);
        var unknownLength = new ArrayType(Const(Builtin("double")), 0);

        // Act
        string[] declarations = [matrix.Spelling, matrix.Declare("theJ"), unknownLength.Declare("theCoeffs")];

        // Assert
        Assert.That(declarations, Is.EqualTo(new[] { "const double[3][3]", "const double theJ[3][3]", "const double theCoeffs[]" }));
    }

    [Test]
    public void Spelling_PointerToWidthTypedef_IsAsWritten()
    {
        // Arrange (int64_t is long long on Windows, long on Linux: only the typedef names both)
        var pointer = Pointer(new BuiltinType("long long", Const: true, Written: "int64_t"));

        // Act
        var spelling = pointer.Spelling;

        // Assert
        Assert.That(spelling, Is.EqualTo("const int64_t*"));
    }

    [Test]
    public void Declare_ReferenceToPointer_KeepsTheSymbolsTogether()
    {
        // Act
        var declaration = Ref(Pointer(Const(Builtin("char")))).Declare("theText");

        // Assert
        Assert.That(declaration, Is.EqualTo("const char*& theText"));
    }
}
