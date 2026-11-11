using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace July.Font.Authoring
{
    internal sealed class FontToolSession : IDisposable
    {
        private enum Operation { DetectPython, Environment, Install, Check, Subset }
        private readonly FontToolProcess _process = new();
        private readonly string _script, _cache, _dependencies, _requirements, _fontToolsVersion;
        private Operation _operation;
        private string _job, _sourceDirectory, _jobId;
        private Queue<PythonCandidate> _candidates;
        private readonly List<string> _detectionDiagnostics = new();
        private double _probeStarted;
        internal string PythonPath { get; private set; } = "";
        internal string PythonVersion { get; private set; } = "";
        internal bool HasPython => !string.IsNullOrEmpty(PythonPath);
        private bool _cancelRequested;
        internal bool Busy => _process.Running;
        internal bool Ready { get; private set; }
        internal bool HasEnvironment => Directory.Exists(_dependencies);
        internal string Status { get; private set; } = "请先初始化工具环境。";
        internal string Failure { get; private set; } = "";
        internal FontSubsetResult Result { get; private set; }
        internal event Action Changed;
        internal event Action<FontSubsetResult> Completed;

        internal FontToolSession(string packageRoot, string projectRoot)
        {
            _script = Path.Combine(packageRoot, "Tools~", "subset_font.py");
            _requirements = Path.Combine(packageRoot, "Tools~", "requirements.txt");
            _fontToolsVersion = File.ReadAllText(_requirements).Trim().Substring("fonttools==".Length);
            _cache = Path.Combine(projectRoot, "Library", "July", "FontAuthoring");
            _dependencies = Path.Combine(_cache, "dependencies");
            EditorApplication.update += Pump;
        }

        internal void InvalidateResult()
        {
            RequireIdle(); Result = null; Failure = "";
        }

        internal void DetectEnvironment()
        {
            RequireIdle();
            Ready = false; Failure = ""; PythonPath = PythonVersion = ""; _cancelRequested = false;
            _detectionDiagnostics.Clear();
            _candidates = PythonDiscovery.Candidates(_detectionDiagnostics);
            StartNextCandidate();
        }

        private void StartNextCandidate()
        {
            _operation = Operation.DetectPython;
            while (_candidates.Count > 0)
            {
                var candidate = _candidates.Dequeue();
                try
                {
                    _process.Start(candidate.Executable, candidate.Arguments.Concat(new[] { "-I", "-c", PythonDiscovery.Probe }).ToArray());
                    _probeStarted = EditorApplication.timeSinceStartup;
                    Status = "正在自动检测可用 Python…";
                    return;
                }
                catch (Win32Exception exception)
                {
                    _detectionDiagnostics.Add(candidate.Executable + "：" + exception.Message);
                }
            }
            Status = "未检测到可用 Python。";
            Failure = "请安装 Python 3.10+（含 pip），然后点击重新检测。\n" + string.Join("\n", _detectionDiagnostics);
        }

        private void CheckInstalledEnvironment()
        {
            Failure = ""; Ready = false; _cancelRequested = false; _operation = Operation.Environment;
            _process.Start(PythonPath, "-S", _script, "environment", "--dependencies", _dependencies);
            Status = "正在检查工具环境…";
        }

        internal void InitializeEnvironment()
        {
            RequireIdle();
            if (!HasPython) throw new InvalidOperationException("请先等待 Python 自动检测完成，或安装 Python 后重新检测。");
            Directory.CreateDirectory(_dependencies);
            Ready = false; Failure = ""; _cancelRequested = false; _operation = Operation.Install;
            // A pinned wheel: no system site-packages modifications or build subprocesses.
            _process.Start(PythonPath, "-m", "pip", "--isolated", "--disable-pip-version-check", "install",
                "--no-input", "--only-binary=:all:", "--no-deps", "--upgrade", "--target", _dependencies,
                "-r", _requirements);
            Status = "正在下载并初始化 fontTools（需要网络）…";
        }

        internal void Start(FontSubsetRequest request, bool subset)
        {
            RequireIdle();
            if (!Ready) throw new InvalidOperationException("请先检查或初始化工具环境。");
            request.input = Path.GetFullPath(request.input);
            FontSubsetRequest.OutputPath(request.input);
            if (!File.Exists(request.input)) throw new FileNotFoundException("源字体不存在。", request.input);
            if (!string.IsNullOrEmpty(request.characterFile)) request.characterFile = Path.GetFullPath(request.characterFile);
            _jobId = Guid.NewGuid().ToString("N");
            _job = Path.Combine(_cache, "Jobs", _jobId);
            _sourceDirectory = Path.GetDirectoryName(request.input);
            Directory.CreateDirectory(_job);
            request.jobId = _jobId;
            request.cancelFile = Path.Combine(_job, "cancel");
            var requestPath = Path.Combine(_job, "request.json");
            File.WriteAllText(requestPath, JsonUtility.ToJson(request), new UTF8Encoding(false));
            Result = null; Failure = ""; _cancelRequested = false;
            _operation = subset ? Operation.Subset : Operation.Check;
            try { _process.Start(PythonPath, "-S", _script, subset ? "subset" : "check", "--dependencies", _dependencies, "--request", requestPath); }
            finally { if (!_process.Running) CleanupJob(); }
            Status = subset ? "正在裁剪并验证字体…" : "正在预检查字符覆盖…";
        }

        internal void Cancel()
        {
            if (!Busy) return;
            if (_operation == Operation.DetectPython || _operation == Operation.Environment || _operation == Operation.Install)
            {
                _process.Dispose(); Ready = false; Status = "工具环境操作已取消；可重新初始化。";
            }
            else
            {
                File.WriteAllText(Path.Combine(_job, "cancel"), "cancel");
                _cancelRequested = true; Status = "正在取消，等待当前字体处理步骤结束…";
            }
            Changed?.Invoke();
        }

        private void Pump()
        {
            // Broken launchers and interpreter startup hooks can hang; limit only discovery probes.
            if (_operation == Operation.DetectPython && Busy && EditorApplication.timeSinceStartup - _probeStarted > 5)
            {
                _process.Dispose(); _detectionDiagnostics.Add("一个 Python 候选启动超时。");
                StartNextCandidate(); Changed?.Invoke(); return;
            }
            if (!_process.TryComplete(out var code, out var output, out var error)) return;
            FontSubsetResult completed = null;
            try
            {
                if (_operation == Operation.DetectPython)
                {
                    AcceptPythonProbe(code, output, error);
                }
                else if (code != 0)
                {
                    if (_cancelRequested && code == 130) { Status = "已取消，未发布裁剪结果。"; Failure = ""; }
                    else
                    {
                        Failure = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
                        Status = "任务失败。";
                        Debug.LogError("[July Font Authoring] " + Failure);
                    }
                }
                else if (_operation == Operation.Install) { CheckInstalledEnvironment(); }
                else if (_operation == Operation.Environment)
                {
                    var environment = JsonUtility.FromJson<EnvironmentResult>(output);
                    if (environment == null || environment.version != _fontToolsVersion)
                        throw new InvalidDataException("工具环境返回了无效版本信息。");
                    Ready = true; Status = "工具环境就绪。";
                }
                else
                {
                    var result = JsonUtility.FromJson<FontSubsetResult>(output);
                    if (result == null || string.IsNullOrEmpty(result.fingerprint) || result.missing == null ||
                        result.written != (_operation == Operation.Subset))
                        throw new InvalidDataException("字体工具返回了无效结果。");
                    Result = result;
                    Status = result.written ? "裁剪完成，字符保留验证通过。" : "预检查完成。";
                    completed = result;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException)
            {
                Failure = exception.Message; Status = "任务失败。";
                Debug.LogException(exception);
            }
            finally { CleanupJob(); Changed?.Invoke(); }
            if (completed != null) Completed?.Invoke(completed);
        }

        private void AcceptPythonProbe(int code, string output, string error)
        {
            PythonProbe probe = null;
            if (code == 0)
            {
                try { probe = JsonUtility.FromJson<PythonProbe>(output); }
                catch (ArgumentException exception) { _detectionDiagnostics.Add("候选未返回有效 Python 信息：" + exception.Message); }
            }
            else _detectionDiagnostics.Add(string.IsNullOrWhiteSpace(error) ? "一个候选无法启动。" : error.Trim());
            if (probe == null || probe.major != 3 || probe.minor < 10 || !probe.hasPip || !File.Exists(probe.executable))
            {
                if (probe != null) _detectionDiagnostics.Add($"Python {probe.major}.{probe.minor}.{probe.micro}：" +
                    (probe.hasPip ? "需 Python 3.10+。" : "缺少 pip。"));
                StartNextCandidate();
                return;
            }
            PythonPath = probe.executable;
            PythonVersion = $"{probe.major}.{probe.minor}.{probe.micro}";
            if (HasEnvironment) CheckInstalledEnvironment();
            else Status = "已检测到 Python，请初始化 fontTools 工具环境。";
        }

        private void RequireIdle()
        {
            if (Busy) throw new InvalidOperationException("请等待或取消当前字体任务。");
        }

        private void CleanupJob()
        {
            if (_job == null) return;
            var job = _job;
            var sourceDirectory = _sourceDirectory;
            var jobId = _jobId;
            _job = null; _jobId = null; _sourceDirectory = null;
            // Both locations are constructed from this session's GUID, never from a search over user files.
            try
            {
                if (Directory.Exists(sourceDirectory))
                    foreach (var temporary in Directory.GetFiles(sourceDirectory, ".july-font-" + jobId + "-*"))
                        File.Delete(temporary);
                if (Directory.Exists(job)) Directory.Delete(job, true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[July Font Authoring] 任务临时文件清理失败，请检查 " + job + "\n" + exception.Message);
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= Pump;
            _process.Dispose();
            CleanupJob();
        }

        [Serializable] private sealed class EnvironmentResult { public string version; }
        [Serializable] private sealed class PythonProbe
        {
            public string executable;
            public int major, minor, micro;
            public bool hasPip;
        }
    }
}
