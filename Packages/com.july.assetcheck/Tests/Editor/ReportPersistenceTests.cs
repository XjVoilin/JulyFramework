using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.AssetCheck.Tests
{
    public sealed class ReportPersistenceTests
    {
        private string _root, _directory;
        private AssetCheckReportStore _store;

        [SetUp]
        public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__AssetCheckPersistence_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _directory = Path.GetFullPath(Path.Combine("Temp", "AssetCheckPersistence_" + id));
            Directory.CreateDirectory(_directory);
            _store = new AssetCheckReportStore(_directory);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_root);
            Assert.That(_directory, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_directory, true);
        }

        [Test]
        public void ScanAllWritesOneLatestFilePerModuleIncludingEmptyResults()
        {
            using (var workspace = Workspace())
            {
                workspace.StartScan();
                Drain(workspace);
                Assert.That(Directory.GetFiles(_store.DirectoryPath).Length, Is.EqualTo(AssetCheckCatalog.Entries.Length));
                foreach (var entry in AssetCheckCatalog.Entries)
                {
                    var saved = Read(entry.Kind);
                    Assert.That(saved.module, Is.EqualTo(entry.Kind.ToString()));
                    Assert.That(saved.complete, Is.True);
                    Assert.That(saved.status, Is.EqualTo("Completed"));
                }
            }
        }

        [Test]
        public void RepeatedScanOverwritesItsModuleAndLeavesOtherModulesIntact()
        {
            Mesh("a.asset");
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Material);
                Drain(workspace);
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                Assert.That(Read(AssetCheckKind.Model).checkedFiles, Is.EqualTo(1));
                Mesh("b.asset");
                // 导入资源会使范围内已有报告过期；此处只要求新扫描不影响其它报告。
                var material = File.ReadAllText(_store.PathFor(AssetCheckKind.Material));
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                Assert.That(Read(AssetCheckKind.Model).checkedFiles, Is.EqualTo(2));
                Assert.That(File.ReadAllText(_store.PathFor(AssetCheckKind.Material)), Is.EqualTo(material));
                Assert.That(Directory.GetFiles(_store.DirectoryPath).Length, Is.EqualTo(2));
            }
            Assert.That(Read(AssetCheckKind.Model).complete, Is.True);
        }

        [Test]
        public void StartingNewScanReplacesPreviousResultWithExplicitIncompleteState()
        {
            Mesh("a.asset");
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                workspace.StartScan(AssetCheckKind.Model);
                workspace.Step();
                Assert.That(Read(AssetCheckKind.Model).status, Is.EqualTo("Scanning"));
                Assert.That(Read(AssetCheckKind.Model).complete, Is.False);
                workspace.Cancel();
                Assert.That(Read(AssetCheckKind.Model).status, Is.EqualTo("Cancelled"));
            }
        }

        [Test]
        public void CancellingScanSavesOnlyCompletelyCheckedFiles()
        {
            Mesh("a.asset");
            Mesh("b.asset");
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Model);
                workspace.Step();
                workspace.Step();
                workspace.Cancel();
                var saved = Read(AssetCheckKind.Model);
                Assert.That(saved.status, Is.EqualTo("Cancelled"));
                Assert.That(saved.checkedFiles, Is.EqualTo(1));
                Assert.That(saved.totalFiles, Is.EqualTo(2));
                Assert.That(saved.complete, Is.False);
            }
        }

        [Test]
        public void FailedScanSavesTheFailureAndPartialCounts()
        {
            Mesh("a.asset");
            Mesh("b.asset");
            using (var workspace = Workspace())
            {
                workspace.StartScan(AssetCheckKind.Model);
                workspace.Step();
                workspace.Step();
                AssetDatabase.DeleteAsset(_root + "/b.asset");
                LogAssert.Expect(LogType.Exception, new Regex("IOException: 检查对象已移除"));
                workspace.Step();
                var saved = Read(AssetCheckKind.Model);
                Assert.That(saved.status, Is.EqualTo("Failed"));
                Assert.That(saved.failure, Does.Contain("检查对象已移除"));
                Assert.That(saved.checkedFiles, Is.EqualTo(1));
                Assert.That(saved.complete, Is.False);
            }
        }

        [Test]
        public void IgnoreAndRestoreUpdateSavedReportAndKeepItsOriginalScope()
        {
            Mesh("a.asset");
            using (var workspace = Workspace())
            {
                workspace.ChangeSettings(s => s.CurrentStandards.model.maxTrianglesPerMesh = 1);
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                var issue = workspace.GetReport(AssetCheckKind.Model).Issues.Single();
                workspace.Ignore(issue);
                var saved = Read(AssetCheckKind.Model);
                Assert.That(saved.issues, Is.Empty);
                Assert.That(saved.ignoredFiles, Is.EqualTo(1));
                workspace.Restore(issue.TargetId);
                Assert.That(Read(AssetCheckKind.Model).stale, Is.True);
                workspace.ChangeSettings(s => s.scope = "Assets");
                Assert.That(Read(AssetCheckKind.Model).scope, Is.EqualTo(_root));
                Assert.That(Read(AssetCheckKind.Model).standardsContext, Does.Contain(_root));
            }
        }

        [Test]
        public void SavedRowsPreserveKnownProblemsUnverifiedTargetsAndExactLocalIdentifiers()
        {
            var report = new AssetCheckReport(AssetCheckKind.Texture, "original-context", AssetCheckPlatform.MiniGame, _root);
            report.CheckedTargets.Add("asset-guid");
            report.Issues.Add(new AssetCheckIssue(_root + "/texture.png", "asset-guid", AssetCheckKind.Texture,
                "texture.format", "压缩格式", "RGBA32 / Automatic", "ASTC 6x6", true, true,
                localId: long.MaxValue, targets: new[] { "WebGL", "MiniGame" }, hasProblem: true, propertyPath: "texture.format"));
            report.Complete = true;
            _store.Save(report, false);
            var saved = Read(AssetCheckKind.Texture);
            Assert.That(saved.problemFiles, Is.EqualTo(1));
            Assert.That(saved.unverifiedFiles, Is.EqualTo(1));
            Assert.That(saved.passedFiles, Is.Zero);
            Assert.That(saved.issues.Single().localId, Is.EqualTo(long.MaxValue.ToString()));
            Assert.That(saved.issues.Single().targets, Is.EqualTo(new[] { "WebGL", "MiniGame" }));
            Assert.That(saved.issues.Single().hasProblem && saved.issues.Single().unverified, Is.True);
            Assert.That(saved.issues.Single().propertyPath, Is.EqualTo("texture.format"));
        }

        [Test]
        public void StorageFailureIsVisibleWithoutDiscardingValidMemoryResults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_store.DirectoryPath));
            File.WriteAllText(_store.DirectoryPath, "Blocks the report directory");
            using (var workspace = Workspace())
            {
                LogAssert.Expect(LogType.Error, new Regex("模型结果保存失败："));
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Model).Complete, Is.True);
                Assert.That(workspace.ReportSaveFailure, Does.Contain("模型结果保存失败"));
            }
        }

        private AssetCheckWorkspace Workspace()
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_directory));
            workspace.ChangeSettings(s => s.scope = _root);
            return workspace;
        }

        private void Mesh(string name) => AssetDatabase.CreateAsset(new Mesh
        {
            vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
            triangles = new[] { 0, 1, 2, 0, 1, 2 }
        }, _root + "/" + name);

        private AssetCheckReportSnapshot Read(AssetCheckKind kind) =>
            JsonUtility.FromJson<AssetCheckReportSnapshot>(File.ReadAllText(_store.PathFor(kind)));

        private static void Drain(AssetCheckWorkspace workspace)
        {
            for (var index = 0; workspace.Running && index < 2000; index++) workspace.Step();
            Assert.That(workspace.Running, Is.False);
            Assert.That(workspace.Failure, Is.Null);
        }
    }
}
