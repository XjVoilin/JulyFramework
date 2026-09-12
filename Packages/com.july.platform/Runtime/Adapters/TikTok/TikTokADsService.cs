using July.Arch;
#if JULYGF_DY_MINIGAME
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public class TikTokADsService : IADsService, ICanEvent
    {

        private TTRewardedVideoAd _videoAd;
        private bool _isLoaded;
        private readonly string _adUnitId;
        public TikTokADsService(string adUnitId) => _adUnitId = adUnitId;

        public void Init()
        {
        }

        public void DeferredInit()
        {
            if (_videoAd != null || string.IsNullOrWhiteSpace(_adUnitId)) return;
            var param = new CreateRewardedVideoAdParam { AdUnitId = _adUnitId };
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

