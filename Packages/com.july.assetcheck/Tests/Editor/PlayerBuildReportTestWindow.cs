using UnityEditor;

namespace July.AssetCheck.Tests
{
    // A concrete, script-backed window that Unity can identify during test cleanup/reload.
    internal sealed class PlayerBuildReportTestWindow : EditorWindow { }
}
