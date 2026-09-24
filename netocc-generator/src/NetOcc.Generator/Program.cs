// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;

//
using NetOcc.Generator;
using NetOcc.Generator.Parsing;

const string Usage = """
    netocc-gen <command> [options]

    commands:
      bootstrap --occt-src <dir> [--out config/toolkits.yaml]
          toolkit -> package map from OCCT src/MODULES.cmake, TOOLKITS.cmake, PACKAGES.cmake
      dump      --occt-src <dir> --occt-include <dir> <package>
          what the parser sees in one package (development aid)
      generate  --occt-src <dir> --occt-include <dir> --core <netocc-core> [--occt-lib <dir>] [--config config/modules.yaml] [--check] [names...]
          writes <netocc-core>/src/SWIG_files/{wrapper,headers} for the named packages, toolkits or modules (default: config's generate list) and
          src/SWIG_files/{modules,classes}.json; skipped members go to log/skips/<package>.txt. --occt-lib: OCCT's DLLs
          (default <occt-include>/../../bin), whose exports tell which declared members exist. --check: write nothing,
          exit 1 if netocc-core's files differ from the output
    """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return 0;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

string Required(string command, string name) =>
    Option(name) ?? throw new ArgumentException($"netocc-gen {command}: {name} <dir> is required");

try
{
    switch (args[0])
    {
        case "bootstrap":
            return Bootstrap.Run(Required("bootstrap", "--occt-src"), Option("--out") ?? Path.Combine("config", "toolkits.yaml"));

        case "dump":
            var source = new OcctSource(Required("dump", "--occt-src"), Path.Combine("config", "toolkits.yaml"));
            var package = args[^1];
            var parser = new PackageParser(PackageParser.IncludeDirectories(Required("dump", "--occt-include")), []);
            Dump.Write(parser.Parse(package, source.Headers(package)), Console.Out);
            return 0;

        case "generate":
            // positional arguments (not options, not option values) are package names; --check is the only flag
            var packages = args.Skip(1)
                .Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || !args[i].StartsWith("--", StringComparison.Ordinal) || args[i] == "--check"))
                .ToList();
            var include = Required("generate", "--occt-include");
            return Generate.Run(Required("generate", "--occt-src"), include, Option("--occt-lib") ?? Path.Combine(include, "..", "..", "bin"),
                Required("generate", "--core"), Option("--config") ?? Path.Combine("config", "modules.yaml"), packages, Console.Out,
                check: args.Contains("--check"));

        default:
            Console.Error.WriteLine($"netocc-gen: unknown command '{args[0]}'");
            return 1;
    }
}
catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.Collections.Generic.KeyNotFoundException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
