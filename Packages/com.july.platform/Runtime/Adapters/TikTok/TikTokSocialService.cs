#if JULYGF_DY_MINIGAME
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UI;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace July.Platform
{
    public class TikTokSocialService : ISocialService
    {
        public void ShowFriendRank(RawImage render, object data)
        {
            if (data is not JsonData jsonData) return;
            TT.GetImRankList(jsonData, (_, _) => { });
        }

        public void HideFriendRank()
        {
        }

        public void OpenCustomerService()
        {
            var param = new JsonData { ["type"] = 3 };
            TT.OpenCustomerServiceConversation(param, _ => { });
        }

        public void NavigateToMiniProgram(string appId)
        {
            var param = new NavigateToMiniProgramParam
            {
                AppId = appId,
                Success = _ => { },
                Fail = _ => { },
            };
            TT.NavigateToMiniProgram(param);
        }

        public UniTask<RelationFriendRawResult> GetRelationFriendsRawAsync()
        {
            return UniTask.FromResult(RelationFriendRawResult.Fail("Not supported on TikTok platform"));
        }
    }
}
#endif

