// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;

// static usings
using static NetOcc.Generator.Emit.ClassRules;
using static NetOcc.Generator.Emit.MemberRules;

namespace NetOcc.Generator.Emit;

/// <summary>A generated module: its .i, its aggregate header, its value-type structs, the modules it imports and what it left out.</summary>
/// <param name="Collections">The NCollection instantiations its members use, which their packages instantiate.</param>
/// <param name="NestedHeader">The C++ aliases of the package's nested and namespace types (<see cref="InterfaceWriter.NestedHeaderOf"/>), if it has any.</param>
internal sealed record GeneratedModule(string Package, string Interface, string ModuleHeader, IReadOnlyList<string> Imports, IReadOnlyList<string> Skipped,
    IReadOnlyList<GeneratedStruct>? Structs = null, IReadOnlyList<KnownInstantiation>? Collections = null, string? NestedHeader = null);

/// <summary>What writing one module collects: the types it uses, the typemap macros it calls, the members it leaves out.</summary>
internal sealed class ModuleContext(string package, PackageConfig config)
{
    public string Package { get; } = package;

    public PackageConfig Config { get; } = config;

    /// <summary>The types the module uses: its imports and the headers of its module header.</summary>
    public HashSet<TypeUse> Uses { get; } = [];

    /// <summary>
    /// Macros the module calls before its declarations: typemaps its types need (<see cref="MappedType.Typemap"/>: addresses)
    /// and range-for enumerators (<c>%occt_forward_range</c>).
    /// </summary>
    public SortedSet<string> Typemaps { get; } = new(StringComparer.Ordinal);

    /// <summary>The skip log: what the module leaves out, and why.</summary>
    public List<string> Skipped { get; } = [];

    /// <summary>Records what a type in an emitted declaration needs.</summary>
    public void Use(MappedType type)
    {
        Uses.UnionWith(type.Uses);
        if (type.Typemap is { } typemap)
        {
            Typemaps.Add(typemap);
        }
    }
}

