# July Point Allocation

`com.july.pointallocation` provides a JSON-authored, UI-independent point-allocation graph for JulyFramework. Its namespace is `July.PointAllocation`; the Unity editor opens from `JulyGF/加点图编辑器`.

## Ownership

- A graph JSON is the only logical definition source shared by Editor, client runtime, and server.
- `PointAllocationSystem` directly parses graph JSON and applies allocation rules.
- `PointAllocationStore` owns the loaded graph collection, mutable shared balance, and graph-grouped sparse node states.
- Each loaded `PointAllocationGraph` contains only static structure and owns non-serialized node and incoming-connection indexes derived from that structure. Node levels exist only in the Store's sparse `GraphStates` and are not cached in the graph.
- The project owns JSON file IO, server communication, persistence declaration, lifecycle, and game UI.
- An optional companion `PointAllocationEditorWorkspace` stores only canvas positions, labels, notes, locks, and view state. Runtime never reads it.

Runtime does not load files or depend on an exported ScriptableObject. Initialize the `ArchContext` first, then pass JSON text to `LoadGraph`. Duplicate `GraphId` values are rejected and graphs remain loaded for the System lifetime.

Loading is configuration, not a recoverable gameplay operation. LitJson reads `PointAllocationGraph` directly, after which Runtime builds only the lookup indexes required for execution. Runtime trusts the graph structure exported by Editor and does not repeat node, connection, cost, or DAG validation. Duplicate `GraphId` values are still rejected because the loaded graph collection requires unique keys.

## JSON schema

```json
{
  "GraphId": 1001,
  "Nodes": [
    {
      "Id": 1,
      "MaxLevel": 3,
      "UpgradeCosts": [1, 2, 3]
    }
  ],
  "Connections": [
    {
      "FromNodeId": 1,
      "ToNodeId": 2,
      "RequiredLevel": 2
    }
  ]
}
```

Rules:

- `GraphId` and node IDs are positive `int` values; node IDs are unique within a graph.
- `MaxLevel >= 1` and `UpgradeCosts.Count == MaxLevel`.
- `UpgradeCosts[currentLevel]` is the cost of `currentLevel -> currentLevel + 1`; costs are non-negative.
- Connections are graph-owned unique facts. Both endpoints must exist, `RequiredLevel` is within the source node's level range, and the graph must be a DAG.
- Every incoming connection is required (AND).

These definition rules are checked by the Editor `Validate` command and again before JSON is saved. Runtime validates only mutable state supplied through `ReplaceState`.

## Mutable state

```text
PointAllocationStoreData
├─ AvailablePoints
└─ GraphStates[]
   ├─ GraphId
   └─ NodeStates[]
      ├─ NodeId
      └─ Level
```

Each `PointAllocationGraphState` owns one graph's identity and complete sparse node state. `PointAllocationNodeState` contains only `NodeId` and `Level`; it does not know which graph it belongs to. A graph's node-state list contains every positive level, and absence means level zero. All nodes therefore start at level zero; an empty list is the complete state of a graph with no allocated nodes. Its order has no business meaning.

`ReplaceState(graphId, nodeStates, availablePoints)` validates and atomically replaces only the specified graph's node state, leaves other graph states unchanged, and replaces the shared balance. It never merges, sorts, or copies the node list. The supplied `List<PointAllocationNodeState>` becomes that graph's Store data, so the caller must not mutate it afterwards. An invalid authoritative state is a protocol/configuration error: the method throws and leaves all old state untouched.

When a server returns several graphs, the project calls `ReplaceState` once per graph. Each call carries the authoritative shared balance; therefore the final call determines the Store's shared `AvailablePoints`. Version 1 does not provide a bulk multi-graph replacement API.

Read `IPointAllocationSystem.AvailablePoints` and `GetNodeLevel(graphId, nodeId)` for current values. `GetNodeLevel` throws when the graph is not loaded or the node does not exist; absence from the sparse node-state list means level zero. Read `PointAllocationStore.GetData()` when the complete transferable or persistable state is required. The System does not create a duplicate snapshot or aggregate UI model.

## Commands

- `CanUpgrade(graphId, nodeId)` checks the next level without mutation.
- `TryUpgrade(graphId, nodeId)` returns `false` for a normal rule rejection; otherwise it atomically deducts shared points, raises one level, and returns `true`.
- `ResetGraph(graphId)` removes every node state in that graph, refunds all of its allocated levels, and leaves other graphs unchanged.
- `ReplaceState(graphId, nodeStates, availablePoints)` accepts one graph's complete sparse state from a save, server, or new-state flow and transfers that list to the Store.

After `ReplaceState`, a successful `TryUpgrade`, or an effective `ResetGraph` commits state, Point Allocation publishes one parameterless `PointAllocationChangedEvent`. Consumers such as red-dot rules use it only as an invalidation signal and then query current state again. Failed upgrades and resetting an already empty graph publish no event. The event is intentionally global because `AvailablePoints` is shared by every loaded graph.

The project Procedure still owns local or server orchestration, presentation, and project-specific operation events. `PointAllocationChangedEvent` does not describe why state changed or carry a duplicate state snapshot.

Maximum level, unmet prerequisites, and insufficient points are normal upgrade rejections represented by `false`. Invalid lifecycle, unknown IDs, invalid snapshots, duplicate graphs, and arithmetic overflow throw exceptions because they are invalid usage or data rather than gameplay outcomes.

Point Allocation has no authority-mode flag. A local-authority project calls local commands. A server-authority project may use evaluation for UI feedback, sends commands to its server, and applies the returned complete snapshot with `ReplaceState`.

## Composition

Register the Store before the System and initialize the `ArchContext` before loading graphs. When local restoration is desired, the persistence System should initialize before `PointAllocationSystem`, so restored Store data is available before project code starts querying it.

```csharp
var store = new PointAllocationStore();
context.RegisterStore(store); // or saveSystem.Persist(store, key, importance)

var system = new PointAllocationSystem();
context.RegisterSystem(system);

await context.InitializeAsync();
system.LoadGraph(graphJson);
system.ReplaceState(graphId, nodeStates, availablePoints);
```

There is no persisted `HasState` flag. The project lifecycle must keep consumers inactive until initialization and any required `ReplaceState` call have completed.

## UI

The project UI chooses node positions, tree/radial/constellation presentation, styles, icons, animation, visibility, and which existing logical connections to draw. Editor canvas positions are authoring metadata and are not game UI positions.

In the Editor, double-click an empty canvas position or use the canvas context menu to create a node at that position. Double-clicking an existing node, port, or connection does not create a node.
