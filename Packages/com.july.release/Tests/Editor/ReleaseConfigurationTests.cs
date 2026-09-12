using System;
using System.IO;
using System.Reflection;
using HybridCLR.Editor;
using July.Build;
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
        public System.Collections.Generic.IReadOnlyList<string> AdditionalAotMetadataAssemblies { get; } = new[] { "Shared.Aot.dll" };
        public ReleaseResourceSettings Resources { get; } = new ReleaseResourceSettings { PackageName = "SharedPackage" };
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
            var hot = new AssetBundleCollectorGroup { GroupName = ReleaseConventions.HotFixGroup, AssetTags = ReleaseResourceConventions.HotUpdateTag };
            hot.Collectors.Add(new AssetBundleCollector { CollectPath = "Assets/Existing/Hotfix" });
            var aot = new AssetBundleCollectorGroup { GroupName = ReleaseConventions.AotMetaGroup, AssetTags = ReleaseResourceConventions.AotMetadataTag };
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
            Assert.AreSame(config.aot.AdditionalAotMetadataAssemblies, config.AdditionalAotMetadataAssemblies);
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
                Assert.AreSame(shared.AdditionalAotMetadataAssemblies, config.AdditionalAotMetadataAssemblies);
                collectors.Packages[0].PackageName = "SharedPackage";
                CollectionAssert.AreEqual(shared.AdditionalAotMetadataAssemblies, config.CreateProfile().HybridCLR.MandatoryAotAssemblies);
                Assert.AreEqual("SharedPackage", config.ResourceSettings.PackageName);
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

        [Test] public void CodeResourceTagsMustMatchFrameworkConventions()
        {
            var hot = collectors.Packages[0].Groups[0];
            hot.AssetTags = "CustomCode";
            var error = Assert.Throws<InvalidOperationException>(() => config.CreateProfile());
            StringAssert.Contains(ReleaseResourceConventions.HotUpdateTag, error.Message);
            hot.Collectors[0].AssetTags = ReleaseResourceConventions.HotUpdateTag;
            Assert.DoesNotThrow(() => config.CreateProfile());
            collectors.Packages[0].Groups[1].AssetTags = "";
            Assert.Throws<InvalidOperationException>(() => config.CreateProfile());
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
                File.Copy(typeof(ReleaseEnvironment).Assembly.Location, Path.Combine(hot, "July.Release.Runtime.dll.bytes"));
                File.WriteAllText(Path.Combine(metadata, "Aot.Runtime.dll.bytes"), "fixture");
                File.WriteAllText(Path.Combine(hot, "Removed.dll.bytes"), "stale");
                File.WriteAllText(Path.Combine(metadata, "RemovedAot.dll.bytes"), "stale");
                HybridClrAssemblyManifest.Write(hot, new[] { "July.Release.Runtime" }, new[] { "Aot.Runtime" });
                var manifest = JsonUtility.FromJson<HybridClrAssemblyManifest.Manifest>(File.ReadAllText(Path.Combine(hot, "hybridclr-manifest.json")));
                CollectionAssert.AreEqual(new[] { "July.Release.Runtime" }, manifest.hotUpdateAssemblies);
                Assert.AreEqual(HybridClrManifest.FormatVersion, manifest.formatVersion);
                CollectionAssert.AreEqual(new[] { "Aot.Runtime" }, manifest.aotMetadataAssemblies);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public void ObsoleteDllCleanupPreservesCurrentGuidsAndUnrelatedFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "JulyDllCleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (var name in new[] { "Current.dll.bytes", "Current.dll.bytes.meta",
                    "Removed.dll.bytes", "Removed.dll.bytes.meta", "notes.bytes", "notes.bytes.meta" })
                    File.WriteAllText(Path.Combine(root, name), name);
                typeof(HybridCLRBuildService).GetMethod("RemoveObsoleteDlls", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { root, new[] { "Current" } });
                Assert.IsFalse(File.Exists(Path.Combine(root, "Removed.dll.bytes")));
                Assert.IsFalse(File.Exists(Path.Combine(root, "Removed.dll.bytes.meta")));
                foreach (var name in new[] { "Current.dll.bytes", "Current.dll.bytes.meta", "notes.bytes", "notes.bytes.meta" })
                    Assert.AreEqual(name, File.ReadAllText(Path.Combine(root, name)));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test] public void AotCopyReportsOnlyThisBuildsCopiedAssemblies()
        {
            var root = Path.Combine(Path.GetTempPath(), "JulyAotCopy-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "source"); var output = Path.Combine(root, "aot");
            Directory.CreateDirectory(source); Directory.CreateDirectory(output);
            try
            {
                var references = Path.Combine(source, "AOTGenericReferences.cs");
                File.WriteAllText(references, "// {{ AOT assemblies\n\"Current.dll\",\n\"Missing.dll\"\n// }}");
                File.WriteAllText(Path.Combine(source, "Current.dll"), "current");
                File.WriteAllText(Path.Combine(output, "Missing.dll.bytes"), "previous build");
                File.WriteAllText(Path.Combine(output, "Removed.dll.bytes"), "removed");
                var profile = new HybridCLRBuildProfile(Path.Combine(root, "hot"), output, root, references);
                var copied = (System.Collections.Generic.List<string>)typeof(HybridCLRBuildService)
                    .GetMethod("CopyAotMetadataFrom", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { profile, source, false, source });
                CollectionAssert.AreEqual(new[] { "Current" }, copied);
                Assert.AreEqual("current", File.ReadAllText(Path.Combine(output, "Current.dll.bytes")));
                Assert.IsFalse(File.Exists(Path.Combine(output, "Missing.dll.bytes")));
                Assert.IsFalse(File.Exists(Path.Combine(output, "Removed.dll.bytes")));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
