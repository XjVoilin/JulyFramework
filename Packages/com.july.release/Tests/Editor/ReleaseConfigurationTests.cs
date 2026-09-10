using System;
using System.IO;
using HybridCLR.Editor;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;
using BuildContext = July.Release.Editor.BuildContext;

namespace July.Release.Tests
{
    public sealed class MinimalRuntimeConfig : ScriptableObject, IReleaseBootConfig
    {
        public UnityEngine.Object Asset => this;
        public ReleaseEnvironment env { get; set; }
        public string cdnUrl => "https://cdn.example.com/Project";
        public string EnvName => env.ToString();
        public string GetConfigServerUrl() => GetConfigServerUrl(env);
        public string GetConfigServerUrl(ReleaseEnvironment environment) => "https://backend.example.com";
    }

    public sealed class SharedRuntimeConfig : ScriptableObject, IReleaseBootConfig, IReleaseResourceConfig
    {
        public UnityEngine.Object Asset => this;
        public ReleaseEnvironment env { get; set; }
        public string cdnUrl => "https://cdn.example.com/Project";
        public string EnvName => env.ToString();
        public ReleaseResourceSettings Resources { get; } = new ReleaseResourceSettings { packageName = "SharedPackage" };
        public string GetConfigServerUrl() => "https://backend.example.com";
        public string GetConfigServerUrl(ReleaseEnvironment environment) => GetConfigServerUrl();
    }

    public sealed class ReleaseConfigurationTests
    {
        BuildConfig config;
        MinimalRuntimeConfig runtime;
        AssetBundleCollectorSetting collectors;
        [SetUp] public void SetUp()
        {
            config = ScriptableObject.CreateInstance<BuildConfig>();
            runtime = ScriptableObject.CreateInstance<MinimalRuntimeConfig>();
            collectors = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();
            config.bootConfig = runtime;
            config.collectorSettings = collectors;
            var package = new AssetBundleCollectorPackage { PackageName = "DefaultPackage" };
            var hot = new AssetBundleCollectorGroup { GroupName = ReleaseConventions.HotFixGroup };
            hot.Collectors.Add(new AssetBundleCollector { CollectPath = "Assets/Existing/Hotfix" });
            var aot = new AssetBundleCollectorGroup { GroupName = ReleaseConventions.AotMetaGroup };
            aot.Collectors.Add(new AssetBundleCollector { CollectPath = "Assets/Existing/Metadata" });
            package.Groups.Add(hot); package.Groups.Add(aot); collectors.Packages.Add(package);
        }
        [TearDown] public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(config);
            UnityEngine.Object.DestroyImmediate(runtime);
            UnityEngine.Object.DestroyImmediate(collectors);
        }

        [Test] public void MinimalRuntimeContractDoesNotRequireGooseResourcePolicy()
        {
            Assert.AreSame(runtime, config.GetBootConfig());
            Assert.AreSame(config.resources, config.ResourceSettings);
            Assert.IsFalse(config.sharedBundles.enabled);
            Assert.IsNull(config.launchFont);
            Assert.IsInstanceOf<TaskGetBuildMap_SBP>(AssetBundleBuildStep.CreateBuildMapTask(false));
            Assert.IsInstanceOf<CustomTaskGetBuildMap_SBP>(AssetBundleBuildStep.CreateBuildMapTask(true));
        }

        [Test] public void SharedRuntimeSettingsHaveSingleOwnership()
        {
            var shared = ScriptableObject.CreateInstance<SharedRuntimeConfig>();
            try
            {
                config.bootConfig = shared;
                Assert.AreSame(shared.Resources, config.ResourceSettings);
                Assert.AreEqual("SharedPackage", config.ResourceSettings.packageName);
            }
            finally { UnityEngine.Object.DestroyImmediate(shared); }
        }

        [Test] public void PathsComeFromExistingCollectorAndHybridClrSettings()
        {
            var before = EditorJsonUtility.ToJson(collectors);
            var profile = config.CreateProfile();
            Assert.AreEqual("Assets/Existing/Hotfix", profile.HybridCLR.HotUpdateDllDirectory);
            Assert.AreEqual("Assets/Existing/Metadata", profile.HybridCLR.AotMetadataDirectory);
            Assert.AreEqual("HybridCLRData/AOTBackup", profile.HybridCLR.AotBackupRoot);
            Assert.AreEqual("CDN", profile.CdnRoot);
            Assert.AreEqual(Path.Combine("Assets", SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile).Replace('\\', '/'), profile.HybridCLR.AotGenericReferencesPath);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(collectors));
            collectors.Packages[0].Groups[0].Collectors[0].CollectPath = "Assets/Other/Hotfix";
            Assert.AreEqual("Assets/Other/Hotfix", config.CreateProfile().HybridCLR.HotUpdateDllDirectory);
        }

        [Test] public void AmbiguousMissingAndOverlappingDllDirectoriesFail()
        {
            Assert.Throws<InvalidOperationException>(() => config.GetCollectorDirectory("Missing"));
            var hot = collectors.Packages[0].Groups[0];
            hot.Collectors.Add(new AssetBundleCollector { CollectPath = "Assets/Other" });
            Assert.Throws<InvalidOperationException>(() => config.GetCollectorDirectory(ReleaseConventions.HotFixGroup));
            hot.Collectors.RemoveAt(1);
            hot.Collectors[0].CollectPath = "../Outside";
            Assert.Throws<InvalidOperationException>(() => config.CreateProfile());
            hot.Collectors[0].CollectPath = "Assets/Existing";
            Assert.Throws<InvalidOperationException>(() => config.CreateProfile());
        }

        [Test] public void UnselectedCapabilitiesDoNotDemandDirectoriesOrUploadTools()
        {
            config.aot.sourceDirectory = "missing-aot";
            var context = new BuildContext { Platform = "WeChat", CoreVersion = "1.0.0", PlanVersion = "1.0.0" };
            Assert.IsEmpty(ReleaseConfigurationCheck.Inspect(config, context, false, false, false, false));
            var before = EditorJsonUtility.ToJson(collectors);
            var errors = ReleaseConfigurationCheck.Inspect(config, context, true, true, false, true);
            Assert.That(errors.Count, Is.GreaterThan(1));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(collectors));
        }

        [Test] public void AssemblyManifestPreservesExistingRuntimeFormat()
        {
            var root = Path.Combine(Path.GetTempPath(), "JulyManifestTest-" + Guid.NewGuid().ToString("N"));
            var hot = Path.Combine(root, "hot"); var metadata = Path.Combine(root, "aot");
            Directory.CreateDirectory(hot); Directory.CreateDirectory(metadata);
            try
            {
                File.WriteAllText(Path.Combine(hot, "Hotfix.Runtime.dll.bytes"), "fixture");
                File.WriteAllText(Path.Combine(metadata, "Aot.Runtime.dll.bytes"), "fixture");
                HybridClrAssemblyManifest.Write(hot, metadata);
                var manifest = JsonUtility.FromJson<HybridClrAssemblyManifest.Manifest>(File.ReadAllText(Path.Combine(hot, "hybridclr-manifest.json")));
                CollectionAssert.AreEqual(new[] { "Hotfix.Runtime" }, manifest.hotUpdateAssemblies);
                CollectionAssert.AreEqual(new[] { "Aot.Runtime" }, manifest.aotMetadataAssemblies);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
