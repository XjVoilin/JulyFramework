using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using July.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace July.Scene
{
    /// <summary>
    /// 场景系统 — 合并原 SceneModule 的业务逻辑。
    /// 通过 IResourceSystem 加载/卸载场景，维护场景栈，发布生命周期事件。
    /// </summary>
    public class SceneSystem : SystemBase, ISceneSystem
    {
        private IResourceSystem _resourceSystem;

        private string _currentSceneName;
        private readonly Stack<string> _sceneStack = new();

        public string CurrentSceneName => _currentSceneName;

        protected override UniTask OnInitializeAsync()
        {
            _resourceSystem = GetSystem<IResourceSystem>();

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
                _currentSceneName = activeScene.name;
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown()
        {
            _sceneStack.Clear();
            _resourceSystem = null;
            _currentSceneName = null;
        }

        public async UniTask<UnityScene> LoadSceneAsync(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken ct = default)
        {
            Publish(new SceneLoadStartEvent
            {
                SceneName = sceneName,
                LoadMode = mode
            });

            try
            {
                var scene = await _resourceSystem.LoadSceneAsync(sceneName, mode, ct);

                if (mode == LoadSceneMode.Single)
                    _currentSceneName = sceneName;

                Publish(new SceneLoadCompleteEvent
                {
                    SceneName = sceneName,
                    Scene = scene,
                    LoadMode = mode
                });

                return scene;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SceneSystem] 场景 {sceneName} 加载失败: {ex.Message}");
                throw;
            }
        }

        public async UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default)
        {
            Publish(new SceneUnloadStartEvent { SceneName = sceneName });

            try
            {
                var success = await _resourceSystem.UnloadSceneAsync(sceneName, ct);

                if (success && _currentSceneName == sceneName)
                    _currentSceneName = null;

                Publish(new SceneUnloadCompleteEvent
                {
                    SceneName = sceneName,
                    Success = success
                });

                return success;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SceneSystem] 场景 {sceneName} 卸载失败: {ex.Message}");

                Publish(new SceneUnloadCompleteEvent
                {
                    SceneName = sceneName,
                    Success = false
                });

                throw;
            }
        }

        public UniTask<UnityScene> SwitchSceneAsync(string sceneName, CancellationToken ct = default)
        {
            return SwitchSceneAsync(sceneName, deferUnusedAssetCleanup: false, ct);
        }

        public async UniTask<UnityScene> SwitchSceneAsync(
            string sceneName,
            bool deferUnusedAssetCleanup,
            CancellationToken ct = default)
        {
            var fromSceneName = _currentSceneName;

            Publish(new SceneSwitchStartEvent
            {
                FromSceneName = fromSceneName ?? string.Empty,
                ToSceneName = sceneName
            });

            try
            {
                if (!string.IsNullOrEmpty(fromSceneName) && fromSceneName != sceneName)
                    _sceneStack.Push(fromSceneName);

                var scene = await LoadSceneAsync(sceneName, LoadSceneMode.Single, ct);
                if (deferUnusedAssetCleanup)
                {
                    JLogger.Log($"[SceneSystem] Deferred unused asset cleanup: {fromSceneName ?? "None"} -> {sceneName}");
                }
                else
                {
                    var cleanupWatch = Stopwatch.StartNew();
                    await _resourceSystem.UnloadUnusedAssetsAsync();
                    cleanupWatch.Stop();
                    JLogger.Log($"[SceneSystem] UnloadUnusedAssets completed in {cleanupWatch.ElapsedMilliseconds}ms: " +
                                $"{fromSceneName ?? "None"} -> {sceneName}");
                }

                Publish(new SceneSwitchCompleteEvent
                {
                    FromSceneName = fromSceneName ?? string.Empty,
                    ToSceneName = sceneName,
                    Scene = scene
                });

                return scene;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SceneSystem] 场景切换失败: {fromSceneName ?? "无"} -> {sceneName}, 错误: {ex.Message}");
                throw;
            }
        }

        public async UniTask<UnityScene?> GoBackAsync(CancellationToken ct = default)
        {
            if (_sceneStack.Count == 0)
            {
                JLogger.LogWarning("[SceneSystem] 场景栈为空，无法返回上一场景");
                return null;
            }

            var previousSceneName = _sceneStack.Pop();
            var fromSceneName = _currentSceneName;

            Publish(new SceneSwitchStartEvent
            {
                FromSceneName = fromSceneName ?? string.Empty,
                ToSceneName = previousSceneName
            });

            try
            {
                var scene = await LoadSceneAsync(previousSceneName, LoadSceneMode.Single, ct);
                await _resourceSystem.UnloadUnusedAssetsAsync();

                Publish(new SceneSwitchCompleteEvent
                {
                    FromSceneName = fromSceneName ?? string.Empty,
                    ToSceneName = previousSceneName,
                    Scene = scene
                });

                return scene;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SceneSystem] 返回场景失败: {fromSceneName ?? "无"} -> {previousSceneName}, 错误: {ex.Message}");
                throw;
            }
        }

        public bool TryGetScene(string sceneName, out UnityScene scene)
        {
            scene = default;
            if (string.IsNullOrEmpty(sceneName))
                return false;

            scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        public void ClearSceneStack()
        {
            _sceneStack.Clear();
        }
    }
}
