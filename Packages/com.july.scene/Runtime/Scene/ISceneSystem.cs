using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace July.Scene
{
    /// <summary>
    /// 场景系统接口 — 场景加载、卸载、切换、栈管理。
    /// 通过 Scope.GetSystem&lt;ISceneSystem&gt;() 获取。
    /// </summary>
    public interface ISceneSystem
    {
        string CurrentSceneName { get; }

        #region Load / Unload

        UniTask<UnityScene> LoadSceneAsync(string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken ct = default);

        UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default);

        #endregion

        #region Switch / GoBack

        UniTask<UnityScene> SwitchSceneAsync(string sceneName, CancellationToken ct = default);
        UniTask<UnityScene> SwitchSceneAsync(string sceneName, bool deferUnusedAssetCleanup,
            CancellationToken ct = default);
        UniTask<UnityScene?> GoBackAsync(CancellationToken ct = default);
        void ClearSceneStack();

        #endregion

        #region Query

        bool TryGetScene(string sceneName, out UnityScene scene);

        #endregion
    }
}
