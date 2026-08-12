using System;
using System.Collections.Generic;
using July.Logging;

namespace July.PointAllocation
{
    /// <summary>一份独立且只能通过命令修改的运行时加点进度。</summary>
    public sealed class PointAllocationRuntime
    {
        private readonly Dictionary<int, int> _ranks = new Dictionary<int, int>();
        private readonly Queue<PendingPointAllocationEvent> _eventQueue = new Queue<PendingPointAllocationEvent>();
        private bool _isDispatching;
        private int _availablePoints;

        public PointAllocationGraphDefinition Definition { get; }
        public int AvailablePoints => _availablePoints;

        public event Action<PointAllocationChangedEvent> ProgressChanged;
        public event Action<PointAllocationReplacedEvent> ProgressReplaced;

        internal PointAllocationRuntime(
            PointAllocationGraphDefinition definition,
            PointAllocationSnapshot initialProgress)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            ApplyProgress(initialProgress);
        }

        public PointAllocationOperationResult EvaluateAddRank(int nodeId)
        {
            if (!Definition.TryGetNode(nodeId, out var node))
                return PointAllocationOperationResult.Failed(PointAllocationFailureReason.NodeNotFound, nodeId);

            var currentRank = GetRank(nodeId);
            if (currentRank >= node.MaxRank)
            {
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.MaxRankReached,
                    nodeId,
                    requiredValue: node.MaxRank,
                    actualValue: currentRank);
            }

