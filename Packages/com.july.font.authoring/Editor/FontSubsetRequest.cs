using System;
using System.IO;

namespace July.Font.Authoring
{
    [Serializable]
    internal sealed class FontSubsetRequest
    {
        public string input;
        public bool useDefault = true;
        public string extraText = "";
        public string characterFile = "";
        public string fingerprint = "";
        public bool allowMissing;
        public bool replace;
        public string cancelFile = "";
        public string jobId = "";

        internal static string OutputPath(string source)
        {
            var path = Path.GetFullPath(source);
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择 TTF／OTF 源字体。");
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-subset" + extension);
        }

        internal static string CharacterOutputPath(string source) => Path.ChangeExtension(OutputPath(source), ".charset.txt");
    }

    [Serializable]
    internal sealed class FontSubsetResult
    {
        public string input, output, charsetOutput, fingerprint, missingText;
        public int requested, covered;
        public string[] missing, lost;
        public long originalBytes, outputBytes;
        public bool written;
    }
}
