using Cysharp.Threading.Tasks;
using UnityEngine.UI;

namespace July.Platform
{
    public class DefaultSocialService : ISocialService
    {
        public void ShowFriendRank(RawImage render, object data) { }
        public void HideFriendRank() { }
        public void OpenCustomerService() { }
        public void NavigateToMiniProgram(string appId) { }

        public UniTask<RelationFriendRawResult> GetRelationFriendsRawAsync()
        {
            return UniTask.FromResult(RelationFriendRawResult.Fail("Not supported on this platform"));
        }
    }
}

