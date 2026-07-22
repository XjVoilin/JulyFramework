# July Build

`com.july.build` owns the vendor-neutral build pipeline and editor-side build algorithms.
Projects derive `BuildContext` for their release policy and implement thin typed step adapters.

Install `com.july.build.hybridclr` when the project uses HybridCLR. The provider directly
references the supported HybridCLR editor SDK, so incompatible SDK changes fail at compile time.
YooAsset collector configuration belongs to `com.july.resource.yooasset`.
