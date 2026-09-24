// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

//
using NetOcc.Generator.Model;

namespace NetOcc.Generator.Mapping;

/// <summary>A type the generated code needs: its module (for %import) and header (for the module header).</summary>
/// <param name="Collection">For an NCollection instantiation, which one: its package instantiates it once a member uses it.</param>
internal sealed record TypeUse(string Package, string Header, KnownInstantiation? Collection = null);

/// <summary>
/// A collection's element in C#: <c>double</c>, <c>gp_Pnt</c>, <c>TopoDS_Shape</c>, <c>string</c>, <c>Geom_Curve</c> (a handle),
/// <c>TopTools_ListOfShape</c> (a collection).
/// </summary>
/// <param name="IsBlittable">C# copies it as bytes (numbers and value-type structs), so an array of it gets bulk copies.</param>
/// <param name="Typemap">The macro the element's type needs in the module that instantiates the collection (a bitset's).</param>
internal sealed record CollectionElement(string CsType, bool IsBlittable, IReadOnlyList<TypeUse> Uses, string? Typemap = null);

/// <summary>A collection's template arguments in C#: the elements' C# types, in order (hashers have none), and what they use.</summary>
/// <param name="IsBlittable">Every element is blittable.</param>
/// <param name="Typemaps">The macros the elements' types need (<see cref="CollectionElement.Typemap"/>).</param>
internal sealed record CollectionArguments(IReadOnlyList<string> CsTypes, bool IsBlittable, IReadOnlyList<TypeUse> Uses, IReadOnlyList<string> Typemaps);

/// <param name="Spelling">The type as the .i declares it (C++ spelling the netocc-core typemaps match).</param>
/// <param name="CsKey">What the type becomes in a C# signature, for overload collisions.</param>
/// <param name="CsType">
/// The type in a C# signature as the typemaps produce it, parameter modifier included: <c>in gp_Pnt</c>,
/// <c>ref double</c>, <c>string</c>. Returns never have a modifier.
/// </param>
/// <param name="Typemap">
/// A typemap macro the module calls before its declarations, for a type the typemap library can't cover generically:
/// <c>%netocc_address(%arg(double*))</c>.
/// </param>
/// <param name="Declared">
/// The type a parameter is declared with when that isn't <c>Spelling name</c>: a C array, <c>const double theJ[3][3]</c>.
/// </param>
internal sealed record MappedType(string Spelling, IReadOnlyList<TypeUse> Uses, string CsKey, string CsType, string? Typemap = null,
    CppType? Declared = null)
{
    /// <summary>The parameter declaration in the .i.</summary>
    public string Declare(string name) => Declared?.Declare(name) ?? $"{Spelling} {name}";
}

/// <summary>Either a mapped type or the reason it can't be wrapped (for the skip log).</summary>
internal readonly record struct Mapping(MappedType? Type, string? Skip)
{
    public static Mapping Of(string spelling, string csKey, string csType, params TypeUse[] uses) =>
        new(new MappedType(spelling, uses, csKey, csType), null);

    public static Mapping Skipped(string reason) => new(null, reason);
}

/// <summary>
/// Maps C++ signature types to netocc-core's wrapper contract: <c>&amp;</c> → <c>ref</c>, struct <c>const&amp;</c> →
/// <c>in</c>, handles ⇄ proxies, strings and GUIDs through the common typemaps. Anything else is skipped with a reason.
/// </summary>
internal sealed class SignatureMapper(TypeRegistry registry)
{
    // builtins with the same C# width on every platform, and their C# type: char its byte, whose sign differs per platform
    // (C long, 32-bit on Windows and 64-bit elsewhere, has its own cases: a C# long, range-checked)
    private static readonly Dictionary<string, string> Builtins = new()
    {
        ["bool"] = "bool",
        ["char"] = "byte",
        ["signed char"] = "sbyte",
        ["unsigned char"] = "byte",
        ["char32_t"] = "uint",
        ["short"] = "short",
        ["unsigned short"] = "ushort",
        ["int"] = "int",
        ["unsigned int"] = "uint",
        ["long long"] = "long",
        ["unsigned long long"] = "ulong",
        ["float"] = "float",
        ["double"] = "double",
        ["size_t"] = "ulong",
        ["intptr_t"] = "global::System.IntPtr",
        ["uintptr_t"] = "global::System.UIntPtr",
        ["ptrdiff_t"] = "global::System.IntPtr",
    };

    // by-reference builtins with an INOUT typemap in Types.i and a ref return in References.i
    private static readonly HashSet<string> RefBuiltins =
        ["bool", "int", "unsigned int", "short", "unsigned short", "signed char", "unsigned char", "long long", "unsigned long long", "float", "double"];

    // collection elements C# copies as bytes: Types.i's %netocc_array list (bool and char marshal differently)
    private static readonly HashSet<string> BlittableBuiltins =
        ["double", "float", "int", "unsigned int", "short", "unsigned short", "signed char", "unsigned char"];

    // template arguments are canonical types: size_t or int64_t spell differently per platform (unsigned long on Linux)
    private static readonly HashSet<string> PlatformSpelledBuiltins = ["long long", "unsigned long long", "size_t"];

