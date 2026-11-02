using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using static July.AssetCheck.Tests.BundleAnalysisPanelTests;

namespace July.AssetCheck.Tests
{
    public sealed class ResourceResultsExtensionTests
    {
        private string _root, _temp, _report;
        private AssetCheckWorkspace _workspace;
        private EditorWindow _host;
        private BundleAnalysisPanel _panel;

        [SetUp] public void Setup()
        {
            _root = "Assets/__ResultExtension_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _temp = Path.GetFullPath("Temp/ResultExtension_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_temp);
            _report = Path.Combine(_temp, "fixture.report"); _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp));
        }
        [TearDown] public void Cleanup()
        {
            _panel?.Dispose(); if (_host != null) Object.DestroyImmediate(_host); _workspace.Dispose(); AssetDatabase.DeleteAsset(_root);
            Assert.That(_temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(_temp, true);
        }
        private string MeshFile(bool compound)
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, subMeshCount = 5 };
            for (var i = 0; i < 5; i++) mesh.SetTriangles(new[] { 0, 1, 2 }, i);
            var path = _root + "/mesh.asset"; AssetDatabase.CreateAsset(mesh, path);
            if (compound) AssetDatabase.AddObjectToAsset(new Material(Shader.Find("Sprites/Default")), path);
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); return path;
        }
        private void Open()
        {
            _host = ScriptableObject.CreateInstance<EditorWindow>(); _host.position = new Rect(0, 0, 1050, 700); _host.Show();
            _host.rootVisualElement.AddToClassList("asset-check"); _host.rootVisualElement.AddToClassList("theme-dark");
            _host.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Views/AssetCheckWindow.uss"));
            _panel = new BundleAnalysisPanel(() => _workspace, () => { }); _host.rootVisualElement.Add(_panel); _panel.ImportReport(_report);
        }
        private static AssetCheckFileData Data(string id, string status, bool complete = true, bool unverified = false, bool candidate = false) =>
            new AssetCheckFileData { Id = id, Path = "Assets/" + id + ".asset", Status = status, Complete = complete, Unverified = unverified, Candidate = candidate, ResolvePath = () => "" };

        [Test] public void EmptyIssueRowsKeepIdentityAndDistinctPassCandidateUnverifiedAndPendingStates()
        {
            var entries = new[] { Data("passed", "通过"), Data("duplicate", "优化候选", candidate: true), Data("pending", "未完成", false), Data("missing", "未能检查", unverified: true) };
            var model = new AssetCheckResultsModel(); model.BindFiles(new object(), AssetCheckKind.Model, 1, entries);
            Assert.That(model.Files.Count, Is.EqualTo(4)); Assert.That(model.UnknownFiles, Is.EqualTo(1)); Assert.That(model.CanModify, Is.False);
            model.ExpandAll(true); Assert.That(model.Rows.Where(row => row.IsFile).Select(row => row.File.Id), Is.EquivalentTo(entries.Select(item => item.Id)));
            foreach (var sort in new[] { AssetCheckSort.Problem, AssetCheckSort.Actual, AssetCheckSort.Expected }) model.SortBy(sort);
            foreach (var filter in new[] { "通过", "优化候选", "未能检查", "未完成" })
            { model.Query("", filter); Assert.That(model.Files.Count, Is.EqualTo(1)); Assert.That(model.Files[0].Data.Status, Is.EqualTo(filter)); }
            model.Query("missing", "全部"); Assert.That(model.Files.Single().Id, Is.EqualTo("missing"));
        }

        [Test] public void NewReportIdentityClearsRuleAndExpansionWhileProgressUpdatesRetainThem()
        {
            var issue = new AssetCheckIssue("Assets/a.asset", "same-guid", AssetCheckKind.Model, "rule", "问题", "5", "4", true);
            var file = Data("same-guid", "违规"); file.Issues = new[] { issue, issue };
            var source = new object(); var model = new AssetCheckResultsModel(); model.BindFiles(source, "Model", 1, new[] { file });
            model.Toggle(file.Id); model.Filter("rule"); model.BindFiles(source, "Model", 2, new[] { file });
            Assert.That(model.Expanded, Does.Contain(file.Id)); Assert.That(model.Rule, Is.EqualTo("rule"));
            model.BindFiles(new object(), "Model", 0, new[] { file });
            Assert.That(model.Expanded, Is.Empty); Assert.That(model.Rule, Is.Null); Assert.That(model.Selected, Is.Empty);
        }

        [Test] public void ConfirmedProblemsAndOptimizationCandidatesRemainIndependentlyFilterable()
        {
            var file = Data("mixed", "违规／优化候选", candidate: true);
            file.Issues = new[] { new AssetCheckIssue(file.Path, file.Id, AssetCheckKind.Model, "rule", "违规", "5", "4", false) };
            var model = new AssetCheckResultsModel(); model.BindFiles(new object(), "Model", 0, new[] { file });
            foreach (var status in new[] { "违规", "优化候选" })
            { model.Query("", status); Assert.That(model.Files.Single().Id, Is.EqualTo(file.Id)); }
            model.Query("", "通过"); Assert.That(model.Files, Is.Empty);
            Assert.That(file.Issues.Length, Is.EqualTo(1)); Assert.That(model.AllFixable(), Is.Empty);
        }

        [Test] public void ClassificationRecordsKeepPassingMaterialSeparateFromFailingModelWithoutDuplicatingGlobalIdentity()
        {
            var path = MeshFile(true); Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)), Bundle("B", 200, Array.Empty<string>(), Asset(path)) });
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => false))
            {
                Finish(session); var result = session.Resources.Single();
                Assert.That(result.Checks.Select(check => check.Kind), Is.EquivalentTo(new[] { AssetCheckKind.Model, AssetCheckKind.Material }));
                Assert.That(result.Checks.Single(check => check.Kind == AssetCheckKind.Model).Issues.Any(issue => issue.RuleId == "model.mesh-submeshes"), Is.True);
                Assert.That(result.Checks.Single(check => check.Kind == AssetCheckKind.Material).Issues, Is.Empty);
                Assert.That(result.Checks.All(check => check.Complete), Is.True); Assert.That(result.Asset.Owners.Count, Is.EqualTo(2));
                Assert.That(result.Issues.Count, Is.EqualTo(result.Checks.Sum(check => check.Issues.Count)));
            }
        }

        [Test] public void CancellingBetweenApplicableModulesKeepsLaterCategoryIncomplete()
        {
            var path = MeshFile(true); Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) });
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => false))
            {
                for (var i = 0; i < 10000 && !session.Resources[0].Checks.Any(check => check.Complete); i++) session.Step();
                var checks = session.Resources[0].Checks; Assert.That(session.ClassificationReady, Is.True);
                Assert.That(checks.Single(check => check.Kind == AssetCheckKind.Model).Complete, Is.True);
                Assert.That(checks.Single(check => check.Kind == AssetCheckKind.Material).Complete, Is.False);
                session.Cancel(); Assert.That(session.Resources[0].Checked, Is.False); Assert.That(session.Resources[0].ContentChecked, Is.False);
            }
        }

        [UnityTest] public IEnumerator SharedTableHasInlineEvidenceAndRelatedColumnWithoutAnyWriteActions()
        {
            var path = MeshFile(true); var before = File.ReadAllBytes(path);
            Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) }); Open(); Finish(_panel.Session); _panel.ShowResources(true); _panel.SelectResourceType(AssetCheckKind.Model);
            yield return null; yield return null;
            var results = _panel.Q<AssetCheckResultsView>("resource-results"); var table = results.Q<MultiColumnListView>();
            Assert.That(results.Model.Files.Single().VisibleIssues.Any(issue => issue.RuleId == "model.mesh-submeshes"), Is.True);
            Assert.That(table.columns["actual"].title, Is.EqualTo("当前值")); Assert.That(table.columns["expected"].title, Is.EqualTo("允许／要求"));
            Assert.That(table.columns.Any(column => column.name == "related" && column.title == "所属 AB"), Is.True);
            Assert.That(table.columns.Any(column => column.name == "actions"), Is.False);
            Assert.That(results.Query<Toggle>().ToList(), Is.Empty);
            Assert.That(results.Query<Button>().ToList().Any(button => button.name == "fix" || button.name == "ignore"), Is.False);
            Assert.That(table.worldBound.height, Is.GreaterThan(80));
            Assert.That(_panel.Q<Button>("bundle-tab-Model").worldBound.xMax, Is.LessThan(_host.rootVisualElement.worldBound.xMax));
            _panel.SelectResourceType(AssetCheckKind.Material); Assert.That(results.Model.Files.Single().AllIssues, Is.Empty);
            Assert.That(results.Model.Files.Single().Data.Status, Is.EqualTo("通过"));
            _panel.InspectResource(_panel.Session.Resources.Single());
            Assert.That(_panel.Q<ScrollView>("analysis-inspector").Query<Label>().ToList().Any(label => label.text.StartsWith("资源整体（所有检查分类）：违规")), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before)); Assert.That(_workspace.GetReport(AssetCheckKind.Model), Is.Null);
        }

        [Test] public void CancelledResultsCannotBecomePassingWhenSwitchingCategories()
        {
            var path = MeshFile(true); Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) }); Open();
            for (var i = 0; i < 10000 && !_panel.Session.Resources[0].Checks.Any(check => check.Complete); i++) _panel.Session.Step();
            _panel.Session.Cancel(); _panel.ShowResources(true); _panel.SelectResourceType(AssetCheckKind.Material);
            var results = _panel.Q<AssetCheckResultsView>("resource-results");
            Assert.That(results.Model.Files.Single().Data.Status, Is.EqualTo("未完成"));
            Assert.That(results.Model.Files.Single().Data.Complete, Is.False);
            _panel.SelectResourceType(AssetCheckKind.Model);
            Assert.That(results.Model.Files.Single().Data.Status, Does.Contain("违规").And.Contain("未完成"));
            Assert.That(_panel.Session.Resources.Single().Checked, Is.False);
        }

        [Test] public void AttentionChoiceIncludesProblemCandidatesOnceAndExcludesPassingCategories()
        {
            var path = MeshFile(true); Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) });
            Open(); Finish(_panel.Session); _panel.ShowResources(true); _panel.SelectResourceType(AssetCheckKind.Model);
            var filter = _panel.Q<PopupField<string>>("analysis-filter");
            Assert.That(filter.choices, Does.Contain("需要关注")); filter.value = "需要关注";
            var results = _panel.Q<AssetCheckResultsView>("resource-results");
            Assert.AreEqual(1, results.Model.Files.Count); Assert.IsTrue(results.Model.Files.Single().HasProblem);
            _panel.SelectResourceType(AssetCheckKind.Material); Assert.IsEmpty(results.Model.Files);
            Assert.AreEqual("需要关注", filter.value);
        }

        [Test] public void MissingAndUnsupportedResourcesRemainVisibleThroughUnclassifiedTab()
        {
            var text = _root + "/unknown.bytes"; File.WriteAllText(text, "read-only"); AssetDatabase.ImportAsset(text, ImportAssetOptions.ForceSynchronousImport);
            var missing = new BundleReportAssetData { AssetPath = _root + "/gone.asset", AssetGUID = new string('f', 32) };
            Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(text), missing) }); Open(); Finish(_panel.Session); _panel.ShowResources(true); _panel.SelectResourceType(null);
            var results = _panel.Q<AssetCheckResultsView>("resource-results"); Assert.That(results.Model.Files.Count, Is.EqualTo(2));
            Assert.That(results.Model.Files.All(file => file.AllIssues.Length == 0 && file.Data.Unverified), Is.True);
            Assert.That(results.Model.Files.All(file => file.Data.Status == "未能检查"), Is.True);
            results.Model.ExpandAll(true); Assert.That(results.Model.Rows.Count(row => row.IsFile), Is.EqualTo(2));
            Assert.That(_panel.Q<Button>("bundle-tab-Project").enabledSelf, Is.False);
        }

        [UnityTest] public IEnumerator DeletedGuidCannotPreviewOrLocateReplacementAtTheSamePathAndSnapshotStaysClassified()
        {
            var path = MeshFile(false); Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) }); Open(); Finish(_panel.Session);
            var session = _panel.Session; var guid = AssetDatabase.AssetPathToGUID(path); AssetDatabase.DeleteAsset(path);
            var replacement = _root + "/replacement.asset";
            AssetDatabase.CreateAsset(new Material(Shader.Find("Sprites/Default")), replacement);
            Assert.That(AssetDatabase.MoveAsset(replacement, path), Is.Empty);
            Assert.That(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets), Is.Not.EqualTo(guid));
            _panel.ShowResources(true); _panel.SelectResourceType(AssetCheckKind.Model);
            yield return null; yield return null;
            var row = _panel.Q<AssetCheckResultsView>("resource-results").Model.Files.Single();
            Assert.That(row.Data.ResolvePath(), Is.Empty); Assert.That(row.Data.Kind, Is.EqualTo(AssetCheckKind.Model));
            Assert.That(_panel.Session, Is.SameAs(session)); Assert.That(row.AllIssues.Any(issue => issue.RuleId == "model.mesh-submeshes"), Is.True);
            Assert.That(_panel.Q<AssetCheckResultsView>("resource-results").Query<AssetCheckThumbnail>().ToList().All(preview => preview.image == null), Is.True);
        }
    }
}
