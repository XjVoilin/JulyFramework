namespace July.Config
{
    public interface IConfigProvider
    {
        bool TryGetTable<T>(out T table) where T : class;
    }
}
