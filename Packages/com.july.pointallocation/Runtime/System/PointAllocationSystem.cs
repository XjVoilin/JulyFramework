using System.Collections.Generic;
using July.Arch;

namespace July.PointAllocation
{
    /// <summary>PointAllocation 模块默认的 July System 实现。</summary>
    public sealed class PointAllocationSystem : SystemBase, IPointAllocationSystem
    {
        private readonly Dictionary<int, PointAllocationGraphDefinition> _definitions =
            new Dictionary<int, PointAllocationGraphDefinition>();

        public PointAllocationOperationResult RegisterDefinition(PointAllocationGraphDefinition definition)
        {
            if (definition == null)
                return PointAllocationOperationResult.Failed(PointAllocationFailureReason.InvalidDefinition);

            if (!_definitions.TryAdd(definition.Id, definition))
            {
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.DuplicateDefinition,
                    definition.Id);
            }

            return PointAllocationOperationResult.Succeeded();
        }

        public bool RemoveDefinition(int definitionId) => _definitions.Remove(definitionId);

        public bool TryGetDefinition(int definitionId, out PointAllocationGraphDefinition definition) =>
            _definitions.TryGetValue(definitionId, out definition);

        public PointAllocationOperationResult CreateRuntime(
            int definitionId,
            PointAllocationSnapshot initialProgress,
            out PointAllocationRuntime runtime)
        {
            if (!_definitions.TryGetValue(definitionId, out var definition))
            {
                runtime = null;
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.DefinitionNotFound,
                    definitionId);
            }

            var validation = PointAllocationRuntime.ValidateProgress(
                definition,
                initialProgress,
                out _);
            if (!validation.Success)
            {
                runtime = null;
                return validation;
            }

            runtime = new PointAllocationRuntime(definition, initialProgress);
            return PointAllocationOperationResult.Succeeded();
        }

        protected override void OnShutdown()
        {
            _definitions.Clear();
        }
    }
}

