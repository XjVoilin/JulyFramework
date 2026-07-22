using Cysharp.Threading.Tasks;
using July.Arch;

namespace July.Persistence
{
    /// <summary>Store lifecycle glue for loading, registering and dirtying save data.</summary>
    public abstract class SavableStoreBase<TData> : StoreBase<TData>
        where TData : class, ISaveData, new()
    {
        protected abstract string SaveKey { get; }

        private ISaveSystem _saveSystem;

        protected override async UniTask OnInitializeAsync()
        {
            _saveSystem = ArchContext.Current.GetSystem<ISaveSystem>();
            Data = await _saveSystem.LoadAndRegisterAsync<TData>(SaveKey);
        }

        protected void MarkDirty() => _saveSystem?.MarkDirty(SaveKey);

        protected override void OnShutdown()
        {
            _saveSystem?.Unregister(SaveKey);
            _saveSystem = null;
        }
    }
}
