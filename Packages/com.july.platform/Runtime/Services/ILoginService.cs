using Cysharp.Threading.Tasks;

namespace July.Platform
{
    public interface ILoginService : IPlatformService
    {
        string Code { get; }
        UniTask LoginAsync();
    }
}

