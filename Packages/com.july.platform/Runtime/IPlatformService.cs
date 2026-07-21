using Cysharp.Threading.Tasks;

namespace July.Platform
{
    public interface IPlatformService
    {
        void Init() { }
        void PostInit() { }
        UniTask PostInitAsync() => UniTask.CompletedTask;
        void DeferredInit() { }
        void Shutdown() { }
    }
}
