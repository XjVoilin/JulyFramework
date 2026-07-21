using System;
using System.Text;
using July.Arch;
using July.Logging;
using LitJson;
using UnityEngine;

namespace July.Persistence
{
    public class JsonSerializeSystem : SystemBase, ISerializeSystem
    {
        public byte[] Serialize<T>(T data)
        {
            if (data == null) return Array.Empty<byte>();
            try
            {
                var json = JsonMapper.ToJson(data);
                return Encoding.UTF8.GetBytes(json);
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                throw;
            }
        }

        public T Deserialize<T>(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return default;
            try
            {
                var json = Encoding.UTF8.GetString(bytes);
                return JsonMapper.ToObject<T>(json);
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                throw;
            }
        }

        public string SerializeToJson(object data)
        {
            if (data == null) return "{}";
            try
            {
                return JsonMapper.ToJson(data);
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                throw;
            }
        }

        public object DeserializeFromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonMapper.ToObject(json, type);
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                throw;
            }
        }
    }
}
