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

            ShowOpenData(render);
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

        private void ShowOpenData(RawImage rawImage)
        {
            var minPoint = rawImage.rectTransform.Find("MinPoint").position;
            var maxPoint = rawImage.rectTransform.Find("MaxPoint").position;

            minPoint = RectTransformUtility.WorldToScreenPoint(null, minPoint);
            maxPoint = RectTransformUtility.WorldToScreenPoint(null, maxPoint);

            WX.ShowOpenData(rawImage.texture, (int)minPoint.x, Screen.height - (int)minPoint.y,
                Mathf.Abs((int)(maxPoint.x - minPoint.x)), Mathf.Abs((int)(maxPoint.y - minPoint.y)));
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

