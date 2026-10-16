# ADLX (prebuilt)

`ADLXWrapper.dll` (C#, netstandard2.0) and `ADLXBindings.dll` (native x64) from
[Rem0o/ADLXWrapper](https://github.com/Rem0o/ADLXWrapper), built against the
[AMD ADLX SDK](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX) v1.5. There is no NuGet package, so they are committed here.
`amdadlx64.dll` itself is part of the AMD driver and is not shipped.

Two changes to the upstream build (both in `build.ps1`):

1. `%pointer_functions(IADLXFPS*, fpsP_Ptr)` in `ADLXBindings.i`, so C# can call `GetCurrentFPS`.
2. Static VC++ runtime (`static-crt.props`), so no VC++ redistributable is needed.

## Rebuild

Needs once: VS Build Tools with the C++ workload and swig (see the top of `build.ps1`). Then:

```powershell
lib/adlx/build.ps1
```

## License

The ADLXWrapper repository has no LICENSE file. The ADLX SDK is MIT.
