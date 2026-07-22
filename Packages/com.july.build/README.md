# July Build

`com.july.build` owns the reusable build pipeline and editor-side build algorithms.
Projects derive `BuildContext` for their release policy and implement thin typed step adapters.

The HybridCLR implementation is optional at runtime: it discovers the installed HybridCLR editor
SDK, validates the required API surface, and receives all project paths through
`HybridCLRBuildProfile`. YooAsset collector configuration belongs to
`com.july.resource.yooasset` rather than this package.
