using System;
using System.IO;
using System.Linq;
using July.Release;
using July.Release.Editor;
using LitJson;
using NUnit.Framework;

namespace July.Release.Tests
{
    public sealed class ReleaseContractTests
    {
        [Test]
        public void BuildPreviewResolvesFullHotUpdateAndQaVersions()
        {
            var selection = new BuildToolSelection();
            var full = Context();
            selection.ApplyTo(full);
            Assert.AreEqual("1.6.1", full.CoreVersion);
            Assert.IsNull(full.AOTBackupVersion);
            selection.Mode = ReleaseBuildMode.HotUpdate;
            selection.AotBaseline = "1.5.0";
            var hot = Context();
            selection.ApplyTo(hot);
            Assert.AreEqual("1.5.0", hot.CoreVersion);
            Assert.AreEqual("1.5.0", hot.AOTBackupVersion);
            Assert.AreEqual("1.6.1", hot.PlanVersion);
            selection.QA = true;
            selection.ApplyTo(hot);
            Assert.AreEqual(BuildUtils.QAPlanVersion, hot.CoreVersion);
            Assert.AreEqual(BuildUtils.QAPlanVersion, hot.PlanVersion);
            Assert.AreEqual("1.5.0", hot.AOTBackupVersion);
            Assert.IsTrue(hot.IsQABuild);
        }

        [Test]
        public void MissingHotUpdateBaselineDoesNotFallBackToCurrentPlayerVersion()
        {
            var selection = new BuildToolSelection { Mode = ReleaseBuildMode.HotUpdate };
            var context = Context();
            selection.ApplyTo(context);
            Assert.IsNull(context.CoreVersion);
            Assert.IsNotNull(context.Validate());
        }

        [Test]
        public void LocalPlayerExportNeverUploadsDataOrPreload()
        {
            var steps = PipelinePresets.FullBuild(upload: false, miniGame: true);
            Assert.IsTrue(steps.Any(step => step is MiniGameBuildStep));
            Assert.IsFalse(steps.Any(step => step is CloudUploadStep || step is DataFileUploadStep));
            Assert.IsFalse(steps.OfType<PreloadInjectionStep>().Single().UploadEnabled);
            var publishing = PipelinePresets.FullBuild(upload: true, miniGame: true);
            Assert.IsTrue(publishing.Any(step => step is DataFileUploadStep));
            Assert.IsTrue(publishing.OfType<PreloadInjectionStep>().Single().UploadEnabled);
        }

        [Test]
        public void StandardBuildIgnoresStaleCustomStepPreferences()
        {
            var selection = new BuildToolSelection { HybridCLR = false, AssetBundles = false, MiniGame = false };
            var standard = selection.CreateSteps();
            Assert.IsTrue(standard.Any(step => step is HybridCLRGenerateAllStep));
            Assert.IsTrue(standard.Any(step => step is AssetBundleBuildStep));
            Assert.IsTrue(standard.Any(step => step is MiniGameBuildStep));
            selection.CustomSteps = true;
            var custom = selection.CreateSteps();
            Assert.IsFalse(custom.Any(step => step is HybridCLRGenerateAllStep || step is AssetBundleBuildStep || step is MiniGameBuildStep));
        }

        [Test]
        public void PreloadSelectionTracksSharedResourceTags()
        {
            var resources = new ReleaseResourceSettings();
            resources.aotMetaTag = "Metadata";
            resources.hotUpdateTag = "Code";
            resources.lobbyTag = "Home";
            CollectionAssert.AreEqual(new[] { "Metadata", "Code", "Home" }, resources.RequiredPreloadTags);
            resources.lobbyTag = "Start";
            CollectionAssert.AreEqual(new[] { "Metadata", "Code", "Start" }, resources.RequiredPreloadTags);
        }

        static BuildContext Context(string prefix = "SampleProject") => new BuildContext
        {
            CdnUrl = "https://cdn.example.com/" + prefix,
            CloudUrl = "https://bucket.cos.ap-chengdu.myqcloud.com/" + prefix,
            Env = "Dev", Platform = "WeChat", CoreVersion = "1.6.0", PlanVersion = "1.6.1"
        };

