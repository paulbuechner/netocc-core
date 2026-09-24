// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//
using NetOcc.Generator.Config;
using NetOcc.Generator.Emit;
using NetOcc.Generator.Mapping;
using NetOcc.Generator.Model;
using NetOcc.Generator.Parsing;

namespace NetOcc.Generator;

/// <summary>
/// <c>netocc-gen generate</c>: parse the configured packages, map them to netocc-core's contract and write
/// <c>src/SWIG_files/{wrapper,headers}</c> plus <c>src/SWIG_files/modules.json</c> (the modules per native library) and
/// <c>classes.json</c> (the types per package, for the documentation's class index). Packages not
/// generated stay hand-written; their .i files tell the generator which types exist.
/// </summary>
internal static class Generate
{
    /// <param name="occtLibraries">OCCT's DLLs, whose export tables tell which out-of-line members exist.</param>
    /// <param name="check">Compare instead of writing: 1 if any output file would change.</param>
    public static int Run(string occtSource, string occtInclude, string occtLibraries, string core, string configPath, IReadOnlyList<string> only,
        TextWriter output, bool check = false)
    {
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var config = GeneratorConfig.Load(configPath);
        var source = new OcctSource(occtSource, Path.Combine(configDirectory, "toolkits.yaml"));
        var configured = Expand([.. config.Generate, .. config.AliasPackages.Keys], source, config);
        var packages = only.Count > 0 ? Expand(only, source, config) : configured;
        var swigFiles = Path.Combine(core, "src", "SWIG_files");
        var wrapper = Path.Combine(swigFiles, "wrapper");
        var headers = Path.Combine(swigFiles, "headers");
        var skipDirectory = Path.Combine(configDirectory, "..", "log", "skips");
        Directory.CreateDirectory(skipDirectory);

        // modules this run doesn't write: hand-written, or generated earlier. Their .i files tell what they provide and import.
        var registry = new TypeRegistry();
        foreach (var file in Directory.EnumerateFiles(wrapper, "*.i").Order(StringComparer.Ordinal))
        {
            var package = Path.GetFileNameWithoutExtension(file);
            if (packages.Contains(package))
            {
                continue;
            }

            registry.ScanHandWritten(file);
            if (!configured.Contains(package))
            {
                output.WriteLine($"warning: {package}.i is hand-written and not in config generate, so build.py won't build it (modules.json)");
            }
        }

        var exports = LibraryExports.Load(occtLibraries);
        output.WriteLine(exports is null
            ? $"warning: no OCCT DLLs (TK*.dll) in {Path.GetFullPath(occtLibraries)}: members declared exported but never defined won't link"
            : $"exports: {exports.Count} symbols from {exports.Libraries} OCCT libraries");
        // one translation unit per package, parsed in parallel (each has its own libclang index); a package that doesn't
        // parse is reported at the end, and the others are still written. Registering stays in package order.
        var parser = new PackageParser(PackageParser.IncludeDirectories(occtInclude), [], exports);
        // every package's value types: a plain-data struct may hold another package's (Geom_Curve::ResD1 holds a gp_Pnt)
        HashSet<string> valueTypes = [.. config.Packages.Values.SelectMany(p => p.ValueTypes)];
        var parsed = new (PackageModel? Model, string? Error)[packages.Count];
        var done = 0;
        var clock = Stopwatch.StartNew();
        Parallel.For(0, packages.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, i =>
        {
            try
            {
                var packageConfig = config.For(packages[i]);
                IReadOnlyList<string> all = config.AliasPackages.ContainsKey(packages[i]) ? [] : source.Headers(packages[i]);
                // generated on Windows, built everywhere: OCCT's Windows-only headers (OSD_WNT.hxx includes windows.h) stay out
                List<string> headers = [.. all.Where(h => !IsWindowsOnly(h))];
                var model = parser.Parse(packages[i], headers, packageConfig.Prelude, valueTypes, source.AliasHeaders(packages[i]), packageConfig.Classes);
                parsed[i] = (model with
                {
                    ExcludedHeaders = [.. all.Where(IsWindowsOnly).Select(h => $"{h}: Windows only (OCCT's WNT headers)"), .. model.ExcludedHeaders ?? []],
                    // exclude_namespaces: gone before anything sees them, the nested header included
                    Enums = [.. model.Enums.Where(e => !packageConfig.IsExcludedScope(e.QualifiedName))],
                    Classes = [.. model.Classes.Where(c => !packageConfig.IsExcludedScope(c.QualifiedName))],
                    NamespaceFunctions = [.. model.NamespaceFunctions.Where(f => !packageConfig.IsExcludedScope(f.Namespace))],
                }, null);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException)
            {
                parsed[i] = (null, e.Message);
            }

            Console.Error.WriteLine($"parsed {packages[i]} ({Interlocked.Increment(ref done)}/{packages.Count}, {clock.Elapsed.TotalSeconds:F0} s)");
        });

        List<PackageModel> models = [];
        List<string> failed = [];
        foreach (var (model, error) in parsed)
        {
            if (model is null)
            {
                failed.Add(error!);
                continue;
            }

            models.Add(model);
        }

        models = OwnInstances(models);

        // every class first: the collections the aliases name may hold instances of later packages
        foreach (var model in models)
        {
            Register(registry, model, config.For(model.Name));
        }

        foreach (var model in models)
        {
            RegisterAliases(registry, model);
        }

        // a run over some packages keeps what their modules instantiate: modules it doesn't write may use it
        if (only.Count > 0)
        {
            foreach (var model in models.Where(m => File.Exists(Path.Combine(wrapper, $"{m.Name}.i"))))
            {
                registry.KeepRequested(Path.Combine(wrapper, $"{model.Name}.i"));
            }
        }

        // value-type structs: the generated part next to the hand-written partial (src/NetOcc/<Pkg>/<Type>.cs)
        var structs = Path.Combine(core, "src", "NetOcc");
        Dictionary<string, HandWritten> partials = [];
        HandWritten HandWrittenOf(string package, string type)
        {
            var path = Path.Combine(structs, package, $"{type}.cs");
            if (!partials.TryGetValue(path, out var partial))
            {
                partial = partials[path] = HandWritten.Load(path);
            }

            return partial;
        }

        var classes = models.SelectMany(m => m.Classes).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
        var writer = new InterfaceWriter(registry, new SignatureMapper(registry), source.Version, p => config.For(p).Prelude, HandWrittenOf,
            classes.GetValueOrDefault);
        bool HasExtras(PackageModel model) => File.Exists(Path.Combine(swigFiles, "extras", $"{model.Name}.i"));

        // a package instantiates the collections its aliases name once a member anywhere uses them: a first pass finds those,
        // and gives the ones no alias names to their first user
        List<(string Package, IEnumerable<KnownInstantiation> Collections)> used = [];
        foreach (var model in models)
        {
            var collections = writer.Write(model, config.For(model.Name), _ => [], HasExtras(model)).Collections ?? [];
            used.Add((model.Name, collections));
        }

        registry.AssignOwners(used, models.Select(m => m.Name).ToHashSet());
        foreach (var collection in used.SelectMany(u => u.Collections))
        {
            registry.Request(collection);
        }

        var requested = registry.RequestCount;

        // a module imports what it uses and, transitively, what those use; packages may use each other (SWIG is fine with
        // cycles). A second pass collects every module's direct imports.
        var direct = models.ToDictionary(m => m.Name, m => writer.Write(m, config.For(m.Name), _ => [], HasExtras(m)).Imports);
        IReadOnlyList<string> ImportsOf(string package) => direct.TryGetValue(package, out var imports) ? imports : registry.ImportsOf(package);

        var outputs = new Outputs(check);
        foreach (var model in models)
        {
            var module = writer.Write(model, config.For(model.Name), ImportsOf, HasExtras(model));
            foreach (var collection in module.Collections ?? [])
            {
                registry.Request(collection);
            }

            outputs.Emit(Path.Combine(wrapper, $"{model.Name}.i"), module.Interface);
            outputs.Emit(Path.Combine(headers, $"{model.Name}_module.hxx"), module.ModuleHeader);
            var nestedHeader = Path.Combine(headers, InterfaceWriter.NestedHeaderOf(model.Name));
            if (module.NestedHeader is { } nested)
            {
                outputs.Emit(nestedHeader, nested);
            }

            var directory = Path.Combine(structs, model.Name);
            foreach (var generated in module.Structs ?? [])
            {
                Directory.CreateDirectory(directory);
                outputs.Emit(Path.Combine(directory, $"{generated.Name}.g.cs"), generated.Code);
            }

            // what the package no longer has: a struct, its nested types
            IEnumerable<string> owned = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.g.cs") : [];
            foreach (var path in owned.Append(nestedHeader))
            {
                outputs.RemoveUnlessWritten(path);
            }

            WriteLf(Path.Combine(skipDirectory, $"{model.Name}.txt"), string.Concat(module.Skipped.Select(s => s + "\n")));
            output.WriteLine($"{model.Name}: {model.Enums.Count} enums, {model.Classes.Count} classes, {module.Skipped.Count} members skipped"
                + (module.Imports.Count > 0 ? $"; imports {string.Join(" ", module.Imports)}" : ""));
        }

        // the first pass saw every member: a later request would mean a module was written without its collections
        if (registry.RequestCount != requested)
        {
            throw new InvalidOperationException($"collections requested after the first pass ({registry.RequestCount - requested})");
        }

        // the modules build.py runs SWIG over, in toolkits.yaml order
        outputs.Emit(Path.Combine(swigFiles, "modules.json"),
            ModulesJson(configured, p => config.AliasPackages.TryGetValue(p, out var module) ? module : source.ModuleOf(p)));
        // the types each package gives C#, which the documentation links to OCCT's reference manual; a run over some
        // packages keeps the others' entries
        var classIndex = Path.Combine(swigFiles, "classes.json");
        outputs.Emit(classIndex, ClassIndex.Json(configured, models.ToDictionary(m => m.Name, m => ClassIndex.Of(m, config.For(m.Name), registry)),
            File.Exists(classIndex) ? File.ReadAllText(classIndex) : null));

        output.WriteLine($"skip logs: {Path.GetFullPath(skipDirectory)}");
        foreach (var message in failed)
        {
            output.WriteLine($"error: {message}");
        }

        if (!check)
        {
            return failed.Count == 0 ? 0 : 1;
        }

        foreach (var path in outputs.Changed)
        {
            output.WriteLine($"out of date: {Path.GetFullPath(path)}");
        }

        output.WriteLine(outputs.Changed.Count == 0 ? "check: netocc-core is up to date" : $"check: {outputs.Changed.Count} file(s) differ; run generate");
        return outputs.Changed.Count == 0 && failed.Count == 0 ? 0 : 1;
    }

