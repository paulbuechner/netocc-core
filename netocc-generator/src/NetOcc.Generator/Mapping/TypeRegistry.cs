// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

/// <summary>How netocc-core wraps a class (see its CLAUDE.md, "Wrapper contract").</summary>
internal enum WrapKind
{
    /// <summary>Standard_Transient subclass: %occt_transient, Handle(T) ⇄ proxy.</summary>
    Transient,

    /// <summary>Copyable class: %occt_valueclass, const&amp; returns are owned copies.</summary>
    ValueClass,

    /// <summary>C# struct with the C++ layout (%occt_valuetype), hand-written in netocc-core.</summary>
    ValueType,

    /// <summary>Other classes (not copyable, or abstract): plain proxies.</summary>
    Plain,
}

/// <param name="Header">The header that declares the class; unknown for scanned modules, where the file name is the class name.</param>
/// <param name="HasDefaultConstructor"><c>T()</c> compiles: arrays default-construct their elements.</param>
/// <param name="IsMoveOnly">Not copyable, but movable: a by-value return moves into the proxy (SwigValueWrapper).</param>
internal sealed record KnownClass(string Name, string Package, WrapKind Kind, string? Header = null, bool HasDefaultConstructor = true,
    bool IsMoveOnly = false)
{
    public string DeclaringHeader => Header ?? $"{Name}.hxx";

    /// <summary>What a declaration using the class needs: its module and header.</summary>
    public TypeUse Use => new(Package, DeclaringHeader);
}

/// <param name="Header">The header that declares the enum; unknown for scanned modules, where the file name is the enum name.</param>
/// <param name="Underlying">The underlying type when it isn't int (unsigned char), otherwise null.</param>
/// <param name="QualifiedName">C++'s name of a nested or namespace enum, whose flat name is an alias.</param>
internal sealed record KnownEnum(string Name, string Package, string? Header = null, string? Underlying = null, string? QualifiedName = null)
{
    public string DeclaringHeader => Header ?? $"{Name}.hxx";

    /// <summary>What a declaration using the enum needs: its module and header.</summary>
    public TypeUse Use => new(Package, DeclaringHeader);
}

/// <summary>What an argument of a collection template is: an element C# sees (value, key or item), or a hasher, which only C++ does.</summary>
internal enum CollectionArgument
{
    Element,
    Hasher,
}

/// <summary>
/// A collection template the typemap library declares (netocc-core's Collections.i; math_VectorBase in its math companion),
/// with the macro that instantiates it: <c>%macro(NAME, each argument's C++ type, each element's C# type)</c>.
/// </summary>
/// <param name="Base">For a handle-managed template (NCollection_HArray1), the collection it derives from.</param>
/// <param name="DefaultConstructs">The wrapped constructors default-construct elements (the arrays).</param>
/// <param name="HasBlittableVariant">A <c>_blittable</c> macro adds bulk copies when every element is blittable.</param>
/// <param name="Include">The header that declares the template, when it isn't <c>&lt;Name&gt;.hxx</c>.</param>
internal sealed record CollectionTemplate(string Name, string Macro, IReadOnlyList<CollectionArgument> Arguments, string? Base = null,
    bool DefaultConstructs = false, bool HasBlittableVariant = false, string? Include = null)
{
    /// <summary>The header that declares the template.</summary>
    public string Header => Include ?? $"{Name}.hxx";

    private static readonly CollectionArgument[] Elements = [CollectionArgument.Element];
    private static readonly CollectionArgument[] Keys = [CollectionArgument.Element, CollectionArgument.Hasher];
    private static readonly CollectionArgument[] Items = [CollectionArgument.Element, CollectionArgument.Element, CollectionArgument.Hasher];
    private static readonly CollectionArgument[] Pair = [CollectionArgument.Element, CollectionArgument.Element];

    public static readonly IReadOnlyList<CollectionTemplate> All =
    [
        new("NCollection_Array1", "occt_array1", Elements, DefaultConstructs: true, HasBlittableVariant: true),
        new("NCollection_Array2", "occt_array2", Elements, DefaultConstructs: true),
        new("NCollection_List", "occt_list", Elements),
        new("NCollection_Sequence", "occt_sequence", Elements),
        new("NCollection_LinearVector", "occt_linearvector", Elements, HasBlittableVariant: true),
        new("NCollection_DynamicArray", "occt_dynamicarray", Elements),
        new("NCollection_HArray1", "occt_harray1", Elements, "NCollection_Array1", DefaultConstructs: true),
        new("NCollection_HArray2", "occt_harray2", Elements, "NCollection_Array2", DefaultConstructs: true),
        new("NCollection_HSequence", "occt_hsequence", Elements, "NCollection_Sequence"),
        new("NCollection_Map", "occt_map", Keys),
        new("NCollection_IndexedMap", "occt_indexedmap", Keys),
        new("NCollection_DataMap", "occt_datamap", Items),
        new("NCollection_FlatMap", "occt_flatmap", Keys),
        new("NCollection_FlatDataMap", "occt_flatdatamap", Items),
        new("NCollection_IndexedDataMap", "occt_indexeddatamap", Items),
        new("math_VectorBase", "occt_math_vector", Elements, DefaultConstructs: true),
        // two values of any element type, as First and Second (netocc-core's Std.i)
        new("std::pair", "netocc_pair", Pair, Include: "utility"),
    ];

    public bool IsTransient => Base is not null;

    public static CollectionTemplate? Named(string name) => All.FirstOrDefault(t => t.Name == name);
}

