using UnityEngine;

namespace July.Platform
{
    /// <summary>
    /// JS→C# 消息接收器。由 JsBridge.Init 自动创建并挂载到 "JulyJsBridge" GameObject 上。
    /// </summary>
    public class JsBridgeReceiver : MonoBehaviour
    {
        [System.Serializable]
        private struct Message
        {
            public int id;
            public string status;
            public string data;
        }

        public void OnMessage(string json)
        {
            var msg = JsonUtility.FromJson<Message>(json);
            JsBridge.Dispatch(msg.id, msg.status, msg.data);
        }
    }
}
