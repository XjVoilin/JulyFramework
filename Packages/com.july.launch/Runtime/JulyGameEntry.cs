using System;
using Cysharp.Threading.Tasks;
using July.Logging;
using UnityEngine;

namespace July.Launch
{
    public abstract class JulyGameEntry : MonoBehaviour
    {
        private bool _isInit;

        protected bool IsInitialized => _isInit;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            RunPipeline().Forget();
        }

        private async UniTask RunPipeline()
        {
            try
            {
                var pipeline = new LaunchPipeline();
                ConfigurePipeline(pipeline);

                if (await pipeline.ExecuteAsync(destroyCancellationToken))
                {
                    _isInit = true;
                    JLogger.Log("[Launch] Complete");
                }
            }
            catch (OperationCanceledException)
            {
                JLogger.Log("[Launch] Cancelled");
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
            }
        }

        protected abstract void ConfigurePipeline(LaunchPipeline pipeline);

        protected virtual void OnDestroy()
        {
        }
    }
}
