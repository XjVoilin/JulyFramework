# July Networking - Protobuf

`com.july.networking.protobuf` adds Google.Protobuf request and response entities to
`com.july.networking` without adding Protobuf to the base HTTP module.

The public types remain in the `July.Networking` namespace:

- `ProtobufJsonCodec`
- `ProtobufHttpEntity<TRequest, TResponse>`
- `ProtobufHttpQueueEntity<TRequest, TResponse>`

The provider bundles the Google.Protobuf 3.30.0 runtime and its required .NET support
assemblies. Response JSON always ignores unknown fields for forward compatibility.