/// <summary>
/// A collection instantiation, named by OCCT's alias for it (<c>typedef NCollection_Array1&lt;gp_Pnt&gt; TColgp_Array1OfPnt</c>):
/// the C# class in the alias's package. The first alias in package order names it. One no alias names gets a name of its
/// own (<c>NCollection_Array1_BRepGraph_NodeId</c>) in the package of its first user. Equal by spelling.
/// </summary>
/// <param name="Arguments">The template arguments as the .i spells them: <c>gp_Pnt</c>, <c>opencascade::handle&lt;Geom_Curve&gt;</c>, hashers.</param>
/// <param name="ArgumentTypes">The parsed arguments; unknown for scanned modules, which this run doesn't write.</param>
/// <param name="IsSynthesized">No alias names it: <see cref="Alias"/> is made up from the template and its arguments.</param>
internal sealed record KnownInstantiation(CollectionTemplate Template, IReadOnlyList<string> Arguments, string Alias, string Package,
    IReadOnlyList<CppType>? ArgumentTypes = null, bool IsSynthesized = false)
{
    /// <summary>The instantiation as the .i spells it, e.g. <c>NCollection_Array1&lt;gp_Pnt&gt;</c>.</summary>
    public string Spelling => Spell(Template.Name, Arguments);

    /// <summary>The template's header, which declares the instantiation.</summary>
    public string Header => Template.Header;

    public bool Equals(KnownInstantiation? other) => other is not null && Spelling == other.Spelling;

    public override int GetHashCode() => Spelling.GetHashCode(StringComparison.Ordinal);

    public static string Spell(string template, IEnumerable<string> arguments) => $"{template}<{string.Join(", ", arguments)}>";

    /// <summary>A template argument as spellings compare: without a top-level const.</summary>
    public static CppType Unconst(CppType argument) => argument is NamedType n ? n with { Const = false } : argument;
}

/// <summary>
/// Every type the generated code may use, and where it lives. Filled from the packages being generated and
/// from netocc-core's hand-written .i files (the packages not generated yet).
/// </summary>
internal sealed partial class TypeRegistry
{
    /// <summary>Types the common typemap library maps to .NET types (Strings.i, Guid.i): no proxy, no import.</summary>
    public static readonly IReadOnlySet<string> TypemappedClasses = new HashSet<string>
    {
        "TCollection_AsciiString",
        "TCollection_ExtendedString",
        "Standard_GUID",
    };

    private readonly Dictionary<string, KnownClass> _classes = [];
    private readonly Dictionary<string, KnownEnum> _enums = [];
    private readonly Dictionary<string, KnownInstantiation> _instantiations = [];
    private readonly Dictionary<string, string> _instances = [];
    private readonly HashSet<string> _requested = [];
    private readonly Dictionary<string, List<string>> _imports = [];

