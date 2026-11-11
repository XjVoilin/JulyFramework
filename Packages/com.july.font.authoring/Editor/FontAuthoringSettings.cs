using UnityEditor;

namespace July.Font.Authoring
{
    // Recent input paths are local workstation state, not project policy.
    [FilePath("Library/July/FontAuthoring/state.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FontAuthoringSettings : ScriptableSingleton<FontAuthoringSettings>
    {
        public string source = "";
        public bool useDefault = true;
        public string extraText = "";
        public string characterFile = "";
        public void Save() => Save(true);
    }
}
