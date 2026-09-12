using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Logging;
using UnityEngine;

namespace July.Launch
{
    /// <summary>管理一次启动任务；销毁时先取消并等待任务收尾，再清理应用。</summary>
    public abstract class JulyGameEntry : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new();
        private UniTask _launchTask;
        private bool _isInit;
        protected bool IsInitialized => _isInit;

        private void Awake() => DontDestroyOnLoad(gameObject);

        // 所有场景视图执行完 Awake 后才配置启动流程，确保配置错误能够立即显示。
        private void Start()
        {
            _launchTask = RunPipelineAsync(_lifetime.Token).Preserve();
            _launchTask.Forget();
        }

        private async UniTask RunPipelineAsync(CancellationToken ct)
        {
            var pipeline = new LaunchPipeline();
            var executing = false;
            try
            {
                OnBeforeLaunch();
                ConfigurePipeline(pipeline);
                executing = true;
                if (await pipeline.ExecuteAsync(ct))
                {
                    _isInit = true;
                    JLogger.Log("[Launch] Complete");
                }
            }
            catch (OperationCanceledException) { JLogger.Log("[Launch] Cancelled"); }
            catch (Exception error)
            {
                JLogger.LogException(error);
                // 配置错误发生在 ExecuteAsync 开始之前，需要在此上报失败阶段。
                if (!executing && pipeline.OnFailed != null)
                {
                    try { await pipeline.OnFailed("Configure Startup", error, ct); }
                    catch (OperationCanceledException) { }
                }
            }
        }

        protected virtual void OnBeforeLaunch() { }
        protected abstract void ConfigurePipeline(LaunchPipeline pipeline);
        protected virtual void OnShutdown() { }

        protected virtual void OnDestroy()
        {
            _lifetime.Cancel();
            ShutdownAfterLaunchAsync().Forget();
        }

        private async UniTask ShutdownAfterLaunchAsync()
        {
            try { await _launchTask; }
            finally
            {
                try { OnShutdown(); }
                finally { _lifetime.Dispose(); }
            }
        }
    }
}
