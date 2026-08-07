using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace July.RedDot.Tests
{
    [TestFixture]
    public sealed class RedDotKeyPathTests
    {
        private RedDotTreeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<RedDotTreeConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void SameLeafNameUnderDifferentParents_ProducesDistinctRuntimeKeys()
        {
            var shop = Add("Shop");
            var tasks = Add("Tasks");
            var shopClaimable = Add("Claimable", shop.key);
            var taskClaimable = Add("Claimable", tasks.key);

            Assert.AreEqual("Shop/Claimable", _config.GetRuntimeKey(shopClaimable));
            Assert.AreEqual("Tasks/Claimable", _config.GetRuntimeKey(taskClaimable));
            Assert.AreNotEqual(
                _config.GetRuntimeKey(shopClaimable),
                _config.GetRuntimeKey(taskClaimable));
        }

        [Test]
        public void RuntimeDisplayAndCodeRepresentations_AreIndependent()
        {
            var root = Add("GooseDrawEntry");
            var child = Add("GooseDrawMultiple", root.key);

            Assert.AreEqual("GooseDrawEntry/GooseDrawMultiple", _config.GetRuntimeKey(child));
            Assert.AreEqual("GooseDrawEntry › GooseDrawMultiple", _config.GetDisplayPath(child));
            Assert.AreEqual("GooseDrawEntry_GooseDrawMultiple", _config.GetCodeIdentifier(child));
        }

        [Test]
        public void ConfigTable_ExportsCanonicalRuntimeKeys()
        {
            var root = Add("Root");
            Add("Child", root.key);

            var table = _config.ToConfigTable();
            var child = table.Nodes.Single(node => node.Key == "Root/Child");

            Assert.AreEqual("Root", child.ParentKey);
        }

        [Test]
        public void Validate_RejectsRuntimeSeparatorInsideLocalKey()
        {
            Add("Root/Child");

            Assert.That(
                _config.Validate(),
                Has.Some.Contains(RedDotKeyPath.RuntimeSeparator));
        }

        [Test]
        public void Validate_RejectsAmbiguousParentKeys()
        {
            Add("Group");
            Add("Group");
            Add("Leaf", "Group");

            Assert.That(
                _config.Validate(),
                Has.Some.Contains("不唯一"));
        }

        private RedDotNodeDefinition Add(string key, string parentKey = null)
        {
            var node = new RedDotNodeDefinition
            {
                key = key,
                parentKey = parentKey
            };
            _config.nodes.Add(node);
            return node;
        }
    }
}
