using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using System.Collections;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class DisplayAndFormatTests
    {
        private string _root, _config;
        private AssetCheckReportStore _store;

        [SetUp]
        public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__AssetCheckDisplay_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.GetFullPath(Path.Combine("Temp", "AssetCheckDisplay_" + id));
            Directory.CreateDirectory(_config);
            _store = new AssetCheckReportStore(_config);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_root);
            Assert.That(_config, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_config, true);
        }

        [Test]
        public void DefaultsAllowSixAndEightAndRejectTargetsOutsideTheAllowedSet()
        {
            var settings = AssetCheckSettings.CreateDefault();
            Assert.That(settings.version, Is.EqualTo(4));
            Assert.That(settings.CurrentStandards.texture.allowedFormats,
                Is.EqualTo(new[] { TextureImporterFormat.ASTC_6x6, TextureImporterFormat.ASTC_8x8 }));
            settings.CurrentStandards.texture.compressionFormat = TextureImporterFormat.ASTC_4x4;
            Assert.Throws<InvalidDataException>(() => settings.Validate());
            settings.CurrentStandards.texture.allowedFormats = new[] { TextureImporterFormat.ASTC_4x4 };
            Assert.DoesNotThrow(() => settings.Validate());
            settings.CurrentStandards.texture.allowedAtlasFormats = Array.Empty<TextureImporterFormat>();
            Assert.Throws<InvalidDataException>(() => settings.Validate());
        }

        [TestCase(TextureImporterFormat.ASTC_6x6, 2)]
        [TestCase(TextureImporterFormat.ASTC_4x4, 1)]
        public void VersionTwoMigrationPreservesCustomTargetsAndExpandsTheOldDefault(TextureImporterFormat target, int count)
        {
            var old = AssetCheckSettings.CreateDefault();
            old.version = 2;
            old.CurrentStandards.texture.compressionFormat = target;
            old.CurrentStandards.texture.atlasCompressionFormat = target;
            old.CurrentStandards.model.allowLights = true;
            File.WriteAllText(Path.Combine(_config, AssetCheckSettingsFile.FileName), JsonUtility.ToJson(old));
            var current = new AssetCheckSettingsFile(_config).LoadOrCreate();
            Assert.That(current.version, Is.EqualTo(4));
            Assert.That(current.CurrentStandards.texture.compressionFormat, Is.EqualTo(target));
            Assert.That(current.CurrentStandards.texture.allowedFormats.Length, Is.EqualTo(count));
            Assert.That(current.CurrentStandards.texture.allowedAtlasFormats.Length, Is.EqualTo(count));
            Assert.That(current.CurrentStandards.model.allowLights, Is.True);
        }

        [Test]
        public void EightByEightPassesEveryTargetAndIsNotRewrittenToSixBySix()
        {
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            WriteFormat(importer, "WebGL", TextureImporterFormat.ASTC_8x8);
            TuanjieTextureSettings.WriteFormat(importer, "MiniGame", TextureImporterFormat.ASTC_8x8);
            TuanjieTextureSettings.WriteFormat(importer, "WeixinMiniGame", TextureImporterFormat.ASTC_8x8);
            importer.isReadable = true;
            importer.SaveAndReimport();
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Texture); Drain(workspace);
                var report = workspace.GetReport(AssetCheckKind.Texture);
                Assert.That(report.Issues.Select(issue => issue.RuleId), Is.EqualTo(new[] { "texture.read-write" }));
                workspace.StartFix(AssetCheckKind.Texture, report.Issues); Drain(workspace);
                importer = (TextureImporter)AssetImporter.GetAtPath(path);
                foreach (var platform in MiniGameProfile.TextureTargets)
                    Assert.That(importer.GetPlatformTextureSettings(platform).format, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            }
        }

        [Test]
        public void AtlasEightByEightIsAllowedAndFourByFourUsesTheConfiguredFixTarget()
        {
            var atlas = new SpriteAtlas();
            var path = _root + "/atlas.spriteatlas";
            AssetDatabase.CreateAsset(atlas, path);
            var standard = new TextureStandards();
            foreach (var target in MiniGameProfile.TextureTargets)
            {
                var value = atlas.GetPlatformSettings(target);
                value.overridden = true; value.format = TextureImporterFormat.ASTC_8x8;
                atlas.SetPlatformSettings(value);
            }
            Assert.That(new TextureCheckModule().Check(path, new PlatformStandards { texture = standard })
                .Any(issue => issue.RuleId == "atlas.compression-format"), Is.False);
            var web = atlas.GetPlatformSettings("WebGL");
            web.format = TextureImporterFormat.ASTC_4x4;
            atlas.SetPlatformSettings(web);
            var issue = new TextureCheckModule().Check(path, new PlatformStandards { texture = standard })
                .Single(item => item.RuleId == "atlas.compression-format");
            Assert.That(issue.Targets, Is.EqualTo(new[] { "WebGL" }));
            new TextureCheckModule().Fix(path, new[] { issue }, new PlatformStandards { texture = standard });
            Assert.That(atlas.GetPlatformSettings("WebGL").format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            Assert.That(atlas.GetPlatformSettings("MiniGame").format, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
        }

        [Test]
        public void ConfirmedFixChangesOnlyTheKnownBadTargetAndLeavesUnknownOverridesUntouched()
        {
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            WriteFormat(importer, "WebGL", TextureImporterFormat.ASTC_4x4);
            WriteFormat(importer, "MiniGame", TextureImporterFormat.Automatic);
            WriteFormat(importer, "WeixinMiniGame", TextureImporterFormat.Automatic);
            importer.SaveAndReimport();
            var before = JsonUtility.ToJson(importer.GetPlatformTextureSettings("MiniGame"));
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Texture); Drain(workspace);
                var issue = workspace.GetReport(AssetCheckKind.Texture).Issues.Single();
                Assert.That(issue.HasProblem && issue.Unverified, Is.True);
                Assert.That(issue.ConfirmedFix().Targets, Is.EqualTo(new[] { "WebGL" }));
                workspace.StartFix(AssetCheckKind.Texture, new[] { issue.ConfirmedFix() }); Drain(workspace);
                importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.GetPlatformTextureSettings("WebGL").format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
                Assert.That(JsonUtility.ToJson(importer.GetPlatformTextureSettings("MiniGame")), Is.EqualTo(before));
                var remaining = workspace.GetReport(AssetCheckKind.Texture).Issues.Single();
                Assert.That(remaining.HasProblem, Is.False);
                Assert.That(remaining.UnverifiedFix().Targets, Does.Contain("MiniGame"));
            }
        }

        [Test]
        public void MissingMiniGameOverridesAreIncludedInFixAllAndPreserveAllowedWebGL()
        {
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            WriteFormat(importer, "WebGL", TextureImporterFormat.ASTC_8x8);
            WriteFormat(importer, "Android", TextureImporterFormat.ASTC_4x4);
            importer.SaveAndReimport();
            var defaults = JsonUtility.ToJson(importer.GetDefaultPlatformTextureSettings());
            var android = JsonUtility.ToJson(importer.GetPlatformTextureSettings("Android"));
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Texture); Drain(workspace);
                var report = workspace.GetReport(AssetCheckKind.Texture);
                var issue = report.Issues.Single();
                Assert.That(issue.HasProblem, Is.True, "缺少小游戏平台配置必须纳入已确认问题。");
                Assert.That(issue.Unverified, Is.False);
                Assert.That(report.UnverifiedOnlyFiles, Is.Zero);
                Assert.That(issue.Actual, Does.Contain("未配置平台覆盖"));
                Assert.That(issue.ConfirmedFix().Targets, Is.EquivalentTo(new[] { "MiniGame", "WeixinMiniGame" }));
                var view = new AssetCheckResultsModel();
                view.Bind(report);
                workspace.StartFix(AssetCheckKind.Texture, view.AllFixable()); Drain(workspace);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
                Assert.That(workspace.LastFix.Rechecked, Is.True);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues, Is.Empty);
            }
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            foreach (var target in new[] { "MiniGame", "WeixinMiniGame" })
            {
                var value = importer.GetPlatformTextureSettings(target);
                Assert.That(value.overridden, Is.True);
                Assert.That(value.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            }
            Assert.That(importer.GetPlatformTextureSettings("WebGL").format, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            Assert.That(JsonUtility.ToJson(importer.GetDefaultPlatformTextureSettings()), Is.EqualTo(defaults));
            Assert.That(JsonUtility.ToJson(importer.GetPlatformTextureSettings("Android")), Is.EqualTo(android));
        }

        [Test]
        public void MissingAtlasMiniGameOverridesAreConfirmedEvenWithAllowedDefaultFormat()
        {
            var atlas = new SpriteAtlas();
            var path = _root + "/missing.spriteatlas";
            AssetDatabase.CreateAsset(atlas, path);
            var defaults = atlas.GetPlatformSettings("DefaultTexturePlatform");
            defaults.format = TextureImporterFormat.ASTC_8x8;
            atlas.SetPlatformSettings(defaults);
            var module = new TextureCheckModule();
            var standards = new PlatformStandards();
            var issue = module.Check(path, standards).Single(item => item.RuleId == "atlas.compression-format");
            Assert.That(issue.HasProblem, Is.True);
            Assert.That(issue.Unverified, Is.False);
            Assert.That(issue.ConfirmedFix().Targets, Is.EquivalentTo(new[] { "MiniGame", "WeixinMiniGame" }));
            module.Fix(path, new[] { issue.ConfirmedFix() }, standards);
            atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            Assert.That(module.Check(path, standards).Any(item => item.RuleId == issue.RuleId), Is.False);
            Assert.That(atlas.GetPlatformSettings("DefaultTexturePlatform").format, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            foreach (var target in new[] { "MiniGame", "WeixinMiniGame" })
                Assert.That(atlas.GetPlatformSettings(target).format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
        }

        [Test]
        public void MissingMiniGameTargetCanBeFixedWithoutRewritingExplicitUnknownOverride()
        {
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            WriteFormat(importer, "WebGL", TextureImporterFormat.ASTC_8x8);
            WriteFormat(importer, "MiniGame", TextureImporterFormat.Automatic);
            importer.SaveAndReimport();
            var before = JsonUtility.ToJson(importer.GetPlatformTextureSettings("MiniGame"));
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Texture); Drain(workspace);
                var issue = workspace.GetReport(AssetCheckKind.Texture).Issues.Single();
                Assert.That(issue.HasProblem, Is.True);
                Assert.That(issue.UnverifiedTargets, Is.EqualTo(new[] { "MiniGame" }));
                Assert.That(issue.ConfirmedFix().Targets, Is.EqualTo(new[] { "WeixinMiniGame" }));
                var model = new AssetCheckResultsModel();
                model.Bind(workspace.GetReport(AssetCheckKind.Texture));
                workspace.StartFix(AssetCheckKind.Texture, model.AllFixable()); Drain(workspace);
                importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.GetPlatformTextureSettings("WeixinMiniGame").format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
                Assert.That(JsonUtility.ToJson(importer.GetPlatformTextureSettings("MiniGame")), Is.EqualTo(before));
                var remaining = workspace.GetReport(AssetCheckKind.Texture).Issues.Single();
                Assert.That(remaining.HasProblem, Is.False);
                Assert.That(remaining.UnverifiedTargets, Is.EqualTo(new[] { "MiniGame" }));
            }
        }

        [Test]
        public void OpeningWorkspaceRestoresCountsAndRowsButCannotFixASnapshot()
        {
            Texture();
            using (var workspace = Workspace()) { workspace.StartScan(AssetCheckKind.Texture); Drain(workspace); }
            using (var restored = Workspace())
            {
                var report = restored.GetReport(AssetCheckKind.Texture);
                Assert.That(report.IsSnapshot, Is.True);
                Assert.That(report.CheckedFiles, Is.EqualTo(1));
                Assert.That(report.CheckedTargets, Is.Empty, "Cached counts must not be represented as fabricated identifiers.");
                Assert.That(report.Issues, Is.Not.Empty);
                Assert.Throws<InvalidOperationException>(() => restored.StartFix(AssetCheckKind.Texture, report.Issues));
                restored.StartScan(AssetCheckKind.Texture); Drain(restored);
                Assert.That(restored.GetReport(AssetCheckKind.Texture).IsSnapshot, Is.False);
            }
        }

        [Test]
        public void CorruptOneSnapshotIsReportedWithoutDiscardingOtherModulesOrOverwritingIt()
        {
            using (var workspace = Workspace()) { workspace.StartScan(AssetCheckKind.Material); Drain(workspace); }
            var path = _store.PathFor(AssetCheckKind.Model);
            File.WriteAllText(path, "{ broken json");
            using (var workspace = Workspace())
            {
                Assert.That(workspace.GetReport(AssetCheckKind.Material).IsSnapshot, Is.True);
                Assert.That(workspace.GetReport(AssetCheckKind.Model), Is.Null);
                Assert.That(workspace.ReportLoadFailure, Does.Contain("模型"));
                Assert.That(File.ReadAllText(path), Is.EqualTo("{ broken json"));
            }
        }

        [Test]
        public void FileRowsMergeIssuesAndSortingKeepsSelectionAttachedToTheSameFile()
        {
            var report = Report(Issue("tex10.png", 'a', "size"), Issue("tex2.png", 'b', "format"), Issue("tex2.png", 'b', "size"));
            var model = new AssetCheckResultsModel(); model.Bind(report);
            Assert.That(model.Files.Count, Is.EqualTo(2));
            Assert.That(model.Rows.Count, Is.EqualTo(2));
            Assert.That(model.Files[0].Name, Is.EqualTo("tex2.png"));
            model.Selected.Add(new string('b', 32));
            model.Descending = true; model.Rebuild();
            Assert.That(model.Fixable(true).Select(issue => issue.TargetId).Distinct(), Is.EqualTo(new[] { new string('b', 32) }));
            model.Toggle(new string('b', 32));
            Assert.That(model.Rows.Count(row => row.Issue != null), Is.EqualTo(2));
            Assert.That(model.Rows.Where(row => row.IsFile).Select(row => row.File.Id).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void TypeFilterLimitsBulkScopeClearsHiddenSelectionsAndRetainsOtherIssuesInTheReport()
        {
            var report = Report(Issue("one.png", 'a', "size"), Issue("one.png", 'a', "format"), Issue("two.png", 'b', "format"));
            var model = new AssetCheckResultsModel(); model.Bind(report);
            model.Selected.Add(new string('b', 32));
            model.Filter("size");
            Assert.That(model.Selected, Is.Empty);
            Assert.That(model.Files.Single().AllIssues.Length, Is.EqualTo(2));
            Assert.That(model.Fixable(false).Select(issue => issue.RuleId), Is.EqualTo(new[] { "size" }));
            Assert.That(report.Issues.Count, Is.EqualTo(3));
            Assert.That(model.AllFixable().Length, Is.EqualTo(3), "修改全部覆盖当前 Tab，不受问题筛选影响。");
        }

        [Test]
        public void ColumnSortingTogglesDirectionAndComparesNumbersRatherThanText()
        {
            AssetCheckIssue Value(string file, char id, string problem, string actual, string expected) =>
                new AssetCheckIssue(_root + "/" + file, new string(id, 32), AssetCheckKind.Texture,
                    problem, problem, actual, expected, true);
            var model = new AssetCheckResultsModel();
            model.Bind(Report(Value("z.png", 'a', "B", "3,400", "≤ 2,000"),
                Value("a.png", 'b', "A", "999", "≤ 500")));
            model.Selected.Add(new string('a', 32));
            foreach (var column in new[] { AssetCheckSort.Problem, AssetCheckSort.Actual, AssetCheckSort.Expected })
            {
                model.SortBy(column);
                Assert.That(model.Descending, Is.False);
                Assert.That(model.Files[0].Name, Is.EqualTo("a.png"));
                model.SortBy(column);
                Assert.That(model.Descending, Is.True);
                Assert.That(model.Files[0].Name, Is.EqualTo("z.png"));
                Assert.That(model.Selected, Does.Contain(new string('a', 32)));
            }
            model.SortBy(AssetCheckSort.FileName);
            Assert.That(model.Files[0].Name, Is.EqualTo("a.png"));
            model.SortBy(AssetCheckSort.FileName);
            Assert.That(model.Files[0].Name, Is.EqualTo("z.png"));
        }

        [Test]
        public void ValueSortingKeepsChildrenWithTheirFileAndUnverifiedFilesInTheirSection()
        {
            var model = new AssetCheckResultsModel();
            model.Bind(Report(Issue("one.png", 'a', "size"), Issue("one.png", 'a', "format"),
                Issue("two.png", 'b', "format"),
                new AssetCheckIssue(_root + "/unknown.png", new string('c', 32), AssetCheckKind.Texture,
                    "unknown", "待验证", "Automatic", "ASTC", true, true,
                    targets: new[] { "MiniGame" }, unverifiedTargets: new[] { "MiniGame" })));
            model.ExpandAll(true);
            model.SortBy(AssetCheckSort.Problem);
            var parent = model.Rows.FindIndex(row => row.IsFile && row.File.Id == new string('a', 32));
            Assert.That(model.Rows[parent + 1].File.Id, Is.EqualTo(new string('a', 32)));
            Assert.That(model.Rows[parent + 2].File.Id, Is.EqualTo(new string('a', 32)));
            Assert.That(model.Rows[parent + 1].Issue.Problem, Is.EqualTo("format"));
            Assert.That(model.Rows[parent + 2].Issue.Problem, Is.EqualTo("size"));
            Assert.That(model.Rows.Last().File.Id, Is.EqualTo(new string('c', 32)));
            Assert.That(model.Rows[model.Rows.Count - 2].UnknownHeader, Is.True);
            Assert.That(model.AllFixable().Length, Is.EqualTo(3), "未验证目标不进入修改全部。");
        }

        [Test]
        public void UnknownFilesStartFoldedAndMixedFilesAppearOnlyOnce()
        {
            var known = Issue("mixed.png", 'a', "format");
            var unknown = new AssetCheckIssue(_root + "/mixed.png", new string('a', 32), AssetCheckKind.Texture,
                "unknown", "待验证", "Automatic", "ASTC", true, true, targets: new[] { "MiniGame" },
                unverifiedTargets: new[] { "MiniGame" });
            var pure = new AssetCheckIssue(_root + "/unknown.png", new string('b', 32), AssetCheckKind.Texture,
                "unknown", "待验证", "Automatic", "ASTC", true, true, targets: new[] { "MiniGame" },
                unverifiedTargets: new[] { "MiniGame" });
            var model = new AssetCheckResultsModel(); model.Bind(Report(known, unknown, pure));
            Assert.That(model.UnknownFiles, Is.EqualTo(1));
            Assert.That(model.Rows.Count, Is.EqualTo(2));
            Assert.That(model.Fixable(false).Length, Is.EqualTo(1));
            model.ExpandAll(true);
            Assert.That(model.Rows.Count(row => row.IsFile && row.File.Id == known.TargetId), Is.EqualTo(1));
            Assert.That(model.Rows.Count(row => row.IsFile), Is.EqualTo(2));
            Assert.That(model.Fixable(false).Length, Is.EqualTo(1));
        }

        [Test]
        public void NativeResultViewBuildsFileRowsAndKeepsSnapshotActionsReadOnly()
        {
            var view = new AssetCheckResultsView(_ => Assert.Fail("No modification should run."), _ => { }, (_, __) => { });
            var report = Report(Issue("a.png", 'a', "format"), Issue("a.png", 'a', "size"));
            report.IsSnapshot = true; report.SnapshotCheckedFiles = 1;
            view.Bind(report, false);
            Assert.That(view.Q<MultiColumnListView>().itemsSource.Count, Is.EqualTo(1));
            Assert.That(view.Model.CanModify, Is.False);
            Assert.That(view.Model.Files.Single().Name, Is.EqualTo("a.png"));
        }

        [Test]
        public void LegacySnapshotSchemaRestoresAsReadOnlyAndIncorrectStatisticsAreRejected()
        {
            Texture();
            using (var workspace = Workspace()) { workspace.StartScan(AssetCheckKind.Texture); Drain(workspace); }
            var path = _store.PathFor(AssetCheckKind.Texture);
            var snapshot = JsonUtility.FromJson<AssetCheckReportSnapshot>(File.ReadAllText(path));
            snapshot.schemaVersion = 1;
            foreach (var issue in snapshot.issues) issue.unverifiedTargets = null;
            File.WriteAllText(path, JsonUtility.ToJson(snapshot));
            Assert.That(_store.Load(AssetCheckKind.Texture).IsSnapshot, Is.True);
            snapshot.passedFiles = 999;
            File.WriteAllText(path, JsonUtility.ToJson(snapshot));
            Assert.Throws<InvalidDataException>(() => _store.Load(AssetCheckKind.Texture));
        }

        [UnityTest]
        public IEnumerator NativeWindowBindsVirtualizedFileCellsAndExpandedChildren()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("真实窗口绑定验证需要图形设备；请在图形模式运行此用例。");
            var window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            try
            {
                window.position = new Rect(0, 0, 1050, 600);
                window.Show();
                window.CreateGUI();
                var view = window.rootVisualElement.Q<AssetCheckResultsView>();
                view.Bind(Report(Issue("a.png", 'a', "format"), Issue("a.png", 'a', "size")), false);
                yield return null;
                yield return null;
                Assert.That(view.Query<Label>("name").ToList().Any(label => label.text == "a.png"), Is.True);
                var overview = window.rootVisualElement.Q<AssetCheckOverview>();
                var displayedReport = Report(Issue("a.png", 'a', "format"), Issue("a.png", 'a', "size"));
                displayedReport.IsSnapshot = true;
                displayedReport.SnapshotCheckedFiles = 1;
                overview.Bind(displayedReport);
                Assert.That(overview.Q<Label>("problem-count").text, Is.EqualTo("1"));
                Assert.That(overview.Q<Label>("report-state").text, Does.Contain("只读"));
                window.rootVisualElement.Query<Label>(className: "summary").ToList().Last().text =
                    "检查 Assets/Game/VeryLongDirectory/" + new string('x', 180) + ".prefab";
                yield return null;
                var actions = window.rootVisualElement.Query<Button>().ToList().Where(button =>
                    button.text.StartsWith("修改全部") || button.text == "检查全部").ToArray();
                Assert.That(actions.Length, Is.EqualTo(2));
                foreach (var action in actions)
                {
                    Assert.That(action.worldBound.xMin, Is.GreaterThanOrEqualTo(window.rootVisualElement.worldBound.xMin));
                    Assert.That(action.worldBound.xMax, Is.LessThanOrEqualTo(window.rootVisualElement.worldBound.xMax + 1));
                    Assert.That(action.parent.worldBound.height, Is.GreaterThanOrEqualTo(24));
                    Assert.That(action.worldBound.height, Is.GreaterThanOrEqualTo(16));
                    Assert.That(action.worldBound.yMin, Is.GreaterThanOrEqualTo(action.parent.worldBound.yMin));
                    Assert.That(action.worldBound.yMax, Is.LessThanOrEqualTo(action.parent.worldBound.yMax + 1));
                }
                var standards = window.rootVisualElement.Q<Foldout>(className: "standards");
                standards.value = true;
                yield return null;
                Assert.That(standards.Q<ScrollView>().worldBound.height, Is.GreaterThanOrEqualTo(100));
                standards.value = false;
                window.rootVisualElement.RemoveFromClassList("theme-dark");
                window.rootVisualElement.AddToClassList("theme-light");
                yield return null;
                Assert.That(overview.Q<Label>("problem-count").resolvedStyle.color.r, Is.EqualTo(43f / 255).Within(0.01));
                window.rootVisualElement.RemoveFromClassList("theme-light");
                window.rootVisualElement.AddToClassList("theme-dark");
                yield return null;
                Assert.That(overview.Q<Label>("problem-count").resolvedStyle.color.r, Is.GreaterThan(0.8));
                window.rootVisualElement.EnableInClassList("theme-dark", EditorGUIUtility.isProSkin);
                window.rootVisualElement.EnableInClassList("theme-light", !EditorGUIUtility.isProSkin);
                view.Model.Toggle(new string('a', 32));
                view.Bind(Report(Issue("a.png", 'a', "format"), Issue("a.png", 'a', "size")), false);
                yield return null;
                Assert.That(view.Q<MultiColumnListView>().itemsSource.Count, Is.EqualTo(3));
                var ignores = view.Query<Button>("ignore").ToList()
                    .Where(button => button.style.display.value != DisplayStyle.None).ToArray();
                Assert.That(ignores.Length, Is.EqualTo(1), "忽略文件入口只出现在文件主行。");
                Assert.That(view.Q<Button>("locate"), Is.Null);
                Assert.That(view.Q<UnityEditor.UIElements.ToolbarMenu>("more"), Is.Null);
                foreach (var name in new[] { "resource", "problem", "actual", "expected" })
                    Assert.That(view.Q<Button>("sort-" + name), Is.Not.Null);
                foreach (var button in window.rootVisualElement.Query<Button>(className: "header-action").ToList())
                {
                    Assert.That(button.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleCenter));
                    Assert.That(button.worldBound.height, Is.GreaterThanOrEqualTo(25));
                }
                var selectedIssue = (AssetCheckIssue)null;
                var ignoredIssue = (AssetCheckIssue)null;
                var preciseSelection = true;
                var clickedView = new AssetCheckResultsView(_ => { }, issue => ignoredIssue = issue, (issue, precise) =>
                { selectedIssue = issue; preciseSelection = precise; });
                view.RemoveFromHierarchy();
                window.rootVisualElement.Add(clickedView);
                clickedView.Bind(Report(Issue("click.png", 'c', "format")), false);
                yield return null;
                yield return null;
                var fileLabel = clickedView.Q<Label>("name");
                Assert.That(fileLabel.enabledInHierarchy, Is.True);
                var received = false;
                fileLabel.RegisterCallback<ClickEvent>(_ => received = true);
                using (var evt = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
                {
                    Assert.That(evt.button, Is.Zero);
                    evt.target = fileLabel;
                    fileLabel.SendEvent(evt);
                }
                Assert.That(received, Is.True, "模拟点击必须发送到文件名。");
                Assert.That(selectedIssue, Is.Not.Null);
                Assert.That(selectedIssue.TargetId, Is.EqualTo(new string('c', 32)));
                Assert.That(preciseSelection, Is.False, "文件名选择文件，子问题对象可精确定位。");
                Submit(clickedView.Q<Button>("ignore"));
                Assert.That(ignoredIssue.TargetId, Is.EqualTo(new string('c', 32)));
                Submit(clickedView.Q<Button>("sort-actual"));
                Assert.That(clickedView.Model.Sort, Is.EqualTo(AssetCheckSort.Actual));
                Assert.That(clickedView.Model.Descending, Is.False);
                Submit(clickedView.Q<Button>("sort-actual"));
                Assert.That(clickedView.Model.Descending, Is.True);

            }
            finally { window.Close(); }
        }

        [Test]
        public void TextureThumbnailTracksGuidMovesAndClearsRecycledRows()
        {
            var path = Texture();
            var id = AssetDatabase.AssetPathToGUID(path);
            var thumbnail = new AssetCheckThumbnail();
            thumbnail.Bind(id);
            Assert.That(thumbnail.image, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(path)));
            var other = _root + "/other.png";
            Assert.That(AssetDatabase.CopyAsset(path, other), Is.True);
            thumbnail.Bind(AssetDatabase.AssetPathToGUID(other));
            Assert.That(thumbnail.image, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(other)));
            var moved = _root + "/moved.png";
            Assert.That(AssetDatabase.MoveAsset(path, moved), Is.Empty);
            thumbnail.Bind(id);
            Assert.That(thumbnail.tooltip, Is.EqualTo(moved));
            thumbnail.Bind(null);
            Assert.That(thumbnail.image, Is.Null);
            Assert.That(thumbnail.style.display.value, Is.EqualTo(DisplayStyle.None));
            AssetDatabase.DeleteAsset(moved);
            thumbnail.Bind(id);
            Assert.That(thumbnail.image, Is.Null);
            Assert.That(thumbnail.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator NativeTextureThumbnailsRespectGroupingTabsAndReadOnlySnapshots()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("缩略图窗口验证需要图形设备。");
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = false; importer.SaveAndReimport();
            var originalMeta = File.ReadAllBytes(path + ".meta");
            var id = AssetDatabase.AssetPathToGUID(path);
            var selectedBefore = Selection.objects;
            var window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            try
            {
                window.position = new Rect(0, 0, 1050, 600);
                window.Show(); window.CreateGUI();
                var view = window.rootVisualElement.Q<AssetCheckResultsView>();
                var report = Report(new AssetCheckIssue(path, id, AssetCheckKind.Texture, "format", "格式", "当前", "允许", true),
                    new AssetCheckIssue(path, id, AssetCheckKind.Texture, "size", "尺寸", "当前", "允许", true));
                view.Bind(report, false);
                yield return null; yield return null;
                var thumbnail = view.Q<AssetCheckThumbnail>();
                Assert.That(thumbnail.image, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(path)));
                Assert.That(thumbnail.worldBound.width, Is.EqualTo(32).Within(1));
                Assert.That(thumbnail.worldBound.height, Is.EqualTo(32).Within(1));
                using (var evt = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
                {
                    evt.target = thumbnail; thumbnail.SendEvent(evt);
                }
                Assert.That(Selection.activeObject, Is.SameAs(AssetDatabase.LoadMainAssetAtPath(path)));
                view.Model.Toggle(id); view.Bind(report, false);
                yield return null;
                Assert.That(view.Query<AssetCheckThumbnail>().ToList().Count(item => item.style.display.value == DisplayStyle.Flex), Is.EqualTo(1),
                    "问题子行不重复展示文件缩略图。");
                var parentCell = view.Query<VisualElement>(className: "resource-cell").ToList().Single(cell => !cell.ClassListContains("child"));
                var parentName = parentCell.Q<Label>("name");
                foreach (var childCell in view.Query<VisualElement>(className: "child").ToList())
                    Assert.That(childCell.Q<Label>("name").worldBound.xMin, Is.GreaterThan(parentName.worldBound.xMin),
                        "添加缩略图后，问题子行仍须缩进到文件名右侧。");
                report.IsSnapshot = true; view.Bind(report, false);
                yield return null;
                Assert.That(view.Q<AssetCheckThumbnail>().image, Is.Not.Null);
                var other = new AssetCheckReport(AssetCheckKind.Audio, "context", AssetCheckPlatform.MiniGame, _root) { Complete = true };
                other.Issues.Add(new AssetCheckIssue(path, id, AssetCheckKind.Audio, "test", "测试", "当前", "允许", false));
                view.Bind(other, false);
                yield return null;
                Assert.That(view.Query<AssetCheckThumbnail>().ToList().All(item => item.image == null), Is.True,
                    "切换类型时必须清除虚拟行中上一次绑定的预览。");
                Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(originalMeta));
            }
            finally { window.Close(); Selection.objects = selectedBefore; }
        }

        [UnityTest]
        public IEnumerator AtlasThumbnailUsesNativePreviewWithoutChangingOrPackingTheAsset()
        {
            var path = _root + "/preview.spriteatlas";
            var atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, path);
            var original = File.ReadAllBytes(path);
            var originalMeta = File.ReadAllBytes(path + ".meta");
            var thumbnail = new AssetCheckThumbnail();
            thumbnail.Bind(AssetDatabase.AssetPathToGUID(path));
            yield return null;
            yield return null;
            Assert.That(thumbnail.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(thumbnail.image, Is.Not.Null, "预览尚未可用时保留 Unity 原生图集图标。");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(originalMeta));
            thumbnail.ResetPreview();
            Assert.That(thumbnail.image, Is.Null);
        }

        [UnityTest]
        public IEnumerator NativeModelAndMaterialPreviewsKeepFileSelectionAndChildIndentation()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("模型和材质预览验证需要图形设备。");
            var modelPath = _root + "/model.obj";
            File.WriteAllText(modelPath, "o Triangle\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            var meshPath = _root + "/mesh.asset";
            AssetDatabase.CreateAsset(new Mesh { name = "Triangle", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 } }, meshPath);
            var materialPath = _root + "/material.mat";
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Texture")), materialPath);
            var paths = new[] { modelPath, meshPath, materialPath };
            var originalFiles = paths.Select(File.ReadAllBytes).ToArray();
            var originalMetas = paths.Select(path => File.ReadAllBytes(path + ".meta")).ToArray();
            var selectedBefore = Selection.objects;
            var window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            try
            {
                window.position = new Rect(0, 0, 1050, 600);
                window.Show(); window.CreateGUI();
                var view = window.rootVisualElement.Q<AssetCheckResultsView>();
                for (var index = 0; index < paths.Length; index++)
                {
                    var path = paths[index];
                    var kind = index == 2 ? AssetCheckKind.Material : AssetCheckKind.Model;
                    var id = AssetDatabase.AssetPathToGUID(path);
                    var report = new AssetCheckReport(kind, "context", AssetCheckPlatform.MiniGame, _root) { Complete = true };
                    report.Issues.Add(new AssetCheckIssue(path, id, kind, "first", "问题一", "当前", "允许", false));
                    report.Issues.Add(new AssetCheckIssue(path, id, kind, "second", "问题二", "当前", "允许", false));
                    view.Bind(report, false);
                    yield return null; yield return null;
                    var thumbnail = view.Q<AssetCheckThumbnail>();
                    Assert.That(thumbnail.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                    Assert.That(thumbnail.image, Is.Not.Null, "预览未就绪时应保留资源图标。");
                    Assert.That(thumbnail.tooltip, Is.EqualTo(path));
                    Assert.That(thumbnail.worldBound.width, Is.EqualTo(32).Within(1));
                    using (var evt = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
                    { evt.target = thumbnail; thumbnail.SendEvent(evt); }
                    Assert.That(Selection.activeObject, Is.SameAs(AssetDatabase.LoadMainAssetAtPath(path)));
                    view.Model.Toggle(id); view.Bind(report, false);
                    yield return null;
                    Assert.That(view.Query<AssetCheckThumbnail>().ToList().Count(item => item.style.display.value == DisplayStyle.Flex), Is.EqualTo(1));
                    var parentCell = view.Query<VisualElement>(className: "resource-cell").ToList().Single(cell => !cell.ClassListContains("child"));
                    foreach (var childCell in view.Query<VisualElement>(className: "child").ToList())
                        Assert.That(childCell.Q<Label>("name").worldBound.xMin, Is.GreaterThan(parentCell.Q<Label>("name").worldBound.xMin));
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(originalFiles[index]));
                    Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(originalMetas[index]));
                }
                var unsupported = new AssetCheckReport(AssetCheckKind.PrefabScene, "context", AssetCheckPlatform.MiniGame, _root) { Complete = true };
                unsupported.Issues.Add(new AssetCheckIssue(modelPath, AssetDatabase.AssetPathToGUID(modelPath), AssetCheckKind.PrefabScene,
                    "test", "测试", "当前", "允许", false));
                view.Bind(unsupported, false);
                yield return null;
                Assert.That(view.Query<AssetCheckThumbnail>().ToList().All(item => item.image == null), Is.True,
                    "切换到未启用预览的类型后，不应残留模型或材质预览。");
            }
            finally { window.Close(); Selection.objects = selectedBefore; }
        }

        [Test]
        public void NestedReferenceLabelKeepsAffectedObjectComponentAndField()
        {
            var path = _root + "/effect.prefab";
            var issue = new AssetCheckIssue(path, new string('a', 32), AssetCheckKind.PrefabScene,
                "reference.missing", "失效引用", "丢失", "有效引用", false,
                resourceName: path + " / Root[0]/Rewards[1]/confetti_large[2] / ParticleSystemRenderer[2] / Mesh");
            var label = AssetCheckResultsModel.Subject(issue);
            Assert.That(label, Does.StartWith("confetti_large[2]"));
            Assert.That(label, Does.Contain("ParticleSystemRenderer[2]"));
            Assert.That(label, Does.EndWith("Mesh"));
            Assert.That(label, Does.Not.Contain("Root[0]"));
        }

        private AssetCheckReport Report(params AssetCheckIssue[] issues)
        {
            var report = new AssetCheckReport(AssetCheckKind.Texture, "context", AssetCheckPlatform.MiniGame, _root) { Complete = true };
            report.Issues.AddRange(issues);
            report.CheckedTargets.AddRange(issues.Select(issue => issue.TargetId).Distinct());
            report.TotalFiles = report.CheckedTargets.Count;
            return report;
        }

        private static void Submit(Button button)
        {
            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
        }

        private AssetCheckIssue Issue(string name, char id, string rule) => new AssetCheckIssue(_root + "/" + name,
            new string(id, 32), AssetCheckKind.Texture, rule, rule, "current", "expected", true);

        private AssetCheckWorkspace Workspace()
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings => settings.scope = _root);
            return workspace;
        }

        private string Texture()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var path = _root + "/texture.png";
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static void WriteFormat(TextureImporter importer, string target, TextureImporterFormat format)
        {
            var settings = importer.GetPlatformTextureSettings(target);
            settings.overridden = true; settings.format = format; importer.SetPlatformTextureSettings(settings);
        }

        private static void Drain(AssetCheckWorkspace workspace)
        {
            for (var index = 0; workspace.Running && index < 2000; index++) workspace.Step();
            Assert.That(workspace.Running, Is.False); Assert.That(workspace.Failure, Is.Null);
        }
    }
}
