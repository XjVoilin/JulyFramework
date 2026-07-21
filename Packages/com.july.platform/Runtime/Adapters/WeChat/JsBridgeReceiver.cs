using UnityEngine;

namespace July.Platform
{
    /// <summary>
    /// JS鈫扖# 娑堟伅鎺ユ敹鍣ㄣ€傜敱 JsBridge.Init 鑷姩鍒涘缓骞舵寕杞藉埌 "JulyJsBridge" GameObject 涓娿€?    /// </summary>
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

