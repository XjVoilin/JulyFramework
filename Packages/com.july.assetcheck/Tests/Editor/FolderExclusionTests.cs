using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class FolderExclusionTests
    {
        private string _root, _config;

        [SetUp]
        public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__AssetCheckFolders_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "AssetCheckFolders_" + id);
            Directory.CreateDirectory(_config);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_root);
            Assert.That(Path.GetFullPath(_config), Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_config, true);
        }

        [Test]
        public void IgnoredFolderSkipsNestedResourcesAcrossTypesWithoutMatchingSimilarNames()
        {
            var folder = Folder(_root, "Skip");
            var nested = Folder(folder, "Nested");
            var sibling = Folder(_root, "SkipOther");
            var excluded = Texture(nested, "excluded.png");
            var included = Texture(sibling, "included.png");
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Texture")), nested + "/material.mat");
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            AssetDatabase.CreateAsset(mesh, nested + "/mesh.asset");
            using (var workspace = Workspace())
            {
                workspace.SetFoldersIgnored(new[] { folder }, true);
                Scan(workspace, AssetCheckKind.Texture);
                var report = workspace.GetReport(AssetCheckKind.Texture);
                Assert.That(report.TotalFiles, Is.EqualTo(2));
                Assert.That(report.IgnoredTargets, Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(excluded) }));
                Assert.That(report.CheckedTargets, Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(included) }));
                foreach (var kind in new[] { AssetCheckKind.Material, AssetCheckKind.Model })
                {
                    Scan(workspace, kind);
                    Assert.That(workspace.GetReport(kind).IgnoredFiles, Is.EqualTo(1));
                    Assert.That(workspace.GetReport(kind).CheckedFiles, Is.Zero);
                    Assert.That(workspace.GetReport(kind).Issues, Is.Empty);
                }
            }
        }

        [Test]
        public void FolderGuidFollowsMovesAndDoesNotExcludeANewFolderAtTheOldPath()
        {
            var folder = Folder(_root, "Original");
            Texture(folder, "ignored.png");
            using (var workspace = Workspace())
            {
                workspace.SetFoldersIgnored(new[] { folder }, true);
                Assert.That(AssetDatabase.MoveAsset(folder, _root + "/Renamed"), Is.Empty);
                Folder(_root, "Original");
                var included = Texture(folder, "new.png");
                Scan(workspace, AssetCheckKind.Texture);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).IgnoredFiles, Is.EqualTo(1));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).CheckedTargets,
                    Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(included) }));
            }
        }

        [Test]
        public void CancellingFolderIgnorePreservesIndependentFilesAndNestedFolderRecords()
        {
            var parent = Folder(_root, "Parent");
            var child = Folder(parent, "Child");
            var independent = Texture(parent, "independent.png");
            Texture(child, "nested.png");
            var included = Texture(parent, "included.png");
            using (var workspace = Workspace())
            {
                workspace.Ignore(new AssetCheckIssue(independent, AssetDatabase.AssetPathToGUID(independent),
                    AssetCheckKind.Texture, "test", "test", "current", "expected", false));
                workspace.SetFoldersIgnored(new[] { child }, true);
                workspace.SetFoldersIgnored(new[] { parent }, true);
                workspace.SetFoldersIgnored(new[] { parent }, false);
                Assert.That(workspace.Settings.ignoredAssets.Count, Is.EqualTo(2));
                Scan(workspace, AssetCheckKind.Texture);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).IgnoredFiles, Is.EqualTo(2));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).CheckedTargets,
                    Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(included) }));
                workspace.SetFoldersIgnored(new[] { child }, false);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                Scan(workspace, AssetCheckKind.Texture);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).IgnoredFiles, Is.EqualTo(1));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).CheckedFiles, Is.EqualTo(2));
            }
        }

        [Test]
        public void MenuOperationPersistsMultipleFoldersWithoutAnOpenWindowAndPreservesStandards()
        {
            var a = Folder(_root, "A");
            var b = Folder(_root, "B");
            var file = new AssetCheckSettingsFile(_config);
            var settings = file.LoadOrCreate();
            settings.CurrentStandards.texture.maxTextureSize = 2048;
            file.Save(settings);
            AssetCheckProjectMenu.Apply(file, new[] { a, b }, true);
            AssetCheckProjectMenu.Apply(file, new[] { a, b, a }, true);
            var loaded = new AssetCheckSettingsFile(_config).LoadOrCreate();
            Assert.That(loaded.ignoredAssets.Select(record => record.guid),
                Is.EquivalentTo(new[] { AssetDatabase.AssetPathToGUID(a), AssetDatabase.AssetPathToGUID(b) }));
            Assert.That(loaded.CurrentStandards.texture.maxTextureSize, Is.EqualTo(2048));
            AssetCheckProjectMenu.Apply(new AssetCheckSettingsFile(_config), new[] { a, b }, false);
            Assert.That(new AssetCheckSettingsFile(_config).LoadOrCreate().ignoredAssets, Is.Empty);
        }

        [Test]
        public void FolderMenuActionsFollowSingleMixedAndInheritedIgnoreStates()
        {
            var parent = Folder(_root, "Parent");
            var child = Folder(parent, "Child");
            var other = Folder(_root, "Other");
            using (var workspace = Workspace())
            {
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { parent }, workspace.Settings, true), Is.True);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { parent }, workspace.Settings, false), Is.False,
                    "未忽略的单个文件夹不能取消忽略。");
                workspace.SetFoldersIgnored(new[] { parent }, true);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { parent }, workspace.Settings, true), Is.False);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { parent }, workspace.Settings, false), Is.True);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { parent, other }, workspace.Settings, true), Is.True);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { parent, other }, workspace.Settings, false), Is.True);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { child }, workspace.Settings, true), Is.False,
                    "继承上级忽略时不能重复添加无效操作。");
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { child }, workspace.Settings, false), Is.False,
                    "没有独立记录的子目录不能取消上级忽略。");
                workspace.SetFoldersIgnored(new[] { child }, true);
                Assert.That(workspace.Settings.ignoredAssets.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void ReadingMenuStateDoesNotCreateOrUpgradeProjectConfiguration()
        {
            var file = new AssetCheckSettingsFile(_config);
            var path = Path.Combine(_config, AssetCheckSettingsFile.FileName);
            Assert.That(file.ReadExistingOrDefault().ignoredAssets, Is.Empty);
            Assert.That(File.Exists(path), Is.False);
            var legacy = AssetCheckSettings.CreateDefault();
            legacy.version = 2;
            var text = JsonUtility.ToJson(legacy);
            File.WriteAllText(path, text);
            Assert.That(new AssetCheckSettingsFile(_config).ReadExistingOrDefault().version, Is.EqualTo(4));
            Assert.That(File.ReadAllText(path), Is.EqualTo(text));
        }

        [Test]
        public void InvalidMixedSelectionCannotPartiallySaveAndMenuRejectsFiles()
        {
            var folder = Folder(_root, "Folder");
            var texture = Texture(_root, "texture.png");
            using (var workspace = Workspace())
            {
                Assert.Throws<ArgumentException>(() => workspace.SetFoldersIgnored(new[] { folder, texture }, true));
                Assert.That(workspace.Settings.ignoredAssets, Is.Empty);
                Assert.That(new AssetCheckSettingsFile(_config).LoadOrCreate().ignoredAssets, Is.Empty);
            }
            var old = Selection.objects;
            try
            {
                Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(folder) };
                Assert.That(AssetCheckProjectMenu.ValidateSelection(), Is.True);
                Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(folder), AssetDatabase.LoadMainAssetAtPath(texture) };
                Assert.That(AssetCheckProjectMenu.ValidateSelection(), Is.False);
                Selection.objects = Array.Empty<Object>();
                Assert.That(AssetCheckProjectMenu.ValidateSelection(), Is.False);
            }
            finally { Selection.objects = old; }
        }

        [Test]
        public void FolderChangesInvalidateResultsAndOldSelectionsCannotModifyIgnoredFiles()
        {
            var folder = Folder(_root, "Excluded");
            var path = Texture(folder, "readable.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.SaveAndReimport();
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                var old = workspace.GetReport(AssetCheckKind.Texture).Issues.Where(issue => issue.RuleId == "texture.read-write").ToArray();
                Assert.That(old.Length, Is.EqualTo(1));
                workspace.SetFoldersIgnored(new[] { folder }, true);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                Assert.Throws<InvalidOperationException>(() => workspace.StartFix(AssetCheckKind.Texture, old));
                Scan(workspace, AssetCheckKind.Texture);
                workspace.StartFix(AssetCheckKind.Texture, old);
                Assert.That(workspace.Running, Is.False);
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, Is.True);
                workspace.StartScan(AssetCheckKind.Texture);
                Assert.Throws<InvalidOperationException>(() => workspace.SetFoldersIgnored(new[] { folder }, false));
                workspace.Cancel();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IgnoringParentAndChildInOneBatchCreatesOnlyTheParentRecord(bool reverse)
        {
            var parent = Folder(_root, "Parent");
            var child = Folder(parent, "Child");
            Texture(parent, "parent.png");
            Texture(child, "child.png");
            using (var workspace = Workspace())
            {
                workspace.SetFoldersIgnored(reverse ? new[] { child, parent } : new[] { parent, child }, true);
                Assert.That(workspace.Settings.ignoredAssets.Select(record => record.guid),
                    Is.EqualTo(new[] { AssetDatabase.AssetPathToGUID(parent) }), "同批父子选择只新增父目录，不能留下意外子目录忽略。");
                workspace.SetFoldersIgnored(new[] { parent }, false);
                Scan(workspace, AssetCheckKind.Texture);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).IgnoredFiles, Is.Zero);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).CheckedFiles, Is.EqualTo(2));
            }
        }

        [Test]
        public void FolderIgnoreImmediatelyRemovesOldProblemsAndSelectionsAndUpdatesKnownCounts()
        {
            var folder = Folder(_root, "Excluded");
            var path = Texture(folder, "excluded.png");
            Texture(_root, "included.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true; importer.SaveAndReimport();
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                var report = workspace.GetReport(AssetCheckKind.Texture);
                var model = new AssetCheckResultsModel(); model.Bind(report);
                var id = AssetDatabase.AssetPathToGUID(path);
                model.Selected.Add(id);
                workspace.SetFoldersIgnored(new[] { folder }, true);
                Assert.That(report.Issues.Any(issue => issue.TargetId == id), Is.False, "已经忽略的目录不应继续展示旧问题。");
                Assert.That(report.IgnoredFiles, Is.EqualTo(1));
                Assert.That(report.CheckedFiles, Is.EqualTo(1));
                model.Bind(report);
                Assert.That(model.Selected, Is.Empty);
                Assert.That(model.Files.Any(file => file.Id == id), Is.False);
                var saved = new AssetCheckReportStore(_config).Load(AssetCheckKind.Texture);
                Assert.That(saved.IgnoredFiles, Is.EqualTo(1));
                Assert.That(saved.Issues.Any(issue => issue.TargetId == id), Is.False);
            }
        }

        [Test]
        public void IgnoreListChangesWhileWindowIsClosedInvalidateTheSavedReport()
        {
            var folder = Folder(_root, "Excluded"); Texture(folder, "texture.png");
            using (var workspace = Workspace()) Scan(workspace, AssetCheckKind.Texture);
            var file = new AssetCheckSettingsFile(_config);
            var settings = file.LoadOrCreate();
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(folder), lastKnownPath = folder });
            file.Save(settings);
            using (var workspace = Workspace())
            {
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True,
                    "缓存必须识别忽略名单变化，不能继续声称旧统计有效。");
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues, Is.Empty,
                    "重新打开窗口也应清理已经被当前配置忽略的旧问题。");
            }
        }

        [Test]
        public void RestoringAnUnknownRecordDoesNotInvalidateFreshResultsButRealRestoreClearsSelection()
        {
            var path = Texture(_root, "texture.png");
            var other = Texture(_root, "other.png");
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                var report = workspace.GetReport(AssetCheckKind.Texture);
                workspace.Restore(new string('a', 32));
                Assert.That(report.Stale, Is.False, "没有记录可移除时不应使全部结果过期。");
                var issue = report.Issues.First(row => row.TargetId == AssetDatabase.AssetPathToGUID(path));
                workspace.Ignore(issue);
                var model = new AssetCheckResultsModel(); model.Bind(report);
                model.Selected.Add(AssetDatabase.AssetPathToGUID(other));
                workspace.Restore(issue.TargetId);
                model.Bind(report);
                Assert.That(model.Selected, Is.Empty, "恢复改变检查范围，旧勾选应清空。");
            }
        }

        [Test]
        public void IgnoringAnAlreadyCoveredFileDoesNotAddAnIndependentRecord()
        {
            var folder = Folder(_root, "Excluded");
            var path = Texture(folder, "texture.png");
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                var issue = workspace.GetReport(AssetCheckKind.Texture).Issues.First();
                workspace.SetFoldersIgnored(new[] { folder }, true);
                workspace.Ignore(issue);
                Assert.That(workspace.Settings.ignoredAssets.Count, Is.EqualTo(1), "旧结果的忽略按钮不能新增冗余单文件记录。");
            }
        }

        [Test]
        public void GenericSettingsChangesToExclusionsCannotLeaveAnOldReportFresh()
        {
            var folder = Folder(_root, "Excluded"); Texture(folder, "texture.png");
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                workspace.ChangeSettings(settings => settings.ignoredAssets.Add(new IgnoredAsset
                    { guid = AssetDatabase.AssetPathToGUID(folder), lastKnownPath = folder }));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
            }
        }

        [Test]
        public void SnapshotPassingFilesAreNotSilentlyReportedAsFreshAfterIgnoringTheirSharedFile()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                triangles = new[] { 0, 1, 2, 1, 3, 2 } };
            var path = _root + "/shared.asset";
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.AddObjectToAsset(new Material(Shader.Find("Unlit/Texture")), path);
            AssetDatabase.SaveAssets();
            using (var workspace = Workspace())
            {
                workspace.ChangeSettings(settings => settings.CurrentStandards.model.maxTrianglesPerMesh = 1);
                Scan(workspace, AssetCheckKind.Model); Scan(workspace, AssetCheckKind.Material);
                Assert.That(workspace.GetReport(AssetCheckKind.Material).PassedAssets, Is.EqualTo(1));
            }
            using (var workspace = Workspace())
            {
                workspace.Ignore(workspace.GetReport(AssetCheckKind.Model).Issues.Single());
                Assert.That(workspace.GetReport(AssetCheckKind.Material).Stale, Is.True,
                    "快照没有全部通过文件的标识，无法准确重算时必须标记过期。");
            }
        }

        [Test]
        public void RemovingAChildRecordExplainsThatTheParentStillExcludesIt()
        {
            var parent = Folder(_root, "Parent");
            var child = Folder(parent, "Child");
            using (var workspace = Workspace())
            {
                workspace.SetFoldersIgnored(new[] { child }, true);
                workspace.SetFoldersIgnored(new[] { parent }, true);
                var exclusions = new AssetCheckExclusions(workspace.Settings);
                Assert.That(exclusions.FindParentFolder(child), Is.EqualTo(parent));
                workspace.Restore(AssetDatabase.AssetPathToGUID(child));
                Assert.That(workspace.Activity, Does.Contain(parent));
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { child }, workspace.Settings, true), Is.False);
                Assert.That(AssetCheckProjectMenu.CanChangeFolders(new[] { child }, workspace.Settings, false), Is.False);
            }
        }

        [Test]
        public void ExclusionUpdatesDoNotRelabelHistoricalScopeOrStandards()
        {
            var folder = Folder(_root, "Excluded"); Texture(folder, "texture.png");
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                var original = workspace.GetReport(AssetCheckKind.Texture).Context;
                workspace.ChangeSettings(settings => settings.scope = "Assets");
                workspace.SetFoldersIgnored(new[] { folder }, true);
                var saved = new AssetCheckReportStore(_config).Load(AssetCheckKind.Texture);
                Assert.That(saved.Scope, Is.EqualTo(_root));
                Assert.That(saved.Context, Is.EqualTo(original));
                Assert.That(saved.Stale, Is.True);
                Assert.That(saved.Issues, Is.Empty);
            }
        }

        [Test]
        public void ReorderingIgnoreRecordsDoesNotMakeAnUnchangedReportStale()
        {
            var a = Folder(_root, "A"); var b = Folder(_root, "B");
            Texture(_root, "visible.png");
            using (var workspace = Workspace())
            {
                workspace.SetFoldersIgnored(new[] { a, b }, true);
                Scan(workspace, AssetCheckKind.Texture);
                workspace.ChangeSettings(settings => settings.ignoredAssets.Reverse());
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.False);
            }
            using (var workspace = Workspace())
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MovingAnIgnoredPipelineOutOfItsFolderInvalidatesTheProjectWideReport(bool duringScan)
        {
            var source = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (source == null) Assert.Ignore("需要真实 URP 资源运行此集成用例。");
            var folder = Folder(_root, "Excluded");
            var path = folder + "/pipeline.asset";
            var pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(source));
            AssetDatabase.CreateAsset(pipeline, path);
            var previous = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            try
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
                using (var workspace = Workspace())
                {
                    workspace.SetFoldersIgnored(new[] { folder }, true);
                    if (duringScan)
                    {
                        workspace.StartScan(AssetCheckKind.Urp);
                        workspace.Step(); workspace.Step();
                    }
                    else Scan(workspace, AssetCheckKind.Urp);
                    var report = workspace.GetReport(AssetCheckKind.Urp);
                    Assert.That(report.CollectedPaths, Does.Contain(path));
                    if (!duringScan) Assert.That(report.IgnoredTargets, Does.Contain(AssetCheckTarget.Id(path)));
                    Assert.That(report.Stale, Is.False);
                    Assert.That(AssetDatabase.MoveAsset(path, _root + "/pipeline.asset"), Is.Empty);
                    Assert.That(report.Stale, Is.True, "URP 不依赖普通扫描目录，但移出忽略目录后必须重新检查。");
                }
            }
            finally { UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = previous; }
        }

        private AssetCheckWorkspace Workspace()
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings => settings.scope = _root);
            return workspace;
        }

        private static string Folder(string parent, string name)
        {
            AssetDatabase.CreateFolder(parent, name);
            return parent + "/" + name;
        }

        private static string Texture(string parent, string name)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var path = parent + "/" + name;
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static void Scan(AssetCheckWorkspace workspace, AssetCheckKind kind)
        {
            workspace.StartScan(kind);
            for (var step = 0; workspace.Running && step < 1000; step++) workspace.Step();
            Assert.That(workspace.Running, Is.False);
            Assert.That(workspace.Failure, Is.Null);
        }
    }
}
