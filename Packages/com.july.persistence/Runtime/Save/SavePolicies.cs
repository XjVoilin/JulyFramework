namespace July.Persistence
{
    public class ImportanceBasedSaveStrategy : ISaveStrategy
    {
        public bool ShouldSave(SaveContext context)
        {
            var importance = context.Data.Importance;
            var signal = context.Signal;

            switch (signal)
            {
                case SaveSignal.Low:
                    return importance == SaveImportance.Critical;

                case SaveSignal.Medium:
                    return importance <= SaveImportance.Important;

                case SaveSignal.High:
                    return importance <= SaveImportance.Normal;

                case SaveSignal.Immediate:
                    return true;

                default:
                    return false;
            }
        }
    }
}
