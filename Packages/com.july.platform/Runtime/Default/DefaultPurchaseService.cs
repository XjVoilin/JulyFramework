using July.Arch;
namespace July.Platform
{
    public class DefaultPurchaseService : IPurchaseService, ICanEvent
    {
        public void Purchase(int orderAmount)
        {
            this.Publish(new PurchaseResultEvent(true, orderAmount));
        }
    }
}

