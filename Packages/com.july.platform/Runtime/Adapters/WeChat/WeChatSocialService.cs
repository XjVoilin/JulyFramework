#if JULYGF_WX_MINIGAME
using System;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WeChatWASM;

namespace July.Platform
{
    public class WeChatSocialService : ISocialService, INeedGetService
    {
        private readonly Vector3[] _worldCorners = new Vector3[4];
        private WXOpenDataContext _openDataContext;

        public Func<Type, object> ServiceGetter { get; set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void JulyBridge_GetRelationFriendList(int callbackId);
#endif

        public void Init() { }

        public void DeferredInit()
        {
            _openDataContext = WX.GetOpenDataContext(new OpenDataContextOption
            {
                sharedCanvasMode = CanvasType.ScreenCanvas
            });
        }

        public void ShowFriendRank(RawImage render, object data)
        {
            if (data is not string msgStr)
            {
                Debug.LogError("[WeChatSocial] data must be a JSON string for OpenData context");
                return;
            }

            if (!TryShowOpenData(render))
            {
                return;
            }

            _openDataContext.PostMessage(msgStr);
        }

        public void HideFriendRank()
        {
            WX.HideOpenData();
        }

        public void OpenCustomerService()
        {
            var serviceConversation = new OpenCustomerServiceConversationOption();
            WX.OpenCustomerServiceConversation(serviceConversation);
        }

        public void NavigateToMiniProgram(string appId)
        {
            var option = new NavigateToMiniProgramOption
            {
                appId = appId,
                success = _ => { },
                fail = _ => { },
            };
            WX.NavigateToMiniProgram(option);
        }

        public async UniTask<RelationFriendRawResult> GetRelationFriendsRawAsync()
        {
            try
            {
                string json = string.Empty;
#if UNITY_WEBGL && !UNITY_EDITOR
                json = await JsBridge.CallJsAsync(JulyBridge_GetRelationFriendList);
#else
                await UniTask.CompletedTask;
                throw new Exception("JsBridge only works on WebGL platform");
#endif
                var response = JsonUtility.FromJson<RelationFriendResponse>(json);
                return new RelationFriendRawResult(
                    response.encryptedData,
                    response.iv,
                    response.signature,
                    response.cloudID);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WeChatSocial] GetRelationFriendsRawAsync failed: {e.Message}");
                return RelationFriendRawResult.Fail(e.Message);
            }
        }

        private bool TryShowOpenData(RawImage rawImage)
        {
            if (rawImage == null)
            {
                Debug.LogError("[WeChatSocial] Rank RawImage is null.");
                return false;
            }

            var canvas = rawImage.canvas;
            if (canvas == null)
            {
                Debug.LogError("[WeChatSocial] Rank RawImage is not under a Canvas.");
                return false;
            }

            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && camera == null)
            {
                Debug.LogError("[WeChatSocial] Canvas worldCamera is missing.");
                return false;
            }

            Canvas.ForceUpdateCanvases();
            rawImage.rectTransform.GetWorldCorners(_worldCorners);

            var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _worldCorners[0]);
            var topRight = RectTransformUtility.WorldToScreenPoint(camera, _worldCorners[2]);

            var left = Mathf.FloorToInt(bottomLeft.x);
            var bottom = Mathf.FloorToInt(bottomLeft.y);
            var right = Mathf.CeilToInt(topRight.x);
            var top = Mathf.CeilToInt(topRight.y);
            var width = right - left;
            var height = top - bottom;

            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[WeChatSocial] Invalid rank viewport: ({left}, {bottom}) - ({right}, {top}).");
                return false;
            }

            WX.ShowOpenData(rawImage.texture, left, Screen.height - top, width, height);
            return true;
        }

        [Serializable]
        private struct RelationFriendResponse
        {
            public string signature;
            public string encryptedData;
            public string iv;
            public string cloudID;
        }
    }
}
#endif
