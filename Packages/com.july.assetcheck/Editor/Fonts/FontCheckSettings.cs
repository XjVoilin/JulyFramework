using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace July.AssetCheck
{
    [Serializable]
    public sealed class FontCheckSettings
    {
        public bool initialized;
        public string characterFileGuid = "";
        public List<string> sourceGuids = new();
        public List<string> excludedSourceGuids = new();
        public List<string> scanFolderGuids = new();

        internal void Validate()
        {
            if (sourceGuids == null || excludedSourceGuids == null || scanFolderGuids == null ||
                sourceGuids.Concat(excludedSourceGuids).Concat(scanFolderGuids).Any(guid => !IsGuid(guid)) ||
                sourceGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceGuids.Count ||
                excludedSourceGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != excludedSourceGuids.Count ||
                scanFolderGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != scanFolderGuids.Count ||
                sourceGuids.Intersect(excludedSourceGuids, StringComparer.OrdinalIgnoreCase).Any() ||
                !string.IsNullOrEmpty(characterFileGuid) && !IsGuid(characterFileGuid))
                throw new InvalidDataException("字体检查配置包含无效、重复或同时选中和排除的资源 GUID。");
        }

        private static bool IsGuid(string value) => value != null && Regex.IsMatch(value, "^[a-fA-F0-9]{32}$");
    }
}
