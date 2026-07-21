using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Platform
{
    /// <summary>
    /// JS-C# 妗ユ帴鍩虹璁炬柦銆備粎璐熻矗娑堟伅璺敱锛屼笉瑙ｉ噴 status 璇箟銆?    /// status 鐢卞悇涓氬姟 API 鑷绾﹀畾锛坰uccess / fail / complete / cancel / 鑷畾涔夛級銆?    /// data 涓?JSON 瀛楃涓诧紝涓氬姟渚ц嚜琛屽弽搴忓垪鍖栥€?    /// </summary>
    public static class JsBridge
    {
        public delegate void Handler(string status, string data);

        /// <summary>
        /// jslib 鍑芥暟绛惧悕锛氭帴鏀朵竴涓?callbackId锛岀敱 jslib 鍦ㄥ紓姝ュ畬鎴愭椂閫氳繃妗ュ洖璋冦€?        /// </summary>
        public delegate void JsCall(int callbackId);

        private static int _nextId = 1;
        private static readonly Dictionary<int, Handler> _handlers = new();
        private static bool _initialized;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void JulyBridge_Init(string goName);
#endif

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var go = new GameObject("JulyJsBridge");
            go.AddComponent<JsBridgeReceiver>();
            UnityEngine.Object.DontDestroyOnLoad(go);

#if UNITY_WEBGL && !UNITY_EDITOR
            JulyBridge_Init("JulyJsBridge");
#endif
        }

        /// <summary>
        /// 娉ㄥ唽涓€娆℃€у洖璋冿細棣栨瑙﹀彂鍚庤嚜鍔ㄦ敞閿€锛岄€傜敤浜庤姹?鍝嶅簲鍨?API銆?        /// </summary>
        public static int RegisterOnce(Handler handler)
        {
            var id = _nextId++;
            _handlers[id] = (status, data) =>
            {
                _handlers.Remove(id);
                handler(status, data);
            };
            return id;
        }

        /// <summary>
        /// 娉ㄥ唽鍙噸澶嶅洖璋冿細浜嬩欢鍨?API锛堝杩涘害銆佽闃咃級浣跨敤锛岄渶璋冪敤鏂规墜鍔?Remove銆?        /// </summary>
        public static int Register(Handler handler)
        {
            var id = _nextId++;
            _handlers[id] = handler;
            return id;
        }

        public static bool Remove(int id) => _handlers.Remove(id);

        public static void Dispatch(int id, string status, string data)
        {
            if (_handlers.TryGetValue(id, out var h))
                h(status, data);
        }

        /// <summary>
        /// 璋冪敤涓€涓?jslib 鍑芥暟骞剁瓑寰呭畠鐨勬ˉ鍥炶皟锛岃繑鍥?success 鏃剁殑 data銆?        /// fail 鈫?鎶涘紓甯革紱ct 鍙栨秷 鈫?鎶?OperationCanceledException銆?        /// </summary>
        /// <param name="jsCall">瑕佽皟鐢ㄧ殑 jslib 鍑芥暟锛堢鍚嶅繀椤绘槸 void(int callbackId)锛?/param>
        /// <param name="ct">鍙栨秷浠ょ墝</param>
        public static async UniTask<string> CallJsAsync(JsCall jsCall, CancellationToken ct = default)
        {
            var utcs = new UniTaskCompletionSource<string>();

            var callbackId = RegisterOnce((status, data) =>
            {
                if (status == "success") utcs.TrySetResult(data);
                else utcs.TrySetException(new Exception(data));
            });

            using var ctReg = ct.Register(() =>
            {
                if (Remove(callbackId)) utcs.TrySetCanceled(ct);
            });

            jsCall(callbackId);
            return await utcs.Task;
        }
    }
}