/// <summary>Writes one package's SWIG interface in netocc-core's conventions (declarations only, deterministic).</summary>
/// <param name="preludeOf">A package's configured prelude: headers its own headers need first.</param>
/// <param name="handWrittenOf">The hand-written partial of a value-type struct (package, type), whose members aren't generated.</param>
/// <param name="classOf">Any parsed class by name, for the bases of an argument's class in other packages (<see cref="IsKept"/>).</param>
internal sealed class InterfaceWriter(TypeRegistry registry, SignatureMapper mapper, string occtVersion,
    Func<string, IReadOnlyList<string>>? preludeOf = null, Func<string, string, HandWritten>? handWrittenOf = null,
    Func<string, ClassModel?>? classOf = null)
{
    private readonly ValueTypeWriter _structs = new(registry, mapper, occtVersion);

    // members every SWIG C# proxy already has. Not GetType: OCCT's (GeomAdaptor_Curve::GetType) hides object's, which
    // isn't virtual, and the proxy's own code never calls it
    private static readonly HashSet<string> ProxyMembers = ["Dispose", "Finalize", "MemberwiseClone"];

    // the root of the handle hierarchy: handle typemaps, but no base class and no DownCast
    private const string RootTransient = "Standard_Transient";

    /// <param name="hasExtras">
    /// netocc-core has a hand-written companion, <c>src/SWIG_files/extras/&lt;Pkg&gt;.i</c>, for what the generator can't
    /// derive (lifetime typemaps, value-type thunks, %extend). It is included after the enums, before the classes.
    /// </param>
    public GeneratedModule Write(PackageModel package, PackageConfig config, Func<string, IReadOnlyList<string>> importsOf, bool hasExtras = false)
    {
        var own = package.Name;
        var module = new ModuleContext(own, config);
        module.Skipped.AddRange([.. (package.ExcludedHeaders ?? []).Select(h => $"header {h}"), .. package.ExcludedTypes ?? []]);
        var uses = module.Uses;
        var valueTypes = ValueTypes(package, config);
        var classes = Classes(package, config);
        var macros = ClassMacros(valueTypes, classes);
        var enums = EnumDeclarations(package);

        var body = new StringBuilder();
        foreach (var c in classes)
        {
            WriteClass(body, c, module);
        }

        // value types: C# structs whose native members call thunks, declared after the classes their signatures may use
        List<GeneratedStruct> structs = [.. valueTypes.Select(v => _structs.Write(v, module, handWrittenOf?.Invoke(own, v.Name) ?? HandWritten.None))];
        var thunks = string.Concat(structs.Select(s => s.Thunks));
        if (thunks.Length > 0)
        {
            body.AppendLine($"// the native members of the C# structs (src/NetOcc/{own}/*.g.cs)").AppendLine("%inline %{").Append(thunks).AppendLine("%}").AppendLine();
        }

        var functions = NamespaceFunctions(package, module);
        // what the members use, before the package's own collections add their elements and bases
        List<KnownInstantiation> used = [.. uses.Select(u => u.Collection).OfType<KnownInstantiation>().Distinct().OrderBy(i => i.Spelling, StringComparer.Ordinal)];
        var collections = Collections(own, module);

        var direct = uses.Select(u => u.Package).ToHashSet();
        // handle-managed classes and collections: Standard_Transient's typemaps (DownCast takes a Handle(Standard_Transient))
        if (classes.Any(c => Kind(c) == WrapKind.Transient) || registry.RequestedIn(own).Any(i => i.Template.IsTransient))
        {
            direct.Add("Standard");
        }

        var imports = Closure(own, direct, importsOf);

        var text = new StringBuilder();
        text.Append(GeneratedText.Header)
            .AppendLine($"/* {own}: generated by netocc-gen from OCCT {occtVersion}. Don't edit; change the generator or its config. */")
            .AppendLine($"%module {own}Module")
            .AppendLine()
            .AppendLine("%include \"NetOcc.i\"")
            .AppendLine()
            .AppendLine("%{")
            .AppendLine($"#include <{own}_module.hxx>")
            .AppendLine("%}")
            .AppendLine();
        foreach (var import in imports)
        {
            text.AppendLine($"%import \"{import}.i\"");
        }

        // C# usings: the namespaces of the imported modules (after the imports, whose own calls this one overrides)
        text.AppendLine($"%netocc_csimports({string.Join(" ", imports.Select(i => $"using OCC.Core.{i};"))})").AppendLine();
        // the macros its declarations need (address typemaps, range-for enumerators): before every declaration that uses them
        foreach (var typemap in module.Typemaps)
        {
            macros.AppendLine(typemap);
        }

        if (macros.Length > 0)
        {
            text.Append(macros).AppendLine();
        }

        if (valueTypes.Count > 0)
        {
            text.Append(LayoutGuards(own, valueTypes)).AppendLine();
        }

        text.Append(enums);
        if (hasExtras)
        {
            text.AppendLine($"%include \"extras/{own}.i\"").AppendLine();
        }

        text.Append(collections).Append(body).Append(functions);
        // the preludes of the packages it uses too: their headers miss the same includes here
        List<string> preludes = [.. config.Prelude, .. uses.Select(u => u.Package).Where(p => p != own).Distinct().Order(StringComparer.Ordinal)
            .SelectMany(p => preludeOf?.Invoke(p) ?? [])];
        var nested = NestedHeader(package);
        var header = ModuleHeader(package, preludes, uses.Where(u => u.Package != own).Select(u => u.Header), nested is not null);
        return new GeneratedModule(own, GeneratedText.Lf(text.ToString().TrimEnd()) + "\n", GeneratedText.Lf(header), imports, module.Skipped, structs, used,
            nested is null ? null : GeneratedText.Lf(nested));
    }

    private WrapKind Kind(ClassModel c) => registry.Class(c.Name)?.Kind ?? WrapKind.Plain;

    // the typemap macros and features of the package's structs and classes, which go before every declaration
    private StringBuilder ClassMacros(List<ClassModel> valueTypes, List<ClassModel> classes)
    {
        var macros = new StringBuilder();
        foreach (var v in valueTypes)
        {
            macros.AppendLine($"%occt_valuetype({v.Name})");
        }

        foreach (var c in classes)
        {
            var kind = Kind(c);
            if (kind == WrapKind.Transient)
            {
                macros.AppendLine(c.Name == RootTransient ? $"%occt_handle({c.Name}, {c.Name})" : $"%occt_transient({c.Name})");
            }
            // a const& return is a copy, whatever the class declares: a static member may return one without an object
            // (Graphic3d_CubeMapOrder::Default returns a Graphic3d_ValidatedCubeMapOrder, which C# only gets that way)
            else if (kind == WrapKind.ValueClass)
            {
                macros.AppendLine($"%occt_valueclass({c.Name})");
            }

            if (c.IsDeprecated)
            {
                macros.AppendLine($"%typemap(csattributes) {c.Name} {SwigObsolete}");
            }

            if (!c.Traits.HasPublicDestructor)
            {
                macros.AppendLine($"%nodefaultdtor {c.Name};");
            }

            // SWIG gives a class that declares no constructor a default one, and default-constructs by-value results;
            // without a usable T(), it must know (the value wrapper copies results instead)
            if (!c.Traits.HasDefaultConstructor)
            {
                macros.AppendLine($"%nodefaultctor {c.Name};").AppendLine($"%feature(\"valuewrapper\") {c.Name};");
            }
            // a move-only class's by-value results go through the value wrapper, which moves them
            else if (IsMoveOnly(c))
            {
                macros.AppendLine($"%feature(\"valuewrapper\") {c.Name};");
            }
            else if (c.Traits.IsAbstract || !c.Traits.IsCreatable)
            {
                macros.AppendLine($"%nodefaultctor {c.Name};");
            }
        }

        return macros;
    }

    private static StringBuilder EnumDeclarations(PackageModel package)
    {
        var enums = new StringBuilder();
        foreach (var e in package.Enums)
        {
            // the C# enum has C++'s underlying type (structs hold it)
            if (e.Underlying is { } underlying && SignatureMapper.CsBuiltin(underlying) is { } csBase)
            {
                enums.AppendLine($"%typemap(csbase) {e.Name} \"{csBase}\"");
            }

            // a nested enum's constants are only unique in its scope: scoped in the .i, where SWIG sees all of them
            enums.AppendLine($"enum {(e.QualifiedName is null ? "" : "class ")}{e.Name} {{");
            foreach (var constant in e.Constants)
            {
                enums.AppendLine($"  {constant.Name} = {constant.Value},");
            }

            enums.AppendLine("};").AppendLine();
        }

        return enums;
    }

    /// <summary>The header with the C++ aliases of a package's nested and namespace types (<c>using Geom_Curve_ResD1 = Geom_Curve::ResD1;</c>).</summary>
    public static string NestedHeaderOf(string package) => $"{package}_nested.hxx";

    // the attribute as a SWIG string, for %csattributes and the csattributes typemap
    private static readonly string SwigObsolete = $"\"{CsObsolete.Replace("\"", "\\\"")}\"";

    // the flat names the generated code uses for nested and namespace types, as C++ aliases of the real ones
    private static string? NestedHeader(PackageModel package)
    {
        // class template instances no alias names too, under their flat names, with the headers of their arguments
        List<(string Name, string Qualified, IEnumerable<string> Headers)> nested =
        [
            .. package.Enums.Where(e => e.QualifiedName is not null).Select(e => (e.Name, e.QualifiedName!, (IEnumerable<string>)[e.Header])),
            .. package.Classes.Where(c => c.QualifiedName is not null).Select(c => (c.Name, c.QualifiedName!, (IEnumerable<string>)[c.Header])),
            .. package.Classes.Where(c => c.Instance is not null).Select(c => (c.Name, c.Instance!.Alias ?? c.Instance.Spelling, c.Instance.Includes.AsEnumerable())),
        ];
        if (nested.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder(GeneratedText.Header)
            .AppendLine($"// Flat names of the nested and namespace types of {package.Name} for the NetOcc SWIG modules: generated by netocc-gen, don't edit")
            .AppendLine("#pragma once");
        foreach (var header in nested.SelectMany(n => n.Headers).Distinct().Order(StringComparer.Ordinal))
        {
            text.AppendLine($"#include <{header}>");
        }

        text.AppendLine();
        foreach (var (name, qualified, _) in nested)
        {
            text.AppendLine($"using {name} = {qualified};");
        }

        return text.ToString();
    }

    // the package's proxied classes, bases before derived classes
    private List<ClassModel> Classes(PackageModel package, PackageConfig config)
    {
        List<ClassModel> selected = [.. package.Classes.Where(c => IsSelected(c, config) && !IsCovered(c) && Kind(c) != WrapKind.ValueType)];

        List<ClassModel> ordered = [];
        HashSet<string> placed = [];
        void Place(ClassModel c)
        {
            if (!placed.Add(c.Name))
            {
                return;
            }

            foreach (var ancestor in c.Ancestors)
            {
                if (selected.FirstOrDefault(s => s.Name == ancestor) is { } local)
                {
                    Place(local);
                }
            }

            ordered.Add(c);
        }

        foreach (var c in selected)
        {
            Place(c);
        }

        return ordered;
    }

    // every field type below the fields (array elements, members of nested classes)
    private static IEnumerable<CppType> FieldTypes(IEnumerable<FieldModel> fields) =>
        fields.SelectMany(f => FieldTypes(f.Members ?? []).Prepend(f.Type is ArrayType array ? array.Innermost : f.Type));

    // libclang's layout, checked by the C++ compiler; the managed layout tests compare the C# structs with NetOcc_SizeOf_*
    private string LayoutGuards(string own, List<ClassModel> valueTypes)
    {
        var text = new StringBuilder()
            .AppendLine("%{")
            .AppendLine($"// Layout guards for the C# structs in src/NetOcc/{own}/: a mismatch fails the native build.");
        foreach (var v in valueTypes)
        {
            var layout = v.Layout ?? throw new InvalidOperationException($"{own}: no layout for value type {v.Name}");
            text.AppendLine($"static_assert(sizeof({v.Name}) == {layout.Size} && alignof({v.Name}) == {layout.Align} && std::is_trivially_copyable<{v.Name}>::value, \"{v.Name} layout\");");
        }

        // the structs hold enums as C# enums (their underlying type, int by default) and bool as byte
        foreach (var e in valueTypes.SelectMany(v => FieldTypes(v.Fields ?? [])).OfType<NamedType>().Where(t => t.Kind == NamedKind.Enum)
                     .Select(t => t.Name).Distinct().Order(StringComparer.Ordinal))
        {
            var underlying = registry.Enum(e)?.Underlying ?? "int";
            text.AppendLine($"static_assert(sizeof({e}) == sizeof({underlying}), \"{e}: a{(underlying == "int" ? "n" : "")} {SignatureMapper.CsBuiltin(underlying) ?? underlying} in C#\");");
        }

        if (valueTypes.SelectMany(v => FieldTypes(v.Fields ?? [])).Any(t => t is BuiltinType { Name: "bool" }))
        {
            text.AppendLine("static_assert(sizeof(bool) == 1, \"bool: a byte in C#\");");
        }

        text.AppendLine("%}").AppendLine().AppendLine("%inline %{").AppendLine("// sizes for the managed layout tests");
        foreach (var v in valueTypes)
        {
            text.AppendLine($"inline int NetOcc_SizeOf_{v.Name}() {{ return (int)sizeof({v.Name}); }}");
        }

        return text.AppendLine("%}").ToString();
    }

    private void WriteClass(StringBuilder body, ClassModel c, ModuleContext module)
    {
        var baseName = c.Name == RootTransient ? null
            : c.Ancestors.FirstOrDefault(a => registry.Class(a) is { Kind: not WrapKind.ValueType })
              ?? (Kind(c) == WrapKind.Transient ? RootTransient : null);
        if (baseName is not null && registry.Class(baseName) is { } baseClass)
        {
            module.Uses.Add(baseClass.Use);
        }

        body.AppendLine(baseName is null ? $"class {c.Name} {{" : $"class {c.Name} : public {baseName} {{").AppendLine("public:");
        Constructors(body, c, module);
        Methods(body, c, module);
        body.AppendLine("};").AppendLine();
    }

    private void Constructors(StringBuilder body, ClassModel c, ModuleContext module)
    {
        var label = $"{c.Name}::{c.Name}";
        // an object the shim creates is deleted by its proxy: without a usable destructor, the finalizer would throw
        var deletable = c.Traits.HasPublicDestructor || Kind(c) == WrapKind.Transient;
        if (!c.Traits.IsCreatable && c.Constructors.Count > 0)
        {
            module.Skipped.Add($"{label}: the class declares only placement forms of operator new");
        }
        else if (!deletable && !c.Traits.IsAbstract && c.Constructors.Count > 0)
        {
            module.Skipped.Add($"{label}: the destructor isn't public or doesn't link, so C# couldn't release the object");
        }

        if (c.Traits.IsAbstract || !c.Traits.IsCreatable || !deletable)
        {
            return;
        }

        var overloads = c.Constructors.Select(o => o.Parameters).ToList();
        var referenced = Referenced(c);
        List<(ConstructorModel Model, MappedMember Member)> constructors = [];
        foreach (var ctor in c.Constructors)
        {
            if (Declarable(module.Config, label, ctor.Parameters, overloads, overloads.Concat(HiddenConstructors(c)), NotCallable(label, ctor), null, module.Skipped) is { } declared
                && Map(c.Name, c.Name, declared, module, KeptStream("a constructor")) is { } member)
            {
                constructors.Add((ctor, member));
            }
        }

        foreach (var ((ctor, member), firstDefault) in Claim(constructors, x => x.Member, _ => 0))
        {
            var text = Declare(member, firstDefault, "", module);
            if (ctor.IsDeprecated)
            {
                body.AppendLine($"  %csattributes {text.Signature} {SwigObsolete};");
            }

            // arguments the object keeps a reference to: their proxies go into the new proxy (References.i's NETOCC_KEEP), by
            // type and name, for this declaration only
            var kept = Kept(member, text, referenced);
            if (kept.Count > 0)
            {
                module.Typemaps.Add($"%netocc_keep_construct({c.Name})");
            }

            foreach (var group in kept.GroupBy(k => k.Pointer))
            {
                body.AppendLine($"  %apply SWIGTYPE {(group.Key ? "*" : "&")} NETOCC_KEEP {{ {string.Join(", ", group.Select(k => k.Declaration))} }};");
            }

            body.AppendLine($"  {text.Declaration};");
            if (kept.Count > 0)
            {
                body.AppendLine($"  %clear {string.Join(", ", kept.Select(k => k.Declaration))};");
            }
        }
    }

    // the classes (and collections, by spelling) an object of c refers to by raw pointer or reference, through the fields
    // of the class and its bases: a C# argument of one must outlive the object (a BRepGraph_FaceIterator holds its graph)
    private HashSet<string> Referenced(ClassModel c) => [.. (c.Held ?? []).Select(Target).OfType<string>()];

    // what a pointer or lvalue reference to a proxied class or collection refers to, by its spelling without const. Not a
    // handle (a reference count keeps the object, and a reference to a handle variable is to the call's copy), a standard
    // library type or a .NET one of the typemaps (strings, GUIDs), which have no proxy to keep
    private string? Target(CppType type) => registry.Resolve(type) switch
    {
        PointerType { Pointee: NamedType { Kind: NamedKind.Class } n } when SignatureMapper.IsProxied(n) => (n with { Const = false }).Spelling,
        ReferenceType { IsRValue: false, Referee: NamedType { Kind: NamedKind.Class } n } when SignatureMapper.IsProxied(n) => (n with { Const = false }).Spelling,
        _ => null,
    };

    /// <summary>
    /// An argument the object keeps a reference to: a class or collection passed by reference or pointer that the object
    /// refers to, or one of its bases. Proxies only (<paramref name="mapped"/>): a struct argument is a copy for the call, an
    /// address (a class no header defines) has no proxy to keep.
    /// </summary>
    private bool IsKept(CppType type, MappedType mapped, HashSet<string> referenced)
    {
        if (Target(type) is not { } target || mapped.CsType == SignatureMapper.CsAddress || registry.Class(target) is { Kind: WrapKind.ValueType })
        {
            return false;
        }

        return referenced.Contains(target) || (classOf?.Invoke(target)?.Ancestors ?? []).Any(referenced.Contains);
    }

    // a declaration's kept arguments (IsKept): position, pointer or reference, and the parameter as the declaration writes it
    private List<(int Index, bool Pointer, string Declaration)> Kept(MappedMember member, MemberText text, HashSet<string> referenced) =>
    [
        .. member.Parameters.Select((p, i) => (p, i)).Where(x => IsKept(x.p.Parameter.Type, x.p.Type, referenced))
            .Select(x => (x.i, registry.Resolve(x.p.Parameter.Type) is PointerType, x.p.Type.Declare(text.Arguments[x.i]))),
    ];

    private void Methods(StringBuilder body, ClassModel c, ModuleContext module)
    {
        // a class with a stream member may keep a stream it's given
        var keepsStreams = (c.Fields ?? []).Any(f => f.Type is PointerType { Pointee: var held } && SignatureMapper.StreamClass(held) is not null
            || SignatureMapper.StreamOf(f.Type) is not null);
        // begin() and end() of the range-for protocol: IEnumerable in C#, or the reason there's none
        var range = Range(c);
        if (range.Macro is { } rangeMacro)
        {
            module.Typemaps.Add(rangeMacro);
        }

        List<(MethodModel Model, MappedType Returned, MappedMember Member, string? Accessor)> methods = [];
        foreach (var m in c.Methods)
        {
            var label = $"{c.Name}::{m.Name}";
            if (IsRangeMember(m))
            {
                if (range.Skip is { } rangeSkip)
                {
                    module.Skipped.Add($"{label}: {rangeSkip}");
                }

                continue;
            }

            var named = c.Methods.Where(o => o.Name == m.Name).ToList();
            var twins = named.Where(o => o.IsStatic == m.IsStatic).ToList();
            var clash = m.Name == c.Name || ProxyMembers.Contains(m.Name) ? "name clashes with the C# proxy" : null;
            if (Declarable(module.Config, label, m.Parameters, twins.Select(o => o.Parameters), named.Select(o => o.Parameters).Concat(Hidden(c, m.Name)),
                    NotCallable(label, m), clash, module.Skipped) is not { } declared)
            {
                continue;
            }

            // a member's references borrow from its object (References.i); a static one has no object to keep alive
            var returned = mapper.Return(m.Return, m.Parameters, m.IsStatic ? null : c.Name);
            string? accessor = null;
            if (returned.Type is null && Accessor(c, m) is { } shim)
            {
                (returned, accessor) = shim;
            }

            if (Returned(returned, label, m.Return, m.Parameters, twins.Select(o => (o.Return, o.Parameters)), module.Skipped) is { } returnType
                && Map(c.Name, m.Name, declared, module, keepsStreams ? KeptStream("the class") : null) is { } member)
            {
                methods.Add((m, returnType, member, accessor));
            }
        }

        var statics = methods.Where(x => x.Model.IsStatic).Select(x => x.Member).ToList();
        foreach (var shadowed in methods.Where(x => !x.Model.IsStatic && statics.Any(s => Shadows(s, x.Member, c.Name))).ToList())
        {
            module.Skipped.Add($"{shadowed.Member.Label}: a static overload takes the object and the same arguments, which SWIG's wrappers can't tell apart");
            methods.Remove(shadowed);
        }

        var referenced = Referenced(c);
        foreach (var ((m, returned, member, accessor), firstDefault) in Claim(methods, x => x.Member, x => TwinRank(x.Returned)))
        {
            module.Use(returned);
            var text = Declare(member, firstDefault, returned.Spelling + " ", module);
            var (modifier, qualifier) = (m.IsStatic ? "static " : "", m.IsConst ? " const" : "");
            // an accessor is an %extend member SWIG can return, named as the member in C#
            var signature = accessor is null ? text.Signature : $"NetOcc_{text.Signature}";
            // SWIG matches the feature by name, parameters and const, so it marks this overload only
            if (m.IsDeprecated)
            {
                body.AppendLine($"  %csattributes {signature}{qualifier} {SwigObsolete};");
            }

            // arguments a non-const member may keep (Extrema_ExtPS::Initialize): the proxy keeps each until the member's next
            // call replaces it (References.i's %netocc_keep_argument), for this declaration only
            var kept = m.IsStatic || m.IsConst ? [] : Kept(member, text, referenced);
            foreach (var (index, _, declaration) in kept)
            {
                body.AppendLine($"  %netocc_keep_argument({m.Name}.{index}, {(declaration.Contains(',') ? $"%arg({declaration})" : declaration)})");
            }

            if (accessor is null)
            {
                body.AppendLine($"  {modifier}{text.Declaration}{qualifier};");
            }
            else
            {
                body.AppendLine($"  %rename({m.Name}) NetOcc_{m.Name};")
                    .AppendLine("  %extend {")
                    .AppendLine($"    {modifier}{returned.Spelling} {signature}{qualifier} {{ return {string.Format(accessor, string.Join(", ", text.Arguments))}; }}")
                    .AppendLine("  }");
            }

            if (kept.Count > 0)
            {
                body.AppendLine($"  %clear {string.Join(", ", kept.Select(k => k.Declaration))};");
            }
        }
    }

    /// <summary>
    /// A wrapped static overload that takes the object first and an instance member's arguments after
    /// (<c>NCollection_Mat4::Multiply(m)</c> beside static <c>Multiply(m1, m2)</c>): SWIG's wrappers pass the object as the
    /// first argument, so they can't tell the two apart. As SWIG compares them: class arguments by their class, without
    /// const and references (a handle is another type), others by their type.
    /// </summary>
    private bool Shadows(MappedMember s, MappedMember m, string owner)
    {
        string Plain(CppType type) => (registry.Resolve(type) is ReferenceType { Referee: var referee } ? referee : registry.Resolve(type)).WithoutConst().Spelling;

        return s.Name == m.Name && s.Parameters.Count == m.Parameters.Count + 1 && Plain(s.Parameters[0].Parameter.Type) == owner
            && s.Parameters.Skip(1).Select(p => Plain(p.Parameter.Type)).SequenceEqual(m.Parameters.Select(p => Plain(p.Parameter.Type)));
    }

    /// <summary>
    /// A return SWIG can't give as declared, through an accessor that can (an <c>%extend</c> member; the call in the body,
    /// <c>{0}</c> the arguments), or null. A static member's class reference is the object's address: a proxy that borrows
    /// it and keeps no owner, since the object isn't the caller's (<c>MoniTool_Stat::Current</c>). A transient returned by
    /// value is constructed in place from the returned value (C++17 elides the copy, so it needn't be copyable), and its
    /// proxy owns a reference (<c>BRepGraph_Tool::Edge::CurveAdaptor</c>).
    /// </summary>
    private (Mapping.Mapping Returned, string Body)? Accessor(ClassModel c, MethodModel m)
    {
        var call = m.IsStatic ? $"{c.Name}::{m.Name}({{0}})" : $"$self->{m.Name}({{0}})";
        return m.Return switch
        {
            ReferenceType { IsRValue: false, Referee: NamedType { IsConst: false, TemplateArguments.Count: 0 } t }
                when m.IsStatic && registry.Class(t.Name) is { Kind: not WrapKind.ValueType } =>
                (mapper.Return(new PointerType(t, Const: true), m.Parameters), $"&{call}"),
            NamedType { TemplateArguments.Count: 0 } t when registry.Class(t.Name) is { Kind: WrapKind.Transient } =>
                (mapper.Return(new PointerType(t with { Const = false }, Const: true), m.Parameters), $"new {t.Name}({call})"),
            _ => null,
        };
    }

    // OCCT 8's range-for protocol (NCollection_ForwardRange.hxx): begin() and end() iterate with the class's own More(),
    // Next() and accessor
    private static bool IsRangeMember(MethodModel m) =>
        m is { Name: "begin" or "end" or "cbegin" or "cend", Parameters.Count: 0, Return: NamedType { Name: "NCollection_ForwardRangeIterator" or "NCollection_ForwardRangeSentinel" } };

    /// <summary>
    /// The IEnumerable of a class with range-for members (netocc-core's <c>%occt_forward_range</c>): C# enumerates with the
    /// same More(), Next() and accessor, the first of Value(), Current() and CurrentId() the class has, as
    /// NCollection_ForwardRangeDetail::AccessorTraits picks it. Or why there's none; neither without range members.
    /// </summary>
    private (string? Macro, string? Skip) Range(ClassModel c)
    {
        if (!c.Methods.Any(IsRangeMember))
        {
            return (null, null);
        }

        bool IsWrapped(string name) => c.Methods.Any(m => m.Name == name && m.Parameters.Count == 0 && !m.IsStatic && m.IsCallable);
        if (!IsWrapped("More") || !IsWrapped("Next"))
        {
            return (null, "range-for needs More() and Next(), which the class doesn't wrap");
        }

        foreach (var accessor in (string[])["Value", "Current", "CurrentId"])
        {
            if (c.Methods.FirstOrDefault(m => m.Name == accessor && m.Parameters.Count == 0 && m.IsConst && !m.IsStatic) is not { } method)
            {
                continue;
            }

            var returned = mapper.Return(method.Return, method.Parameters, c.Name);
            if (!method.IsCallable || returned.Type is null)
            {
                return (null, $"range-for reads {accessor}(), which isn't wrapped");
            }

            // a ref return is read as a value
            var csType = returned.Type.CsType.StartsWith("ref ", StringComparison.Ordinal) ? returned.Type.CsType[4..] : returned.Type.CsType;
            return ($"%occt_forward_range({c.Name}, {csType}, {accessor})", null);
        }

        return (null, "range-for reads Value(), Current() or CurrentId(), which the class doesn't have");
    }

    // the NCollection instantiations this package's aliases name and signatures use (the registry's requests), the
    // handle-managed ones after their bases. They go before the classes: SWIG applies typemaps (the %occt_valueclass copies,
    // the handle conversions) to the declarations after them only, and resolves element classes declared later.
    private string Collections(string own, ModuleContext module)
    {
        var uses = module.Uses;
        var text = new StringBuilder();
        foreach (var instantiation in registry.RequestedIn(own))
        {
            var template = instantiation.Template;
            var types = instantiation.ArgumentTypes ?? throw new InvalidOperationException($"{own}: {instantiation.Alias} has no parsed arguments");
            var (arguments, skip) = mapper.Arguments(template, types);
            if (arguments is null)
            {
                throw new InvalidOperationException($"{own}: {instantiation.Alias} was requested, but {skip}");
            }

            uses.UnionWith(arguments.Uses);
            module.Typemaps.UnionWith(arguments.Typemaps);
            if (registry.BaseOf(instantiation) is { } baseCollection)
            {
                uses.Add(new TypeUse(baseCollection.Package, baseCollection.Header, baseCollection));
            }

            // an Array1 of numbers or structs: bulk copies to and from C# arrays
            var macro = template.HasBlittableVariant && arguments.IsBlittable ? $"{template.Macro}_blittable" : template.Macro;
            // SWIG macro arguments end at a comma, template brackets or not: one that holds commas goes through %arg
            var cppArguments = instantiation.Arguments.Select(a => a.Contains(',') ? $"%arg({a})" : a);
            text.AppendLine($"%{macro}({instantiation.Alias}, {string.Join(", ", cppArguments)}, {string.Join(", ", arguments.CsTypes)})");
        }

        return text.Length > 0 ? text.AppendLine().ToString() : "";
    }

    // OCCT 8 namespace functions (TopoDS::Face) as static methods of a shim struct, which C# sees as a class of the namespace's name
    private string NamespaceFunctions(PackageModel package, ModuleContext module)
    {
        var (config, skipped) = (module.Config, module.Skipped);
        var text = new StringBuilder();
        foreach (var group in package.NamespaceFunctions.GroupBy(f => f.Namespace))
        {
            var csName = group.Key.Replace("::", "_");
            if (registry.Class(csName) is { } clash)
            {
                skipped.Add($"namespace {group.Key}: class {clash.Name} ({clash.Package}) has its C# name");
                continue;
            }

            List<(FunctionModel Model, MappedType Returned, MappedMember Member)> functions = [];
            foreach (var f in group)
            {
                var label = $"{group.Key}::{f.Name}";
                var named = group.Where(o => o.Name == f.Name).ToList();
                if (Declarable(config, label, f.Parameters, named.Select(o => o.Parameters), named.Select(o => o.Parameters), NotCallable(label, f), null,
                        skipped) is not { } declared)
                {
                    continue;
                }

                if (Returned(mapper.Return(f.Return, f.Parameters), label, f.Return, f.Parameters, named.Select(o => (o.Return, o.Parameters)), skipped)
                        is { } returnType
                    && Map(group.Key, f.Name, declared, module) is { } member)
                {
                    functions.Add((f, returnType, member));
                }
            }

            List<string> members = [];
            List<string> deprecated = [];
            foreach (var ((f, returned, member), firstDefault) in Claim(functions, x => x.Member, _ => 0))
            {
                module.Use(returned);
                var memberText = Declare(member, firstDefault, returned.Spelling + " ", module);
                var call = $"{group.Key}::{f.Name}({string.Join(", ", memberText.Arguments)})";
                members.Add($"  static {memberText.Declaration} {{ {(returned.Spelling == "void" ? "" : "return ")}{call}; }}");
                if (f.IsDeprecated)
                {
                    deprecated.Add(memberText.Signature);
                }
            }

            if (members.Count == 0)
            {
                continue;
            }

            // the shim sits in the namespace, where the functions' default arguments and unqualified names resolve as declared
            var namespaces = group.Key.Split("::");
            text.AppendLine($"// namespace {group.Key}: static class {csName}")
                .AppendLine($"%rename({csName}) {group.Key}::NetOcc_{csName};");
            // SWIG directives stay out of %inline, whose text the compiler sees too
            foreach (var signature in deprecated)
            {
                text.AppendLine($"%csattributes {group.Key}::NetOcc_{csName}::{signature} {SwigObsolete};");
            }

            text.AppendLine("%inline %{")
                .AppendLine(string.Join(" ", namespaces.Select(n => $"namespace {n} {{")))
                .AppendLine($"struct NetOcc_{csName} {{");
            foreach (var member in members)
            {
                text.AppendLine(member);
            }

            text.AppendLine("};").AppendLine(string.Join(" ", namespaces.Select(_ => "}"))).AppendLine("%}").AppendLine();
        }

        return text.ToString();
    }

    // Streams.i lends a C# stream for the call only
    private static string KeptStream(string keeper) => $"{keeper} may keep the stream, which C# lends for the call only";

    /// <param name="Signature">Name and parameters, defaults included: the member for SWIG features, all its default overloads.</param>
    /// <param name="Declaration">Return type, name and parameters, without qualifiers or the trailing ';'.</param>
    /// <param name="Arguments">The parameter names, for forwarding calls.</param>
    private sealed record MemberText(string Signature, string Declaration, IReadOnlyList<string> Arguments);

    /// <summary>A member whose parameters map: what <see cref="Claim"/> weighs and <see cref="Declare"/> writes.</summary>
    /// <param name="Parameters">The parameters a declaration keeps (<see cref="MemberRules.Kept"/>) with their mappings.</param>
    private sealed record MappedMember(string Label, string Name, List<(ParameterModel Parameter, MappedType Type)> Parameters)
    {
        /// <summary>The fewest arguments a call takes: SWIG makes a C# overload for each arity from here on.</summary>
        public int FirstDefault => Parameters.FindIndex(p => p.Parameter.Default is not null) is var index and >= 0 ? index : Parameters.Count;

        /// <summary>The C# signature of the overload taking the first <paramref name="arity"/> arguments.</summary>
        public string Key(int arity) => $"{Name}({string.Join(",", Parameters.Take(arity).Select(p => p.Type.CsKey))})";

        /// <summary>Parameters C# passes as UTF-16 strings: exact, where a <c>const char*</c> overload may copy bytes as Latin-1.</summary>
        public int Utf16 => Parameters.Count(p => (p.Parameter.Type is ReferenceType reference ? reference.Referee : p.Parameter.Type)
            is NamedType { Name: "TCollection_ExtendedString" } or PointerType { Pointee: BuiltinType { Name: "char16_t" } });
    }

    // a member's mapped parameters, or null (skipped, reason logged). keptStream: why a stream parameter can't be lent here.
    private MappedMember? Map(string owner, string name, IReadOnlyList<ParameterModel> parameters, ModuleContext module, string? keptStream = null)
    {
        var label = $"{owner}::{name}";
        return MapParameters(mapper, label, parameters, MayKeep(owner, name), module.Skipped, keptStream) is { } mapped
            ? new MappedMember(label, name, mapped)
            : null;
    }

    /// <summary>
    /// The members C# gets, in declaration order, each with the arity its defaults start at. Overloads that map to one C#
    /// signature (const twins, string encodings, a pointer next to a reference) are one call in C#, which reaches the first
    /// claimant: by <paramref name="rank"/> (<see cref="TwinRank"/>), then the one with more UTF-16 strings, then the first
    /// declared. A member whose full signature is claimed is covered, not logged; one whose shorter default overloads are
    /// claimed keeps its defaults past those (such calls reach the other member).
    /// </summary>
    private static List<(T Item, int FirstDefault)> Claim<T>(List<T> candidates, Func<T, MappedMember> member, Func<T, int> rank)
    {
        HashSet<string> signatures = [];
        SortedDictionary<int, int> claimed = [];
        foreach (var index in Enumerable.Range(0, candidates.Count).OrderBy(i => rank(candidates[i])).ThenByDescending(i => member(candidates[i]).Utf16))
        {
            var m = member(candidates[index]);
            var count = m.Parameters.Count;
            if (signatures.Contains(m.Key(count)))
            {
                continue;
            }

            var first = m.FirstDefault;
            for (var arity = count - 1; arity >= first; arity--)
            {
                if (signatures.Contains(m.Key(arity)))
                {
                    first = arity + 1;
                    break;
                }
            }

            for (var arity = first; arity <= count; arity++)
            {
                signatures.Add(m.Key(arity));
            }

            claimed[index] = first;
        }

        return [.. claimed.Select(p => (candidates[p.Key], p.Value))];
    }

    /// <summary>
    /// Which of two twins C# gets first (0 before 1): one returning a C# ref to a value (a number, enum or struct in the
    /// object), which reads as well as writes; not a pointer slot (ref IntPtr, and a const char*&amp;, which Strings.i returns
    /// as one), which reads worse than the value. Class returns keep declaration order: neither an owned copy nor a borrowed
    /// proxy is safe for every class (a view's copy holds a raw pointer to its graph; an iterator's borrowed item outlives
    /// neither the iterator nor the list).
    /// </summary>
    private static int TwinRank(MappedType returned) =>
        returned.CsType.StartsWith("ref ", StringComparison.Ordinal) && returned.CsType != $"ref {SignatureMapper.CsAddress}" ? 0 : 1;

    // a claimed member's declaration, its defaults from firstDefault on; records the types it uses
    private static MemberText Declare(MappedMember member, int firstDefault, string prefix, ModuleContext module)
    {
        var names = new HashSet<string>();
        List<string> arguments = [];
        List<string> declared = [];
        for (var i = 0; i < member.Parameters.Count; i++)
        {
            var (parameter, type) = member.Parameters[i];
            module.Use(type);
            var parameterName = SafeName(parameter.Name, i, names);
            arguments.Add(parameterName);
            var initializer = i >= firstDefault ? Initializer(parameter, member.Label, module.Skipped) : "";
            declared.Add($"{type.Declare(parameterName)}{initializer}");
        }

        var signature = $"{member.Name}({string.Join(", ", declared)})";
        return new MemberText(signature, prefix + signature, arguments);
    }

    // transitive imports: every module this one or its imports use, in a stable order; never the module itself, which
    // a cycle between packages reaches too
    private static List<string> Closure(string own, IEnumerable<string> direct, Func<string, IReadOnlyList<string>> importsOf)
    {
        List<string> ordered = [];
        HashSet<string> seen = [own];
        void Visit(string package)
        {
            if (!seen.Add(package))
            {
                return;
            }

            foreach (var dependency in importsOf(package))
            {
                Visit(dependency);
            }

            ordered.Add(package);
        }

        foreach (var package in direct.Order(StringComparer.Ordinal))
        {
            Visit(package);
        }

        return ordered;
    }

    private static string ModuleHeader(PackageModel package, IReadOnlyList<string> prelude, IEnumerable<string> foreignHeaders, bool hasNested)
    {
        var text = new StringBuilder(GeneratedText.Header)
            .AppendLine($"// Aggregate header for the NetOcc SWIG module {package.Name}: generated by netocc-gen, don't edit")
            .AppendLine("#pragma once");
        // the types the package uses first: some OCCT headers miss includes they need
        var own = new HashSet<string>(package.Headers, StringComparer.OrdinalIgnoreCase);
        var foreign = foreignHeaders.Where(h => !own.Contains(h)).Distinct().Order(StringComparer.Ordinal);
        foreach (var header in prelude.Concat(foreign).Distinct().Concat(package.Headers))
        {
            text.AppendLine($"#include <{header}>");
        }

        if (hasNested)
        {
            text.AppendLine($"#include <{NestedHeaderOf(package.Name)}>");
        }

        return text.ToString();
    }
}
