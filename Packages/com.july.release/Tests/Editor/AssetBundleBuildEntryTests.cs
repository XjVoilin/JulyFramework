using System;
using System.IO;
using System.Linq;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor;
using YooAsset.Editor;

namespace July.Release.Tests
{
    public sealed class AssetBundleBuildEntryTests
    {
        [Test] public void AnalysisRejectsDifferentTargetBeforeReadingProjectInputs()
        {
            var target = EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL ? BuildTarget.StandaloneWindows64 : BuildTarget.WebGL;
            var exception = Assert.Throws<InvalidOperationException>(() => ReleaseAssetBundleBuilder.BuildAnalysis(target));
            Assert.That(exception.Message, Does.Contain(target.ToString()));
        }
        [Test] public void FormalTaskOrderAndOriginalYooBuildingTaskArePreserved()
        {
            CollectionAssert.AreEqual(new[] { "TaskPrepare_SBP", "TaskGetBuildMap_SBP", "TaskBuilding_SBP", "TaskVerifyBuildResult_SBP", "TaskEncryption_SBP", "TaskUpdateBundleInfo_SBP", "TaskCreateManifest_SBP", "TaskCreateReport_SBP", "TaskCreatePackage_SBP", "TaskCopyBuildinFiles_SBP", "TaskCreateCatalog_SBP" }, ReleaseAssetBundleBuilder.CreateTasks(false, null).Select(t => t.GetType().Name));
        }
        [Test] public void AnalysisUsesSamePipelineWithoutBuiltinCopyCatalogOrReleasePreparation()
        {
            var tasks = ReleaseAssetBundleBuilder.CreateTasks(false, new BundleSizeCollector()).Select(t => t.GetType().Name).ToArray();
            CollectionAssert.AreEqual(new[] { "TaskPrepare_SBP", "TaskGetBuildMap_SBP", "AnalysisBuildingTask", "TaskVerifyBuildResult_SBP", "TaskEncryption_SBP", "TaskUpdateBundleInfo_SBP", "TaskCreateManifest_SBP", "TaskCreateReport_SBP", "TaskCreatePackage_SBP" }, tasks);
        }
        [Test] public void AnalysisOutputUsesLocalJulyDirectory()
        {
            var formal = Path.GetFullPath(AssetBundleBuilderHelper.GetDefaultBuildOutputRoot()); var analysis = ReleaseAssetBundleBuilder.AnalysisRoot();
            Assert.AreEqual(Path.GetFullPath("Library/July/Release/Analysis"), analysis); Assert.AreNotEqual(formal, analysis);
        }
        [Test] public void SharedParametersKeepFormalCompressionPackingAndCacheContract()
        {
            var parameters = ReleaseAssetBundleBuilder.CreateParameters("Temp/BundleEntry", BuildTarget.WebGL, "Fixture", "1", EBuildinFileCopyOption.None, "");
            Assert.AreEqual(ECompressOption.LZ4, parameters.CompressOption); Assert.IsTrue(parameters.EnableSharePackRule); Assert.IsFalse(parameters.SingleReferencedPackAlone);
            Assert.IsTrue(parameters.VerifyBuildingResult); Assert.IsFalse(parameters.ClearBuildCacheFiles); Assert.IsTrue(parameters.UseAssetDependencyDB);
            Assert.IsFalse(parameters.DisableWriteTypeTree); Assert.IsFalse(parameters.StripUnityVersion); Assert.AreEqual("shared_unity_shaders", parameters.BuiltinShadersBundleName);
        }
        [Test] public void PackagePathCannotEscapeAnalysisOutputRoot()
        {
            var valid = ReleaseAssetBundleBuilder.CreateParameters(ReleaseAssetBundleBuilder.AnalysisRoot(), BuildTarget.WebGL, "Fixture", "1", EBuildinFileCopyOption.None, "");
            Assert.DoesNotThrow(() => ReleaseAssetBundleBuilder.ValidateAnalysisOutput(valid));
            var invalid = ReleaseAssetBundleBuilder.CreateParameters(ReleaseAssetBundleBuilder.AnalysisRoot(), BuildTarget.WebGL, "../../Bundles", "1", EBuildinFileCopyOption.None, "");
            Assert.Throws<InvalidOperationException>(() => ReleaseAssetBundleBuilder.ValidateAnalysisOutput(invalid));
        }
    }
}
