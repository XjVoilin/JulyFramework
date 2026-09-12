using System;
using July.Release;
using NUnit.Framework;

namespace July.Bootstrap.Tests
{
    public sealed class HotUpdateManifestTests
    {
        [Test]
        public void ManifestOrderIsPreservedAndCanIncludeNewAssemblies()
        {
            var names = HybridClrManifest.ReadHotUpdateAssemblies(
                "{\"formatVersion\":1,\"hotUpdateAssemblies\":[\"Z.Dependency\",\"A.Game\",\"NewGame\"]}", "A.Game");
            CollectionAssert.AreEqual(new[] { "Z.Dependency", "A.Game", "NewGame" }, names);
            CollectionAssert.AreEqual(new[] { "Z.Dependency.dll", "A.Game.dll", "NewGame.dll" }, BootstrapAssemblyLoader.Normalize(names));
        }

        [Test]
        public void OldOrUnknownManifestRequiresRebuild()
        {
            foreach (var json in new[] { "{", "null", "[]", "{}", "{\"hotUpdateAssemblies\":[\"Game\"]}",
                "{\"formatVersion\":2,\"hotUpdateAssemblies\":[\"Game\"]}", "{\"formatVersion\":\"1\"}" })
                Assert.Throws<InvalidOperationException>(() => HybridClrManifest.ReadHotUpdateAssemblies(json, "Game"), json);
        }

        [Test]
        public void InvalidAssemblyEntriesFailAtTheManifestBoundary()
        {
            foreach (var entries in new[] { "null", "[]", "{}", "[null]", "[1]", "[\"\"]", "[\"Game.dll\"]",
                "[\"../Game\"]", "[\"Game\",\"Game\"]" })
                Assert.Throws<InvalidOperationException>(() => HybridClrManifest.ReadHotUpdateAssemblies(
                    "{\"formatVersion\":1,\"hotUpdateAssemblies\":" + entries + "}", "Game"), entries);
        }

        [Test]
        public void RegistrarMustBeIncludedBeforeAnyAssemblyIsLoaded()
        {
            var error = Assert.Throws<InvalidOperationException>(() => HybridClrManifest.ReadHotUpdateAssemblies(
                "{\"formatVersion\":1,\"hotUpdateAssemblies\":[\"Other\"]}", "Game"));
            StringAssert.Contains("Game", error.Message);
        }
    }
}
