using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace July.Animation.Editor
{
    /// <summary>
    /// 多选 Sprite / Texture2D → 右键生成帧动画 Prefab（SpriteRenderer + SpriteFrameAnimator）。
    /// 自动按 _r{N} 命名规则分组为多个 Clip；无规则时归为单个 Clip。
    /// </summary>
    public static class SpriteAnimPrefabCreator
    {
        private const float DefaultFps = 10f;
        private static readonly Regex RowPattern = new(@"_r(\d+)$", RegexOptions.Compiled);
        private static readonly Regex ColRowPattern = new(@"_c(\d+)_r(\d+)$", RegexOptions.Compiled);

        [MenuItem("Assets/July/Animation/生成帧动画 Prefab", false, 110)]
        public static void CreateAnimPrefab()
        {
            var sprites = CollectSelectedSprites();
            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请选中 Sprite（独立 PNG 或 Multiple 子 Sprite）", "确定");
                return;
            }

            sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var clips = GroupIntoClips(sprites);
            var baseName = DeriveBaseName(sprites[0].name);

            var outputDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sprites[0]));
            var prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{outputDir}/{baseName}.prefab");

            var go = new GameObject(baseName);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites[0];

            var animator = go.AddComponent<SpriteFrameAnimator>();
            ApplyClips(animator, clips);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            var clipSummary = string.Join("\n",
                clips.Select((c, i) => $"  [{i}] {c.name}: {c.frames.Length} 帧"));
            Debug.Log($"[SpriteAnimPrefabCreator] 生成 Prefab: {prefabPath}\n{clipSummary}");

            EditorUtility.DisplayDialog("生成完成",
                $"Prefab: {Path.GetFileName(prefabPath)}\n\n" +
                $"{clips.Count} 个 Clip, 共 {sprites.Count} 帧\n\n" +
                "请在 Inspector 中修改 Clip 名称和 FPS",
                "确定");
        }

        [MenuItem("Assets/July/Animation/生成帧动画 Prefab", true)]
        private static bool Validate() => CollectSelectedSprites().Count > 0;

        #region Clip Grouping

        private struct ClipInfo
        {
            public string name;
            public Sprite[] frames;
        }

        private static List<ClipInfo> GroupIntoClips(List<Sprite> sprites)
        {
            var groups = new SortedDictionary<int, List<Sprite>>();
            var ungrouped = new List<Sprite>();

            foreach (var sprite in sprites)
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(sprite.name);
                var match = RowPattern.Match(nameWithoutExt);
                if (match.Success)
                {
                    int row = int.Parse(match.Groups[1].Value);
                    if (!groups.ContainsKey(row))
                        groups[row] = new List<Sprite>();
                    groups[row].Add(sprite);
                }
                else
                {
                    ungrouped.Add(sprite);
                }
            }

            var clips = new List<ClipInfo>();

            if (groups.Count > 0)
            {
                foreach (var kvp in groups)
                {
                    SortByColumn(kvp.Value);
                    clips.Add(new ClipInfo
                    {
                        name = $"clip_{kvp.Key}",
                        frames = kvp.Value.ToArray()
                    });
                }
            }

            if (ungrouped.Count > 0 || clips.Count == 0)
            {
                clips.Add(new ClipInfo
                {
                    name = clips.Count == 0 ? "default" : "clip_misc",
                    frames = ungrouped.Count > 0 ? ungrouped.ToArray() : sprites.ToArray()
                });
            }

            return clips;
        }

        private static void SortByColumn(List<Sprite> sprites)
        {
            sprites.Sort((a, b) =>
            {
                var ma = ColRowPattern.Match(Path.GetFileNameWithoutExtension(a.name));
                var mb = ColRowPattern.Match(Path.GetFileNameWithoutExtension(b.name));
                if (ma.Success && mb.Success)
                    return int.Parse(ma.Groups[1].Value).CompareTo(int.Parse(mb.Groups[1].Value));
                return string.CompareOrdinal(a.name, b.name);
            });
        }

        #endregion

        #region Prefab Assembly

        private static void ApplyClips(SpriteFrameAnimator animator, List<ClipInfo> clips)
        {
            var so = new SerializedObject(animator);
            var clipsProp = so.FindProperty("_clips");
            clipsProp.arraySize = clips.Count;

            for (int i = 0; i < clips.Count; i++)
            {
                var element = clipsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = clips[i].name;
                element.FindPropertyRelative("fps").floatValue = DefaultFps;
                element.FindPropertyRelative("loop").boolValue = true;

                var framesProp = element.FindPropertyRelative("frames");
                framesProp.arraySize = clips[i].frames.Length;
                for (int f = 0; f < clips[i].frames.Length; f++)
                    framesProp.GetArrayElementAtIndex(f).objectReferenceValue = clips[i].frames[f];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion

        #region Helpers

        private static List<Sprite> CollectSelectedSprites()
        {
            var result = new List<Sprite>();

            foreach (var obj in Selection.objects)
            {
                if (obj is Sprite s)
                {
                    result.Add(s);
                    continue;
                }

                if (obj is not Texture2D) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var sprites = assets.OfType<Sprite>().ToList();

                if (sprites.Count > 0)
                    result.AddRange(sprites);
            }

            return result;
        }

        private static string DeriveBaseName(string spriteName)
        {
            var name = Path.GetFileNameWithoutExtension(spriteName);
            var match = ColRowPattern.Match(name);
            if (match.Success)
                return name[..match.Index].TrimEnd('_');

            match = RowPattern.Match(name);
            if (match.Success)
                return name[..match.Index].TrimEnd('_');

            return name;
        }

        #endregion
    }
}
