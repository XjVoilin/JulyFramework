# July Build - HybridCLR

`com.july.build.hybridclr` is the strongly typed HybridCLR implementation for
`com.july.build`.

The package targets HybridCLR 8.7.0 and directly references `HybridCLR.Editor`.
Projects provide their output paths and assembly policy through
`HybridCLRBuildProfile`, then call `HybridCLRBuildService` from their build steps.

Because Unity does not resolve Git dependencies between packages, Git-based projects
must also list `com.code-philosophy.hybridclr` and `com.july.build` in their project
manifest. No reflection fallback is provided for incompatible SDK versions.
