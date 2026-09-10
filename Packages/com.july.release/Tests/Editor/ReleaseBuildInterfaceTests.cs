using System;
using System.IO;
using System.Linq;
using July.Build;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using BuildContext = July.Release.Editor.BuildContext;
using BuildStep = July.Release.Editor.BuildStep;

namespace July.Release.Tests
{
    public sealed class ReleaseBuildInterfaceTests
    {
        string root;
        [SetUp] public void SetUp() => root = Path.Combine(Path.GetTempPath(), "July interface " + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        [Test] public void CleanupOnlyRemovesRequestedPlatformVersion()
        {
            var selected = Path.Combine(root, "WeChat", "1.6.0");
            var other = Path.Combine(root, "TikTok", "1.6.0");
            Directory.CreateDirectory(selected); Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(selected, "stale.js"), "old");
            File.WriteAllText(Path.Combine(other, "keep.js"), "keep");
            PlatformBuildPaths.CleanExportDirectory(root, "WeChat", "1.6.0");
            Assert.IsFalse(Directory.Exists(selected));
            Assert.IsTrue(File.Exists(Path.Combine(other, "keep.js")));
            Assert.Throws<ArgumentException>(() => PlatformBuildPaths.CleanExportDirectory(root, "../escape", "1.6.0"));
            Assert.Throws<ArgumentException>(() => PlatformBuildPaths.CleanExportDirectory(root, "WeChat", "../../escape"));
        }

        [Test] public void ReportUsesActualPackageAndAotPathsAndRecordsFailure()
        {
            Directory.CreateDirectory(root);
            var game = Path.Combine(root, "game.js"); File.WriteAllText(game, "fixture");
            var ctx = new BuildContext { Platform="WeChat", Env="Dev", Target=BuildTarget.WebGL,
                CoreVersion="1.6.0", PlanVersion="1.6.1", SavedAotBackupPath=Path.Combine(root,"aot"),
                Artifacts=new PlatformBuildArtifacts(root,game) };
            var success = new BuildRunner(new TestHost()).Run(ctx, new IBuildStep[] { new FixtureStep(true) });
            var report = ReleaseBuildReport.Create(ctx,success,"FullBuild",true);
            Assert.IsTrue(report.succeeded); Assert.IsTrue(report.cdnUploaded);
            Assert.AreEqual(root,report.packageDirectory); Assert.AreEqual(ctx.SavedAotBackupPath,report.aotBackupPath);
            var failure = new BuildRunner(new TestHost()).Run(ctx,new IBuildStep[] { new FixtureStep(false) });
            report=ReleaseBuildReport.Create(ctx,failure,"FullBuild",true);
            Assert.IsFalse(report.succeeded); Assert.IsFalse(report.cdnUploaded);
            Assert.AreEqual("Fixture",report.failedStep);
            File.Delete(game);
            Assert.Throws<InvalidDataException>(() => ReleaseBuildReport.Create(ctx,success,"FullBuild",true));
        }

        [Test] public void PlayerGenerationPrecedesPublishingAndNoGitRunsInUnity()
        {
            var full=PipelinePresets.FullBuild(upload:true,miniGame:true).Select(s=>s.GetType().Name).ToList();
            Assert.Less(full.IndexOf("MiniGameBuildStep"),full.IndexOf("CloudUploadStep"));
            Assert.IsFalse(full.Contains("GitTagStep"));
            Assert.IsFalse(PipelinePresets.HotUpdate(upload:true).Any(s=>s.GetType().Name=="GitTagStep"));
            Assert.AreEqual("../Build",ReleaseConventions.ExportRoot);
            Assert.AreEqual("Tools/coscli/.cos.yaml",ReleaseConventions.CoscliConfig);
        }

        sealed class TestHost : IBuildHost
        {
            public bool Confirm(July.Build.BuildContext context, int count) => true;
            public void SaveAssets() { }
            public void RefreshAssets() { }
            public void ShowProgress(string name, int index, int count) { }
            public void ClearProgress() { }
            public void Log(string message) { }
            public void LogError(string message) { }
        }

        sealed class FixtureStep : BuildStep
        {
            readonly bool success;
            public FixtureStep(bool success) => this.success=success;
            public override string Name=>"Fixture";
            public override string Validate(BuildContext c)=>null;
            public override bool Execute(BuildContext c)=>success;
        }
    }
}
