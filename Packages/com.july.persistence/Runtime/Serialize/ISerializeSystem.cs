using System;

namespace July.Persistence
{
    public interface ISerializeSystem
    {
        byte[] Serialize<T>(T data);
        T Deserialize<T>(byte[] bytes);

        string SerializeToJson(object data);
        object DeserializeFromJson(string json, Type type);
    }
}