        [Test]
        public void ProjectRootsPreservePrefixAndVersionLevels()
        {
            foreach (var prefix in new[] { "", "SampleProject", "games/SampleProject" })
            foreach (var cloudSlash in new[] { "", "/" })
            foreach (var cdnSlash in new[] { "", "/" })
            {
                var ctx = Context(prefix);
                ctx.CloudUrl += cloudSlash;
                ctx.CdnUrl += cdnSlash;
                var info = BuildUtils.ParseCloudInfo(ctx.CloudUrl);
                Assert.AreEqual("bucket", info.bucket);
                Assert.AreEqual("ap-chengdu", info.region);
                Assert.AreEqual(prefix, info.pathPrefix);
                Assert.IsNull(BuildUtils.ValidateResourceUrls(ctx, out _, out _, out _));
                var core = "cos://bucket/" + (prefix.Length == 0 ? "" : prefix + "/") + "Dev/WeChat/1.6.0";
                Assert.AreEqual(core, BuildUtils.BuildCosObjectUrl(info.bucket, info.pathPrefix, "/Dev/", "WeChat/", "1.6.0"));
                foreach (var file in new[] { "preload.json", "data.bin.br" })
                    Assert.AreEqual(core + "/" + file, BuildUtils.BuildCosObjectUrl(info.bucket, info.pathPrefix, "Dev", "WeChat", "1.6.0", file));
                var cdnCore = ctx.CdnUrl.TrimEnd('/') + "/Dev/WeChat/1.6.0";
                Assert.AreEqual(cdnCore + "/preload.json", PreloadHelper.BuildPreloadJsonUrl(ctx));
                Assert.AreEqual(cdnCore + "/1.6.1", ResourceUrls.PlanRoot(ctx.CdnUrl, ctx.Env, ctx.Platform, ctx.CoreVersion, ctx.PlanVersion));
            }
        }

        [Test]
        public void MismatchedPrefixesStopEveryUploadRoute()
        {
            foreach (var prefix in new[] { "OtherProject", "sampleproject", "" })
            {
                var ctx = Context();
                ctx.CdnUrl = "https://cdn.example.com/" + prefix;
                var error = BuildUtils.ValidateResourceUrls(ctx, out _, out _, out _);
                StringAssert.Contains("BootConfig.cdnUrl", error);
                StringAssert.Contains("BuildConfig.cloudUrl", error);
                foreach (var step in new BuildStep[] { new CloudUploadStep(), new DataFileUploadStep(), new MiniGameBuildStep(), new PreloadInjectionStep(), new PreloadJsonUpdateStep() })
                    Assert.AreEqual(error, step.Validate(ctx), step.GetType().Name);
            }
        }

        [Test]
        public void InvalidCloudRootsAreRejectedAtTheBoundary()
        {
            foreach (var url in new[] { null, "not a url", "ftp://bucket.cos.ap-chengdu.myqcloud.com/Project",
                "https://bucket.cos.ap-chengdu.myqcloud.com.evil.test/Project",
                "https://bucket.cos.ap-chengdu.myqcloud.com/Project?x=1", "https://bucket.cos.ap-chengdu.myqcloud.com/Project#x" })
                Assert.IsNull(BuildUtils.ParseCloudInfo(url).bucket);
        }

        [Test]
        public void RuntimeAndEditorShareTheBackendVersionContract()
        {
            var body = JsonMapper.ToObject(ClientVersionProtocol.RequestJson("1.6.0"));
            Assert.AreEqual(1, body.Count);
            Assert.AreEqual("1.6.0", (string)body["CoreVersion"]);
            var root = JsonMapper.ToObject("{\"serverUrl\":\"https://api.example.com\",\"platforms\":{\"WeChat\":{\"PlanVersion\":\"1.6.1\",\"isAudit\":true}}}");
            Assert.AreEqual("1.6.1", ClientVersionProtocol.ReadPlanVersion(root, "WeChat"));
            Assert.IsNull(ClientVersionProtocol.ReadPlanVersion(root, "TikTok"));
            Assert.IsNull(ClientVersionProtocol.ReadPlanVersion(JsonMapper.ToObject("{\"platforms\":{\"WeChat\":{\"resVersion\":\"1.6.1\"}}}"), "WeChat"));
            var quotedVersion = "1.6.0\"\\test";
            Assert.AreEqual(quotedVersion, (string)JsonMapper.ToObject(ClientVersionProtocol.RequestJson(quotedVersion))["CoreVersion"]);
        }