    // a pointer to a builtin as a parameter: a C# array of it (Types.i's %netocc_pointer_array). char is a byte there (const
    // char* is a string), char16_t a UTF-16 char. size_t, long and wchar_t have no fixed width.
    private static readonly Dictionary<string, string> ArrayElements = new()
    {
        ["bool"] = "bool",
        ["char"] = "byte",
        ["signed char"] = "sbyte",
        ["unsigned char"] = "byte",
        ["char16_t"] = "char",
        ["short"] = "short",
        ["unsigned short"] = "ushort",
        ["int"] = "int",
        ["unsigned int"] = "uint",
        ["long long"] = "long",
        ["unsigned long long"] = "ulong",
        ["float"] = "float",
        ["double"] = "double",
        ["intptr_t"] = "global::System.IntPtr",
        ["uintptr_t"] = "global::System.UIntPtr",
        ["ptrdiff_t"] = "global::System.IntPtr",
    };

    /// <summary>The C# type of an address: void*, and pointers C# can't type (kept by the callee, functions, pointers to pointers).</summary>
    public const string CsAddress = "global::System.IntPtr";

    /// <summary>The C# type of a builtin that maps (see <see cref="Builtins"/>), or null.</summary>
    public static string? CsBuiltin(string name) => Builtins.GetValueOrDefault(name);

    /// <summary>The C# type of a stream parameter: netocc-core's Streams.i lends a System.IO.Stream for the call.</summary>
    public const string CsStream = "global::System.IO.Stream";

    /// <summary>
    /// <c>std::ostream</c> or <c>std::istream</c> for a reference to one (<c>Standard_OStream&amp;</c>, canonical
    /// <c>std::basic_ostream&lt;char, ...&gt;&amp;</c>), otherwise null.
    /// </summary>
    public static string? StreamOf(CppType type) => type is ReferenceType { IsRValue: false, Referee: var referee } ? StreamClass(referee) : null;

    /// <summary><c>std::ostream</c> or <c>std::istream</c> for the (non-const) stream class, as <see cref="StreamOf"/>; otherwise null.</summary>
    public static string? StreamClass(CppType type) =>
        type is NamedType { IsConst: false, TemplateArguments: [BuiltinType { Name: "char" }, ..] } stream && stream.Name.StartsWith("std::", StringComparison.Ordinal)
            ? stream.Name.EndsWith("::basic_ostream", StringComparison.Ordinal) ? "std::ostream"
            : stream.Name.EndsWith("::basic_istream", StringComparison.Ordinal) ? "std::istream"
            : null
            : null;

