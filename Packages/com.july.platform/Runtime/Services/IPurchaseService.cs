
namespace July.Platform
{
    public readonly struct PurchaseResultEvent
    {
        public readonly bool IsSuccess;
        public readonly int OrderAmount;

        public PurchaseResultEvent(bool isSuccess, int orderAmount)
        {
            IsSuccess = isSuccess;
            OrderAmount = orderAmount;
        }
    }

    public interface IPurchaseService : IPlatformService
    {
        void Purchase(int orderAmount);
    }
}

