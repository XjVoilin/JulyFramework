using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.UI
{
    internal interface IUIAnimationStrategy
    {
        UniTask PlayAsync(GameObject target, bool isOpen, CancellationToken ct = default);
    }
}
