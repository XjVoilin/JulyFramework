using System;
using System.IO;
using System.Linq;
using July.Release.Editor;
using LitJson;
using NUnit.Framework;
using UnityEditor;

namespace July.Release.Tests
{
    public sealed class AotBackupTests
    {
        string root, workspace, output, legacy;
        static readonly string[] Mandatory = { "System" };

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "JulyAotTests-" + Guid.NewGuid().ToString("N"));
            workspace = Path.Combine(root, "workspace");
            output = Path.Combine(root, "Release Sources", "chosen backup");
            legacy = Path.Combine(root, "legacy");
            Directory.CreateDirectory(root);
            WriteWorkspace(workspace);
        }

        [TearDown]
        public void TearDown()
        {
            // 只清理本用例创建的独立临时根目录。
            Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(root));
            StringAssert.StartsWith("JulyAotTests-", Path.GetFileName(root));
            Directory.Delete(root, true);
        }

        static void WriteWorkspace(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, "metadata"));
            File.WriteAllText(Path.Combine(path, "mscorlib.dll"), "aot baseline bytes");
            File.WriteAllText(Path.Combine(path, "System.dll"), "mandatory baseline bytes");
            File.WriteAllText(Path.Combine(path, "AOTGenericReferences.cs"), "// {{ AOT assemblies\n// \"mscorlib.dll\",\n// }}\n");
            File.WriteAllText(Path.Combine(path, "aot-source.hash"), new string('a', 64));
            File.WriteAllText(Path.Combine(path, "metadata", "extra.json"), "additional metadata");
        }

        BuildContext Context() => new BuildContext
        {
            Platform = "WeChat", Target = BuildTarget.WebGL, CoreVersion = "1.6.0", PlanVersion = "1.6.2"
        };
        AotBackupSnapshot Save() => AotBackupStore.Save(workspace, output, "WeChat", "WebGL", "1.6.0", Mandatory);
        BuildContext Select()
        {
            var context = Context();
            context.AotBackupInputPath = output;
            AotBackupStore.SelectInput(context);
            return context;
        }
        void ChangeManifest(Action<AotBackupManifest> change)
        {
            var file = Path.Combine(output, AotBackupStore.ManifestName);
            var data = JsonMapper.ToObject<AotBackupManifest>(File.ReadAllText(file));
            change(data);
            File.WriteAllText(file, JsonMapper.ToJson(data));
        }
        void AssertNoPublishedBackup()
        {
            Assert.IsFalse(Directory.Exists(output));
            Assert.IsEmpty(Directory.GetDirectories(root, "*.aot-tmp-*", SearchOption.AllDirectories));
        }

        [Test]
        public void ArgumentsAcceptAbsolutePathsWithSpacesAndRemainOptional()
        {
            var ctx = Context();
            AotBackupArguments.Apply(new[] { "-aotBackupOutputPath", output + Path.DirectorySeparatorChar }, "FullBuild", ctx);
            Assert.AreEqual(output, ctx.AotBackupOutputPath);
            AotBackupArguments.Apply(new[] { "-aotBackupInputPath", output }, "HotUpdateBuild", ctx);
            Assert.AreEqual(output, ctx.AotBackupInputPath);
            Assert.IsNull(ctx.AotBackupOutputPath);
            AotBackupArguments.Apply(Array.Empty<string>(), "FullBuild", ctx);
            Assert.IsNull(ctx.AotBackupInputPath);
            Assert.IsNull(ctx.AotBackupOutputPath);
        }

        [Test]
        public void ArgumentsRejectConflictDuplicateWrongEntryMissingValueAndInvalidPath()
        {
            foreach (var flag in new[] { "-aotBackupInputPath", "-aotBackupOutputPath" })
            {
                var entry = flag == "-aotBackupInputPath" ? "HotUpdateBuild" : "FullBuild";
                foreach (var args in new[] { new[] { flag }, new[] { flag, "" }, new[] { flag, "-platform", "WeChat" },
                    new[] { flag, "relative/aot" }, new[] { flag, @"\\?\C:\aot" }, new[] { flag, Path.Combine(root, "invalid?") }, new[] { flag, output, flag, output } })
                    Assert.Throws<ArgumentException>(() => AotBackupArguments.Apply(args, entry, Context()));
                foreach (var wrong in new[] { "FullBuild", "HotUpdateBuild", "RunStep", "SyncPlatformDefines" }.Where(v => v != entry))
                    Assert.Throws<ArgumentException>(() => AotBackupArguments.Apply(new[] { flag, output }, wrong, Context()));
            }
            Assert.Throws<ArgumentException>(() => AotBackupArguments.Apply(new[] { "-aotBackupInputPath", output, "-aotBackupOutputPath", output }, "FullBuild", Context()));
        }

        [Test]
        public void ExplicitInputCannotSilentlyDropAMalformedVersionAssertion()
        {
            foreach (var assertion in new[] { new[] { "-aotBackupVersion" }, new[] { "-aotBackupVersion", "" },
                new[] { "-aotBackupVersion", "-debug" }, new[] { "-aotBackupVersion", "invalid" },
                new[] { "-aotBackupVersion", "1.6.0", "-aotBackupVersion", "1.7.0" } })
                Assert.Throws<ArgumentException>(() => AotBackupArguments.Apply(
                    new[] { "-aotBackupInputPath", output }.Concat(assertion).ToArray(), "HotUpdateBuild", Context()));
            AotBackupArguments.Apply(new[] { "-aotBackupInputPath", output, "-aotBackupVersion", "1.6.0" }, "HotUpdateBuild", Context());
        }

        [Test]
        public void ExplicitArchivePublishesOnePersistentCopyAndKeepsWorkspace()
        {
            var ctx = Context();
            ctx.AotBackupOutputPath = output;
            var snapshot = AotBackupStore.Archive(ctx, workspace, legacy, Mandatory);
            AotBackupStore.Validate(output, snapshot, "WeChat", "WebGL", "1.6.0", Mandatory);
            Assert.IsFalse(Directory.Exists(legacy));
            Assert.AreEqual(5, snapshot.Manifest.files.Length);
            Assert.AreEqual(5, Directory.GetFiles(workspace, "*", SearchOption.AllDirectories).Length);
            Assert.IsFalse(File.Exists(Path.Combine(workspace, AotBackupStore.ManifestName)));
            Assert.AreEqual("additional metadata", File.ReadAllText(Path.Combine(output, "metadata", "extra.json")));
        }

        [Test]
        public void ExistingEmptyDirectoryFileOrBackupCannotBeOverwritten()
        {
            Directory.CreateDirectory(output);
            Assert.Throws<IOException>(() => Save());
            Directory.Delete(output);
            File.WriteAllText(output, "keep");
            Assert.Throws<IOException>(() => Save());
            Assert.AreEqual("keep", File.ReadAllText(output));
            File.Delete(output);
            var snapshot = Save();
            Assert.Throws<IOException>(() => Save());
            AotBackupStore.Validate(output, snapshot, "WeChat", "WebGL", "1.6.0", Mandatory);
        }

        [Test]
        public void InputAndOutputRejectEqualParentAndChildWorkspacePaths()
        {
            foreach (var path in new[] { workspace, root, Path.Combine(workspace, "child"), Path.Combine(workspace, "child", "..") })
            {
                Assert.Throws<IOException>(() => AotBackupStore.Save(workspace, path, "WeChat", "WebGL", "1.6.0", Mandatory));
                var ctx = Context();
                ctx.AotBackupInputPath = path;
                Assert.Throws<IOException>(() => AotBackupStore.Restore(ctx, workspace, legacy, Mandatory));
            }
        }

        [Test]
        public void CopyIoFailureLeavesNoPublishedOrTemporaryBackup()
        {
            using (File.Open(Path.Combine(workspace, "System.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => Save());
            AssertNoPublishedBackup();
        }

        [Test]
        public void IncompleteWorkspaceCannotProduceAValidBackup()
        {
            foreach (var name in new[] { "aot-source.hash", "System.dll", "AOTGenericReferences.cs" })
            {
                var path = Path.Combine(workspace, name);
                var contents = File.ReadAllText(path);
                File.Delete(path);
                Assert.That(Assert.Catch(() => Save()), Is.InstanceOf<IOException>().Or.InstanceOf<InvalidDataException>());
                AssertNoPublishedBackup();
                File.WriteAllText(path, contents);
            }
            File.WriteAllText(Path.Combine(workspace, "AOTGenericReferences.cs"), "// no assembly list");
            Assert.Throws<InvalidDataException>(() => Save());
            AssertNoPublishedBackup();
        }

        [Test]
        public void ManifestSetsOnlyCoreBaselineAndHonorsVersionAssertion()
        {
            Save();
            var ctx = Context();
            ctx.CoreVersion = "9.0.0";
            ctx.AotBackupInputPath = output;
            ctx.AOTBackupVersion = "1.6.0";
            AotBackupStore.SelectInput(ctx);
            Assert.AreEqual("1.6.0", ctx.CoreVersion);
            Assert.AreEqual("1.6.0", ctx.AOTBackupVersion);
            Assert.AreEqual("1.6.2", ctx.PlanVersion);
            Assert.AreEqual("WeChat", ctx.Platform);
            Assert.AreEqual(BuildTarget.WebGL, ctx.Target);
            ctx.AOTBackupVersion = "1.7.0";
            Assert.Throws<InvalidDataException>(() => AotBackupStore.SelectInput(ctx));
            Assert.AreEqual("1.7.0", ctx.AOTBackupVersion);
        }

        [Test]
        public void RequestedPlatformAndTargetAreNeverOverriddenByManifest()
        {
            Save();
            var ctx = Context();
            ctx.AotBackupInputPath = output;
            ctx.Platform = "TikTok";
            Assert.Throws<InvalidDataException>(() => AotBackupStore.SelectInput(ctx));
            Assert.AreEqual("TikTok", ctx.Platform);
            ctx.Platform = "WeChat";
            ctx.Target = BuildTarget.Android;
            Assert.Throws<InvalidDataException>(() => AotBackupStore.SelectInput(ctx));
            Assert.AreEqual(BuildTarget.Android, ctx.Target);
        }

        [Test]
        public void ExplicitRestoreReplacesExistingAotAndRemovesResidue()
        {
            var snapshot = Save();
            var ctx = Select();
            File.WriteAllText(Path.Combine(workspace, "mscorlib.dll"), "other baseline");
            File.WriteAllText(Path.Combine(workspace, "obsolete.dll"), "remove old residue");
            WriteWorkspace(legacy);
            AotBackupStore.ValidateInput(ctx, workspace, Mandatory);
            Assert.Throws<InvalidDataException>(() => AotBackupStore.ValidateRestored(ctx, workspace, Mandatory));
            AotBackupStore.Restore(ctx, workspace, legacy, Mandatory);
            Assert.IsFalse(File.Exists(Path.Combine(workspace, "obsolete.dll")));
            Assert.AreEqual("aot baseline bytes", File.ReadAllText(Path.Combine(workspace, "mscorlib.dll")));
            AotBackupStore.ValidateRestored(ctx, workspace, Mandatory);
            AotBackupStore.Validate(output, snapshot, "WeChat", "WebGL", "1.6.0", Mandatory);
        }

        [Test]
        public void RelativeWorkspaceCanBeRestoredAndValidatedWithoutRelaxingExternalPaths()
        {
            var relativeWorkspace = Path.Combine("Temp", "JulyAotRelativeTests-" + Guid.NewGuid().ToString("N"));
            var absoluteWorkspace = Path.GetFullPath(relativeWorkspace);
            Assert.AreEqual(Path.GetFullPath("Temp"), Path.GetDirectoryName(absoluteWorkspace));
            try
            {
                Save();
                var ctx = Select();
                WriteWorkspace(relativeWorkspace);
                File.WriteAllText(Path.Combine(relativeWorkspace, "obsolete.dll"), "old workspace residue");
                AotBackupStore.ValidateInput(ctx, relativeWorkspace, Mandatory);
                AotBackupStore.Restore(ctx, relativeWorkspace, legacy, Mandatory);
                Assert.IsFalse(File.Exists(Path.Combine(relativeWorkspace, "obsolete.dll")));
                Assert.Throws<ArgumentException>(() => AotBackupStore.Read(relativeWorkspace, "WeChat", "WebGL", "1.6.0"));
                AotBackupStore.ValidateRestored(ctx, relativeWorkspace, Mandatory);
                File.WriteAllText(Path.Combine(relativeWorkspace, "System.dll"), "damaged restored file");
                Assert.Throws<InvalidDataException>(() => AotBackupStore.ValidateRestored(ctx, relativeWorkspace, Mandatory));
            }
            finally
            {
                if (Directory.Exists(absoluteWorkspace)) Directory.Delete(absoluteWorkspace, true);
            }
        }

        [Test]
        public void PreflightUsesExplicitBackupBeforeAnyWorkspaceExists()
        {
            Save();
            Directory.Delete(workspace, true);
            var ctx = Select();
            AotBackupStore.ValidateInput(ctx, workspace, Mandatory);
            Assert.IsFalse(Directory.Exists(workspace));
            Assert.IsFalse(Directory.Exists(legacy));
            AotBackupStore.Restore(ctx, workspace, legacy, Mandatory);
            AotBackupStore.ValidateRestored(ctx, workspace, Mandatory);
        }

        [Test]
        public void MissingOrUnfinishedInputCannotUseExistingWorkspaceOrLegacyArchive()
        {
            WriteWorkspace(legacy);
            var ctx = Context();
            ctx.AotBackupInputPath = output;
            Assert.Throws<InvalidDataException>(() => AotBackupStore.SelectInput(ctx));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "mscorlib.dll"), "partial copy");
            Assert.Throws<InvalidDataException>(() => AotBackupStore.SelectInput(ctx));
            Assert.Throws<InvalidDataException>(() => AotBackupStore.Restore(ctx, workspace, legacy, Mandatory));
            Assert.AreEqual("aot baseline bytes", File.ReadAllText(Path.Combine(workspace, "mscorlib.dll")));
        }

        [Test]
        public void DamagedMissingOrExtraFilesFailWithoutFallbackOrWorkspaceChanges()
        {
            Save();
            var ctx = Select();
            WriteWorkspace(legacy);
            var file = Path.Combine(output, "mscorlib.dll");
            var original = File.ReadAllText(file);
            foreach (var defect in new[] { "changed", "missing", "extra" })
            {
                if (defect == "changed") File.WriteAllText(file, new string('X', original.Length));
                if (defect == "missing") File.Delete(file);
                if (defect == "extra") File.WriteAllText(Path.Combine(output, "unexpected.dll"), "extra");
                Assert.Throws<InvalidDataException>(() => AotBackupStore.ValidateInput(ctx, workspace, Mandatory));
                Assert.Throws<InvalidDataException>(() => AotBackupStore.Restore(ctx, workspace, legacy, Mandatory));
                Assert.AreEqual(original, File.ReadAllText(Path.Combine(workspace, "mscorlib.dll")));
                File.WriteAllText(file, original);
                File.Delete(Path.Combine(output, "unexpected.dll"));
            }
        }

        [Test]
        public void MalformedAndUnsafeManifestEntriesAreRejected()
        {
            Save();
            var file = Path.Combine(output, AotBackupStore.ManifestName);
            var original = File.ReadAllText(file);
            foreach (var json in new[] { "{", "null", "{\"formatVersion\":2}" })
            {
                File.WriteAllText(file, json);
                Assert.Throws<InvalidDataException>(() => Select());
            }
            foreach (var unsafePath in new[] { "../escape.dll", "/absolute.dll", "nested\\escape.dll", "aot-backup.json" })
            {
                File.WriteAllText(file, original);
                ChangeManifest(m => m.files[0].path = unsafePath);
                Assert.Throws<InvalidDataException>(() => Select());
            }
        }

        [Test]
        public void ExplicitFormatRequiresSourceHashReferencesAndCurrentMandatoryAssemblies()
        {
            Save();
            var ctx = Select();
            Assert.Throws<InvalidDataException>(() => AotBackupStore.ValidateInput(ctx, workspace, new[] { "NewMandatory" }));
            ChangeManifest(m => m.files = m.files.Where(f => f.path != "aot-source.hash").ToArray());
            Assert.Throws<InvalidDataException>(() => Select());
        }

        [Test]
        public void SelectionAndRestoredWorkspaceCannotSilentlyChangeLater()
        {
            Save();
            var ctx = Select();
            var manifest = Path.Combine(output, AotBackupStore.ManifestName);
            var original = File.ReadAllText(manifest);
            File.AppendAllText(manifest, " ");
            Assert.Throws<InvalidDataException>(() => AotBackupStore.Restore(ctx, workspace, legacy, Mandatory));
            File.WriteAllText(manifest, original);
            AotBackupStore.Restore(ctx, workspace, legacy, Mandatory);
            File.WriteAllText(Path.Combine(workspace, "System.dll"), "changed by later step");
            Assert.Throws<InvalidDataException>(() => AotBackupStore.ValidateRestored(ctx, workspace, Mandatory));
            ctx.AOTBackupVersion = "9.0.0";
            Assert.Throws<InvalidDataException>(() => AotBackupStore.ValidateRestored(ctx, workspace, Mandatory));
        }

        [Test]
        public void NoPathPreservesLegacyArchiveRestoreAndExistingWorkspaceBehavior()
        {
            var ctx = Context();
            File.Delete(Path.Combine(workspace, "aot-source.hash")); // 旧格式允许缺少 hash。
            Assert.IsNull(AotBackupStore.Archive(ctx, workspace, legacy, Mandatory));
            Assert.IsFalse(File.Exists(Path.Combine(legacy, AotBackupStore.ManifestName)));
            Assert.IsFalse(Directory.Exists(output));
            Directory.Delete(workspace, true);
            AotBackupStore.Restore(ctx, workspace, legacy, Mandatory);
            Assert.IsTrue(File.Exists(Path.Combine(workspace, "mscorlib.dll")));
            File.WriteAllText(Path.Combine(workspace, "mscorlib.dll"), "keep current workspace");
            AotBackupStore.Restore(ctx, workspace, legacy, Mandatory);
            Assert.AreEqual("keep current workspace", File.ReadAllText(Path.Combine(workspace, "mscorlib.dll")));
            Directory.Delete(workspace, true);
            Directory.Delete(legacy, true);
            Assert.Throws<DirectoryNotFoundException>(() => AotBackupStore.Restore(ctx, workspace, legacy, Mandatory));
        }
    }
}
