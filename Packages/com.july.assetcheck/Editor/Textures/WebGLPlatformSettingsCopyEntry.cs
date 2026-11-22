using UnityEditor;

namespace July.AssetCheck
{
    internal readonly struct PlatformSettingsCopyCell
    {
        internal readonly string Text, Details;
        internal readonly bool NeedsCopy;

        internal PlatformSettingsCopyCell(string text, string details, bool needsCopy = false)
        {
            Text = text;
            Details = details;
            NeedsCopy = needsCopy;
        }
    }

    internal abstract class WebGLPlatformSettingsCopyEntry
    {
        internal string Path { get; }
        internal string Guid { get; }
        internal AssetCheckKind Kind { get; }
        internal string NameSuffix { get; }
        internal abstract bool NeedsCopy { get; }
        internal abstract PlatformSettingsCopyCell Source { get; }
        internal abstract PlatformSettingsCopyCell MiniGame { get; }
        internal abstract PlatformSettingsCopyCell WeixinMiniGame { get; }
        internal abstract void Apply();

        protected WebGLPlatformSettingsCopyEntry(string path, AssetCheckKind kind, string nameSuffix)
        {
            Path = path;
            Guid = AssetDatabase.AssetPathToGUID(path);
            Kind = kind;
            NameSuffix = nameSuffix;
        }

        protected static PlatformSettingsCopyCell SourceCell(bool inherited, string summary, string details) =>
            new PlatformSettingsCopyCell((inherited ? "继承 Default\n" : "WebGL 覆盖\n") + summary, details);

        protected static PlatformSettingsCopyCell TargetCell(bool overridden, bool needsCopy, string summary, string details) =>
            new PlatformSettingsCopyCell((needsCopy ? "待复制" : "一致") +
                (overridden ? "\n" : "（未启用覆盖）\n") + summary, details, needsCopy);
    }
}
