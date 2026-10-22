# Native build

Source: meshoptimizer v1.0, commit 73583c335e541c139821d0de2bf5f12960a04941.
The distributed Windows x86_64 DLL was compiled with Zig 0.14.1:

```powershell
New-Item -ItemType Directory -Force build
zig c++ bridge.cpp meshoptimizer/simplifier.cpp -Imeshoptimizer -target x86_64-windows-gnu -shared -static -O2 -DNDEBUG -fno-exceptions -fno-rtti -o build/JulyMeshOptimizer.dll
Copy-Item build/JulyMeshOptimizer.dll ../Editor/Plugins/Windows/x86_64/JulyMeshOptimizer.dll
```

Keep generated import libraries and debug symbols inside this ignored Native~ build directory; only copy the DLL into Editor/Plugins.

Alternatively build this directory with CMake and a C++11 compiler. Put the resulting Windows x86_64 DLL at the path above and retain its Editor-only PluginImporter settings. This package does not yet distribute other platform binaries. Review compatibility and test before adding one.

The upstream header and simplifier source retain their license notices. See the package's Third Party Notices directory for the complete MIT license.
