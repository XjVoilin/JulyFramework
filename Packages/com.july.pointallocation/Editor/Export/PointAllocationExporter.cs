using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace July.PointAllocation.Editor
{
    internal static class PointAllocationExporter
    {
        public static bool ExportInteractive(
            PointAllocationAuthoringAsset authoring,
            out IReadOnlyList<PointAllocationDefinitionError> errors)
        {
            errors = authoring == null
                ? new[]
                {
                    new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.MissingNodes,
                        "No PointAllocationAuthoringAsset is selected.")
                }
                : authoring.ValidateDefinition();

            if (authoring == null || errors.Count > 0)
                return false;

            var runtimeAsset = authoring.RuntimeAsset;
            if (runtimeAsset == null)
            {
                var authoringPath = AssetDatabase.GetAssetPath(authoring);
                var folder = string.IsNullOrEmpty(authoringPath)
                    ? "Assets"
                    : System.IO.Path.GetDirectoryName(authoringPath)?.Replace('\\', '/');
                var path = EditorUtility.SaveFilePanelInProject(
                    "Export PointAllocation Definition",
                    $"PointAllocationGraphDefinition_{authoring.DefinitionId}",
                    "asset",
                    "Choose the runtime definition output asset.",
                    folder);
                if (string.IsNullOrEmpty(path))
                    return false;

                runtimeAsset = ScriptableObject.CreateInstance<PointAllocationGraphDefinitionAsset>();
                AssetDatabase.CreateAsset(runtimeAsset, path);
                Undo.RecordObject(authoring, "Assign PointAllocation Runtime Asset");
                authoring.SetRuntimeAsset(runtimeAsset);
                EditorUtility.SetDirty(authoring);
            }

            return Export(authoring, runtimeAsset, out errors);
        }

        public static bool Export(
            PointAllocationAuthoringAsset authoring,
            PointAllocationGraphDefinitionAsset runtimeAsset,
            out IReadOnlyList<PointAllocationDefinitionError> errors)
        {
            if (authoring == null || runtimeAsset == null)
            {
                errors = new[]
                {
                    new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.MissingNodes,
                        "Authoring and runtime assets are required.")
                };
                return false;
            }

            var nodes = authoring.CreateNodeDefinitions();
            var connections = authoring.CreateConnectionDefinitions();
            if (!PointAllocationGraphDefinition.TryCreate(
                    authoring.DefinitionId,
                    nodes,
                    connections,
                    out var definition,
                    out errors))
            {
                return false;
            }

            Undo.RecordObject(runtimeAsset, "Export PointAllocation Definition");
            runtimeAsset.ReplaceDefinition(
                definition.Id,
                definition.Nodes,
                definition.Connections);
            EditorUtility.SetDirty(runtimeAsset);
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}

