using System;
using System.IO;
using July.Release.Editor;
using NUnit.Framework;

namespace July.Release.Tests
{
    public sealed class HybridClrManifestTests
    {
        [Test]
        public void PublishedManifestUsesDllDependenciesInsteadOfAlphabeticalOrder()
        {
            var root = Path.Combine(Path.GetTempPath(), "JulyManifestOrder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                // Editor 名称排在 Runtime 前，但 DLL 实际依赖 Runtime，加载顺序必须相反。
                File.Copy(typeof(BuildContext).Assembly.Location, Path.Combine(root, "July.Release.Editor.dll.bytes"));
                File.Copy(typeof(ReleaseEnvironment).Assembly.Location, Path.Combine(root, "July.Release.Runtime.dll.bytes"));
                File.WriteAllText(Path.Combine(root, "Stale.dll.bytes"), "不属于本次构建的遗留文件");
                HybridClrAssemblyManifest.Write(root, new[] { "July.Release.Editor", "July.Release.Runtime" }, Array.Empty<string>());
                var names = HybridClrManifest.ReadHotUpdateAssemblies(
                    File.ReadAllText(Path.Combine(root, HybridClrManifest.FileName)), "July.Release.Editor");
                CollectionAssert.AreEqual(new[] { "July.Release.Runtime", "July.Release.Editor" }, names);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void MissingBuildArtifactCannotPublishAManifest()
        {
            var root = Path.Combine(Path.GetTempPath(), "JulyManifestMissing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Assert.Throws<FileNotFoundException>(() => HybridClrAssemblyManifest.Write(root, new[] { "Missing" }, Array.Empty<string>()));
                Assert.IsFalse(File.Exists(Path.Combine(root, HybridClrManifest.FileName)));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
