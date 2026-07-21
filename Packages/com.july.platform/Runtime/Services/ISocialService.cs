using Cysharp.Threading.Tasks;
using UnityEngine.UI;

namespace July.Platform
{
    
    
    public readonly struct RelationFriendRawResult
    {
        public readonly bool Success;
        public readonly string EncryptedData;
        public readonly string Iv;
        public readonly string Signature;
        public readonly string CloudID;
        public readonly string ErrorMsg;

        public RelationFriendRawResult(string encryptedData, string iv, string signature, string cloudID)
        {
            Success = true;
            EncryptedData = encryptedData;
            Iv = iv;
            Signature = signature;
            CloudID = cloudID;
            ErrorMsg = null;
        }

        public static RelationFriendRawResult Fail(string error) => new(error);

        private RelationFriendRawResult(string error)
        {
            Success = false;
            EncryptedData = null;
            Iv = null;
            Signature = null;
            CloudID = null;
            ErrorMsg = error;
        }
    }
    
    public interface ISocialService : IPlatformService
    {
        void ShowFriendRank(RawImage render, object data);
        void HideFriendRank();
        void OpenCustomerService();
        void NavigateToMiniProgram(string appId);
        UniTask<RelationFriendRawResult> GetRelationFriendsRawAsync();
    }
}

