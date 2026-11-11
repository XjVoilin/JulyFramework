using System;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    public static partial class ColliderGeneration
    {
        // Inspector selection may change while a preview runs. Keep the task
        // attached to its original editable output, not to an Inspector instance.
        internal sealed class PreviewLifetime : IDisposable
        {
            private readonly Transform _output;
            private readonly ColliderMeshSource[] _sources;
            private readonly CancellationTokenSource _stop;
            internal CancellationToken Token=>_stop.Token;
            internal string Reason { get; private set; }="生成已取消；原碰撞体未修改。";

            internal PreviewLifetime(ColliderGenerationRequest request,CancellationToken cancellation=default)
            {
                _output=request.OutputRoot;_sources=request.Sources.ToArray();
                _stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=OnPlay;
            }
            private void Cancel(string reason){if(_stop.IsCancellationRequested)return;Reason=reason;_stop.Cancel();}
            private void OnPlay(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)Cancel("进入播放模式，已取消生成；原碰撞体未修改。");}
            private void Poll()
            {
                if(_stop.IsCancellationRequested)return;
                if(_output==null||EditorUtility.IsPersistent(_output)||!_output.gameObject.scene.IsValid()||!_output.gameObject.scene.isLoaded)
                    Cancel("原预制体编辑环境已关闭或输出分组已移除，已取消生成；请打开目标物品后重新生成。原碰撞体未修改。");
                else if(_sources.Any(s=>s==null||s.Transform==null||s.Mesh==null))
                    Cancel("来源模型已移除或重新载入，已取消生成；请重新扫描后生成。原碰撞体未修改。");
            }
            internal void ThrowIfUnavailable(){Poll();if(_stop.IsCancellationRequested)throw new OperationCanceledException(Reason,Token);}
            public void Dispose(){EditorApplication.update-=Poll;EditorApplication.playModeStateChanged-=OnPlay;_stop.Dispose();}
        }
    }
}