    public void AddClass(KnownClass known) => _classes[known.Name] = known;

    public void AddEnum(string name, string package, string? header = null, string? underlying = null, string? qualifiedName = null) =>
        _enums[name] = new KnownEnum(name, package, header, underlying, qualifiedName);

    /// <summary>Registers a class template instance as the class of that name (<see cref="ClassInstance"/>).</summary>
    public void AddInstance(NamedType type, string name) => _instances[(type with { Const = false }).Spelling] = name;

    /// <summary>
    /// A signature type with every class template instance that is a class of its own (<see cref="AddInstance"/>) named by
    /// that class, whose C++ alias the generated code spells: SWIG takes the alias and the template for different types.
    /// </summary>
    public CppType Resolve(CppType type) => type switch
    {
        NamedType { TemplateArguments.Count: > 0 } n when _instances.TryGetValue((n with { Const = false }).Spelling, out var name) =>
            new NamedType(name, n.Kind, [], n.Const),
        NamedType { TemplateArguments.Count: > 0 } n => n with { TemplateArguments = [.. n.TemplateArguments.Select(Resolve)] },
        PointerType p => p with { Pointee = Resolve(p.Pointee) },
        ReferenceType r => r with { Referee = Resolve(r.Referee) },
        ArrayType a => a with { Element = Resolve(a.Element) },
        FunctionType f => f with { Return = Resolve(f.Return), Parameters = [.. f.Parameters.Select(Resolve)] },
        _ => type,
    };

    /// <summary>Registers an alias; the first one for an instantiation names it.</summary>
    public void AddInstantiation(KnownInstantiation instantiation) => _instantiations.TryAdd(instantiation.Spelling, instantiation);

    /// <summary>The package of a synthesized instantiation until <see cref="AssignOwners"/> gives it its first user's.</summary>
    public const string Unowned = "";

    public KnownClass? Class(string name) => _classes.GetValueOrDefault(name);

    public KnownEnum? Enum(string name) => _enums.GetValueOrDefault(name);

    /// <summary>
    /// The instantiation of a collection template, requested or not: the registered one, or, when no alias names it, one with
    /// a name of its own, unowned until the first writer pass (<see cref="AssignOwners"/>).
    /// </summary>
    public KnownInstantiation? Instantiation(NamedType type)
    {
        if (CollectionTemplate.Named(type.Name) is not { } template || type.TemplateArguments.Count != template.Arguments.Count)
        {
            return null;
        }

        List<CppType> arguments = [.. type.TemplateArguments.Select(a => Resolve(KnownInstantiation.Unconst(a)))];
        var spelling = KnownInstantiation.Spell(template.Name, arguments.Select(a => a.Spelling));
        if (_instantiations.TryGetValue(spelling, out var known))
        {
            return known;
        }

        var synthesized = new KnownInstantiation(template, [.. arguments.Select(a => a.Spelling)], FreeName(SynthesizedName(template, arguments)), Unowned,
            arguments, IsSynthesized: true);
        _instantiations[spelling] = synthesized;
        return synthesized;
    }

    /// <summary>The collection a handle-managed instantiation derives from: its alias's, or one with a name of its own.</summary>
    public KnownInstantiation? BaseOf(KnownInstantiation instantiation) =>
        instantiation.Template.Base is not { } template ? null
        : instantiation.ArgumentTypes is { } types ? Instantiation(new NamedType(template, NamedKind.Class, types))
        : _instantiations.GetValueOrDefault(KnownInstantiation.Spell(template, instantiation.Arguments));

