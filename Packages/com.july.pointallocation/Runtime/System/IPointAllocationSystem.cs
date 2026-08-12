namespace July.PointAllocation
{
    /// <summary>注册不可变定义并创建独立 PointAllocationRuntime 的 July 系统入口。</summary>
    public interface IPointAllocationSystem
    {
        PointAllocationOperationResult RegisterDefinition(PointAllocationGraphDefinition definition);
        bool RemoveDefinition(int definitionId);
        bool TryGetDefinition(int definitionId, out PointAllocationGraphDefinition definition);

        PointAllocationOperationResult CreateRuntime(
            int definitionId,
            PointAllocationSnapshot initialProgress,
            out PointAllocationRuntime runtime);
    }
}

