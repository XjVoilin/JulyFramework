using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.Win32;
using UnityEngine;

namespace July.Font.Authoring
{
    internal sealed class PythonCandidate
    {
        internal readonly string Executable;
        internal readonly string[] Arguments;
        internal PythonCandidate(string executable, params string[] arguments)
        { Executable = executable; Arguments = arguments; }
    }

    internal static class PythonDiscovery
    {
        internal const string Probe = "import importlib.util,json,sys; print(json.dumps({'executable':sys.executable,'major':sys.version_info.major,'minor':sys.version_info.minor,'micro':sys.version_info.micro,'hasPip':importlib.util.find_spec('pip') is not None}))";

        internal static Queue<PythonCandidate> Candidates(List<string> diagnostics)
        {
            var result = new List<PythonCandidate>();
            var windows = Application.platform == RuntimePlatform.WindowsEditor;
            var names = windows ? new[] { "python.exe", "python3.exe", "py.exe" } : new[] { "python3", "python" };
            var paths = new List<string> { Environment.GetEnvironmentVariable("PATH") ?? "" };
            if (windows)
            {
                // A running Unity process may have inherited PATH before Python was installed.
                paths.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "");
                paths.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "");
            }
            foreach (var directory in paths.SelectMany(path => path.Split(Path.PathSeparator)).Where(path => !string.IsNullOrWhiteSpace(path)))
                foreach (var name in names)
                {
                    var path = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')), name);
                    if (!File.Exists(path)) continue;
                    // Skip Microsoft Store aliases, which can open the Store instead of running Python.
                    var aliases = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
                    if (windows && string.Equals(Path.GetDirectoryName(path), aliases, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(name == "py.exe" ? new PythonCandidate(path, "-3") : new PythonCandidate(path));
                }
            if (windows)
            {
                ReadRegisteredPython(result, diagnostics, RegistryHive.CurrentUser, RegistryView.Default);
                ReadRegisteredPython(result, diagnostics, RegistryHive.LocalMachine, RegistryView.Registry64);
                ReadRegisteredPython(result, diagnostics, RegistryHive.LocalMachine, RegistryView.Registry32);
                foreach (var path in new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Launcher", "py.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe") })
                    if (File.Exists(path)) result.Add(new PythonCandidate(path, "-3"));
            }
            else
            {
                foreach (var path in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3" })
                    if (File.Exists(path)) result.Add(new PythonCandidate(path));
            }
            var seen = new HashSet<string>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            return new Queue<PythonCandidate>(result.Where(candidate => seen.Add(candidate.Executable + "\n" + string.Join("\n", candidate.Arguments))));
        }

        private static void ReadRegisteredPython(List<PythonCandidate> result, List<string> diagnostics, RegistryHive hive, RegistryView view)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var python = root.OpenSubKey(@"Software\Python\PythonCore");
                if (python == null) return; // Python registration is optional.
                foreach (var tag in python.GetSubKeyNames())
                {
                    using var install = python.OpenSubKey(tag + @"\InstallPath");
                    if (install == null) continue;
                    var path = install.GetValue("ExecutablePath") as string;
                    if (string.IsNullOrEmpty(path) && install.GetValue("") is string directory)
                        path = Path.Combine(directory, "python.exe");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) result.Add(new PythonCandidate(path));
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is SecurityException)
            {
                diagnostics.Add("无法读取 Python 注册信息：" + exception.Message);
            }
        }
    }
}