    /// <summary>A member returning its own object by reference, for chaining (<c>gp_XYZ&amp; Add(...)</c>): void in C#.</summary>
    public static bool IsChaining(CppType returned, string owner) =>
        returned is ReferenceType { IsRValue: false, Referee: NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0, IsConst: false } self }
        && self.Name == owner;

    /// <param name="mayKeep">
    /// The callee may keep a pointer it's given (a constructor, a <c>Set*</c> method). A C# array is pinned for the call only,
    /// so a pointer to numbers or structs is an address there.
    /// </param>
    public Mapping Parameter(CppType type, bool mayKeep = false) => MapParameter(registry.Resolve(type), mayKeep);

    private Mapping MapParameter(CppType type, bool mayKeep) => type switch
    {
        BuiltinType { Name: "void" } => Mapping.Skipped("void parameter"),
        BuiltinType b => Number(b, ValueSpelling(b)) ?? Mapping.Skipped($"{b.Name} has no fixed-width C# type"),
        NamedType { Kind: NamedKind.Enum } e => Enum(e, e.Name),
        NamedType n => Class(n, n with { Const = false }, byMutableReference: false),
        ReferenceType { IsRValue: true } => Mapping.Skipped("rvalue reference"),
        ReferenceType when StreamOf(type) is { } stream => Mapping.Of($"{stream}&", "Stream", CsStream),
        ReferenceType { Referee: BuiltinType { IsConst: true } b } when Number(b, $"const {ValueSpelling(b)}&") is { } number => number,
        ReferenceType { Referee: BuiltinType { Name: "char16_t" } } => Mapping.Of("char16_t&", "ref char", "ref char"),
        // a width typedef by its written name: int64_t& is long& on Linux, and Types.i applies INOUT to the written names
        ReferenceType { Referee: BuiltinType b } when !b.IsConst && RefBuiltins.Contains(b.Name) =>
            Mapping.Of($"{b.Written ?? b.Name}&", $"ref {b.Name}", $"ref {Builtins[b.Name]}"),
        // char's bits (its sign differs per platform), size_t pointer-sized underneath, C long range-checked (Types.i)
        ReferenceType { Referee: BuiltinType { Name: "char", IsConst: false } } => Mapping.Of("char&", "ref unsigned char", "ref byte"),
        ReferenceType { Referee: BuiltinType { Name: "size_t", IsConst: false, Written: null } } => Mapping.Of("size_t&", "ref unsigned long long", "ref ulong"),
        ReferenceType { Referee: BuiltinType { Name: "long", IsConst: false } } => Mapping.Of("long&", "ref long long", "ref long"),
        ReferenceType { Referee: BuiltinType b } => Mapping.Skipped($"{b.Spelling}& has no typemap"),
        // a C array by reference (int (&)[3]): a C# array of its length, checked, which the callee reads and writes in place
        // (Types.i's NetOcc_ArrayRef); bool one byte each
        ReferenceType { IsRValue: false, Referee: ArrayType { Element: BuiltinType { Name: "bool" }, Length: > 0 } a } =>
            new Mapping(new MappedType($"NetOcc_ArrayRef< bool, {a.Length} >", [], "bool[]", "bool[]", $"%netocc_bool_array_ref({a.Length})"), null),
        ReferenceType { IsRValue: false, Referee: ArrayType { Element: BuiltinType e, Length: > 0 } a } when ArrayElement(e) is { } cs =>
            new Mapping(new MappedType($"NetOcc_ArrayRef< {e.Name}, {a.Length} >", [], $"{cs}[]", $"{cs}[]", $"%netocc_array_ref({e.Name}, {a.Length}, {cs})"), null),
        // by value: SWIG's temporary for a const enum& is an elaborated "enum X", which a flat alias (a nested enum's) can't be
        ReferenceType { Referee: NamedType { Kind: NamedKind.Enum, IsConst: true } e } => Enum(e, e.Name),
        // References.i's ref slot is an int
        ReferenceType { Referee: NamedType { Kind: NamedKind.Enum } e } when registry.Enum(e.Name) is { Underlying: { } underlying } =>
            Mapping.Skipped($"{e.Name}& of an enum on {underlying}, and the ref slot is an int"),
        ReferenceType { Referee: NamedType { Kind: NamedKind.Enum } e } => Enum(e, $"{e.Name}&", byReference: true),
        ReferenceType { Referee: NamedType n } => Class(n, n, byMutableReference: !n.IsConst, reference: true),
        // T* const& is the pointer, passed by value
        ReferenceType { Referee: PointerType { IsConst: true } p } => Pointer(p, mayKeep),
        ReferenceType { Referee: PointerType p } => PointerReference(p),
        PointerType p => Pointer(p, mayKeep),
        ArrayType { Element: ArrayType } a => Matrix(a, mayKeep),
        ArrayType a => Pointer(new PointerType(a.Element), mayKeep),
        _ => Mapping.Skipped($"unsupported type {type.Spelling}"),
    };

    /// <summary>The return of a value-type thunk: no object to borrow from, since a C# struct is pinned for the call only.</summary>
    public Mapping Return(CppType type) => Return(registry.Resolve(type), member: false);

    private Mapping Return(CppType type, bool member) => type switch
    {
        BuiltinType { Name: "void" } => Mapping.Of("void", "void", "void"),
        BuiltinType b => Number(b, ValueSpelling(b)) ?? Mapping.Skipped($"returns {b.Name}, which has no fixed-width C# type"),
        NamedType { Kind: NamedKind.Enum } e => Enum(e, e.Name),
        NamedType n => ReturnedClass(n with { Const = false }, reference: false),
        // Types.i returns no const long& (OCCT has none)
        ReferenceType { IsRValue: false, Referee: BuiltinType { IsConst: true, Name: not "long" } b } when Number(b, $"const {ValueSpelling(b)}&") is { } number => number,
        ReferenceType { IsRValue: false, Referee: NamedType { Kind: NamedKind.Enum, IsConst: true } e } => Enum(e, $"const {e.Name}&"),
        ReferenceType { IsRValue: false, Referee: NamedType { IsConst: true } n } => ReturnedClass(n, reference: true),
        // a string's mutable reference comes back as a copy too (Strings.i)
        ReferenceType { IsRValue: false, Referee: NamedType { Name: "TCollection_AsciiString" or "TCollection_ExtendedString" } n } =>
            Mapping.Of($"{n.Name}&", "string", "string"),
        // a pointer returned by const reference is the pointer
        ReferenceType { IsRValue: false, Referee: PointerType { IsConst: true } p } => ReturnedPointer(p, member),
        ReferenceType => Mapping.Skipped($"returns a mutable reference, {type.Spelling}"),
        PointerType p => ReturnedPointer(p, member),
        _ => Mapping.Skipped($"returns unsupported type {type.Spelling}"),
    };

    /// <summary>
    /// A function's return. Returning the stream it was given, for chaining (<c>Standard_OStream&amp; Print(Standard_OStream&amp;)</c>),
    /// is void in C#. A non-static member of <paramref name="owner"/> returns references too (netocc-core's References.i): its
    /// own class, for chaining, is void (<see cref="IsChaining"/>); a number, enum or struct is a C# ref into the object; a
    /// class a proxy that borrows the object and keeps the owner's proxy alive, and so does a pointer to a class.
    /// </summary>
    public Mapping Return(CppType type, IReadOnlyList<ParameterModel> parameters, string? owner = null)
    {
        type = registry.Resolve(type);
        if ((StreamOf(type) is { } stream && parameters.Any(p => StreamOf(p.Type) == stream)) || (owner is not null && IsChaining(type, owner)))
        {
            return Mapping.Of("void", "void", "void");
        }

        return owner is not null && type is ReferenceType { IsRValue: false, Referee: var referee } && Borrowed(referee, owner) is { } borrowed
            ? borrowed
            : Return(type, member: owner is not null);
    }

    // a builtin by value as the .i spells it: a pointer-sized typedef by its written name (Aspect_Drawable is void* on
    // Windows, unsigned long on X11: no one type converts to both), others by the builtin, which converts to theirs
    private static string ValueSpelling(BuiltinType b) => b is { Name: "intptr_t" or "uintptr_t", Written: { } written } ? written : b.Name;

    // a number by value (spelled so) or const& (spelled "const T&"): its C# type, or null without a fixed-width one. Keyed
    // as the builtin whose C# type it shares: char (its byte) as unsigned char, char32_t (a code point) as unsigned int, C
    // long (a C# long; Types.i checks the range) as long long, the pointer-sized ones as void* is.
    private static Mapping? Number(BuiltinType b, string spelling) => b.Name switch
    {
        "char16_t" => Mapping.Of(spelling, "char", "char"),
        "char" => Mapping.Of(spelling, "unsigned char", "byte"),
        "char32_t" => Mapping.Of(spelling, "unsigned int", "uint"),
        "long" => Mapping.Of(spelling, "long long", "long"),
        "intptr_t" or "ptrdiff_t" => Mapping.Of(spelling, "IntPtr", CsAddress),
        "uintptr_t" => Mapping.Of(spelling, "UIntPtr", Builtins["uintptr_t"]),
        _ when Builtins.TryGetValue(b.Name, out var cs) => Mapping.Of(spelling, b.Name, cs),
        _ => null,
    };

    // an address C# can't type, as an IntPtr: NetOcc_Address<T> converts to T in the wrapper (Types.i), and the module
    // instantiates its typemaps. A pointer is passed as a copy: no top-level const.
    private static Mapping Address(PointerType pointer, params TypeUse[] uses)
    {
        var spelling = (pointer with { Const = false }).Spelling;
        return new Mapping(new MappedType($"NetOcc_Address< {spelling} >", uses, "IntPtr", CsAddress, $"%netocc_address(%arg({spelling}))"), null);
    }

    // a pointer parameter (a copy: no top-level const): a class is its proxy, null allowed; numbers and structs are a C# array,
    // pinned for the call, unless the callee may keep them; void* is an IntPtr; a stream is lent like a stream reference
    private Mapping Pointer(PointerType p, bool mayKeep)
    {
        var pointer = p with { Const = false };
        switch (p.Pointee)
        {
            case BuiltinType { Name: "char", IsConst: true }:
                return Mapping.Of("const char*", "string", "string");
            // Standard_ExtString: UTF-16, like TCollection_ExtendedString
            case BuiltinType { Name: "char16_t", IsConst: true }:
                return Mapping.Of("const char16_t*", "string", "string");
            case BuiltinType { Name: "void" }:
                return Mapping.Of(pointer.Spelling, "IntPtr", CsAddress);
            // a pointer-sized typedef Types.i has no arrays of (a window-system handle: void* on Windows, an integer on X11)
            case BuiltinType { Name: "intptr_t" or "uintptr_t" or "ptrdiff_t", Written: not (null or "intptr_t" or "uintptr_t" or "ptrdiff_t") }:
                return Address(pointer);
            case BuiltinType b when ArrayElements.TryGetValue(b.Name, out var element):
                return mayKeep ? Address(pointer) : Mapping.Of(pointer.Spelling, $"{element}[]", $"{element}[]");
            case NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } n when registry.Class(n.Name) is { Kind: WrapKind.ValueType } valueType:
                var use = valueType.Use;
                return mayKeep ? Address(pointer, use) : Mapping.Of(pointer.Spelling, $"{n.Name}[]", $"{n.Name}[]", use);
            case NamedType { Kind: NamedKind.Class } n when StreamClass(n) is { } stream:
                return Mapping.Of($"{stream}*", "Stream", CsStream);
            // an opaque pointer (no package defines the class, the callee only passes it on) or one to an enum
            case NamedType n when IsOpaque(n) || n.Kind == NamedKind.Enum:
                return Address(Opaque(pointer));
            case NamedType { Kind: NamedKind.Class } n when IsProxied(n):
                return PointedClass(n, pointer.Spelling);
            case NamedType { Name: var name } when name.StartsWith("std::", StringComparison.Ordinal):
                return Mapping.Skipped($"raw pointer {p.Spelling}: std type {name}");
            case BuiltinType or NamedType or PointerType or FunctionType:
                return Address(pointer);
            default:
                return Mapping.Skipped($"raw pointer {p.Spelling}");
        }
    }

    // a class the headers only declare: no package defines it (a defined one is known by its flat name)
    private bool IsOpaque(NamedType n) => n.DeclaredOnly is not null && registry.Class(n.Name) is null;

    // a pointer spelled by C++'s names where a flat one won't do: an opaque class has no flat alias, and SWIG spells a
    // known enum in a template argument "enum X", which a nested enum's flat alias can't follow
    private PointerType Opaque(PointerType p) => p.Pointee switch
    {
        NamedType { DeclaredOnly: { } qualified } n => p with { Pointee = n with { Name = qualified } },
        NamedType { Kind: NamedKind.Enum } e when registry.Enum(e.Name) is { QualifiedName: { } qualified } => p with { Pointee = e with { Name = qualified } },
        PointerType inner => p with { Pointee = Opaque(inner) },
        _ => p,
    };

    // a class that is a proxy in C#: not a handle, a standard library type, or one of the .NET types of the typemaps
    internal static bool IsProxied(NamedType n) =>
        n.Name != "opencascade::handle" && !n.Name.StartsWith("std::", StringComparison.Ordinal) && !TypeRegistry.TypemappedClasses.Contains(n.Name);

    // a class a pointer points to: its proxy (a collection's by the alias), or why there's none
    private Mapping PointedClass(NamedType n, string spelling)
    {
        if (n.TemplateArguments.Count > 0)
        {
            var (instantiation, uses, skip) = Collection(n);
            return instantiation is null ? Mapping.Skipped(skip!) : Mapping.Of(spelling, instantiation.Alias, instantiation.Alias, uses);
        }

        return registry.Class(n.Name) is { } known
            ? Mapping.Of(spelling, n.Name, n.Name, known.Use)
            : Mapping.Skipped($"class {n.Name} is not wrapped");
    }

    // T*& (the callee may replace the pointer): a class is ref of its proxy (References.i, Handles.i), const char* a ref
    // string, anything else a ref IntPtr through NetOcc_AddressRef<T>
    private Mapping PointerReference(PointerType p)
    {
        switch (p.Pointee)
        {
            case BuiltinType { Name: "char", IsConst: true }:
                return Mapping.Of("const char*&", "ref string", "ref string");
            case NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } n when registry.Class(n.Name) is { Kind: WrapKind.ValueType }:
                return AddressReference(p);
            case NamedType n when IsOpaque(n) || n.Kind == NamedKind.Enum:
                return AddressReference(Opaque(p));
            case NamedType { Kind: NamedKind.Class } n when IsProxied(n):
                var pointed = PointedClass(n, $"{p.Spelling}&");
                return pointed.Type is { } type ? new Mapping(type with { CsKey = $"ref {type.CsKey}", CsType = $"ref {type.CsType}" }, null) : pointed;
            case NamedType { Name: var name } when name.StartsWith("std::", StringComparison.Ordinal):
                return Mapping.Skipped($"{p.Spelling}&: std type {name}");
            case BuiltinType or NamedType or PointerType or FunctionType:
                return AddressReference(p);
            default:
                return Mapping.Skipped($"unsupported type {p.Spelling}&");
        }
    }

    private static Mapping AddressReference(PointerType p) => new(new MappedType($"NetOcc_AddressRef< {p.Spelling} >", [], "ref IntPtr",
        $"ref {CsAddress}", $"%netocc_address_ref(%arg({p.Spelling}))"), null);

    // a parameter declared as an array of arrays (const double theJ[3][3]): a C# rectangular array of numbers
    private static Mapping Matrix(ArrayType a, bool mayKeep) =>
        a is { Element: ArrayType { Element: BuiltinType b } } && ArrayElement(b) is { } element && !mayKeep
            ? new Mapping(new MappedType(a.Spelling, [], $"{element}[,]", $"{element}[,]", Declared: a), null)
            : Address(new PointerType(a.Element));

    // a returned pointer: a class is a proxy (References.i: a member's borrows from the object and keeps its proxy alive, spelled
    // T*; one without an object doesn't, T* const; Handles.i: a transient's owns a reference, T* const, since constructors
    // return T*), a string a string, anything else an address. C# has no const, so neither has the proxy.
    private Mapping ReturnedPointer(PointerType p, bool member)
    {
        var owned = p.Pointee is NamedType { TemplateArguments.Count: 0 } target && registry.Class(target.Name) is { Kind: WrapKind.Transient }
            || p.Pointee is NamedType { TemplateArguments.Count: > 0 } collection && CollectionTemplate.Named(collection.Name) is { IsTransient: true };
        var pointer = p with { Const = !member || owned };
        switch (p.Pointee)
        {
            case BuiltinType { Name: "char", IsConst: true }:
                return Mapping.Of("const char*", "string", "string");
            case BuiltinType { Name: "char16_t", IsConst: true }:
                return Mapping.Of("const char16_t*", "string", "string");
            case BuiltinType { Name: "void" }:
                return Mapping.Of((p with { Const = false }).Spelling, "IntPtr", CsAddress);
            case NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } n when registry.Class(n.Name) is { Kind: WrapKind.ValueType } valueType:
                return Address(p, valueType.Use);
            case NamedType n when IsOpaque(n) || n.Kind == NamedKind.Enum:
                return Address(Opaque(p));
            case NamedType { Kind: NamedKind.Class } n when IsProxied(n):
                var pointed = PointedClass(n, pointer.Spelling);
                return pointed.Type is null ? Mapping.Skipped($"returns {p.Spelling}: {pointed.Skip}") : pointed;
            case NamedType { Kind: NamedKind.Class } n when StreamClass(n) is { } stream:
                return Mapping.Skipped($"returns a native {stream}: Streams.i lends C# streams to OCCT for a call and has no C# view of OCCT's");
            case NamedType { Name: var name } when name.StartsWith("std::", StringComparison.Ordinal):
                return Mapping.Skipped($"returns raw pointer {p.Spelling}: std type {name}");
            case BuiltinType or NamedType or PointerType or FunctionType:
                return Address(p);
            default:
                return Mapping.Skipped($"returns raw pointer {p.Spelling}");
        }
    }

    // a reference a member returns, or null where Return decides: copies of const value classes and collections, strings
    private Mapping? Borrowed(CppType referee, string owner)
    {
        switch (referee)
        {
            // spelled by the canonical name, not the written one as parameters are: References.i returns those
            case BuiltinType { IsConst: false } b when RefBuiltins.Contains(b.Name):
                return Mapping.Of($"{b.Name}&", $"ref {b.Name}", $"ref {Builtins[b.Name]}");
            // pointer-sized: a ref UIntPtr, four bytes on win-x86 as size_t is
            case BuiltinType { Name: "size_t", IsConst: false, Written: null }:
                return Mapping.Of("size_t&", "ref UIntPtr", "ref global::System.UIntPtr");
            case NamedType { Kind: NamedKind.Enum, IsConst: false } e:
                return registry.Enum(e.Name) is { } knownEnum
                    ? Mapping.Of($"{e.Name}&", $"ref {e.Name}", $"ref {e.Name}", knownEnum.Use)
                    : Mapping.Skipped($"returns enum {e.Name}&, which is not wrapped");
            // a pointer the object holds, as a ref IntPtr into it
            case PointerType { IsConst: false } pointer:
                return Mapping.Of($"{pointer.Spelling}&", "ref IntPtr", $"ref {CsAddress}");
            // the object itself, as for a const& handle
            case NamedType { Name: "opencascade::handle", IsConst: false } handle:
                return Handle(handle, handle, "&", byMutableReference: false);
            case NamedType { TemplateArguments.Count: 0 } n when !TypeRegistry.TypemappedClasses.Contains(n.Name) && registry.Class(n.Name) is { } known:
                var use = known.Use;
                return known.Kind switch
                {
                    WrapKind.ValueType => n.IsConst ? null : Mapping.Of($"{n.Name}&", $"ref {n.Name}", $"ref {n.Name}", use),
                    WrapKind.ValueClass when n.IsConst => null,
                    _ => Mapping.Of($"{n.Spelling}&", n.Name, n.Name, use),
                };
            case NamedType { TemplateArguments.Count: > 0, IsConst: false } n when CollectionTemplate.Named(n.Name) is { IsTransient: false }:
                var (instantiation, uses, skip) = Collection(n);
                return instantiation is null
                    ? Mapping.Skipped($"returns {n.Spelling}&: {skip}")
                    : Mapping.Of($"{n.Spelling}&", instantiation.Alias, instantiation.Alias, uses);
            default:
                return null;
        }
    }

    private Mapping Enum(NamedType e, string spelling, bool byReference = false) =>
        registry.Enum(e.Name) is { } known
            ? Mapping.Of(spelling, byReference ? $"ref {e.Name}" : e.Name, byReference ? $"ref {e.Name}" : e.Name, known.Use)
            : Mapping.Skipped($"enum {e.Name} is not wrapped");

    // parameter: by value, const&, or & (the callee writes the caller's object)
    private Mapping Class(NamedType n, NamedType spelled, bool byMutableReference, bool reference = false)
    {
        var suffix = reference ? "&" : "";
        if (n.Name.StartsWith("std::", System.StringComparison.Ordinal) && CollectionTemplate.Named(n.Name) is null)
        {
            return Std(n, byMutableReference, reference);
        }

        if (TypeRegistry.TypemappedClasses.Contains(n.Name))
        {
            var typemapped = Typemapped(n.Name);
            return byMutableReference
                ? Mapping.Of($"{spelled.Spelling}{suffix}", $"ref {typemapped.Key}", $"ref {typemapped.CsType}")
                : Mapping.Of($"{spelled.Spelling}{suffix}", typemapped.Key, typemapped.CsType);
        }

        if (n.Name == "opencascade::handle")
        {
            return Handle(n, spelled, suffix, byMutableReference);
        }

        if (n.TemplateArguments.Count > 0)
        {
            var (instantiation, uses, skip) = Collection(n);
            if (instantiation is null)
            {
                return Mapping.Skipped(skip!);
            }

            // a handle-managed collection is passed as its handle, or by const& like a transient
            return instantiation.Template.IsTransient && (!reference || byMutableReference)
                ? Mapping.Skipped($"{spelled.Spelling}{suffix}: {instantiation.Alias} is handle-managed (pass the handle)")
                : Mapping.Of($"{spelled.Spelling}{suffix}", instantiation.Alias, instantiation.Alias, uses);
        }

        if (registry.Class(n.Name) is not { } known)
        {
            return Mapping.Skipped($"class {n.Name} is not wrapped");
        }

        // a struct: const& is a pointer to the caller's struct (in), & lets the callee write it (ref)
        var key = known.Kind == WrapKind.ValueType && byMutableReference ? $"ref {n.Name}" : n.Name;
        var csType = known.Kind != WrapKind.ValueType ? n.Name : byMutableReference ? $"ref {n.Name}" : reference ? $"in {n.Name}" : n.Name;
        return Mapping.Of($"{spelled.Spelling}{suffix}", key, csType, known.Use);
    }

    // strings and GUIDs, .NET types through the common typemaps (Strings.i, Guid.i): their key and C# type
    private static (string Key, string CsType) Typemapped(string name) => name == "Standard_GUID" ? ("Guid", "global::System.Guid") : ("string", "string");

    // the width of a bitset a ulong holds (N <= 64), as C++ spells it, or null
    private static string? BitsetWidth(NamedType n) =>
        n.TemplateArguments is [ConstantArgument { Text: var bits }] && int.TryParse(bits, out var count) && count <= 64 ? bits : null;

    /// <summary>
    /// A standard library type netocc-core's typemaps cover (Strings.i: strings and string views by value or const&amp;, a
    /// const&amp; string stream's text; Std.i: stream positions by value, bitsets, optional values, arrays of numbers and
    /// complex numbers by value or const&amp;, the last two by non-const &amp; too).
    /// A bitset, array or optional needs its instantiation's macro in the module (<see cref="MappedType.Typemap"/>).
    /// </summary>
    private Mapping Std(NamedType n, bool byMutableReference, bool reference)
    {
        var (konst, suffix) = (reference && !byMutableReference ? "const " : "", reference ? "&" : "");
        // libstdc++ declares the strings in an inline namespace
        var narrow = n.TemplateArguments is [BuiltinType { Name: "char" }, ..];
        switch (n.Name.Replace("::__cxx11::", "::", StringComparison.Ordinal))
        {
            case "std::basic_string" when narrow && !byMutableReference:
                return Mapping.Of($"{konst}std::string{suffix}", "string", "string");
            case "std::basic_string_view" when narrow && !byMutableReference:
                return Mapping.Of($"{konst}std::string_view{suffix}", "string", "string");
            case "std::basic_stringstream" when narrow && reference && !byMutableReference:
                return Mapping.Of("const std::stringstream&", "string", "string");
            case "std::fpos" when !reference:
                return Mapping.Of("std::streampos", "long", "long");
            case "std::complex" when n.TemplateArguments is [BuiltinType { Name: "double" }]:
                return byMutableReference ? Mapping.Of("std::complex<double>&", "ref Complex", "ref global::OCC.Core.Complex")
                    : Mapping.Of($"{konst}std::complex<double>{suffix}", "Complex", reference ? "in global::OCC.Core.Complex" : "global::OCC.Core.Complex");
            case "std::bitset" when !byMutableReference && BitsetWidth(n) is { } bits:
                return new Mapping(new MappedType($"{konst}std::bitset< {bits} >{suffix}", [], "ulong", "ulong", $"%netocc_bitset({bits})"), null);
            case "std::array" when n.TemplateArguments is [BuiltinType element, ConstantArgument { Text: var length }] && ArrayElement(element) is { } cs:
                return new Mapping(new MappedType($"{konst}std::array< {element.Name}, {length} >{suffix}", [], $"{cs}[]", $"{cs}[]",
                    $"%netocc_std_array(%arg({element.Name}), {length}, {cs})"), null);
            case "std::optional" when !byMutableReference && n.TemplateArguments is [var value] && OptionalValue(value) is var (type, csType, uses):
                return new Mapping(new MappedType($"{konst}std::optional< {type} >{suffix}", uses, $"{csType}?", $"{csType}?",
                    $"%netocc_optional(%arg({type}), {csType})"), null);
            default:
                return Mapping.Skipped($"std type {n.Name}");
        }
    }

    // a number C# arrays hold as C++ does (pinned, copied as bytes; Types.i's %netocc_array list): not bool, which marshals
    // differently, nor a 64-bit integer, whose canonical spelling (template arguments are canonical) differs per platform
    private static string? ArrayElement(BuiltinType element) => BlittableBuiltins.Contains(element.Name) ? Builtins[element.Name] : null;

    // the value of an optional Std.i covers: a number, an enum or a struct. A nested enum goes by C++'s name: in template
    // arguments SWIG writes "enum X", which a flat alias can't follow.
    private (string Type, string CsType, TypeUse[] Uses)? OptionalValue(CppType value) => value switch
    {
        BuiltinType b when ArrayElement(b) is { } cs => (b.Name, cs, []),
        NamedType { Kind: NamedKind.Enum } e when registry.Enum(e.Name) is { } known => (known.QualifiedName ?? e.Name, e.Name, [known.Use]),
        NamedType { Kind: NamedKind.Class, TemplateArguments.Count: 0 } c when registry.Class(c.Name) is { Kind: WrapKind.ValueType } known => (c.Name, c.Name, [known.Use]),
        _ => null,
    };

    private Mapping Handle(NamedType n, NamedType spelled, string suffix, bool byMutableReference)
    {
        // Handle(TColStd_HArray1OfReal): a handle-managed collection
        if (n.TemplateArguments is [NamedType { TemplateArguments.Count: > 0 } collection])
        {
            var (instantiation, uses, skip) = Collection(collection);
            if (instantiation is null)
            {
                return Mapping.Skipped($"handle {n.Spelling}: {skip}");
            }

            var alias = byMutableReference ? $"ref {instantiation.Alias}" : instantiation.Alias;
            return Mapping.Of($"{spelled.Spelling}{suffix}", alias, alias, uses);
        }

        if (n.TemplateArguments is not [NamedType { TemplateArguments.Count: 0 } target] || registry.Class(target.Name) is not { Kind: WrapKind.Transient } known)
        {
            return Mapping.Skipped($"handle {n.Spelling} of an unwrapped class");
        }

        var key = byMutableReference ? $"ref {target.Name}" : target.Name;
        return Mapping.Of($"{spelled.Spelling}{suffix}", key, key, known.Use);
    }

    // an instance of a collection template that an OCCT alias names, holding elements C# can hold
    private (KnownInstantiation? Instantiation, TypeUse[] Uses, string? Skip) Collection(NamedType n)
    {
        var spelling = (n with { Const = false }).Spelling;
        if (CollectionTemplate.Named(n.Name) is not { } template || n.TemplateArguments.Count != template.Arguments.Count)
        {
            return (null, [], $"template {spelling} is not wrapped");
        }

        if (registry.Instantiation(n) is not { } instantiation)
        {
            return (null, [], $"{spelling} has no OCCT alias to name it");
        }

        var (arguments, skip) = Arguments(template, n.TemplateArguments);
        if (arguments is null)
        {
            return (null, [], $"{instantiation.Alias}: {skip}");
        }

        if (template.IsTransient && registry.BaseOf(instantiation) is null)
        {
            return (null, [], $"{instantiation.Alias}: its base {KnownInstantiation.Spell(template.Base!, instantiation.Arguments)} has no OCCT alias to name it");
        }

        return (instantiation, [new TypeUse(instantiation.Package, instantiation.Header, instantiation), .. arguments.Uses], null);
    }

    /// <summary>A collection template's arguments in C#, or why one can't be: every element must map; a hasher is C++ only.</summary>
    public (CollectionArguments? Arguments, string? Skip) Arguments(CollectionTemplate template, IReadOnlyList<CppType> arguments)
    {
        List<string> csTypes = [];
        List<TypeUse> uses = [];
        List<string> typemaps = [];
        var blittable = true;
        for (var i = 0; i < template.Arguments.Count; i++)
        {
            if (template.Arguments[i] == CollectionArgument.Hasher)
            {
                if (arguments[i] is NamedType { TemplateArguments.Count: 0 } hasher && registry.Class(hasher.Name) is { } known)
                {
                    uses.Add(known.Use);
                }

                continue;
            }

            // a pair's accessors are %extend, whose casts SWIG writes with "enum X", which a nested enum's flat alias can't follow
            if (template.Name == "std::pair" && registry.Resolve(arguments[i]) is NamedType { Kind: NamedKind.Enum } e && registry.Enum(e.Name)?.QualifiedName is not null)
            {
                return (null, $"element {e.Name}: a pair's accessors are %extend, which SWIG casts with enum {e.Name}, and a nested enum's flat alias can't follow that");
            }

            var (element, skip) = Element(arguments[i], template.DefaultConstructs);
            if (element is null)
            {
                return (null, skip);
            }

            csTypes.Add(element.CsType);
            uses.AddRange(element.Uses);
            blittable &= element.IsBlittable;
            if (element.Typemap is { } typemap)
            {
                typemaps.Add(typemap);
            }
        }

        return (new CollectionArguments(csTypes, blittable, uses, typemaps), null);
    }

    /// <summary>What a collection's element is in C#, or why the collection can't hold it.</summary>
    /// <param name="defaultConstructed">The collection default-constructs elements (the arrays).</param>
    public (CollectionElement? Element, string? Skip) Element(CppType type, bool defaultConstructed)
    {
        switch (registry.Resolve(type))
        {
            case BuiltinType b when PlatformSpelledBuiltins.Contains(b.Name):
                return (null, $"element {b.Name}: spelled differently per platform");
            case BuiltinType b when Builtins.TryGetValue(b.Name, out var cs):
                return (new CollectionElement(cs, BlittableBuiltins.Contains(b.Name), []), null);
            case NamedType { Kind: NamedKind.Enum } e:
                return registry.Enum(e.Name) is { } known
                    ? (new CollectionElement(e.Name, false, [known.Use]), null)
                    : (null, $"element enum {e.Name} is not wrapped");
            case NamedType { TemplateArguments.Count: 0 } n when TypeRegistry.TypemappedClasses.Contains(n.Name):
                return (new CollectionElement(Typemapped(n.Name).CsType, false, []), null);
            case NamedType { Name: "std::bitset" } n when BitsetWidth(n) is { } bits:
                return (new CollectionElement("ulong", false, [], $"%netocc_bitset({bits})"), null);
            case NamedType { Name: "opencascade::handle", TemplateArguments: [NamedType { TemplateArguments.Count: 0 } target] }:
                return registry.Class(target.Name) is { Kind: WrapKind.Transient } transient
                    ? (new CollectionElement(target.Name, false, [transient.Use]), null)
                    : (null, $"element handle of {target.Name}, which is not wrapped");
            // collections of collections (TopTools_DataMapOfShapeListOfShape): a plain one by value, a handle-managed one by handle
            case NamedType { Name: "opencascade::handle", TemplateArguments: [NamedType { TemplateArguments.Count: > 0 } inner] }:
                return NestedCollection(inner, byHandle: true);
            case NamedType { TemplateArguments.Count: > 0 } n when n.Name != "opencascade::handle":
                return NestedCollection(n, byHandle: false);
            case NamedType { TemplateArguments.Count: 0 } n:
                if (registry.Class(n.Name) is not { } element)
                {
                    return (null, $"element class {n.Name} is not wrapped");
                }

                if (element.Kind is not (WrapKind.ValueType or WrapKind.ValueClass))
                {
                    return (null, element.Kind == WrapKind.Transient ? $"element {n.Name} is a transient held by value" : $"element {n.Name} isn't copyable");
                }

                return defaultConstructed && !element.HasDefaultConstructor
                    ? (null, $"element {n.Name} has no default constructor")
                    : (new CollectionElement(n.Name, element.Kind == WrapKind.ValueType, [element.Use]), null);
            default:
                return (null, $"element {type.Spelling} is not supported");
        }
    }

    private (CollectionElement? Element, string? Skip) NestedCollection(NamedType n, bool byHandle)
    {
        var (instantiation, uses, skip) = Collection(n);
        if (instantiation is null)
        {
            return (null, $"element {skip}");
        }

        return instantiation.Template.IsTransient == byHandle
            ? (new CollectionElement(instantiation.Alias, false, uses), null)
            : (null, byHandle ? $"element handle of {instantiation.Alias}, which isn't handle-managed" : $"element {instantiation.Alias} is handle-managed, held by value");
    }

    private Mapping ReturnedClass(NamedType n, bool reference)
    {
        // a handle-managed collection comes back as its handle
        if (n.TemplateArguments.Count > 0 && CollectionTemplate.Named(n.Name) is { IsTransient: true })
        {
            return Mapping.Skipped($"returns {(n with { Const = false }).Spelling}{(reference ? "&" : "")}, which is handle-managed");
        }

        var mapped = Class(n, n, byMutableReference: false, reference: reference);
        if (mapped.Type is null)
        {
            return mapped;
        }

        // returned structs come back by value: no "in"
        mapped = new Mapping(mapped.Type with { CsType = mapped.Type.CsType.StartsWith("in ", System.StringComparison.Ordinal) ? mapped.Type.CsType[3..] : mapped.Type.CsType }, null);
        if (n.Name == "opencascade::handle" || n.TemplateArguments.Count > 0 || TypeRegistry.TypemappedClasses.Contains(n.Name)
            || n.Name.StartsWith("std::", System.StringComparison.Ordinal))
        {
            return mapped;
        }

        // a returned object is copied into a proxy that owns it (by value, or %occt_valueclass for const&); a move-only one
        // returned by value moves in
        var known = registry.Class(n.Name)!;
        return known.Kind is WrapKind.ValueClass or WrapKind.ValueType || (!reference && known.IsMoveOnly)
            ? mapped
            : Mapping.Skipped(reference ? $"returns const {n.Name}&, which isn't copyable" : $"returns {n.Name} by value, which isn't copyable");
    }
}
