public static class StringExtension
{
    /// <summary>
    /// 规范化路径（统一使用正斜杠）
    /// </summary>
    public static string NormalizePath(this string path)
    {
        return path?.Replace("\\", "/") ?? string.Empty;
    }
}