            var incoming = Definition.GetIncomingConnections(nodeId);
            for (var index = 0; index < incoming.Count; index++)
            {
                var connection = incoming[index];
                var sourceRank = GetRank(connection.FromNodeId);
                if (sourceRank >= connection.RequiredRank)
                    continue;

                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.PrerequisiteNotMet,
                    nodeId,
                    connection.FromNodeId,
                    connection.RequiredRank,
                    sourceRank);
            }

            var cost = node.RankCosts[currentRank];
            if (_availablePoints < cost)
            {
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.InsufficientPoints,
                    nodeId,
                    requiredValue: cost,
                    actualValue: _availablePoints);
            }

            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationOperationResult AddRank(int nodeId)
        {
            var evaluation = EvaluateAddRank(nodeId);
            if (!evaluation.Success)
                return evaluation;

            var previousPoints = _availablePoints;
            var previousRank = GetRank(nodeId);
            var node = GetNode(nodeId);
            _availablePoints -= node.RankCosts[previousRank];
            _ranks[nodeId] = previousRank + 1;

            EnqueueChanged(new PointAllocationChangedEvent(
                PointAllocationChangeReason.RankAdded,
                previousPoints,
                _availablePoints,
                new[] { new PointAllocationNodeRankChange(nodeId, previousRank, previousRank + 1) }));
            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationOperationResult EvaluateRefundRank(int nodeId)
        {
            if (!Definition.TryGetNode(nodeId, out var node))
                return PointAllocationOperationResult.Failed(PointAllocationFailureReason.NodeNotFound, nodeId);

            var currentRank = GetRank(nodeId);
            if (currentRank <= 0)
                return PointAllocationOperationResult.Failed(PointAllocationFailureReason.RankIsZero, nodeId);

            var refundedRank = currentRank - 1;
            var outgoing = Definition.GetOutgoingConnections(nodeId);
            for (var index = 0; index < outgoing.Count; index++)
            {
                var connection = outgoing[index];
                if (GetRank(connection.ToNodeId) <= 0 ||
                    refundedRank >= connection.RequiredRank)
                {
                    continue;
                }

                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.DependentNodeInvested,
                    nodeId,
                    connection.ToNodeId,
                    connection.RequiredRank,
                    refundedRank);
            }

            var refund = node.RankCosts[refundedRank];
            if ((long)_availablePoints + refund > int.MaxValue)
            {
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.PointOverflow,
                    nodeId,
                    requiredValue: refund,
                    actualValue: _availablePoints);
            }

            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationOperationResult RefundRank(int nodeId)
        {
            var evaluation = EvaluateRefundRank(nodeId);
            if (!evaluation.Success)
                return evaluation;

            var previousPoints = _availablePoints;
            var previousRank = GetRank(nodeId);
            var node = GetNode(nodeId);
            var currentRank = previousRank - 1;
            _availablePoints += node.RankCosts[currentRank];
            SetRank(nodeId, currentRank);

            EnqueueChanged(new PointAllocationChangedEvent(
                PointAllocationChangeReason.RankRefunded,
                previousPoints,
                _availablePoints,
                new[] { new PointAllocationNodeRankChange(nodeId, previousRank, currentRank) }));
            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationOperationResult GrantPoints(int amount)
        {
            if (amount <= 0)
                return PointAllocationOperationResult.Failed(PointAllocationFailureReason.InvalidAmount, actualValue: amount);

            if ((long)_availablePoints + amount > int.MaxValue)
            {
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.PointOverflow,
                    requiredValue: amount,
                    actualValue: _availablePoints);
            }

            var previousPoints = _availablePoints;
            _availablePoints += amount;
            EnqueueChanged(new PointAllocationChangedEvent(
                PointAllocationChangeReason.PointsGranted,
                previousPoints,
                _availablePoints,
                Array.Empty<PointAllocationNodeRankChange>()));
            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationOperationResult Reset()
        {
            if (_ranks.Count == 0)
                return PointAllocationOperationResult.Succeeded();

            long refund = 0;
            var changes = new List<PointAllocationNodeRankChange>(_ranks.Count);
            foreach (var pair in _ranks)
            {
                var node = GetNode(pair.Key);
                for (var rankIndex = 0; rankIndex < pair.Value; rankIndex++)
                    refund += node.RankCosts[rankIndex];

                changes.Add(new PointAllocationNodeRankChange(pair.Key, pair.Value, 0));
            }

            if ((long)_availablePoints + refund > int.MaxValue)
                return PointAllocationOperationResult.Failed(PointAllocationFailureReason.PointOverflow);

            changes.Sort((left, right) => left.NodeId.CompareTo(right.NodeId));
            var previousPoints = _availablePoints;
            _availablePoints += (int)refund;
            _ranks.Clear();
            EnqueueChanged(new PointAllocationChangedEvent(
                PointAllocationChangeReason.Reset,
                previousPoints,
                _availablePoints,
                changes));
            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationOperationResult ReplaceProgress(PointAllocationSnapshot progress)
        {
            var validation = ValidateProgress(Definition, progress, out _);
            if (!validation.Success)
                return validation;

            ApplyProgress(progress);
            _eventQueue.Enqueue(PendingPointAllocationEvent.Replaced());
            DrainEvents();
            return PointAllocationOperationResult.Succeeded();
        }

        public PointAllocationSnapshot GetSnapshot()
        {
            var ranks = new PointAllocationNodeRank[_ranks.Count];
            var index = 0;
            foreach (var pair in _ranks)
                ranks[index++] = new PointAllocationNodeRank(pair.Key, pair.Value);
            return new PointAllocationSnapshot(_availablePoints, ranks);
        }

        public bool TryGetNodeState(int nodeId, out PointAllocationNodeState state)
        {
            if (!Definition.TryGetNode(nodeId, out var node))
            {
                state = default;
                return false;
            }

            var currentRank = GetRank(nodeId);
            var addEvaluation = EvaluateAddRank(nodeId);
            var refundEvaluation = EvaluateRefundRank(nodeId);
            state = new PointAllocationNodeState(
                nodeId,
                currentRank,
                node.MaxRank,
                currentRank < node.MaxRank ? node.RankCosts[currentRank] : 0,
                ArePrerequisitesMet(nodeId),
                addEvaluation,
                refundEvaluation);
            return true;
        }

        public IReadOnlyList<PointAllocationConnectionState> GetConnectionStates()
        {
            var states = new PointAllocationConnectionState[Definition.Connections.Count];
            for (var index = 0; index < Definition.Connections.Count; index++)
            {
                var connection = Definition.Connections[index];
                states[index] = new PointAllocationConnectionState(
                    connection,
                    GetRank(connection.FromNodeId));
            }

            return states;
        }

        internal static PointAllocationOperationResult ValidateProgress(
            PointAllocationGraphDefinition definition,
            PointAllocationSnapshot progress,
            out Dictionary<int, int> ranks)
        {
            ranks = new Dictionary<int, int>();
            if (definition == null || progress.AvailablePoints < 0)
            {
                return PointAllocationOperationResult.Failed(
                    PointAllocationFailureReason.InvalidProgress,
                    actualValue: progress.AvailablePoints);
            }

            var nodeRanks = progress.NodeRanks ?? Array.Empty<PointAllocationNodeRank>();
            for (var index = 0; index < nodeRanks.Count; index++)
            {
                var nodeRank = nodeRanks[index];
                if (!definition.TryGetNode(nodeRank.NodeId, out var node) ||
                    nodeRank.CurrentRank <= 0 ||
                    nodeRank.CurrentRank > node.MaxRank ||
                    !ranks.TryAdd(nodeRank.NodeId, nodeRank.CurrentRank))
                {
                    return PointAllocationOperationResult.Failed(
                        PointAllocationFailureReason.InvalidProgress,
                        nodeRank.NodeId,
                        requiredValue: node?.MaxRank ?? 0,
                        actualValue: nodeRank.CurrentRank);
                }
            }

            foreach (var pair in ranks)
            {
                var incoming = definition.GetIncomingConnections(pair.Key);
                for (var index = 0; index < incoming.Count; index++)
                {
                    var connection = incoming[index];
                    ranks.TryGetValue(connection.FromNodeId, out var sourceRank);
                    if (sourceRank >= connection.RequiredRank)
                        continue;

                    return PointAllocationOperationResult.Failed(
                        PointAllocationFailureReason.InvalidProgress,
                        pair.Key,
                        connection.FromNodeId,
                        connection.RequiredRank,
                        sourceRank);
                }
            }

            return PointAllocationOperationResult.Succeeded();
        }

        private void ApplyProgress(PointAllocationSnapshot progress)
        {
            ValidateProgress(Definition, progress, out var ranks);
            _availablePoints = progress.AvailablePoints;
            _ranks.Clear();
            foreach (var pair in ranks)
                _ranks.Add(pair.Key, pair.Value);
        }

        private bool ArePrerequisitesMet(int nodeId)
        {
            var incoming = Definition.GetIncomingConnections(nodeId);
            for (var index = 0; index < incoming.Count; index++)
            {
                var connection = incoming[index];
                if (GetRank(connection.FromNodeId) < connection.RequiredRank)
                    return false;
            }

            return true;
        }

        private int GetRank(int nodeId) => _ranks.TryGetValue(nodeId, out var rank) ? rank : 0;

        private PointAllocationNodeDefinition GetNode(int nodeId)
        {
            Definition.TryGetNode(nodeId, out var node);
            return node;
        }

        private void SetRank(int nodeId, int rank)
        {
            if (rank <= 0)
                _ranks.Remove(nodeId);
            else
                _ranks[nodeId] = rank;
        }

        private void EnqueueChanged(PointAllocationChangedEvent eventData)
        {
            _eventQueue.Enqueue(PendingPointAllocationEvent.Changed(eventData));
            DrainEvents();
        }

        private void DrainEvents()
        {
            if (_isDispatching)
                return;

            _isDispatching = true;
            try
            {
                while (_eventQueue.Count > 0)
                {
                    var pending = _eventQueue.Dequeue();
                    if (pending.Kind == PendingPointAllocationEventKind.Changed)
                        InvokeSafely(ProgressChanged, pending.ChangedEvent);
                    else
                        InvokeSafely(ProgressReplaced, new PointAllocationReplacedEvent());
                }
            }
            finally
            {
                _isDispatching = false;
            }
        }

        private static void InvokeSafely<T>(Action<T> handlers, T eventData)
        {
            if (handlers == null)
                return;

            var invocationList = handlers.GetInvocationList();
            for (var index = 0; index < invocationList.Length; index++)
            {
                try
                {
                    ((Action<T>)invocationList[index]).Invoke(eventData);
                }
                catch (Exception exception)
                {
                    JLogger.LogException(exception);
                }
            }
        }
    }

    internal enum PendingPointAllocationEventKind
    {
        Changed,
        Replaced
    }

    internal readonly struct PendingPointAllocationEvent
    {
        public PendingPointAllocationEventKind Kind { get; }
        public PointAllocationChangedEvent ChangedEvent { get; }

        private PendingPointAllocationEvent(
            PendingPointAllocationEventKind kind,
            PointAllocationChangedEvent changedEvent)
        {
            Kind = kind;
            ChangedEvent = changedEvent;
        }

        public static PendingPointAllocationEvent Changed(PointAllocationChangedEvent eventData) =>
            new PendingPointAllocationEvent(PendingPointAllocationEventKind.Changed, eventData);

        public static PendingPointAllocationEvent Replaced() =>
            new PendingPointAllocationEvent(PendingPointAllocationEventKind.Replaced, default);
    }
}
