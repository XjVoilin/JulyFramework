using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Font.Authoring.Tests
{
    public sealed class FontToolIntegrationTests
    {
        private string _python, _source, _root;

        [SetUp]
        public void SetUp()
        {
            _python = Environment.GetEnvironmentVariable("JULY_FONT_TEST_PYTHON");
            var fixture = Environment.GetEnvironmentVariable("JULY_FONT_TEST_SOURCE");
            if (string.IsNullOrEmpty(_python) || string.IsNullOrEmpty(fixture))
                Assert.Ignore("Set JULY_FONT_TEST_PYTHON and JULY_FONT_TEST_SOURCE to run Python integration tests.");
            _root = Path.GetFullPath("Library/July/FontAuthoring/TestRuns/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _source = Path.Combine(_root, "测试 font.ttf");
            File.Copy(fixture, _source);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private FontToolSession Session() => new(PackageInfo.FindForAssembly(typeof(FontAuthoringWindow).Assembly).resolvedPath, Path.GetFullPath("."));

        private static IEnumerator Wait(FontToolSession session)
        {
            var deadline = EditorApplication.timeSinceStartup + 30;
            while (session.Busy && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(session.Busy, Is.False, "Python process timed out");
            Assert.That(session.Failure, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ProcessArgumentsPreserveQuotesWhitespaceAndUnicode()
        {
            using var process = new FontToolProcess();
            var arguments = new[] { "字体 path", "quote\"inside", "C:\\trailing\\", "A𠮷\nB" };
            process.Start(_python, "-c", "import json,sys; print(json.dumps({'values': sys.argv[1:]}))", arguments[0], arguments[1], arguments[2], arguments[3]);
            var deadline = EditorApplication.timeSinceStartup + 20;
            int code; string output, error;
            while (!process.TryComplete(out code, out output, out error))
            {
                Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Assert.That(code, Is.Zero, error);
            Assert.That(JsonUtility.FromJson<ArgumentResult>(output).values, Is.EqualTo(arguments));
        }

        [UnityTest]
        public IEnumerator PrecheckCanStartSubsetFromCompletionWithoutLosingNextJob()
        {
            using var session = Session();
            session.DetectEnvironment();
            Assert.Throws<InvalidOperationException>(session.DetectEnvironment);
            yield return Wait(session);
            Assert.That(session.Ready, Is.True);
            var request = new FontSubsetRequest { input = _source, useDefault = false, extraText = "A中𠮷" };
            session.Completed += result =>
            {
                if (result.written) return;
                request.fingerprint = result.fingerprint;
                session.Start(request, true);
            };
            session.Start(request, false);
            yield return Wait(session);
            Assert.That(session.Result.written, Is.True);
            Assert.That(session.Result.covered, Is.EqualTo(3));
            Assert.That(File.Exists(FontSubsetRequest.OutputPath(_source)), Is.True);
            Assert.That(File.ReadAllText(FontSubsetRequest.CharacterOutputPath(_source)), Is.EqualTo("A中𠮷"));
            Assert.That(Directory.GetFiles(_root, ".july-font-*"), Is.Empty);
        }

        [UnityTest]
        public IEnumerator CancelDoesNotPublishAndSessionCanRunAgain()
        {
            using var session = Session();
            session.DetectEnvironment();
            yield return Wait(session);
            var request = new FontSubsetRequest { input = _source, useDefault = false, extraText = "A" };
            session.Start(request, true);
            session.Cancel();
            yield return Wait(session);
            Assert.That(session.Status, Does.StartWith("已取消"));
            Assert.That(session.Result, Is.Null);
            Assert.That(File.Exists(FontSubsetRequest.OutputPath(_source)), Is.False);
            session.Start(new FontSubsetRequest { input = _source, useDefault = false, extraText = "B" }, true);
            yield return Wait(session);
            Assert.That(session.Result.written, Is.True);
        }

        [UnityTest]
        public IEnumerator DetectsPythonAndDisplaysActualExecutableWithoutManualConfiguration()
        {
            using var session = Session();
            session.DetectEnvironment();
            yield return Wait(session);
            Assert.That(session.Ready, Is.True);
            Assert.That(session.PythonVersion, Is.Not.Empty);
            Assert.That(Path.IsPathRooted(session.PythonPath), Is.True);
            Assert.That(File.Exists(session.PythonPath), Is.True);
            TestContext.Out.WriteLine("Detected Python " + session.PythonVersion + " at " + session.PythonPath);
        }

        [UnityTest]
        public IEnumerator DetectsPythonEvenWithEmptyInheritedPath()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("Windows installation discovery test");
            var oldPath = Environment.GetEnvironmentVariable("PATH");
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            using var session = Session();
            try
            {
                Environment.SetEnvironmentVariable("PATH", "");
                session.DetectEnvironment();
                yield return Wait(session);
                Assert.That(session.HasPython, Is.True);
                Assert.That(session.Ready, Is.True);
                Assert.That(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Is.EqualTo(userPath));
                Assert.That(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine), Is.EqualTo(machinePath));
            }
            finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
        }

        [UnityTest]
        public IEnumerator DisposeStopsOutstandingProcess()
        {
            var process = new FontToolProcess();
            process.Start(_python, "-c", "import time; time.sleep(30)");
            yield return null;
            process.Dispose();
            Assert.That(process.Running, Is.False);
            process.Dispose();
        }

        [UnityTest]
        public IEnumerator NativeWindowOpensAndCloses()
        {
            FontAuthoringWindow.ShowWindow();
            var window = EditorWindow.GetWindow<FontAuthoringWindow>();
            yield return null;
            yield return null;
            Assert.That(window.titleContent.text, Is.EqualTo("字体裁剪"));
            Assert.That(window.minSize.x, Is.GreaterThanOrEqualTo(440));
            window.Close();
            yield return null;
        }

        [Serializable] private sealed class ArgumentResult { public string[] values; }
    }
}
