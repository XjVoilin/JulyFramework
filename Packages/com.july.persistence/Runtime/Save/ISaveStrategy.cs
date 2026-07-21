namespace July.Persistence
{
    public interface ISaveStrategy
    {
        bool ShouldSave(SaveContext context);
    }
}
