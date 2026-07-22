# Changelog

## 0.1.0

- Move Protobuf HTTP entities out of `com.july.networking`.
- Bundle the verified Google.Protobuf 3.30.0 runtime and required .NET support DLLs.
- Compile the provider unconditionally when installed instead of relying on
  `JULYGF_PROTOBUF`.
