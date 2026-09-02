#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Diagnostics.Editor
{
    /// <summary>
    /// 审计团结小游戏目标平台最终使用的纹理格式。
    /// 与直接读取 .meta 不同，这里会通过 GetAutomaticFormat 解析 Automatic 的实际格式；
    /// 普通 Unity 不认识团结平台时，也会识别“默认平台未压缩”的已知 RGBA32 来源。
    /// </summary>
    public sealed class MiniGameTextureFormatAuditor : EditorWindow
    {
        enum SearchScope
        {
            GameAssets,
            AllAssets,
        }

        enum FormatFilter
        {
            Rgba32,
            NonAstc6x6,
            All,
        }

        const string GameAssetsRoot = "Assets/Game";
        const float SelectionColumnWidth = 20f;
        const float TextureColumnMinWidth = 260f;
        const float FormatColumnWidth = 240f;
        const float SettingsSourceColumnWidth = 150f;
        const float DimensionsColumnWidth = 90f;
        const float SizeComparisonColumnWidth = 200f;
        const float RepairColumnWidth = 52f;
        const float ColumnSpacing = 4f;

        static readonly string[] TargetPlatforms =
        {
            "MiniGame",
            "WeixinMiniGame",
        };

        readonly List<TextureAssetAuditResult> _results = new List<TextureAssetAuditResult>();

        SearchScope _searchScope = SearchScope.GameAssets;
        FormatFilter _formatFilter = FormatFilter.Rgba32;
        string _pathFilter = string.Empty;
        Vector2 _scrollPosition;
        bool _scanCancelled;
        bool _skipExplicitOverridesOnRepair = true;

        [MenuItem("JulyGF/资源管理/检查小游戏纹理格式", priority = 64)]
        static void Open()
        {
            var window = GetWindow<MiniGameTextureFormatAuditor>("小游戏纹理格式");
            window.minSize = new Vector2(1080f, 420f);
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawSummary();
            DrawResults();
        }

        void DrawToolbar()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            _searchScope = (SearchScope)EditorGUILayout.EnumPopup(
                new GUIContent("扫描范围", "GameAssets 只扫描 Assets/Game；AllAssets 扫描 Assets 下所有纹理。"),
                _searchScope,
                GUILayout.Width(260f));
            _formatFilter = (FormatFilter)EditorGUILayout.EnumPopup(
                new GUIContent("结果过滤", "默认只看 RGBA32；也可检查所有非 ASTC 6x6 格式。"),
                _formatFilter,
                GUILayout.Width(260f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _pathFilter = EditorGUILayout.TextField(
                new GUIContent("路径过滤", "只影响结果显示，不影响扫描。"),
                _pathFilter);
            _skipExplicitOverridesOnRepair = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "修复时跳过显式 Override",
                    "默认保护人工指定的格式；关闭后才会把显式 RGBA32 改成 ASTC 6×6。"),
                _skipExplicitOverridesOnRepair,
                GUILayout.Width(190f));
            if (GUILayout.Button("扫描", GUILayout.Width(100f), GUILayout.Height(22f)))
                Scan();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        void DrawSummary()
        {
            if (_results.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "点击“扫描”同时检查 MiniGame 和 WeixinMiniGame，并按纹理合并显示。默认只显示任一平台最终或预计为 RGBA32 的纹理。",
                    MessageType.Info);
                return;
            }

            var visibleResults = GetVisibleResults();
            var assetCount = visibleResults.Select(result => result.AssetPath).Distinct().Count();
            var unresolvedCount = _results.Sum(result =>
                result.PlatformResults.Count(platformResult =>
                    platformResult.EffectiveFormat == TextureImporterFormat.Automatic));
            var state = _scanCancelled ? "扫描已取消，以下为取消前的结果。" : "扫描完成。";
            var unresolvedHint = unresolvedCount == 0
                ? string.Empty
                : $" 当前编辑器无法解析 {unresolvedCount} 条 Automatic；请在对应的团结目标平台下复查。";

            EditorGUILayout.HelpBox(
                $"{state} 当前显示 {assetCount} 张纹理。{unresolvedHint}",
                assetCount == 0 ? MessageType.Info : MessageType.Warning);
        }

        void DrawResults()
        {
            var visibleResults = GetVisibleResults();
            if (visibleResults.Count == 0)
                return;

            DrawRepairToolbar(visibleResults);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawResultsHeader();
            foreach (var result in visibleResults)
                DrawResult(result);
            EditorGUILayout.EndScrollView();
        }

        static void DrawResultsHeader()
        {
            var rowRect = EditorGUILayout.GetControlRect(
                false,
                EditorGUIUtility.singleLineHeight + 4f,
                GUILayout.ExpandWidth(true));
            GUI.Box(rowRect, GUIContent.none, EditorStyles.toolbar);

            var columns = CalculateTableColumns(rowRect);
            GUI.Label(columns.Texture, "纹理（点击定位）", EditorStyles.miniLabel);
            GUI.Label(columns.Format, "双平台最终格式", EditorStyles.miniLabel);
            GUI.Label(columns.SettingsSource, "设置来源", EditorStyles.miniLabel);
            GUI.Label(columns.Dimensions, "估算尺寸", EditorStyles.miniLabel);
            GUI.Label(
                columns.SizeComparison,
                new GUIContent(
                    "GPU估算：RGBA32 / ASTC 6×6",
                    "按目标尺寸和 Mipmap 估算纹理数据；不包含 Read/Write 的 CPU 副本，也不等同于最终压缩包大小。"),
                EditorStyles.miniLabel);
        }

        void DrawRepairToolbar(List<TextureAssetAuditResult> visibleResults)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选当前结果", GUILayout.Width(110f)))
            {
                foreach (var result in visibleResults)
                    result.Selected = true;
            }

            if (GUILayout.Button("清除选择", GUILayout.Width(90f)))
            {
                foreach (var result in _results)
                    result.Selected = false;
            }

            var selectedResults = visibleResults.Where(result => result.Selected).ToList();
            EditorGUI.BeginDisabledGroup(selectedResults.Count == 0);
            if (GUILayout.Button(
                    $"修复选中为 ASTC 6×6（{selectedResults.Count}）",
                    GUILayout.Width(230f)))
            {
                Repair(selectedResults);
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawResult(TextureAssetAuditResult result)
        {
            var rowRect = EditorGUILayout.GetControlRect(
                false,
                EditorGUIUtility.singleLineHeight + 2f,
                GUILayout.ExpandWidth(true));
            var columns = CalculateTableColumns(rowRect);

            result.Selected = EditorGUI.Toggle(columns.Selection, result.Selected);
            if (GUI.Button(
                    columns.Texture,
                    new GUIContent(result.AssetPath, result.AssetPath),
                    EditorStyles.linkLabel))
            {
                Selection.activeObject = result.Asset;
                EditorGUIUtility.PingObject(result.Asset);
            }

            GUI.Label(
                columns.Format,
                new GUIContent(result.FormatLabel, result.FormatLabel));
            GUI.Label(
                columns.SettingsSource,
                new GUIContent(result.SettingsSource, result.SettingsSource));
            GUI.Label(columns.Dimensions, result.DimensionsLabel);
            GUI.Label(
                columns.SizeComparison,
                new GUIContent(result.SizeComparisonLabel, result.SizeComparisonLabel));
            EditorGUI.BeginDisabledGroup(!result.HasRepairablePlatform(_skipExplicitOverridesOnRepair));
            if (GUI.Button(columns.Repair, "修复"))
                Repair(new[] { result });
            EditorGUI.EndDisabledGroup();
        }

        static TableColumns CalculateTableColumns(Rect rowRect)
        {
            const int spacingCount = 6;
            var fixedWidth = SelectionColumnWidth
                             + FormatColumnWidth
                             + SettingsSourceColumnWidth
                             + DimensionsColumnWidth
                             + SizeComparisonColumnWidth
                             + RepairColumnWidth
                             + ColumnSpacing * spacingCount;
            var textureWidth = Mathf.Max(TextureColumnMinWidth, rowRect.width - fixedWidth);
            var x = rowRect.x;

            var selection = TakeColumn(ref x, rowRect.y, rowRect.height, SelectionColumnWidth);
            var texture = TakeColumn(ref x, rowRect.y, rowRect.height, textureWidth);
            var format = TakeColumn(ref x, rowRect.y, rowRect.height, FormatColumnWidth);
            var settingsSource = TakeColumn(ref x, rowRect.y, rowRect.height, SettingsSourceColumnWidth);
            var dimensions = TakeColumn(ref x, rowRect.y, rowRect.height, DimensionsColumnWidth);
            var sizeComparison = TakeColumn(ref x, rowRect.y, rowRect.height, SizeComparisonColumnWidth);
            var repair = new Rect(x, rowRect.y, RepairColumnWidth, rowRect.height);

            return new TableColumns(
                selection,
                texture,
                format,
                settingsSource,
                dimensions,
                sizeComparison,
                repair);
        }

        static Rect TakeColumn(ref float x, float y, float height, float width)
        {
            var rect = new Rect(x, y, width, height);
            x += width + ColumnSpacing;
            return rect;
        }

        void Repair(IReadOnlyCollection<TextureAssetAuditResult> results)
        {
            var selectedPlatformResults = results.SelectMany(result => result.PlatformResults).ToList();
            var repairablePlatformResults = _skipExplicitOverridesOnRepair
                ? selectedPlatformResults.Where(result => !result.IsExplicitOverride).ToList()
                : selectedPlatformResults;
            var skippedCount = selectedPlatformResults.Count - repairablePlatformResults.Count;
            if (repairablePlatformResults.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "没有可修复项",
                    "所选纹理都是显式 Override。取消“修复时跳过显式 Override”后才能修改。",
                    "确定");
                return;
            }

            var assetCount = repairablePlatformResults.Select(result => result.AssetPath).Distinct().Count();
            var skippedMessage = skippedCount == 0
                ? string.Empty
                : $"\n已跳过 {skippedCount} 条显式 Override。";
            if (!EditorUtility.DisplayDialog(
                    "修复小游戏纹理格式",
                    $"将把 {assetCount} 张纹理的 {repairablePlatformResults.Count} 条目标平台设置改为 ASTC 6×6。" +
                    skippedMessage + "\n\n只修改所选平台的 Override，不修改 Default 和其他平台。",
                    "修复并重新导入",
                    "取消"))
            {
                return;
            }

            foreach (var assetGroup in repairablePlatformResults.GroupBy(result => result.AssetPath))
            {
                var importer = AssetImporter.GetAtPath(assetGroup.Key) as TextureImporter;
                Undo.RecordObject(importer, "修复小游戏纹理格式");

                foreach (var result in assetGroup)
                    SetAstc6x6(importer, result.Platform);

                AssetDatabase.WriteImportSettingsIfDirty(assetGroup.Key);
                AssetDatabase.ImportAsset(assetGroup.Key, ImportAssetOptions.ForceUpdate);
            }

            Scan();
            GUIUtility.ExitGUI();
        }

        static void SetAstc6x6(TextureImporter importer, string platform)
        {
            var targetSettings = importer.GetPlatformTextureSettings(platform);
            if (!targetSettings.overridden)
                importer.GetDefaultPlatformTextureSettings().CopyTo(targetSettings);

            targetSettings.name = platform;
            targetSettings.overridden = true;
            targetSettings.format = TextureImporterFormat.ASTC_6x6;
            targetSettings.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(targetSettings);
        }

        void Scan()
        {
            _results.Clear();
            _scanCancelled = false;

            var roots = _searchScope == SearchScope.GameAssets
                ? new[] { GameAssetsRoot }
                : new[] { "Assets" };
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", roots);

            try
            {
                for (var i = 0; i < textureGuids.Length; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "检查小游戏纹理格式",
                            assetPath,
                            (float)i / textureGuids.Length))
                    {
                        _scanCancelled = true;
                        break;
                    }

                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null)
                        continue;

                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    var platformResults = TargetPlatforms
                        .Select(platform => Inspect(importer, asset, platform))
                        .ToArray();
                    _results.Add(new TextureAssetAuditResult(asset, platformResults));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _results.Sort((left, right) =>
            {
                var pathComparison = string.Compare(left.AssetPath, right.AssetPath, StringComparison.Ordinal);
                return pathComparison;
            });

            Repaint();
        }

        List<TextureAssetAuditResult> GetVisibleResults()
        {
            return _results
                .Where(MatchesFormatFilter)
                .Where(result => string.IsNullOrEmpty(_pathFilter)
                                 || result.AssetPath.IndexOf(_pathFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        bool MatchesFormatFilter(TextureAssetAuditResult result)
        {
            switch (_formatFilter)
            {
                case FormatFilter.Rgba32:
                    return result.PlatformResults.Any(platformResult =>
                        platformResult.EffectiveFormat == TextureImporterFormat.RGBA32);
                case FormatFilter.NonAstc6x6:
                    return result.PlatformResults.Any(platformResult =>
                        platformResult.EffectiveFormat != TextureImporterFormat.ASTC_6x6);
                case FormatFilter.All:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal static TextureFormatAuditResult Inspect(
            TextureImporter importer,
            Texture2D asset,
            string platform)
        {
            var platformSettings = importer.GetPlatformTextureSettings(platform);
            if (platformSettings.overridden && platformSettings.format != TextureImporterFormat.Automatic)
            {
                return CreateResult(importer, asset, platform, platformSettings.format,
                    "平台显式覆盖", platformSettings.maxTextureSize,
                    isExplicitOverride: true);
            }

            var automaticFormat = importer.GetAutomaticFormat(platform);
            var inheritedSettings = platformSettings.overridden
                ? platformSettings
                : importer.GetDefaultPlatformTextureSettings();
            if (automaticFormat == TextureImporterFormat.Automatic
                && IsTuanjiePlatform(platform)
                && PredictsUncompressedRgba32(importer, inheritedSettings))
            {
                return CreateResult(importer, asset, platform, TextureImporterFormat.RGBA32,
                    "默认未压缩（预测）", inheritedSettings.maxTextureSize, true);
            }

            var settingsSource = platformSettings.overridden
                ? "平台 Automatic"
                : automaticFormat == TextureImporterFormat.Automatic
                    ? "当前编辑器未解析"
                    : "平台默认";

            return CreateResult(importer, asset, platform, automaticFormat, settingsSource,
                inheritedSettings.maxTextureSize);
        }

        static TextureFormatAuditResult CreateResult(
            TextureImporter importer,
            Texture2D asset,
            string platform,
            TextureImporterFormat effectiveFormat,
            string settingsSource,
            int maxTextureSize,
            bool isPrediction = false,
            bool isExplicitOverride = false)
        {
            GetEstimatedDimensions(importer, maxTextureSize, out var width, out var height);
            return new TextureFormatAuditResult(
                importer.assetPath,
                asset,
                platform,
                effectiveFormat,
                settingsSource,
                maxTextureSize,
                width,
                height,
                EstimateRgba32Bytes(width, height, importer.mipmapEnabled),
                EstimateAstc6x6Bytes(width, height, importer.mipmapEnabled),
                isPrediction,
                isExplicitOverride);
        }

        static void GetEstimatedDimensions(
            TextureImporter importer,
            int maxTextureSize,
            out int width,
            out int height)
        {
            importer.GetSourceTextureWidthAndHeight(out width, out height);
            var sourceMaxSize = Math.Max(width, height);
            if (sourceMaxSize <= maxTextureSize)
                return;

            var scale = (float)maxTextureSize / sourceMaxSize;
            width = Math.Max(1, Mathf.RoundToInt(width * scale));
            height = Math.Max(1, Mathf.RoundToInt(height * scale));
        }

        static long EstimateRgba32Bytes(int width, int height, bool hasMipmaps)
        {
            long bytes = 0;
            do
            {
                bytes += (long)width * height * 4;
                if (!hasMipmaps || width == 1 && height == 1)
                    break;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            } while (true);

            return bytes;
        }

        static long EstimateAstc6x6Bytes(int width, int height, bool hasMipmaps)
        {
            long bytes = 0;
            do
            {
                var blockColumns = (width + 5) / 6;
                var blockRows = (height + 5) / 6;
                bytes += (long)blockColumns * blockRows * 16;
                if (!hasMipmaps || width == 1 && height == 1)
                    break;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            } while (true);

            return bytes;
        }

        static bool IsTuanjiePlatform(string platform)
        {
            return platform == "MiniGame" || platform == "WeixinMiniGame";
        }

        static bool PredictsUncompressedRgba32(
            TextureImporter importer,
            TextureImporterPlatformSettings inheritedSettings)
        {
            return inheritedSettings.textureCompression == TextureImporterCompression.Uncompressed
                   && importer.alphaSource != TextureImporterAlphaSource.None
                   && importer.DoesSourceTextureHaveAlpha();
        }

        readonly struct TableColumns
        {
            internal TableColumns(
                Rect selection,
                Rect texture,
                Rect format,
                Rect settingsSource,
                Rect dimensions,
                Rect sizeComparison,
                Rect repair)
            {
                Selection = selection;
                Texture = texture;
                Format = format;
                SettingsSource = settingsSource;
                Dimensions = dimensions;
                SizeComparison = sizeComparison;
                Repair = repair;
            }

            internal Rect Selection { get; }
            internal Rect Texture { get; }
            internal Rect Format { get; }
            internal Rect SettingsSource { get; }
            internal Rect Dimensions { get; }
            internal Rect SizeComparison { get; }
            internal Rect Repair { get; }
        }
    }

    internal sealed class TextureAssetAuditResult
    {
        internal TextureAssetAuditResult(
            Texture2D asset,
            IReadOnlyList<TextureFormatAuditResult> platformResults)
        {
            Asset = asset;
            PlatformResults = platformResults;
            AssetPath = platformResults[0].AssetPath;

            var largestResult = platformResults
                .OrderByDescending(result => result.Rgba32EstimatedBytes)
                .First();
            Width = largestResult.Width;
            Height = largestResult.Height;
            Rgba32EstimatedBytes = platformResults.Max(result => result.Rgba32EstimatedBytes);
            Astc6x6EstimatedBytes = platformResults.Max(result => result.Astc6x6EstimatedBytes);

            var hasDifferentDimensions = platformResults.Any(result =>
                result.Width != Width || result.Height != Height);
            DimensionsLabel = hasDifferentDimensions
                ? $"最大 {Width}×{Height}"
                : $"{Width}×{Height}";

            var firstResult = platformResults[0];
            var hasSameFormat = platformResults.All(result =>
                result.EffectiveFormat == firstResult.EffectiveFormat
                && result.IsPrediction == firstResult.IsPrediction);
            FormatLabel = hasSameFormat
                ? firstResult.FormatLabel
                : string.Join(" / ", platformResults.Select(result =>
                    $"{ShortPlatformName(result.Platform)}:{result.FormatLabel}"));

            SettingsSource = platformResults.All(result =>
                result.SettingsSource == firstResult.SettingsSource)
                ? firstResult.SettingsSource
                : "平台设置不一致";
        }

        internal string AssetPath { get; }
        internal Texture2D Asset { get; }
        internal IReadOnlyList<TextureFormatAuditResult> PlatformResults { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal long Rgba32EstimatedBytes { get; }
        internal long Astc6x6EstimatedBytes { get; }
        internal string DimensionsLabel { get; }
        internal string FormatLabel { get; }
        internal string SettingsSource { get; }
        internal bool Selected { get; set; }
        internal string SizeComparisonLabel =>
            $"{EditorUtility.FormatBytes(Rgba32EstimatedBytes)} / {EditorUtility.FormatBytes(Astc6x6EstimatedBytes)}";

        internal bool HasRepairablePlatform(bool skipExplicitOverrides)
        {
            return !skipExplicitOverrides
                   || PlatformResults.Any(result => !result.IsExplicitOverride);
        }

        static string ShortPlatformName(string platform)
        {
            return platform == "WeixinMiniGame" ? "微信" : "MiniGame";
        }
    }

    internal sealed class TextureFormatAuditResult
    {
        internal TextureFormatAuditResult(
            string assetPath,
            Texture2D asset,
            string platform,
            TextureImporterFormat effectiveFormat,
            string settingsSource,
            int maxTextureSize,
            int width,
            int height,
            long rgba32EstimatedBytes,
            long astc6x6EstimatedBytes,
            bool isPrediction = false,
            bool isExplicitOverride = false)
        {
            AssetPath = assetPath;
            Asset = asset;
            Platform = platform;
            EffectiveFormat = effectiveFormat;
            SettingsSource = settingsSource;
            MaxTextureSize = maxTextureSize;
            Width = width;
            Height = height;
            Rgba32EstimatedBytes = rgba32EstimatedBytes;
            Astc6x6EstimatedBytes = astc6x6EstimatedBytes;
            IsPrediction = isPrediction;
            IsExplicitOverride = isExplicitOverride;
        }

        internal string AssetPath { get; }
        internal Texture2D Asset { get; }
        internal string Platform { get; }
        internal TextureImporterFormat EffectiveFormat { get; }
        internal string SettingsSource { get; }
        internal int MaxTextureSize { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal long Rgba32EstimatedBytes { get; }
        internal long Astc6x6EstimatedBytes { get; }
        internal bool IsPrediction { get; }
        internal bool IsExplicitOverride { get; }
        internal bool Selected { get; set; }
        internal string FormatLabel => IsPrediction ? $"{EffectiveFormat}（预测）" : EffectiveFormat.ToString();
        internal string SizeComparisonLabel =>
            $"{EditorUtility.FormatBytes(Rgba32EstimatedBytes)} / {EditorUtility.FormatBytes(Astc6x6EstimatedBytes)}";
    }
}
#endif
