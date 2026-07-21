using July.Arch;
#if JULYGF_DY_MINIGAME
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public class TikTokADsService : IADsService, ICanEvent
    {
        private const string AdUnitId = "8k6h6jb2mjbk3hobnc";

        private TTRewardedVideoAd _videoAd;
        private bool _isLoaded;

        public void Init()
        {
        }

        public void DeferredInit()
        {
            if (_videoAd != null) return;
            var param = new CreateRewardedVideoAdParam { AdUnitId = AdUnitId };
            _videoAd = TT.CreateRewardedVideoAd(param);
            _videoAd.OnLoad += OnAdLoaded;
            _videoAd.OnError += OnAdError;
            _videoAd.OnClose += OnAdClose;
            _videoAd.Load();
            Debug.Log("[TikTokADsService] 广告实例延迟创建完成");
        }

        public bool HasRewardedAd() => _isLoaded;

        public void PlayRewardedAd()
        {
            if (_videoAd == null)
            {
                this.Publish(new RewardedAdResultEvent(false, false));
                return;
            }

            if (!_isLoaded)
            {
                _videoAd.Load();
                this.Publish(new RewardedAdResultEvent(false, false));
                return;
            }

            _videoAd.Show();
        }

        private void OnAdLoaded()
        {
            _isLoaded = true;
        }

        private void OnAdClose(bool isComplete, int multitonCount)
        {
            this.Publish(new RewardedAdResultEvent(true, isComplete));
        }

        private void OnAdError(int errCode, string errMsg)
        {
            Debug.Log($"[TikTokAd] Error: {errCode} {errMsg}");
            _isLoaded = false;
        }
    }
}
#endif

