using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace July.Config.Authoring.Tests
{
    public sealed class ConfigGenerationTests
    {
        private string _root;
        private FixtureProcess _process;
        [SetUp] public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "JulyConfigAuthoringTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Tools/Luban/Luban"));
            Directory.CreateDirectory(Path.Combine(_root, "Tools/Luban/DataTables"));
            File.WriteAllText(Path.Combine(_root, "Tools/Luban/Luban/Luban.dll"), "fixture");
            _process = new FixtureProcess();
        }
        [TearDown] public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                var parent = Path.Combine(Path.GetTempPath(), "JulyConfigAuthoringTests") + Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase), Is.True);
                Directory.Delete(_root, true);
            }
        }
        private ConfigGenerationTask Task(string id = "A", params IConfigOutputProcessor[] processors)
            => new ConfigGenerationTask(id, id, "Group", "cfg", "Assets/" + id + "/Code", "Assets/" + id + "/Data",
                dataDirectory: "Datas/" + id, schemas: new[] { new ConfigSchemaFile("Datas/" + id + "/__tables__.xlsx", "table") },
                processors: processors.Length == 0 ? null : processors);
        private ConfigGenerationResult Run(params ConfigGenerationTask[] tasks) => new ConfigGenerationRunner(_root, _process).Run(tasks);
        private string FileAt(string relative) => Path.Combine(_root, relative);

        [Test] public void GeneratesRegistryAndProjectProcessorFromSameMetadata()
        {
            var task = Task("A", new TablesExtensionProcessor(), new AddProvider());
            Assert.That(Run(task).Succeeded, Is.True);
            Assert.That(File.ReadAllText(FileAt("Assets/A/Code/TablesExt.cs")), Does.Contain("registry[typeof(TbThing)] = TbThing"));
            Assert.That(File.ReadAllText(FileAt("Assets/A/Code/ProjectProvider.cs")), Does.Contain("TbThing:tbthing"));
        }
        [Test] public void ToolFailureLeavesPreviouslyPublishedFilesAndMetadataUnchanged()
        {
            Assert.That(Run(Task()).Succeeded, Is.True);
            var path = FileAt("Assets/A/Code/Thing.cs"); var previous = File.ReadAllBytes(path);
            File.WriteAllText(path + ".meta", "guid: stable");
            _process.Fail = true;
            Assert.That(Run(Task()).Succeeded, Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(previous));
            Assert.That(File.ReadAllText(path + ".meta"), Is.EqualTo("guid: stable"));
        }
        [Test] public void LaterProcessorFailurePreventsAllSelectedTasksFromPublishing()
        {
            Assert.That(Run(Task("A"), Task("B")).Succeeded, Is.True);
            var previous = File.ReadAllText(FileAt("Assets/A/Data/tbthing.json"));
            _process.Value = 2;
            Assert.That(Run(Task("A"), Task("B", new FailProcessor())).Succeeded, Is.False);
            Assert.That(File.ReadAllText(FileAt("Assets/A/Data/tbthing.json")), Is.EqualTo(previous));
        }
        [Test] public void RemovesOwnedStaleFilesAndMetasWithoutTouchingHandwrittenNeighbors()
        {
            Assert.That(Run(Task("A", new TablesExtensionProcessor(), new AddProvider())).Succeeded, Is.True);
            File.WriteAllText(FileAt("Assets/A/Code/ProjectProvider.cs.meta"), "guid: stale");
            File.WriteAllText(FileAt("Assets/A/Code/Handwritten.cs"), "// manual");
            Assert.That(Run(Task()).Succeeded, Is.True);
            Assert.That(File.Exists(FileAt("Assets/A/Code/ProjectProvider.cs")), Is.False);
            Assert.That(File.Exists(FileAt("Assets/A/Code/ProjectProvider.cs.meta")), Is.False);
            Assert.That(File.ReadAllText(FileAt("Assets/A/Code/Handwritten.cs")), Is.EqualTo("// manual"));
        }
        [Test] public void MovingConfiguredOutputPreservesUnityGuidAndCleansOldOwnedFiles()
        {
            Assert.That(Run(Task()).Succeeded, Is.True);
            File.WriteAllText(FileAt("Assets/A/Code/Thing.cs.meta"), "guid: stable");
            var task = Task(); task.CodeOutput = "Assets/Moved/Code"; task.DataOutput = "Assets/Moved/Data";
            Assert.That(Run(task).Succeeded, Is.True);
            Assert.That(File.ReadAllText(FileAt("Assets/Moved/Code/Thing.cs.meta")), Is.EqualTo("guid: stable"));
            Assert.That(File.Exists(FileAt("Assets/A/Code/Thing.cs")), Is.False);
            Assert.That(File.Exists(FileAt("Assets/A/Code/Thing.cs.meta")), Is.False);
        }
        [Test] public void RejectsOverlappingModulesBeforeStartingExternalTool()
        {
            var a = Task(); var b = Task("B"); b.CodeOutput = "Assets/A/Code/Child";
            Assert.That(Run(a, b).Succeeded, Is.False);
            Assert.That(_process.Calls, Is.Zero);
        }
        [Test] public void RejectsOutputOutsideAssets()
        {
            var task = Task(); task.CodeOutput = "../Outside";
            Assert.That(Run(task).Succeeded, Is.False);
            Assert.That(_process.Calls, Is.Zero);
        }
        [Test] public void RefusesUnownedDestinationFiles()
        {
            Directory.CreateDirectory(FileAt("Assets/A/Code"));
            File.WriteAllText(FileAt("Assets/A/Code/Thing.cs"), "// manual source");
            Assert.That(Run(Task()).Succeeded, Is.False);
            Assert.That(File.ReadAllText(FileAt("Assets/A/Code/Thing.cs")), Is.EqualTo("// manual source"));
        }
        [Test] public void PublishFailureRestoresEarlierCodeWrites()
        {
            Assert.That(Run(Task()).Succeeded, Is.True);
            var path = FileAt("Assets/A/Data/tbthing.json"); var previous = File.ReadAllText(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            _process.Value = 2;
            Assert.That(Run(Task()).Succeeded, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(previous));
            Assert.That(File.ReadAllText(FileAt("Assets/A/Code/Thing.cs")), Does.Contain("value=1"));
        }
        [Test] public void UsesAllGroupsAndAbsoluteInputPathsInTemporaryConfiguration()
        {
            Assert.That(Run(Task()).Succeeded, Is.True);
            Assert.That(_process.Configuration, Does.Contain("\"name\":\"all\""));
            Assert.That(_process.Configuration.Replace("\\\\", "/"), Does.Contain("Tools/Luban/DataTables/Datas/A"));
            Assert.That(_process.Arguments, Does.Contain("-t all -d json -c cs-simple-json"));
        }
        [Test] public void ProjectProcessorCannotEscapeItsStagedOutput()
        {
            Assert.That(Run(Task("A", new EscapingProcessor())).Succeeded, Is.False);
            Assert.That(File.Exists(FileAt("Assets/A/Code/Tables.cs")), Is.False);
        }

        private sealed class AddProvider : IConfigOutputProcessor
        {
            public void Process(ConfigOutputContext c) => c.WriteCode("ProjectProvider.cs", "// <auto-generated/>\n// " + c.Tables[0].PropertyName + ":" + c.Tables[0].LoadKey);
        }
        private sealed class FailProcessor : IConfigOutputProcessor
        { public void Process(ConfigOutputContext c) => throw new InvalidOperationException("project validation failed"); }
        private sealed class EscapingProcessor : IConfigOutputProcessor
        { public void Process(ConfigOutputContext c) => c.WriteCode("../escape.cs", "bad"); }
        private sealed class FixtureProcess : IConfigProcessRunner
        {
            public bool Fail; public int Value = 1; public int Calls;
            public string Configuration, Arguments;
            public string Run(string executable, string arguments, string directory)
            {
                Calls++;
                if (arguments == "--version") return "8.0";
                if (Fail) throw new InvalidOperationException("process failed");
                Arguments = arguments;
                string Get(string flag) => Regex.Match(arguments, Regex.Escape(flag) + "\"([^\"]+)\"").Groups[1].Value;
                var code = Get("outputCodeDir="); var data = Get("outputDataDir=");
                Configuration = File.ReadAllText(Get("--conf "));
                File.WriteAllText(Path.Combine(code, "Tables.cs"), "// <auto-generated/>\nnamespace cfg { public class Tables {\n TbThing = new TbThing(loader(\"tbthing\"));\n} }");
                File.WriteAllText(Path.Combine(code, "Thing.cs"), "// <auto-generated/> value=" + Value);
                File.WriteAllText(Path.Combine(data, "tbthing.json"), "[{\"value\":" + Value + "}]");
                return "generated";
            }
        }
    }
}
