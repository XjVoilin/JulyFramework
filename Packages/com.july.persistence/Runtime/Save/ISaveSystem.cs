using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;

namespace July.Persistence
{
    /// <summary>
    /// 本地持久化能力。持久化 Store 由组合根显式声明，恢复、修改跟踪和保存由 SaveSystem 统一管理。
    /// Critical Store 标脏后会立即进入写入队列；需要等待保存结果时显式调用 SaveNowAsync。
    /// </summary>
    public interface ISaveSystem
    {
        StoreBase<TData> Persist<TData>(
            StoreBase<TData> store,
            string key,
            SaveImportance importance) where TData : class, new();

        UniTask<IReadOnlyDictionary<string, SaveResult>> FlushAsync(
            SaveSignal signal,
            CancellationToken ct = default);

        UniTask<SaveResult> SaveNowAsync(
            StoreBase store,
            CancellationToken ct = default);
    }
}
