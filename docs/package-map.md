# July package map

July uses package seams only where a capability has an independent installation reason, third-party dependency, editor boundary, or meaningful reuse boundary. Namespaces and assembly definitions handle smaller internal structure.

## Foundation

| Package | Responsibility |
| --- | --- |
| `com.july.events` | Dependency-free typed event bus. |
| `com.july.logging` | Logging facade, channels, reporters, and editor redirection. |
| `com.july.arch` | Context, systems, stores, views, and procedures. |
| `com.july.launch` | Launch pipeline and launch-step orchestration. |

## Runtime capabilities

| Package | Responsibility |
| --- | --- |
| `com.july.fsm` | Finite-state machines. |
| `com.july.input` | Input gating plus reusable pointer/touch input for UI, sprites, and gameplay. |
| `com.july.pooling` | Reusable object pools. |
| `com.july.time` | Game time, server time, and timers. |
| `com.july.resource` | Resource contracts and the Unity Resources implementation. |
| `com.july.resource.yooasset` | Optional YooAsset implementation, including mini-game file-system adapters. |
| `com.july.persistence` | Save data, serialization, encryption, and platform-aware local storage. |
| `com.july.networking` | HTTP, durable request queues, and optional Protobuf JSON adapters. |
| `com.july.config` | Typed providers plus remote JSON fetch/retry and WebGL cache mechanics. |
| `com.july.localization` | Localization providers and localized text behaviour. |
| `com.july.scene` | Scene lifecycle and navigation. |
| `com.july.audio` | Audio playback, pooling, and configuration. |
| `com.july.platform` | Platform capability registration and adapters. |
| `com.july.analytics` | Analytics dispatch and optional SDK channels. |
| `com.july.diagnostics` | Runtime diagnostics and developer tools. |

## Presentation and gameplay

| Package | Responsibility |
| --- | --- |
| `com.july.animation` | General animation helpers for UI, sprites, and scene objects. |
| `com.july.animation.spine` | Optional Spine-specific animation support. |
| `com.july.ui` | UI runtime, controls, transitions, and window management. |
| `com.july.ui.authoring` | Editor-only UI generation and authoring tools. |
| `com.july.tasks` | Task/quest state and events. |
| `com.july.guide` | Guide/tutorial state and persistence. |
| `com.july.reddot` | Red-dot trees and presentation bindings. |
| `com.july.activity` | Time-bounded activity state and progress. |
| `com.july.experiments` | Experiment and A/B assignment. |

## Tooling and composition

| Location | Responsibility |
| --- | --- |
| `com.july.build` | Reusable editor build pipeline and optional HybridCLR build mechanics. |

The external `Template_2022.3` repository owns the game composition root, scenes, project policy, generated configuration, and fixed third-party tooling. It also verifies that the packages work together in a real Unity project.

## Dependency rules

1. `events` and `logging` do not depend on other July packages.
2. Domain packages depend on contracts they consume; they do not access the template repository.
3. Third-party-specific code stays in the owning capability package (`resource.yooasset`, `animation.spine`) rather than an `Integrations` container.
4. Game-specific configuration, input policy, provider selection, and system registration stay in the template composition root.
5. Package dependencies must remain acyclic and must be declared in both `package.json` and the consuming assembly definition.
