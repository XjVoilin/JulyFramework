namespace July.Config
{
    public interface IConfigSystem
    {
        IConfigProvider MainProvider { get; }
        IConfigProvider AdditionalProvider { get; }
        void SetMainProvider(IConfigProvider provider);
        void SetAdditionalProvider(IConfigProvider provider);
        void UnsetAdditionalProvider(IConfigProvider provider);

        T GetTable<T>() where T : class;
        bool TryGetTable<T>(out T table) where T : class;
    }
}
