using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    // 此规则无需注册，位于模块之外的独立程序集。
    internal sealed class DiscoveredHierarchyRule : IAssetCheckRule<PrefabSceneCheckContext>
    {
        private int _visits;
        public void Check(PrefabSceneCheckContext context)
        {
            if (context.GameObject.name == "__ClaimsFixWithoutCapability")
                context.Report(context.GameObject, "repair", "无修复能力的扩展", "错误", "只读", canFix: true);
            if (context.GameObject.name == "__ExtensionFailure")
                throw new InvalidOperationException("extension-check-failure");
            if (context.GameObject.name == "__ExtensionCounter")
                context.Report(context.GameObject, "visits", "实例计数", (++_visits).ToString(), "1", "m_Layer");
        }
    }

    internal sealed class ReadOnlyModuleRepairRule : IAssetCheckFix<PrefabSceneCheckContext>
    {
        public void Check(PrefabSceneCheckContext context)
        {
            if (context.GameObject.name == "__ClaimsFixInReadOnlyModule")
                context.Report(context.GameObject, "repair", "只读模块扩展", "错误", "人工处理", canFix: true);
        }
        public void Fix(PrefabSceneCheckContext context, AssetCheckIssue issue) =>
            throw new InvalidOperationException("A read-only module must never invoke repair.");
    }

    internal sealed class DiscoveredTextureRule : IAssetCheckFix<TextureCheckContext>
    {
        public void Check(TextureCheckContext context)
        {
            if (!context.IsAtlas && Path.GetFileName(context.AssetPath) == "extension.png" && context.Importer.mipmapEnabled)
                context.Report(context.Importer, "mipmaps", "测试扩展修复", "开启", "关闭", canFix: true);
        }
        public void Fix(TextureCheckContext context, AssetCheckIssue issue) => context.Importer.mipmapEnabled = false;
    }

    public sealed class RuleDiscoveryTests
    {
        private string _folder;
        private string _config;

        [SetUp] public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _folder = "Assets/__DiscoveredRules_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder));
            _config = Path.GetFullPath("Temp/DiscoveredRules_" + id);
            Directory.CreateDirectory(_config);
        }

        [TearDown] public void Cleanup()
        {
            StageUtility.GoToMainStage();
            AssetDatabase.DeleteAsset(_folder);
            Directory.Delete(_config, true);
        }

        private string Prefab(string name)
        {
            var root = new GameObject("Sample");
            new GameObject(name).transform.SetParent(root.transform, false);
            var path = _folder + "/Sample.prefab";
            try { PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { Object.DestroyImmediate(root); }
            return path;
        }

        private AssetCheckWorkspace Workspace()
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings => settings.scope = _folder);
            return workspace;
        }

        private static void Scan(AssetCheckWorkspace workspace, AssetCheckKind kind)
        {
            workspace.StartScan(kind);
            while (workspace.Running) workspace.Step();
            Assert.That(workspace.Failure, Is.Null);
        }

        [Test] public void InternalExternalRuleIsDiscoveredOnceAndReportsExactObjectWithoutSaving()
        {
            var path = Prefab("__ExtensionCounter");
            var before = File.ReadAllBytes(path);
            var issue = new PrefabSceneCheckModule().Check(path, new PlatformStandards()).Single();
            Assert.That(issue.RuleId, Does.EndWith("DiscoveredHierarchyRule.visits"));
            Assert.That(issue.Actual, Is.EqualTo("1"));
            Assert.That(issue.CanFix, Is.False);
            Assert.That(issue.PropertyPath, Is.EqualTo("m_Layer"));
            Assert.That(issue.ObjectLocation, Is.Not.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            AssetCheckLocator.Locate(issue, true);
            Assert.That(Selection.activeGameObject.name, Is.EqualTo("__ExtensionCounter"));
            Assert.That(PrefabStageUtility.GetCurrentPrefabStage().assetPath, Is.EqualTo(path));
        }

        [Test] public void EachWorkspaceOwnsFreshModuleAndRuleInstances()
        {
            Prefab("__ExtensionCounter");
            using (var first = Workspace())
            using (var second = Workspace())
            {
                Scan(first, AssetCheckKind.PrefabScene);
                Scan(second, AssetCheckKind.PrefabScene);
                Assert.That(first.GetReport(AssetCheckKind.PrefabScene).Issues.Single().Actual, Is.EqualTo("1"));
                Assert.That(second.GetReport(AssetCheckKind.PrefabScene).Issues.Single().Actual, Is.EqualTo("1"));
            }
        }

        [Test] public void DiscoveredRepairUsesItsProducerThroughWorkspaceRecheckAndImport()
        {
            var path = _folder + "/extension.png";
            var texture = new Texture2D(4, 4);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.Texture);
                var issue = workspace.GetReport(AssetCheckKind.Texture).Issues.Single(i => i.RuleId.EndsWith("DiscoveredTextureRule.mipmaps"));
                workspace.StartFix(AssetCheckKind.Texture, new[] { issue });
                while (workspace.Running) workspace.Step();
                Assert.That(workspace.Failure, Is.Null);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
                Assert.That(workspace.LastFix.Rechecked, Is.True);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues.Any(i => i.RuleId == issue.RuleId), Is.False);
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).mipmapEnabled, Is.False);
            }
        }

        [Test] public void RuleFailurePropagatesAndTemporaryPrefabIsReleased()
        {
            var path = Prefab("__ExtensionFailure");
            var before = File.ReadAllBytes(path);
            var scenes = SceneManager.sceneCount;
            var exception = Assert.Throws<InvalidOperationException>(() => new PrefabSceneCheckModule().Check(path, new PlatformStandards()));
            Assert.That(exception.Message, Is.EqualTo("extension-check-failure"));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [TestCase("__ClaimsFixWithoutCapability", "必须由实现修复接口的规则报告")]
        [TestCase("__ClaimsFixInReadOnlyModule", "不支持自动修改")]
        public void InvalidRepairDeclarationsFailAtReportingWithoutChangingPrefab(string name, string message)
        {
            var path = Prefab(name);
            var before = File.ReadAllBytes(path);
            var scenes = SceneManager.sceneCount;
            var exception = Assert.Throws<InvalidOperationException>(() => new PrefabSceneCheckModule().Check(path, new PlatformStandards()));
            Assert.That(exception.Message, Does.Contain(message));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test] public void ReportContextIncludesDiscoveredRuleAssemblyAndBuildFingerprint()
        {
            Prefab("Ordinary");
            string context;
            using (var workspace = Workspace())
            {
                Scan(workspace, AssetCheckKind.PrefabScene);
                context = workspace.GetReport(AssetCheckKind.PrefabScene).Context;
            }
            Assert.That(context, Does.Contain(typeof(DiscoveredHierarchyRule).FullName));
            Assert.That(context, Does.Contain(typeof(DiscoveredHierarchyRule).Module.ModuleVersionId.ToString()));
        }

        private sealed class InvalidContext : AssetCheckContext
        {
            internal InvalidContext() : base("Assets/invalid", AssetCheckKind.Model, new List<AssetCheckIssue>()) { }
        }

        private sealed class NeedsArgumentsRule : IAssetCheckRule<InvalidContext>
        {
            internal NeedsArgumentsRule(int value) { }
            public void Check(InvalidContext context) { }
        }

        [Test] public void ConcreteRuleWithoutParameterlessConstructorFailsExplicitly()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => AssetCheckRules<InvalidContext>.Create());
            Assert.That(exception.Message, Does.Contain(typeof(NeedsArgumentsRule).FullName));
        }
    }
}
