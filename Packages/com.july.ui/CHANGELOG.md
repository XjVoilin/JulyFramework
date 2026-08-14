# Changelog

## 0.2.14 - 2026-08-14

- Let `UIToggleGroup` obtain a fresh optional `ProcedureBase` from a project-provided factory for every selection.
- Unify immediate and prepared selections behind `SelectAsync` and remove the external manual-commit protocol.
- Add `EnsureUIContentProcedure` for idempotent lazy UI content instantiation.

## 0.2.13 - 2026-08-14

- Allow `UIToggleGroup` to await an optional one-shot `ProcedureBase` before committing a selection.
- Cancel pending asynchronous selections when a newer or synchronous selection supersedes them.

## 0.2.12 - 2026-08-12

- Allow each model preview target to configure its instantiated model.
- Allow callers to cancel model preview loading.