        [Test]
        public void JavascriptPrefetchUsesTheSameBodyOnBothPlatforms()
        {
            foreach (var platform in new[] { "WeChat", "TikTok" })
            {
                var snippet = PreloadInjectionStep.BuildConfigPrefetchSnippet(platform, "https://config.example.com/", "1.6.0");
                StringAssert.Contains("https://config.example.com/client_version", snippet);
                StringAssert.Contains("JSON.stringify(" + ClientVersionProtocol.RequestJson("1.6.0") + ")", snippet);
                StringAssert.Contains(platform == "WeChat" ? "wx.request" : "tt.request", snippet);
                StringAssert.Contains(platform == "WeChat" ? "window.__JULY_CONFIG_CACHE" : "GameGlobal.__JULY_CONFIG_CACHE", snippet);
                StringAssert.DoesNotContain("appVersion", snippet);
            }
        }

        [Test]
        public void MissingCoreVersionFailsBeforeStepsRun()
        {
            var ctx = Context();
            foreach (var invalid in new[] { null, "", "  " })
            {
                ctx.CoreVersion = invalid;
                Assert.AreEqual("Core version is required.", ctx.Validate());
            }
        }

        [Test]
        public void SingleStepKeepsCurrentCoreInsteadOfUsingPlanVersion()
        {
            var ctx = Context();
            ctx.CoreVersion = null;
            ctx.PlanVersion = "1.6.2";
            ctx.UseExistingCoreVersion("1.6.0");
            Assert.IsNull(ctx.Validate());
            Assert.AreEqual("cos://bucket/SampleProject/Dev/WeChat/1.6.0/1.6.2",
                BuildUtils.BuildCosObjectUrl("bucket", "SampleProject", ctx.Env, ctx.Platform, ctx.CoreVersion, ctx.PlanVersion));
            Assert.AreEqual("https://cdn.example.com/SampleProject/Dev/WeChat/1.6.0/preload.json",
                PreloadHelper.BuildPreloadJsonUrl(ctx));
        }

        [Test]
        public void SelectedAotBaselineOverridesCurrentCoreForPathsAndRequests()
        {
            var ctx = Context();
            ctx.CoreVersion = "1.7.0";
            ctx.AOTBackupVersion = "1.6.0";
            ctx.UseExistingCoreVersion("1.7.0");
            Assert.IsNull(ctx.Validate());
            Assert.AreEqual("1.6.1", ctx.PlanVersion);
            Assert.AreEqual("https://cdn.example.com/SampleProject/Dev/WeChat/1.6.0/1.6.1",
                ResourceUrls.PlanRoot(ctx.CdnUrl, ctx.Env, ctx.Platform, ctx.CoreVersion, ctx.PlanVersion));
            Assert.AreEqual("1.6.0", (string)JsonMapper.ToObject(ClientVersionProtocol.RequestJson(ctx.CoreVersion))["CoreVersion"]);
        }

        [Test]
        public void PresetsRetainFullBuildAndHotUpdateOrdering()
        {
            CollectionAssert.AreEqual(new[] { "ReleaseConfigurationStep", "PlatformDefinesValidationStep", "HybridCLRInstallStep", "HybridCLRGenerateAllStep",
                "AssetBundleBuildStep", "AOTBackupStep", "AOTBackupArchiveStep", "WebGLDebugSymbolStep",
                "MiniGameBuildStep", "CloudUploadStep", "DataFileUploadStep", "PreloadInjectionStep" },
                PipelinePresets.FullBuild(upload: true, miniGame: true).Select(s => s.GetType().Name));
            CollectionAssert.AreEqual(new[] { "ReleaseConfigurationStep", "PlatformDefinesValidationStep", "AOTBackupRestoreStep", "AotSourceHashStep", "HybridCLRHotUpdateStep",
                "AssetBundleBuildStep", "CloudUploadStep", "PreloadJsonUpdateStep" },
                PipelinePresets.HotUpdate(upload: true).Select(s => s.GetType().Name));
            Assert.IsFalse(PipelinePresets.FullBuild().Any(s => s is CloudUploadStep));
        }
    }
}
