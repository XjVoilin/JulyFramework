using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.UI
{
    internal sealed class NoneAnimationStrategy : IUIAnimationStrategy
    {
        public static readonly NoneAnimationStrategy Instance = new();
        public UniTask PlayAsync(GameObject target, bool isOpen, CancellationToken ct) => UniTask.CompletedTask;
    }
}
