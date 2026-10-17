using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    internal sealed class TextureBatchImportProbe : AssetPostprocessor
    {
        internal static string Root;
        internal static readonly List<string[]> Batches = new List<string[]>();

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            if (Root == null) return;
            var affected = imported.Where(path => path.StartsWith(Root + "/", StringComparison.Ordinal)).ToArray();
            if (affected.Length > 0) Batches.Add(affected);
        }
    }

    internal sealed class TextureBatchFailureRule : IAssetCheckFix<TextureCheckContext>
    {
        internal static string FailurePath;
        public void Check(TextureCheckContext context)
        {
            if (context.AssetPath == FailurePath)
                context.Report(context.Importer, "failure", "测试批量修改失败", "测试", "测试", canFix: true);
        }
        public void Fix(TextureCheckContext context, AssetCheckIssue issue) =>
            throw new InvalidOperationException("texture-batch-test-failure");
    }

    public sealed class TextureBatchImportTests
    {
        private string _root, _config;

        [SetUp]
        public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__TextureBatch_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.GetFullPath("Temp/TextureBatch_" + id);
            Directory.CreateDirectory(_config);
            TextureBatchImportProbe.Root = _root;
            TextureBatchImportProbe.Batches.Clear();
        }

        [TearDown]
        public void Cleanup()
        {
            TextureBatchImportProbe.Root = null;
            TextureBatchImportProbe.Batches.Clear();
            TextureBatchFailureRule.FailurePath = null;
            AssetDatabase.DeleteAsset(_root);
            Directory.Delete(_config, true);
        }

        [Test]
        public void BulkFixImportsTwentyFilesBeforeYieldingAndRechecksTheRemainingFiles()
        {
            var paths = Textures(25);
            using (var workspace = Workspace())
            {
                StartReadWriteFix(workspace);
                var watch = Stopwatch.StartNew();
                workspace.Step();
                Assert.That(workspace.Failure, Is.Null);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(20));
                Assert.That(TextureBatchImportProbe.Batches.Count, Is.EqualTo(1));
                Assert.That(TextureBatchImportProbe.Batches[0], Is.EquivalentTo(paths.Take(20)));
                Assert.That(Readable(paths), Is.EqualTo(5));
                Assert.That(paths.Take(20).All(path => !AssetDatabase.LoadAssetAtPath<Texture2D>(path).isReadable), Is.True);
                Drain(workspace);
                TestContext.Progress.WriteLine("TextureBatch 25 files: " + watch.ElapsedMilliseconds + " ms");
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(25));
                Assert.That(workspace.LastFix.Rechecked, Is.True);
                Assert.That(TextureBatchImportProbe.Batches.Select(batch => batch.Length), Is.EqualTo(new[] { 20, 5 }));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues, Is.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancelOrDisposeBetweenBatchesKeepsCompletedFilesAndResumesImports(bool dispose)
        {
            var paths = Textures(25);
            using (var workspace = Workspace())
            {
                StartReadWriteFix(workspace);
                workspace.Step();
                if (dispose) workspace.Dispose(); else workspace.Cancel();
                Assert.That(workspace.Running, Is.False);
                Assert.That(workspace.LastFix.Cancelled, Is.True);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(20));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                Assert.That(Readable(paths), Is.EqualTo(5));
                AssertImportsAvailable();
            }
        }

        [Test]
        public void RuleFailureFlushesCompletedWritesAndResumesImports()
        {
            var paths = Textures(3);
            TextureBatchFailureRule.FailurePath = paths[1];
            using (var workspace = Workspace())
            {
                var issues = workspace.GetReport(AssetCheckKind.Texture).Issues;
                var selected = new[]
                {
                    issues.Single(issue => issue.AssetPath == paths[0] && issue.RuleId == "texture.read-write"),
                    issues.Single(issue => issue.AssetPath == paths[1] && issue.RuleId.EndsWith(".failure", StringComparison.Ordinal)),
                    issues.Single(issue => issue.AssetPath == paths[2] && issue.RuleId == "texture.read-write")
                };
                workspace.StartFix(AssetCheckKind.Texture, selected);
                TextureBatchImportProbe.Batches.Clear();
                LogAssert.Expect(LogType.Exception, new Regex("texture-batch-test-failure"));
                workspace.Step();
                Assert.That(workspace.Running, Is.False);
                Assert.That(workspace.Failure, Does.Contain("texture-batch-test-failure"));
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
                Assert.That(workspace.LastFix.Rechecked, Is.False);
                Assert.That(Readable(paths), Is.EqualTo(2));
                Assert.That(TextureBatchImportProbe.Batches.Single(), Is.EqualTo(new[] { paths[0] }));
                AssertImportsAvailable();
            }
        }

        [Test]
        public void ConfigurationChangeBetweenBatchesStopsFurtherWrites()
        {
            var paths = Textures(25);
            using (var workspace = Workspace())
            {
                StartReadWriteFix(workspace);
                workspace.Step();
                File.AppendAllText(Path.Combine(_config, AssetCheckSettingsFile.FileName), "\n");
                LogAssert.Expect(LogType.Exception, new Regex("IOException"));
                workspace.Step();
                Assert.That(workspace.Running, Is.False);
                Assert.That(workspace.Failure, Is.Not.Null);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(20));
                Assert.That(Readable(paths), Is.EqualTo(5));
                AssertImportsAvailable();
            }
        }

        [Test]
        public void AtlasReadWriteFixesShareOneImportBatchAndAreRechecked()
        {
            for (var index = 0; index < 2; index++)
            {
                var atlas = new SpriteAtlas();
                var settings = atlas.GetTextureSettings();
                settings.readable = true;
                atlas.SetTextureSettings(settings);
                AssetDatabase.CreateAsset(atlas, _root + "/atlas" + index + ".spriteatlas");
            }
            using (var workspace = Workspace())
            {
                var selected = workspace.GetReport(AssetCheckKind.Texture).Issues
                    .Where(issue => issue.RuleId == "atlas.read-write").ToArray();
                Assert.That(selected.Length, Is.EqualTo(2));
                workspace.StartFix(AssetCheckKind.Texture, selected);
                TextureBatchImportProbe.Batches.Clear();
                workspace.Step();
                Assert.That(workspace.Failure, Is.Null);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(2));
                Assert.That(TextureBatchImportProbe.Batches.Single().Length, Is.EqualTo(2));
                Drain(workspace);
                Assert.That(workspace.LastFix.Rechecked, Is.True);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues.Any(issue => issue.RuleId == "atlas.read-write"), Is.False);
            }
        }

        private string[] Textures(int count)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            byte[] bytes;
            try { bytes = texture.EncodeToPNG(); }
            finally { Object.DestroyImmediate(texture); }
            var paths = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var path = _root + "/texture" + index.ToString("D2") + ".png";
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.isReadable = true;
                var web = importer.GetPlatformTextureSettings("WebGL");
                web.overridden = true;
                web.format = TextureImporterFormat.ASTC_8x8;
                importer.SetPlatformTextureSettings(web);
                TuanjieTextureSettings.WriteFormat(importer, "MiniGame", TextureImporterFormat.ASTC_6x6);
                TuanjieTextureSettings.WriteFormat(importer, "WeixinMiniGame", TextureImporterFormat.ASTC_8x8);
                importer.SaveAndReimport();
                paths.Add(path);
            }
            return paths.ToArray();
        }

        private AssetCheckWorkspace Workspace()
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(settings => settings.scope = _root);
            workspace.StartScan(AssetCheckKind.Texture);
            Drain(workspace);
            return workspace;
        }

        private static void StartReadWriteFix(AssetCheckWorkspace workspace)
        {
            workspace.StartFix(AssetCheckKind.Texture, workspace.GetReport(AssetCheckKind.Texture).Issues
                .Where(issue => issue.RuleId == "texture.read-write").ToArray());
            TextureBatchImportProbe.Batches.Clear();
        }

        private static int Readable(IEnumerable<string> paths) =>
            paths.Count(path => ((TextureImporter)AssetImporter.GetAtPath(path)).isReadable);

        private void AssertImportsAvailable()
        {
            TextureBatchImportProbe.Batches.Clear();
            var path = _root + "/resume.png";
            File.Copy(_root + "/texture00.png", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(path), Is.Not.Null);
            Assert.That(TextureBatchImportProbe.Batches.SelectMany(batch => batch), Does.Contain(path));
        }

        private static void Drain(AssetCheckWorkspace workspace)
        {
            for (var index = 0; workspace.Running && index < 2000; index++) workspace.Step();
            Assert.That(workspace.Failure, Is.Null);
            Assert.That(workspace.Running, Is.False);
        }
    }
}
