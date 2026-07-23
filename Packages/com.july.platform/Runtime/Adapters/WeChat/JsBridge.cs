using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Platform
{
    /// <summary>
    /// JS-C# 桥接基础设施。仅负责消息路由，不解释 status 语义。
    /// status 由各业务 API 自行约定（success / fail / complete / cancel / 自定义）。
    /// data 为 JSON 字符串，业务侧自行反序列化。
    /// </summary>
    public static class JsBridge
    {
        public delegate void Handler(string status, string data);

        /// <summary>
        /// jslib 函数签名：接收一个 callbackId，由 jslib 在异步完成时通过桥回调。
        /// </summary>
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
        /// 注册一次性回调：首次触发后自动注销，适用于请求-响应型 API。
        /// </summary>
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
        /// 注册可重复回调：事件型 API（如进度、订阅）使用，需调用方手动 Remove。
        /// </summary>
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
        /// 调用一个 jslib 函数并等待它的桥回调，返回 success 时的 data。
        /// fail → 抛异常；ct 取消 → 抛 OperationCanceledException。
        /// </summary>
        /// <param name="jsCall">要调用的 jslib 函数（签名必须是 void(int callbackId)）</param>
        /// <param name="ct">取消令牌</param>
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
