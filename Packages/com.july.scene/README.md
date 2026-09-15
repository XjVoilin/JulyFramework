# July.Scene

## 0.1.1

SceneLoadFailedEvent reports the scene name, load mode and original exception for
failed and cancelled resource loads. It is published before the exception is rethrown,
so presentation subscribers can restore their state. SceneLoadCompleteEvent remains
the success notification. SceneUnloadCompleteEvent reports success or failure.

Observers needing continuous UI during Single scene loads should handle
SceneLoadStartEvent and both completion outcomes. July.UI 0.2.27 does this automatically.
