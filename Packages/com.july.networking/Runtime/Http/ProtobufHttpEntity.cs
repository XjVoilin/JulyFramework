#if JULYGF_PROTOBUF
using Google.Protobuf;

namespace July.Networking
{
    /// <summary>Shared Protobuf JSON policy for HTTP: ignore unknown response fields.</summary>
    public static class ProtobufJsonCodec
    {
        private static readonly JsonParser ResponseParser = new(
            JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

        public static string Serialize(IMessage message) =>
            message == null ? null : JsonFormatter.Default.Format(message);

        public static TResponse Deserialize<TResponse>(string json)
            where TResponse : class, IMessage, new() =>
            ResponseParser.Parse<TResponse>(json);
    }

    public abstract class ProtobufHttpEntity<TRequest, TResponse>
        : HttpEntity<TRequest, TResponse>
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        public override TRequest RqtData { get; } = new();

        protected override string BuildBody() => ProtobufJsonCodec.Serialize(RqtData);

        protected override void SetResponseData(string dataJson) =>
            RespData = ProtobufJsonCodec.Deserialize<TResponse>(dataJson);
    }

    public abstract class ProtobufHttpQueueEntity<TRequest, TResponse>
        : HttpQueueEntity<TRequest, TResponse>
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        public override TRequest RqtData { get; } = new();

        protected override string BuildBody() => ProtobufJsonCodec.Serialize(RqtData);

        protected override void SetResponseData(string dataJson) =>
            RespData = ProtobufJsonCodec.Deserialize<TResponse>(dataJson);
    }
}
#endif
