using System;
using NUnit.Framework;

namespace July.Pooling.Tests
{
    public class PoolSystemTests
    {
        [Test]
        public void DestroyAllPools_ClearsGenericPools()
        {
            var destroyed = 0;
            var system = new PoolSystem();
            var pool = system.CreatePool(() => new PooledItem(),
                onDestroy: _ => destroyed++, initialSize: 2);
            pool.Get();

            system.DestroyAllPools();

            Assert.That(destroyed, Is.EqualTo(2));
            Assert.That(system.GetPool<PooledItem>(), Is.Null);
        }

        [Test]
        public void Statistics_IncludeTypedPoolCounts()
        {
            var system = new PoolSystem();
            system.CreatePool(() => new PooledItem(), initialSize: 1);

            var statistics = system.GetPoolStatistics();

            Assert.That(statistics.ContainsKey(typeof(PooledItem).FullName), Is.True);
        }

        [Test]
        public void MissingFactory_UsesParameterlessConstructor()
        {
            var system = new PoolSystem();
            var pool = system.CreatePool<PooledItem>();
            Assert.That(pool.Get(), Is.Not.Null);
        }

        [Test]
        public void MissingFactory_ReportsTypeWithoutDefaultConstructor()
        {
            var system = new PoolSystem();
            var pool = system.CreatePool<NeedsArgument>();
            Assert.That(() => pool.Get(), Throws.TypeOf<InvalidOperationException>());
        }

        public sealed class PooledItem { }

        public sealed class NeedsArgument
        {
            public NeedsArgument(int value) { }
        }
    }
}
