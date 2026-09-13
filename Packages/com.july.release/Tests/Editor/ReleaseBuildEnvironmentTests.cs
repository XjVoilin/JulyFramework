using System.Collections.Generic;
using System.IO;
using System.Linq;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor;

namespace July.Release.Tests
{
    public sealed class ReleaseBuildEnvironmentTests
    {
        [Test]
        public void FullBuildPreparesBeforeGenerateAllAndOnlyOnce()
        {
            var steps = PipelinePresets.FullBuild(miniGame: true);
            Assert.That(steps.Count(s => s is TuanjieBuildEnvironmentStep), Is.EqualTo(1));
            var preparation = steps.FindIndex(s => s is TuanjieBuildEnvironmentStep);
            Assert.That(preparation, Is.GreaterThan(steps.FindIndex(s => s is PlatformDefinesValidationStep)));
            Assert.That(preparation, Is.LessThan(steps.FindIndex(s => s is HybridCLRGenerateAllStep)));
        }

        [Test]
        public void CustomPlayerBuildStillPreparesWithoutGenerateAll()
        {
            var selection = new BuildToolSelection
            {
                Mode = ReleaseBuildMode.Full, CustomSteps = true,
                HybridCLR = false, AssetBundles = false, MiniGame = true
            };
            var steps = selection.CreateSteps();
            Assert.That(steps.Count(s => s is TuanjieBuildEnvironmentStep), Is.EqualTo(1));
            Assert.That(steps.FindIndex(s => s is TuanjieBuildEnvironmentStep),
                Is.LessThan(steps.FindIndex(s => s is MiniGameBuildStep)));
        }

        [Test]
        public void ResourceAndHotUpdateBuildsDoNotChangeEnvironment()
        {
            Assert.IsFalse(PipelinePresets.FullBuild(hybridCLR: false).Any(s => s is TuanjieBuildEnvironmentStep));
            Assert.IsFalse(PipelinePresets.HotUpdate().Any(s => s is TuanjieBuildEnvironmentStep));
            var steps = new List<BuildStep> { new CloudUploadStep() };
            PipelinePresets.AddBuildEnvironmentStep(steps);
            Assert.That(steps.Count, Is.EqualTo(1));
        }

        [Test]
        public void CiStepListsPrepareBeforeTheFirstDependentStep()
        {
            foreach (var requested in new[]
            {
                new BuildStep[] { new HybridCLRGenerateAllStep() },
                new BuildStep[] { new MiniGameBuildStep() },
                new BuildStep[] { new HybridCLRInstallStep(), new HybridCLRGenerateAllStep(), new MiniGameBuildStep() }
            })
            {
                var steps = requested.ToList();
                PipelinePresets.AddBuildEnvironmentStep(steps);
                Assert.That(steps.Count(s => s is TuanjieBuildEnvironmentStep), Is.EqualTo(1));
                var index = steps.FindIndex(s => s is TuanjieBuildEnvironmentStep);
                Assert.That(steps[index + 1], Is.InstanceOf<HybridCLRGenerateAllStep>().Or.InstanceOf<MiniGameBuildStep>());
                CollectionAssert.AreEqual(requested, steps.Where(s => s is not TuanjieBuildEnvironmentStep));
            }
        }

        [Test]
        public void NonMiniGameTargetLeavesSettingsUntouched()
        {
#if TUANJIE_1_5_OR_NEWER
            var original = PlayerSettings.MiniGame.useSlimMetaFileFormat;
            try
            {
                PlayerSettings.MiniGame.useSlimMetaFileFormat = true;
                Assert.IsTrue(new TuanjieBuildEnvironmentStep().Execute(new BuildContext { Target = BuildTarget.StandaloneWindows64 }));
                Assert.IsTrue(PlayerSettings.MiniGame.useSlimMetaFileFormat);
            }
            finally { PlayerSettings.MiniGame.useSlimMetaFileFormat = original; }
#else
            var before = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
            Assert.IsTrue(new TuanjieBuildEnvironmentStep().Execute(new BuildContext { Target = BuildTarget.WebGL }));
            CollectionAssert.AreEqual(before, File.ReadAllBytes("ProjectSettings/ProjectSettings.asset"));
#endif
        }

#if TUANJIE_1_5_OR_NEWER
        [Test]
        public void MiniGameDisablesSlimMetadataWithoutChangingOtherParameters()
        {
            var original = PlayerSettings.MiniGame.useSlimMetaFileFormat;
            var http = PlayerSettings.insecureHttpOption;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.MiniGame);
            var target = EditorUserBuildSettings.activeBuildTarget;
            try
            {
                PlayerSettings.MiniGame.useSlimMetaFileFormat = true;
                var step = new TuanjieBuildEnvironmentStep();
                var context = new BuildContext { Target = BuildTarget.MiniGame };
                Assert.IsTrue(step.Execute(context));
                Assert.IsFalse(PlayerSettings.MiniGame.useSlimMetaFileFormat);
                StringAssert.Contains("weixinMiniGameUseSlimMetaFileFormat: 0", File.ReadAllText("ProjectSettings/ProjectSettings.asset"));
                Assert.IsTrue(step.Execute(context));
                Assert.IsFalse(PlayerSettings.MiniGame.useSlimMetaFileFormat);
                Assert.AreEqual(http, PlayerSettings.insecureHttpOption);
                Assert.AreEqual(defines, PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.MiniGame));
                Assert.AreEqual(target, EditorUserBuildSettings.activeBuildTarget);
            }
            finally
            {
                PlayerSettings.MiniGame.useSlimMetaFileFormat = original;
                AssetDatabase.SaveAssets();
            }
        }
#endif
    }
}
