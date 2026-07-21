using July.Arch;
#if JULYGF_WX_MINIGAME
using WeChatWASM;
using UnityEngine;

namespace July.Platform
{
    public class WeChatADsService : IADsService, ICanEvent
    {
        private WXRewardedVideoAd _videoAd;
        private bool _isLoaded;
        private bool _isPlaying;

        public void Init()
        {
        }

        public void DeferredInit()
        {
            if (_videoAd != null) return;
            var param = new WXCreateRewardedVideoAdParam { adUnitId = "adunit-9f95df6f408c8ad7" };
            _videoAd = WX.CreateRewardedVideoAd(param);
            _videoAd.OnLoad(OnAdLoaded);
            _videoAd.OnError(OnAdError);
            _videoAd.OnClose(OnAdClose);
            Debug.Log("[WeChatADsService] 骞垮憡瀹炰緥寤惰繜鍒涘缓瀹屾垚");
        }

        public bool HasRewardedAd() => _isLoaded && !_isPlaying;

        public void PlayRewardedAd()
        {
            if (_videoAd == null)
            {
                this.Publish(new RewardedAdResultEvent(false, false));
                return;
            }

            if (!_isLoaded || _isPlaying)
            {
                if (!_isLoaded) _videoAd.Load(null, null);
                this.Publish(new RewardedAdResultEvent(false, false));
                return;
            }

            _isPlaying = true;
            _isLoaded = false;
            _videoAd.Show(OnShowSuccess, OnShowFail);
        }

        private void OnShowSuccess(WXTextResponse res)
        {
            // 灞曠ず鎴愬姛锛岀瓑寰?OnAdClose 鍥炶皟
        }

        private void OnShowFail(WXTextResponse res)
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            Debug.Log($"WeChatADsService Show Failed: {res?.errMsg}");
            _videoAd.Load(null, null);
            this.Publish(new RewardedAdResultEvent(false, false));
        }

        private void OnAdClose(WXRewardedVideoAdOnCloseResponse v)
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            var completed = v != null && v.isEnded;
            this.Publish(new RewardedAdResultEvent(true, completed));
            _videoAd.Load(null, null);
        }

        private void OnAdLoaded(WXADLoadResponse v)
        {
            _isLoaded = true;
        }

        private void OnAdError(WXADErrorResponse v)
        {
            Debug.Log($"WeChatADsService Ad Error: {v?.errCode} {v?.errMsg}");
            _isLoaded = false;
            _videoAd.Load(null, null);
            if (!_isPlaying) return;
            _isPlaying = false;
            this.Publish(new RewardedAdResultEvent(false, false));
        }
    }
}
#endif

