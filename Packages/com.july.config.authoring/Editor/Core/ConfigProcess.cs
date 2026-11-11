using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace July.Config.Authoring
{
    public interface IConfigProcessRunner
    {
        string Run(string executable, string arguments, string workingDirectory);
    }

    public sealed class ConfigProcessRunner : IConfigProcessRunner
    {
        public string Run(string executable, string arguments, string workingDirectory)
        {
            var start = new ProcessStartInfo(executable, arguments)
            {
                WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (Path.IsPathRooted(executable)) start.EnvironmentVariables["DOTNET_ROOT"] = Path.GetDirectoryName(executable);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var log = output.Result + error.Result;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Luban process exited with code {process.ExitCode}.\n{log}");
            return log;
        }

        public static string ResolveDotnet()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                foreach (var path in new[] { "/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet", "/usr/local/bin/dotnet",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet/dotnet") })
                    if (File.Exists(path)) return path;
            }
            return "dotnet";
        }

        internal static string Quote(string path)
        {
            if (path.Contains('"')) throw new ArgumentException("A path cannot contain a quote: " + path);
            return "\"" + path + "\"";
        }
    }
}