    /// <summary>
    /// Gives each synthesized instantiation the package of its first user, in package order, after the first writer pass
    /// saw every use; the collections it needs (its base, the collections it holds) go with it where they have none yet.
    /// </summary>
    /// <param name="uses">Each package's used instantiations, in package order.</param>
    /// <param name="packages">The packages this run writes: one that names no other package's type goes to its template's.</param>
    public void AssignOwners(IEnumerable<(string Package, IEnumerable<KnownInstantiation> Collections)> uses, IReadOnlySet<string>? packages = null)
    {
        void Own(KnownInstantiation instantiation, string package)
        {
            if (_instantiations[instantiation.Spelling] is not { Package: Unowned } unowned)
            {
                return;
            }

            _instantiations[instantiation.Spelling] = unowned with { Package = package };
            foreach (var dependency in DependenciesOf(unowned))
            {
                Own(dependency, package);
            }
        }

        List<(string Package, IEnumerable<KnownInstantiation> Collections)> used = [.. uses];
        foreach (var collection in used.SelectMany(u => u.Collections))
        {
            if (collection.ArgumentTypes is { } arguments && new NamedType(collection.Template.Name, NamedKind.Class, arguments).TemplatePackage is { } home
                && packages?.Contains(home) == true)
            {
                Own(collection, home);
            }
        }

        foreach (var (package, collections) in used)
        {
            foreach (var collection in collections)
            {
                Own(collection, package);
            }
        }
    }

    // a name for an instantiation no alias names: the template and its arguments, flattened (NCollection_Array1_gp_Pnt2d,
    // NCollection_DataMap_TopoDS_Shape_Handle_Geom_Curve); a default hasher is left out
    private string SynthesizedName(CollectionTemplate template, IReadOnlyList<CppType> arguments)
    {
        List<string> parts = [template.Name.Replace("::", "_", StringComparison.Ordinal)];
        for (var i = 0; i < arguments.Count; i++)
        {
            if (template.Arguments[i] != CollectionArgument.Hasher || arguments[i] is not NamedType { Name: "NCollection_DefaultHasher" })
            {
                parts.Add(Flat(arguments[i]));
            }
        }

        return string.Join("_", parts);
    }

    private string Flat(CppType type)
    {
        var flat = type switch
        {
            NamedType { Name: "opencascade::handle", TemplateArguments: [var target] } => $"Handle_{Flat(target)}",
            NamedType { TemplateArguments.Count: > 0 } n when Instantiation(n) is { } nested => nested.Alias,
            NamedType n => string.Join("_", n.TemplateArguments.Select(Flat).Prepend(n.Name)),
            _ => type.Spelling,
        };
        return NonWord().Replace(flat.Replace("::", "_", StringComparison.Ordinal), "_");
    }

    // a synthesized name another class or alias may have already: then numbered
    private string FreeName(string name)
    {
        var free = name;
        for (var i = 2; _classes.ContainsKey(free) || _instantiations.Values.Any(k => k.Alias == free); i++)
        {
            free = $"{name}_{i}";
        }

        return free;
    }

    // what the instantiation's macro needs instantiated first: its base, and the collections among its arguments (by value
    // or, handle-managed, by handle)
    private IEnumerable<KnownInstantiation> DependenciesOf(KnownInstantiation instantiation)
    {
        if (BaseOf(instantiation) is { } baseCollection)
        {
            yield return baseCollection;
        }

        foreach (var argument in instantiation.ArgumentTypes ?? [])
        {
            var collection = argument is NamedType { Name: "opencascade::handle", TemplateArguments: [NamedType inner] } ? inner : argument as NamedType;
            if (collection is { TemplateArguments.Count: > 0 } && Instantiation(collection) is { } nested)
            {
                yield return nested;
            }
        }
    }

    private int Depth(KnownInstantiation instantiation) => DependenciesOf(instantiation).Select(d => Depth(d) + 1).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Marks an instantiation as used by a signature, so its package instantiates it, and a handle-managed one's base
    /// too. Collections nothing uses stay out of the interface files.
    /// </summary>
    public void Request(KnownInstantiation instantiation)
    {
        if (_requested.Add(instantiation.Spelling) && BaseOf(instantiation) is { } baseCollection)
        {
            Request(baseCollection);
        }
    }

    public int RequestCount => _requested.Count;

    /// <summary>The requested instantiations a package owns, each after the ones it needs (bases, collections it holds).</summary>
    public IReadOnlyList<KnownInstantiation> RequestedIn(string package) =>
        [.. _instantiations.Values.Where(i => i.Package == package && _requested.Contains(i.Spelling))
            .OrderBy(Depth).ThenBy(i => i.Alias, StringComparer.Ordinal)];

    /// <summary>The %import list a hand-written module declares (its dependencies).</summary>
    public IReadOnlyList<string> ImportsOf(string package) => _imports.GetValueOrDefault(package) ?? [];

