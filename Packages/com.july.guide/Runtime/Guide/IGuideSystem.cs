using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Guide
{
    public interface IGuideSystem
    {
        bool IsRunning { get; }
        int CurrentGuideId { get; }
        int CurrentStepId { get; }

        void RegisterTarget(IGuideTarget target);
        void UnregisterTarget(IGuideTarget target);
        void RegisterWorldCamera(Camera camera);
        void UnregisterWorldCamera(Camera camera);
        UniTask<bool> SkipCurrentGuideAsync();
    }
}