    // OCCT names its Windows-only headers after WNT (Windows NT): OSD_WNT.hxx, the WNT package
    private static bool IsWindowsOnly(string header) => header.StartsWith("WNT_", StringComparison.Ordinal) || header.Contains("_WNT", StringComparison.Ordinal);

    // config entries (packages, toolkits, modules, alias packages) as packages, each once, in the order they come
    private static List<string> Expand(IEnumerable<string> entries, OcctSource source, GeneratorConfig config) =>
        [.. entries.SelectMany(e => config.AliasPackages.ContainsKey(e) ? [e] : source.Expand(e)).Where(p => !config.ExcludePackages.Contains(p)).Distinct()];

    // the SWIG modules of each OCCT module, which build.py compiles into one native library each (a Windows DLL exports at
    // most 65535 functions); modules in toolkits.yaml order
    private static string ModulesJson(IReadOnlyList<string> packages, Func<string, string> moduleOf)
    {
        var groups = packages.GroupBy(moduleOf).Select(g => $"  \"{g.Key}\": [\n{string.Join(",\n", g.Select(p => $"    \"{p}\""))}\n  ]");
        return "{\n" + string.Join(",\n", groups) + "\n}\n";
    }

    /// <summary>
    /// A class template instance a signature uses is a class of its first user, in package order, the only one that keeps
    /// it, or of its template's package when it names no other package's type (<c>NCollection_Vec3&lt;float&gt;</c>). An OCCT
    /// alias anywhere names it (the first in package order; <c>typedef Extrema_GGExtPC&lt;...&gt; Extrema_ExtPC</c>),
    /// otherwise its flat name does; the owner's nested header declares the name, the same alias again where OCCT has one.
    /// </summary>
    private static List<PackageModel> OwnInstances(List<PackageModel> models)
    {
        Dictionary<string, string> aliases = [];
        foreach (var typedef in models.SelectMany(m => m.Typedefs))
        {
            if (typedef.Target is NamedType { TemplateArguments.Count: > 0 } target && CollectionTemplate.Named(target.Name) is null)
            {
                aliases.TryAdd((target with { Const = false }).Spelling, typedef.Name);
            }
        }

        // the instances that go home to their template's package, the first definition each
        var names = models.Select(m => m.Name).ToHashSet();
        string? Home(ClassModel c) => c.Instance?.Type.TemplatePackage is { } home && names.Contains(home) ? home : null;
        Dictionary<string, List<ClassModel>> homed = [];
        HashSet<string> owned = [];
        foreach (var c in models.SelectMany(m => m.Classes))
        {
            if (Home(c) is { } home && owned.Add(c.Instance!.Type.Spelling))
            {
                (homed.TryGetValue(home, out var list) ? list : homed[home] = []).Add(c);
            }
        }

        // the others stay with their first user in package order: an instance several packages use is one class
        ClassModel Aliased(ClassModel c) => c.Instance is { } instance && aliases.TryGetValue(instance.Type.Spelling, out var alias) ? c with { Name = alias } : c;
        List<PackageModel> owners = [];
        HashSet<string> ownedEnums = [];
        foreach (var m in models)
        {
            List<ClassModel> classes = [];
            foreach (var c in m.Classes)
            {
                if (c.Instance is null || (Home(c) is null && owned.Add(c.Instance.Type.Spelling)))
                {
                    classes.Add(Aliased(c));
                }
            }

            classes.AddRange((homed.GetValueOrDefault(m.Name) ?? []).Select(Aliased));
            // an enum in an instance (BVH_Tools<double, 3>::BVH_PrjStateInTriangle) is its first user's too
            owners.Add(m with
            {
                Classes = classes,
                Enums = [.. m.Enums.Where(e => e.QualifiedName is not { } qualified || !qualified.Contains('<') || ownedEnums.Add(qualified))],
            });
        }

        return owners;
    }

    // what a generated package provides, registered before any module is written so packages can use each other
    private static void Register(TypeRegistry registry, PackageModel model, PackageConfig config)
    {
        // a nested or namespace type is known by its flat name, which the package's nested header declares, and so is a
        // class template instance no alias names
        string HeaderOf(string header, string? qualifiedName, ClassInstance? instance = null) =>
            qualifiedName is null && instance is null ? header : InterfaceWriter.NestedHeaderOf(model.Name);
        foreach (var e in model.Enums)
        {
            registry.AddEnum(e.Name, model.Name, HeaderOf(e.Header, e.QualifiedName), e.Underlying, e.QualifiedName);
        }

        // the configured value types whatever `classes` lists (gp lists only gp), plain data where listed
        List<ClassModel> registered = [];
        foreach (var c in ClassRules.ValueTypes(model, config))
        {
            registry.AddClass(new KnownClass(c.Name, model.Name, WrapKind.ValueType, HeaderOf(c.Header, c.QualifiedName, c.Instance),
                c.Traits.HasDefaultConstructor));
            registered.Add(c);
        }

        foreach (var c in model.Classes.Where(c => ClassRules.IsSelected(c, config) && !ClassRules.IsCovered(c)))
        {
            // a value class's copies are owned by proxies, which delete them
            var kind = c.Traits.IsTransient ? WrapKind.Transient
                : c.Traits.IsCopyable && !c.Traits.IsAbstract && c.Traits.IsCreatable && c.Traits.HasPublicDestructor ? WrapKind.ValueClass
                : WrapKind.Plain;
            registry.AddClass(new KnownClass(c.Name, model.Name, kind, HeaderOf(c.Header, c.QualifiedName, c.Instance),
                c.Traits.HasDefaultConstructor, ClassRules.IsMoveOnly(c)));
            registered.Add(c);
        }

        foreach (var c in registered.Where(c => c.Instance is not null))
        {
            registry.AddInstance(c.Instance!.Type, c.Name);
        }
    }

    // OCCT's aliases for collection instantiations name the C# classes: typedef NCollection_Array1<gp_Pnt> TColgp_Array1OfPnt
    private static void RegisterAliases(TypeRegistry registry, PackageModel model)
    {
        foreach (var typedef in model.Typedefs)
        {
            if (typedef.Target is NamedType { TemplateArguments.Count: > 0 } target && CollectionTemplate.Named(target.Name) is { } template
                && target.TemplateArguments.Count == template.Arguments.Count)
            {
                List<CppType> arguments = [.. target.TemplateArguments.Select(a => registry.Resolve(KnownInstantiation.Unconst(a)))];
                registry.AddInstantiation(new KnownInstantiation(template, [.. arguments.Select(a => a.Spelling)], typedef.Name, model.Name, arguments));
            }
        }
    }

    // generated files are identical on every OS
    private static void WriteLf(string path, string text) => File.WriteAllText(path, GeneratedText.Lf(text));

    /// <summary>The files a run writes, or in a check compares: the ones that differ, and the generator's own it no longer writes.</summary>
    private sealed class Outputs(bool check)
    {
        // case-insensitive: on Windows and macOS a stale path differing in case from a written one is that file
        private readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Changed { get; } = [];

        public void Emit(string path, string text)
        {
            _written.Add(Path.GetFullPath(path));
            var lf = GeneratedText.Lf(text);
            if (File.Exists(path) && File.ReadAllText(path) == lf)
            {
                return;
            }

            Changed.Add(path);
            if (!check)
            {
                WriteLf(path, lf);
            }
        }

        /// <summary>Deletes a generated file the run didn't write (in a check, reports it): its type went away.</summary>
        public void RemoveUnlessWritten(string path)
        {
            if (!File.Exists(path) || _written.Contains(Path.GetFullPath(path)))
            {
                return;
            }

            Changed.Add(path);
            if (!check)
            {
                File.Delete(path);
            }
        }
    }
}
