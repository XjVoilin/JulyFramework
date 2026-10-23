using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    internal static class MeshAssets
    {
        internal static MeshCachedBaseline Baseline(UMesh source)
        {
            if (!MeshAssetEdits.Standalone(source))
                throw new ArgumentException("只处理独立 .asset Mesh；请先在 Prefab 制作流程中提取模型 Mesh。");
            string identity = MeshAssetEdits.Identity(source);
            string currentHash = MeshGeometry.Read(source).Fingerprint();
            var cached = MeshBaselineCache.Load(identity);
            if (cached && cached.ExpectedHash == currentHash) return cached;
            if (cached) cached.Dispose();
            MeshBaselineCache.Save(identity, source, currentHash, false, 100, false, 0);
            return MeshBaselineCache.Load(identity);
        }

        internal static void Apply(MeshRow row, int fidelity)
        {
            Validate(row);
            if (!row.HasChanges) return;
            Write(row, row.Candidate, row.CandidateHash, row.CandidateHash != row.BaselineHash,
                row.Ratio, row.Automatic, fidelity);
        }

        internal static void Restore(MeshRow row)
        {
            Validate(row);
            Write(row, row.Cache.Original, row.BaselineHash, false, 100, false, 0);
        }

        static void Write(MeshRow row, UMesh result, string expectedHash, bool applied, float ratio, bool automatic, int fidelity)
        {
            MeshAssetEdits.CheckColliderUse(row.Source);
            bool readable = row.Source.isReadable;
            var before = UnityEngine.Object.Instantiate(row.Source);
            try
            {
                Undo.RegisterCompleteObjectUndo(row.Source, applied ? "July Mesh 简化" : "July Mesh 还原基准");
                string name = row.Source.name;
                // CopySerialized 不会可靠地清理已使用的 GPU 缓冲；先使旧布局和缓冲失效。
                row.Source.Clear(false);
                EditorUtility.CopySerialized(result, row.Source); row.Source.name = name;
                SetReadWrite(row.Source, readable);
                EditorUtility.SetDirty(row.Source); AssetDatabase.SaveAssetIfDirty(row.Source);
                string hash = MeshGeometry.Read(row.Source).Fingerprint();
                if (hash != expectedHash) throw new InvalidOperationException("写入后的 Mesh 与预览不一致。");
                MeshBaselineCache.Save(MeshAssetEdits.Identity(row.Source), row.Cache.Original, hash, applied, ratio, automatic, fidelity);
            }
            catch
            {
                row.Source.Clear(false);
                EditorUtility.CopySerialized(before, row.Source);
                SetReadWrite(row.Source, readable);
                EditorUtility.SetDirty(row.Source); AssetDatabase.SaveAssetIfDirty(row.Source); throw;
            }
            finally { UnityEngine.Object.DestroyImmediate(before); }
            row.Load(true);
        }

        static void SetReadWrite(UMesh mesh, bool readable)
        {
            // 候选和缓存只提供几何，发布时是否保留 CPU 数据由目标资源决定。
            using (var serialized = new SerializedObject(mesh))
            {
                serialized.FindProperty("m_IsReadable").boolValue = readable;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void Validate(MeshRow row)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出播放模式后修改资源。");
            if (!MeshAssetEdits.Standalone(row.Source)) throw new InvalidOperationException("只允许修改独立 .asset Mesh。");
            string path = AssetDatabase.GetAssetPath(row.Source);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("只允许处理 Assets 内可写的 Mesh。");
            if (!AssetDatabase.IsOpenForEdit(path)) throw new IOException("资源不可写：" + path);
            if (MeshGeometry.Read(row.Source).Fingerprint() != row.ObservedHash) throw new InvalidOperationException("来源已改变，请重新读取后再操作。");
        }
    }
}
