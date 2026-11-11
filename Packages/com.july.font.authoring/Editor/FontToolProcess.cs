using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace July.Font.Authoring
{
    internal sealed class FontToolProcess : IDisposable
    {
        private Process _process;
        private Task<string> _output, _error;
        internal bool Running => _process != null;

        internal void Start(string executable, params string[] arguments)
        {
            if (Running) throw new InvalidOperationException("字体工具已有任务正在执行。");
            var quoted = Array.ConvertAll(arguments, Quote);
            var start = new ProcessStartInfo(executable, string.Join(" ", quoted))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["PYTHONUTF8"] = "1";
            start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            _process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Python。");
            _output = _process.StandardOutput.ReadToEndAsync();
            _error = _process.StandardError.ReadToEndAsync();
        }

        internal bool TryComplete(out int code, out string output, out string error)
        {
            code = 0; output = error = "";
            if (!Running || !_process.HasExited || !_output.IsCompleted || !_error.IsCompleted) return false;
            code = _process.ExitCode;
            output = _output.GetAwaiter().GetResult();
            error = _error.GetAwaiter().GetResult();
            _process.Dispose(); _process = null;
            return true;
        }

        internal static string Quote(string value)
        {
            // ProcessStartInfo.Arguments uses command-line parsing, never a command shell.
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character); slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }

        public void Dispose()
        {
            if (!Running) return;
            if (!_process.HasExited)
            {
                try { _process.Kill(); }
                catch (InvalidOperationException) when (_process.HasExited) { }
                _process.WaitForExit();
            }
            _process.Dispose(); _process = null;
        }
    }
}
