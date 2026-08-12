using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace July.PointAllocation.Editor.Tests
{
    public sealed class PointAllocationEditorTests
    {
        [Test]
        public void LayeredLayout_LeftToRight_PlacesDependenciesInLaterLayer()
        {
            var result = PointAllocationLayeredLayout.Calculate(
                new[]
                {
                    new PointAllocationLayoutNode(1, new Vector2(0f, -100f)),
                    new PointAllocationLayoutNode(2, new Vector2(0f, 100f)),
                    new PointAllocationLayoutNode(3, new Vector2(200f, 0f))
                },
                new[]
                {
                    new PointAllocationConnectionDefinition(1, 3, 1),
                    new PointAllocationConnectionDefinition(2, 3, 1)
                },
                PointAllocationLayoutDirection.LeftToRight);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Positions[1].x, Is.EqualTo(result.Positions[2].x).Within(0.01f));
            Assert.That(result.Positions[3].x, Is.GreaterThan(result.Positions[1].x));
        }

        [Test]
        public void LayeredLayout_TopToBottom_UsesSameLayeringSemantics()
        {
            var result = PointAllocationLayeredLayout.Calculate(
                new[]
                {
                    new PointAllocationLayoutNode(1, Vector2.zero),
                    new PointAllocationLayoutNode(2, Vector2.zero)
                },
                new[] { new PointAllocationConnectionDefinition(1, 2, 1) },
                PointAllocationLayoutDirection.TopToBottom);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Positions[2].y, Is.GreaterThan(result.Positions[1].y));
        }

        [Test]
        public void LayeredLayout_Cycle_ReturnsFailure()
        {
            var result = PointAllocationLayeredLayout.Calculate(
                new[]
                {
                    new PointAllocationLayoutNode(1, Vector2.zero),
                    new PointAllocationLayoutNode(2, Vector2.zero)
                },
                new[]
                {
                    new PointAllocationConnectionDefinition(1, 2, 1),
                    new PointAllocationConnectionDefinition(2, 1, 1)
                },
                PointAllocationLayoutDirection.LeftToRight);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void AuthoringAsset_DeletedNodeId_IsNotReused()
        {
            var authoring = ScriptableObject.CreateInstance<PointAllocationAuthoringAsset>();
            try
            {
                var first = authoring.AddNode(Vector2.zero);
                Assert.That(authoring.RemoveNode(first.Id), Is.True);
                var second = authoring.AddNode(Vector2.one);

                Assert.That(first.Id, Is.EqualTo(1));
                Assert.That(second.Id, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(authoring);
            }
        }

        [Test]
        public void Export_ContainsOnlyRuntimeDefinitionFacts()
        {
            var authoring = ScriptableObject.CreateInstance<PointAllocationAuthoringAsset>();
            var runtimeAsset = ScriptableObject.CreateInstance<PointAllocationGraphDefinitionAsset>();
            try
            {
                var first = authoring.AddNode(new Vector2(100f, 200f));
                first.SetLabel("Editor-only label");
                first.SetLocked(true);
                first.SetMaxRank(2);
                first.SetRankCost(0, 1);
                first.SetRankCost(1, 2);
                var second = authoring.AddNode(new Vector2(500f, 200f));
                Assert.That(authoring.TryAddConnection(first.Id, second.Id, out _), Is.True);

                Assert.That(PointAllocationExporter.Export(
                    authoring,
                    runtimeAsset,
                    out var errors), Is.True, string.Join("\n", errors));
                Assert.That(runtimeAsset.TryCreateDefinition(
                    out var definition,
                    out errors), Is.True, string.Join("\n", errors));

                Assert.That(definition.Nodes, Has.Count.EqualTo(2));
                Assert.That(definition.Nodes[0].MaxRank, Is.EqualTo(2));
                Assert.That(definition.Connections, Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(authoring);
                Object.DestroyImmediate(runtimeAsset);
            }
        }

        [Test]
        public void EditorWindow_CreateGui_DoesNotThrowWithoutSelectedAsset()
        {
            var window = ScriptableObject.CreateInstance<PointAllocationEditorWindow>();
            try
            {
                Assert.DoesNotThrow(window.CreateGUI);
                Assert.That(window.rootVisualElement.childCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ExportedRuntimeAsset_SurvivesAssetDatabaseRoundTrip()
        {
            const string folder = "Assets/__JulyPointAllocationTests";
            const string authoringPath = folder + "/Authoring.asset";
            const string runtimePath = folder + "/Runtime.asset";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "__JulyPointAllocationTests");

            try
            {
                var authoring = ScriptableObject.CreateInstance<PointAllocationAuthoringAsset>();
                AssetDatabase.CreateAsset(authoring, authoringPath);
                var first = authoring.AddNode(Vector2.zero);
                first.SetMaxRank(2);
                first.SetRankCost(0, 2);
                first.SetRankCost(1, 3);
                var second = authoring.AddNode(Vector2.right * 300f);
                Assert.That(authoring.TryAddConnection(first.Id, second.Id, out _), Is.True);

                var runtimeAsset = ScriptableObject.CreateInstance<PointAllocationGraphDefinitionAsset>();
                AssetDatabase.CreateAsset(runtimeAsset, runtimePath);
                Assert.That(PointAllocationExporter.Export(
                    authoring,
                    runtimeAsset,
                    out var errors), Is.True, string.Join("\n", errors));

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(runtimePath, ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<PointAllocationGraphDefinitionAsset>(runtimePath);
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.TryCreateDefinition(
                    out var definition,
                    out errors), Is.True, string.Join("\n", errors));
                Assert.That(definition.Nodes[0].RankCosts, Is.EqualTo(new[] { 2, 3 }));
                Assert.That(definition.Connections, Has.Count.EqualTo(1));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }
    }
}
