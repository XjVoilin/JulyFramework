using System;
using System.IO;
using LitJson;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
                    new PointAllocationConnection(1, 3, 1),
                    new PointAllocationConnection(2, 3, 1)
                },
                PointAllocationLayoutDirection.LeftToRight);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Positions[1].x, Is.EqualTo(result.Positions[2].x).Within(0.01f));
            Assert.That(result.Positions[3].x, Is.GreaterThan(result.Positions[1].x));
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
                    new PointAllocationConnection(1, 2, 1),
                    new PointAllocationConnection(2, 1, 1)
                },
                PointAllocationLayoutDirection.TopToBottom);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void Workspace_ContainsOnlyEditorMetadata()
        {
            var workspace = ScriptableObject.CreateInstance<PointAllocationEditorWorkspace>();
            try
            {
                var node = workspace.GetOrCreateNode(1, new Vector2(100f, 200f));
                node.SetLabel("Editor label");
                node.SetNote("Editor note");

                var serialized = EditorJsonUtility.ToJson(workspace);
                Assert.That(serialized, Does.Contain("Editor label"));
                Assert.That(serialized, Does.Not.Contain("initialLevel"));
                Assert.That(serialized, Does.Not.Contain("maxLevel"));
                Assert.That(serialized, Does.Not.Contain("upgradeCosts"));
                Assert.That(serialized, Does.Not.Contain("connections"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [Test]
        public void Document_DeletedNodeId_IsNotReused_AndSerializesToJson()
        {
            var workspace = ScriptableObject.CreateInstance<PointAllocationEditorWorkspace>();
            try
            {
                var graph = new PointAllocationGraph(
                    10,
                    new[] { new PointAllocationNode(1, 1, new[] { 1 }) },
                    Array.Empty<PointAllocationConnection>());
                var document = new PointAllocationEditorDocument(graph, workspace);

                var second = document.AddNode(Vector2.right);
                Assert.That(document.RemoveNode(second.Id), Is.True);
                var third = document.AddNode(Vector2.up);

                Assert.That(second.Id, Is.EqualTo(2));
                Assert.That(third.Id, Is.EqualTo(3));
                var edited = document.CreateGraph();
                var parsed = JsonMapper.ToObject<PointAllocationGraph>(JsonMapper.ToJson(edited));
                PointAllocationGraphValidator.Validate(parsed);
                Assert.That(parsed.Nodes[1].Id, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [Test]
        public void JsonAndWorkspace_SurviveAssetDatabaseRoundTrip()
        {
            const string folder = "Assets/__JulyPointAllocationTests";
            const string jsonPath = folder + "/Graph.json";
            const string workspacePath = folder + "/Graph.PointAllocationEditor.asset";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "__JulyPointAllocationTests");

            try
            {
                var graph = new PointAllocationGraph(
                    12,
                    new[] { new PointAllocationNode(1, 2, new[] { 2, 3 }) },
                    Array.Empty<PointAllocationConnection>());
                File.WriteAllText(
                    Path.GetFullPath(jsonPath),
                    JsonMapper.ToJson(graph));
                AssetDatabase.ImportAsset(jsonPath, ImportAssetOptions.ForceUpdate);
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);

                var workspace = ScriptableObject.CreateInstance<PointAllocationEditorWorkspace>();
                workspace.SetGraphJson(json);
                workspace.GetOrCreateNode(1, new Vector2(33f, 44f));
                AssetDatabase.CreateAsset(workspace, workspacePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(workspacePath, ImportAssetOptions.ForceUpdate);

                var loadedJson = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
                var loadedWorkspace = AssetDatabase.LoadAssetAtPath<PointAllocationEditorWorkspace>(workspacePath);
                var parsed = JsonMapper.ToObject<PointAllocationGraph>(loadedJson.text);
                PointAllocationGraphValidator.Validate(parsed);
                Assert.That(parsed.GraphId, Is.EqualTo(12));
                Assert.That(loadedWorkspace.GraphJson, Is.SameAs(loadedJson));
                Assert.That(loadedWorkspace.Nodes[0].Position, Is.EqualTo(new Vector2(33f, 44f)));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Validator_RejectsDuplicateNodeIdAndDirectedCycle()
        {
            var duplicate = new PointAllocationGraph(
                1,
                new[]
                {
                    new PointAllocationNode(1, 1, new[] { 1 }),
                    new PointAllocationNode(1, 1, new[] { 1 })
                },
                Array.Empty<PointAllocationConnection>());
            var duplicateException = Assert.Throws<ArgumentException>(
                () => PointAllocationGraphValidator.Validate(duplicate));
            Assert.That(duplicateException.Message, Does.Contain("NodeId 1 重复"));

            var cycle = new PointAllocationGraph(
                1,
                new[]
                {
                    new PointAllocationNode(1, 1, new[] { 1 }),
                    new PointAllocationNode(2, 1, new[] { 1 })
                },
                new[]
                {
                    new PointAllocationConnection(1, 2, 1),
                    new PointAllocationConnection(2, 1, 1)
                });
            var cycleException = Assert.Throws<ArgumentException>(
                () => PointAllocationGraphValidator.Validate(cycle));
            Assert.That(cycleException.Message, Does.Contain("存在有向环"));
        }

        [Test]
        public void EditorWindow_CreateGui_DoesNotThrowWithoutSelectedJson()
        {
            var window = ScriptableObject.CreateInstance<PointAllocationEditorWindow>();
            try
            {
                Assert.DoesNotThrow(window.CreateGUI);
                Assert.That(window.rootVisualElement.childCount, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GraphView_DoubleClickEmptyCanvas_CreatesNode()
        {
            var window = ScriptableObject.CreateInstance<PointAllocationEditorWindow>();
            var workspace = ScriptableObject.CreateInstance<PointAllocationEditorWorkspace>();
            try
            {
                window.Show();
                var document = new PointAllocationEditorDocument(
                    new PointAllocationGraph(
                        1,
                        new[] { new PointAllocationNode(1, 1, new[] { 1 }) },
                        Array.Empty<PointAllocationConnection>()),
                    workspace);
                var graphView = new PointAllocationGraphView(window);
                window.rootVisualElement.Add(graphView);
                graphView.Load(document);
                Assert.That(graphView.panel, Is.Not.Null);

                using (var eventData = MouseDownEvent.GetPooled(new Event
                       {
                           type = EventType.MouseDown,
                           button = 0,
                           clickCount = 2,
                           mousePosition = new Vector2(100f, 100f)
                       }))
                {
                    graphView.contentViewContainer.SendEvent(eventData);
                }

                Assert.That(document.Nodes, Has.Count.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(workspace);
                window.Close();
            }
        }
    }
}
