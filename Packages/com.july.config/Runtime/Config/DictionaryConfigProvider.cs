using System;
using System.Collections.Generic;

namespace July.Config
{
    public sealed class DictionaryConfigProvider : IConfigProvider
    {
        private readonly Dictionary<Type, object> _tables;

        public DictionaryConfigProvider(Dictionary<Type, object> tables)
            => _tables = tables ?? new Dictionary<Type, object>();

        public bool TryGetTable<T>(out T table) where T : class
        {
            if (_tables.TryGetValue(typeof(T), out var obj) && obj is T t)
            {
                table = t;
                return true;
            }
            table = null;
            return false;
        }
    }
}