    /// <summary>Reads what a netocc-core module this run doesn't write provides (classes, enums, value types, collections).</summary>
    public void ScanHandWritten(string interfaceFile)
    {
        var package = Path.GetFileNameWithoutExtension(interfaceFile);
        var text = File.ReadAllText(interfaceFile);
        _imports[package] = [.. ImportPattern().Matches(text).Select(m => m.Groups[1].Value)];

        // %occt_handle: the root, Standard_Transient
        var transients = MacroPattern("occt_transient").Matches(text).Concat(MacroPattern("occt_handle").Matches(text))
            .Select(m => m.Groups[1].Value).ToHashSet();
        var valueTypes = MacroPattern("occt_valuetype").Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
        var valueClasses = MacroPattern("occt_valueclass").Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var name in valueTypes)
        {
            AddClass(new KnownClass(name, package, WrapKind.ValueType));
        }

        foreach (Match m in ClassPattern().Matches(text))
        {
            var name = m.Groups[1].Value;
            var kind = transients.Contains(name) ? WrapKind.Transient : valueClasses.Contains(name) ? WrapKind.ValueClass : WrapKind.Plain;
            AddClass(new KnownClass(name, package, kind));
        }

        foreach (Match m in EnumPattern().Matches(text))
        {
            AddEnum(m.Groups[1].Value, package);
        }

        // the module instantiates these, so they're requested
        foreach (var instantiation in Collections(text, package))
        {
            AddInstantiation(instantiation);
            Request(instantiation);
        }

        // %rename(TopoDS) TopoDS::NetOcc_TopoDS: the static shim for OCCT 8 namespace functions
        foreach (Match m in ShimPattern().Matches(text))
        {
            AddClass(new KnownClass(m.Groups[1].Value, package, WrapKind.Plain));
        }
    }

    /// <summary>
    /// For a run that writes only some packages: the collections a package's current .i instantiates stay requested, as
    /// modules this run doesn't write may use them.
    /// </summary>
    public void KeepRequested(string interfaceFile)
    {
        foreach (var instantiation in Collections(File.ReadAllText(interfaceFile), Path.GetFileNameWithoutExtension(interfaceFile)))
        {
            if (_instantiations.TryGetValue(instantiation.Spelling, out var known))
            {
                Request(known);
            }
        }
    }

    // %occt_array1(TColgp_Array1OfPnt, gp_Pnt, gp_Pnt): the macro names the template (a suffix a variant), then NAME, the
    // arguments' C++ types, the elements' C# types
    private static IEnumerable<KnownInstantiation> Collections(string text, string package)
    {
        foreach (Match m in CollectionPattern().Matches(text))
        {
            var macro = m.Groups[1].Value;
            if (CollectionTemplate.All.FirstOrDefault(t => macro == t.Macro || macro.StartsWith($"{t.Macro}_", StringComparison.Ordinal)) is { } template)
            {
                yield return new KnownInstantiation(template, [.. SplitArguments(m.Groups[3].Value).Take(template.Arguments.Count)], m.Groups[2].Value, package);
            }
        }
    }

    // macro arguments, split at the commas outside template brackets
    private static IEnumerable<string> SplitArguments(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return text[start..i].Trim();
                    start = i + 1;
                    break;
            }
        }

        yield return text[start..].Trim();
    }

    [GeneratedRegex(@"\W+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"^%import ""(\w+)\.i""", RegexOptions.Multiline)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"^class (\w+)\b", RegexOptions.Multiline)]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"^enum (\w+)\b", RegexOptions.Multiline)]
    private static partial Regex EnumPattern();

    [GeneratedRegex(@"^%(occt_\w+)\((\w+), (.+)\)$", RegexOptions.Multiline)]
    private static partial Regex CollectionPattern();

    [GeneratedRegex(@"^%rename\((\w+)\) (?:\w+::)*NetOcc_\w+;", RegexOptions.Multiline)]
    private static partial Regex ShimPattern();

    private static Regex MacroPattern(string macro) => new($@"^%{macro}\((\w+)[,)]", RegexOptions.Multiline);
}
