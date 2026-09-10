namespace July.Release.Editor
{
    /// <summary>所有 July 项目遵守的构建目录约定；不在项目资产中重复配置。</summary>
    public static class ReleaseConventions
    {
        public const string HotFixGroup = "HotFix";
        public const string AotMetaGroup = "AOTMeta";
        public const string ExportRoot = "../Build";
        public const string LocalAotArchiveParent = "../AOTBackup";
        public const string CoscliConfig = "Tools/coscli/.cos.yaml";
        public static string CoscliExecutable => BundledCoscli.GetExecutablePath();
    }
}
