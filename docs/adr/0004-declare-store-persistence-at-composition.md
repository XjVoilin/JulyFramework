# Declare Store persistence at composition

Store inheritance does not determine persistence. The Composition Root explicitly declares persistent Stores to the SaveSystem with a key and SaveImportance; SaveSystem restores those Stores as part of its own asynchronous initialization before dependent Systems initialize, while ArchContext remains unaware of persistence and Procedures remain one-shot business workflows.

## Status

Accepted, 2026-08-12.

## Consequences

- All domain Stores inherit the same `StoreBase<TData>` and can replace their complete data without encoding its source.
- `Persist(store, key, importance)` records a persistence declaration and returns the same Store; only `ArchContext.RegisterStore` registers it with Arch.
- Store has no asynchronous lifecycle. SaveSystem initialization performs load-or-new, dirty subscription, and save registration for every declared Store.
- System initialization order is registration order; serialization and encryption Systems precede SaveSystem, and Systems that consume restored data follow it.
- `MarkDirty` is persistence-neutral: unconfigured Stores have no persistence listener and require no SaveSystem.
- A Store declared as `Critical` is queued for saving whenever it marks itself dirty. This schedules the write immediately but does not turn `MarkDirty` into an async durability barrier; callers that must await the result use `SaveNowAsync`.
- Project Stores may use project or generated server data types directly. July Packages use package-owned data types and are mapped by the consuming project.
- `SavableStoreBase` and data-level `SaveImportance` are removed without compatibility adapters.
