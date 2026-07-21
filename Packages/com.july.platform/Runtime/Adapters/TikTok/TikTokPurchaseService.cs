#if JULYGF_DY_MINIGAME
using System;
using System.Collections.Generic;
using July.Arch;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace July.Platform
{
    public class TikTokPurchaseService : IPurchaseService, ICanEvent
    {
        public void Purchase(int orderAmount)
        {
            TT.CheckSession(
                () =>
                {
                    var platform = TT.GetSystemInfo().platform;
                    if (platform == "ios")
                        PurchaseIOS(orderAmount);
                    else if (platform == "android")
                        PurchaseAndroid(orderAmount);
                    else
                        this.Publish(new PurchaseResultEvent(false, orderAmount));
                },
                _ => this.Publish(new PurchaseResultEvent(false, orderAmount)));
        }

        private void PurchaseIOS(int orderAmount)
        {
            var options = new JsonData
            {
                ["goodType"] = 2,
                ["orderAmount"] = orderAmount,
                ["currencyType"] = "DIAMOND",
                ["zoneId"] = "1",
                ["customId"] = GenerateOrderId(),
            };
            TT.OpenAwemeCustomerService(options,
                () => this.Publish(new PurchaseResultEvent(true, orderAmount)),
                (_, _) => this.Publish(new PurchaseResultEvent(false, orderAmount)));
        }

        private void PurchaseAndroid(int orderAmount)
        {
            var options = new Dictionary<string, object>
            {
                ["goodType"] = 2,
                ["orderAmount"] = orderAmount,
                ["currencyType"] = "CNY",
                ["zoneId"] = "1",
                ["customId"] = GenerateOrderId(),
                ["mode"] = "game",
                ["env"] = 0,
                ["platform"] = "android",
            };
            TT.RequestGamePayment(options,
                () => this.Publish(new PurchaseResultEvent(true, orderAmount)),
                (_, _) => this.Publish(new PurchaseResultEvent(false, orderAmount)));
        }

        private static string GenerateOrderId()
        {
            var timePart = DateTime.Now.ToString("yyyyMMddHHmmss");
            var random = new Random(Guid.NewGuid().GetHashCode());
            return timePart + random.Next(1000, 9999);
        }
    }
}
#endif
