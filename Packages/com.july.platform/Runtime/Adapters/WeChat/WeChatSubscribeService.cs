using July.Arch;
#if JULYGF_WX_MINIGAME
using WeChatWASM;

namespace July.Platform
{
    public class WeChatSubscribeService : ISubscribeService, ICanEvent
    {
        private bool _isSubscribingOnce;
        private bool _isSubscribingLongTerm;

        public void SubscribeOnce(string[] templateIds)
        {
            if (_isSubscribingOnce)
            {
                this.Publish(new SubscribeResultEvent(false, false));
                return;
            }

            _isSubscribingOnce = true;
            var option = new RequestSubscribeMessageOption
            {
                tmplIds = templateIds,
                success = res =>
                {
                    _isSubscribingOnce = false;
                    var id = templateIds[0];
                    this.Publish(new SubscribeResultEvent(res[id] != "reject", false));
                },
                fail = _ =>
                {
                    _isSubscribingOnce = false;
                    this.Publish(new SubscribeResultEvent(false, false));
                },
            };
            WX.RequestSubscribeMessage(option);
        }

        public void SubscribeLongTerm(string[] templateIds)
        {
            if (_isSubscribingLongTerm)
            {
                this.Publish(new SubscribeResultEvent(false, true));
                return;
            }

            _isSubscribingLongTerm = true;
            var opt = new RequestSubscribeSystemMessageOption
            {
                msgTypeList = new[] { "SYS_MSG_TYPE_WHATS_NEW" },
                success = res =>
                {
                    _isSubscribingLongTerm = false;
                    this.Publish(new SubscribeResultEvent(res["SYS_MSG_TYPE_WHATS_NEW"] != "reject", true));
                },
                fail = _ =>
                {
                    _isSubscribingLongTerm = false;
                    this.Publish(new SubscribeResultEvent(false, true));
                },
            };
            WX.RequestSubscribeSystemMessage(opt);
        }
    }
}
#endif

