# Bundled COSCLI

Version: **v1.0.8**. Unmodified official binaries from https://github.com/tencentyun/coscli/releases/tag/v1.0.8 .
The binaries previously used by GooseMarket match the upstream release SHA-256 digests; no tool upgrade is part of this move.

- windows-x64/coscli.exe: Windows x64 editor/build machine.
- macos-arm64/coscli: Apple Silicon macOS. Other editor host architectures are not included.

manifest.json records the source URLs and SHA-256 of each binary. Keep version, binary files, manifest and license together when deliberately updating the tool. Builds never download tools.

Tools~ is distributed with the UPM package and excluded from Unity asset importing. Locate it via PackageInfo.resolvedPath, not a fixed Library/PackageCache folder. macOS execution uses a disposable copy in Library/July.Release/Tools; do not chmod or write the package cache.

Credentials and logs are not part of this directory. The caller supplies Tools/coscli/.cos.yaml in its project workspace.
