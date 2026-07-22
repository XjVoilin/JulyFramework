# Changelog

## 0.3.0

- Move HybridCLR-specific build operations into `com.july.build.hybridclr`.
- Keep this package independent of third-party build SDKs.

## 0.2.1

- Route HybridCLR installation and standalone Generate All through the optional-SDK reflection adapter.

## 0.2.0

- Allow projects to derive their own build context and validate it through the shared runner.
- Add editor asset refresh lifecycle support to build hosts.
- Add reusable HybridCLR generation, DLL copy, metadata check, AOT backup and hot-update operations.
- Add deterministic AOT source-tree fingerprinting.
