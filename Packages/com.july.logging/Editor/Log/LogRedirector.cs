#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace July.Logging.Editor
{
    /// <summary>
    /// 日志跳转重定向器
    /// 双击 Console 日志时，自动跳转到实际调用位置，而不是日志封装层
    /// </summary>
    internal sealed class LogRedirector
    {
        private static LogRedirector _instance;
        private static LogRedirector Instance => _instance ??= new LogRedirector();

        // 需要跳过的日志封装文件（可根据项目需要扩展）
        private static readonly string[] SkipFileNames =
        {
            "JLogger.cs",
            "ModuleBase.cs",
            "ProviderBase.cs"
        };

        // 反射成员缓存
        private readonly Type _consoleWindowType;
        private readonly FieldInfo _activeTextInfo;
        private readonly FieldInfo _consoleWindowFieldInfo;
        private readonly MethodInfo _setActiveEntryMethod;
        private readonly object[] _setActiveEntryArgs;

        private LogRedirector()
        {
            _consoleWindowType = Type.GetType("UnityEditor.ConsoleWindow,UnityEditor");
            if (_consoleWindowType == null) return;

            _activeTextInfo = _consoleWindowType.GetField("m_ActiveText",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _consoleWindowFieldInfo = _consoleWindowType.GetField("ms_ConsoleWindow",
                BindingFlags.Static | BindingFlags.NonPublic);
            _setActiveEntryMethod = _consoleWindowType.GetMethod("SetActiveEntry",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _setActiveEntryArgs = new object[] { null };
        }

        /// <summary>
        /// 拦截资源打开事件
        /// </summary>
        // [OnOpenAsset(0)]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            var instance = EditorUtility.InstanceIDToObject(instanceID);
            if (instance == null) return false;

            var assetPath = AssetDatabase.GetAssetOrScenePath(instance);

            // 只处理 C# 文件
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".cs"))
            {
                return false;
            }

            return Instance.TryRedirect();
        }

        /// <summary>
        /// 尝试重定向到实际调用位置
        /// </summary>
        private bool TryRedirect()
        {
            var stackTrace = GetStackTrace();
            if (string.IsNullOrEmpty(stackTrace))
            {
                return false;
            }

            // 检查是否是我们的日志系统产生的日志
            if (!IsOurLogSystem(stackTrace))
            {
                return false;
            }

            // 解析堆栈跟踪，找到实际调用位置
            var lines = stackTrace.Split('\n');

            foreach (var line in lines)
            {
                // 查找包含 " (at " 的行（堆栈跟踪格式）
                if (!line.Contains(" (at "))
                {
                    continue;
                }

                // 跳过封装文件
                if (ShouldSkipLine(line))
                {
                    continue;
                }

                // 尝试打开这个文件
                if (TryOpenScriptAsset(line))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查是否是我们的日志系统
        /// </summary>
        private static bool IsOurLogSystem(string stackTrace)
        {
            foreach (var skipFile in SkipFileNames)
            {
                if (stackTrace.Contains(skipFile))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查是否应该跳过此行
        /// </summary>
        private static bool ShouldSkipLine(string line)
        {
            foreach (var skipFile in SkipFileNames)
            {
                if (line.Contains(skipFile))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 尝试打开脚本文件
        /// </summary>
        private bool TryOpenScriptAsset(string line)
        {
            // 解析路径：格式为 "MethodName (at Assets/Path/File.cs:123)"
            if (!TryParseStackTraceLine(line, out var filePath, out var lineNumber))
            {
                return false;
            }

            // 尝试加载并打开文件
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath);
            if (script != null)
            {
                ClearActiveEntry();
                AssetDatabase.OpenAsset(script, lineNumber);
                return true;
            }

            // 备用：尝试作为通用 Object 加载
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath);
            if (asset != null)
            {
                ClearActiveEntry();
                AssetDatabase.OpenAsset(asset, lineNumber);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 解析堆栈跟踪行，提取文件路径和行号
        /// </summary>
        private static bool TryParseStackTraceLine(string line, out string filePath, out int lineNumber)
        {
            filePath = null;
            lineNumber = 0;

            // 格式: "MethodName (at Assets/Path/File.cs:123)"
            const string marker = " (at ";
            int startIndex = line.IndexOf(marker, StringComparison.Ordinal);
            if (startIndex < 0) return false;

            startIndex += marker.Length;

            int endIndex = line.LastIndexOf(')');
            if (endIndex < startIndex) return false;

            // 提取 "Assets/Path/File.cs:123"
            var pathWithLine = line.Substring(startIndex, endIndex - startIndex);

            // 找到 .cs: 来定位行号（避免 Windows 盘符问题）
            int csColonIndex = pathWithLine.LastIndexOf(".cs:", StringComparison.Ordinal);
            if (csColonIndex < 0) return false;

            // 文件路径到 .cs 结束
            filePath = pathWithLine.Substring(0, csColonIndex + 3); // +3 包含 ".cs"

            // 行号
            var lineStr = pathWithLine.Substring(csColonIndex + 4); // +4 跳过 ".cs:"
            return int.TryParse(lineStr, out lineNumber);
        }

        /// <summary>
        /// 清除活动条目（防止循环触发）
        /// </summary>
        private void ClearActiveEntry()
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow != null && _setActiveEntryMethod != null)
            {
                _setActiveEntryMethod.Invoke(consoleWindow, _setActiveEntryArgs);
            }
        }

        /// <summary>
        /// 获取当前选中日志的堆栈跟踪
        /// </summary>
        private string GetStackTrace()
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == null || _activeTextInfo == null)
            {
                return string.Empty;
            }

            var value = _activeTextInfo.GetValue(consoleWindow);
            return value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// 获取控制台窗口实例（每次重新获取，避免缓存失效）
        /// </summary>
        private object GetConsoleWindow()
        {
            // 每次都重新获取，因为窗口可能被关闭重开
            return _consoleWindowFieldInfo?.GetValue(null);
        }
    }
}
#endif
