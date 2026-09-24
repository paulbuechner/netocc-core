# Building from source

The [netocc repository](https://github.com/paulbuechner/netocc) holds four folders: netocc-core (the bindings), netocc-generator (writes netocc-core's interface files from OCCT's headers), netocc-documentation (this site) and netocc-demos.

netocc-core builds on its own. It needs Python, vcpkg, CMake with Ninja, the .NET SDK 8 or newer, and on Windows Visual Studio with the C++ tools:

```bash
cd netocc-core
python build.py tools                        # SWIG from conda-forge into .tools/
python build.py occt --triplet x64-windows   # OCCT 8.0.1 through vcpkg, about an hour the first time
python build.py generate                     # SWIG over the interface files
python build.py native --triplet x64-windows # native libraries into artifacts/runtimes/win-x64/native
python build.py test --arch x64              # every framework
python build.py pack --rids win-x64          # NuGet packages into artifacts/packages
```

Triplets: `x64-windows`, `x86-windows`, `x64-linux-dynamic`, `arm64-osx-dynamic`.

This site:

```bash
cd netocc-documentation
dotnet tool restore
python classes.py        # the class index, from netocc-core/src/SWIG_files/classes.json
dotnet docfx docfx.json --serve
```
