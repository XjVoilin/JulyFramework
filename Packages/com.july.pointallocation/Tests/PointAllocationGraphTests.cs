using System;
using LitJson;
using NUnit.Framework;

namespace July.PointAllocation.Tests
{
    public sealed class PointAllocationGraphTests
    {
        [Test]
        public void LitJson_RoundTrip_UsesGraphAsTheJsonModel()
        {
            var source = new PointAllocationGraph(
                10,
                new[]
                {
                    new PointAllocationNode(1, 2, new[] { 1, 2 }),
                    new PointAllocationNode(2, 1, new[] { 3 })
                },
                new[] { new PointAllocationConnection(1, 2, 2) });

            var json = JsonMapper.ToJson(source);
            Assert.That(json, Does.Contain("\"GraphId\""));
            Assert.That(json, Does.Not.Contain("\"InitialLevel\""));
            Assert.That(json, Does.Not.Contain("_nodes"));
            Assert.That(json, Does.Not.Contain("_incomingConnections"));

            var parsed = JsonMapper.ToObject<PointAllocationGraph>(json);
            Assert.That(parsed.GraphId, Is.EqualTo(10));
            Assert.That(parsed.Nodes[0].MaxLevel, Is.EqualTo(2));
            Assert.That(parsed.Nodes[0].UpgradeCosts, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(parsed.Connections[0].RequiredLevel, Is.EqualTo(2));
        }

        [Test]
        public void LitJson_MalformedJson_ThrowsDirectly()
        {
            Assert.Throws<JsonException>(() =>
                JsonMapper.ToObject<PointAllocationGraph>("{not-json"));
        }

        [Test]
        public void UpgradeCosts_UseCurrentLevelIndexAndAllowZero()
        {
            var graph = new PointAllocationGraph(
                1,
                new[] { new PointAllocationNode(1, 2, new[] { 0, 3 }) },
                Array.Empty<PointAllocationConnection>());

            Assert.That(graph.Nodes[0].UpgradeCosts, Is.EqualTo(new[] { 0, 3 }));
        }
    }
}
