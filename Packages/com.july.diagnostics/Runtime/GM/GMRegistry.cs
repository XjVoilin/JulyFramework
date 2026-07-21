#if JULYGF_DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using July.Logging;

namespace July.Diagnostics
{
    public sealed class GMRegistry
    {
        private readonly List<GMCategoryInfo> _categories = new();
        private readonly HashSet<Type> _registered = new();

        public IReadOnlyList<GMCategoryInfo> Categories => _categories;

        public bool Register(Type type)
        {
            if (!_registered.Add(type))
                return false;

            var categoryAttr = type.GetCustomAttribute<GMCategoryAttribute>();
            if (categoryAttr == null)
            {
                JLogger.LogWarning($"[GM] Type {type.Name} has no [GMCategory] attribute, skipped.");
                _registered.Remove(type);
                return false;
            }

            var category = new GMCategoryInfo
            {
                Category = categoryAttr.Category,
                SourceType = type
            };

            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var cmdAttr = method.GetCustomAttribute<GMCommandAttribute>();
                if (cmdAttr == null) continue;

                var rawName = cmdAttr.DisplayName;
                var slashIdx = rawName.IndexOf('/');

                var cmdInfo = new GMCommandInfo
                {
                    DisplayName = slashIdx > 0 ? rawName.Substring(slashIdx + 1) : rawName,
                    Group = slashIdx > 0 ? rawName.Substring(0, slashIdx) : null,
                    Order = cmdAttr.Order,
                    CloseAfter = cmdAttr.CloseAfter,
                    Method = method,
                    Params = BuildParams(method)
                };
                category.Commands.Add(cmdInfo);
            }

            category.Commands.Sort((a, b) => a.Order.CompareTo(b.Order));
            _categories.Add(category);
            return true;
        }

        public bool Unregister(Type type)
        {
            if (!_registered.Remove(type))
                return false;

            for (int i = _categories.Count - 1; i >= 0; i--)
            {
                if (_categories[i].SourceType == type)
                    _categories.RemoveAt(i);
            }
            return true;
        }

        public void Clear()
        {
            _categories.Clear();
            _registered.Clear();
        }

        private static GMParamInfo[] BuildParams(MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                return Array.Empty<GMParamInfo>();

            var result = new GMParamInfo[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var paramAttr = p.GetCustomAttribute<GMParamAttribute>();

                result[i] = new GMParamInfo
                {
                    DisplayName = paramAttr?.DisplayName ?? p.Name,
                    ParamType = p.ParameterType,
                    DefaultValue = p.HasDefaultValue ? p.DefaultValue : GetDefault(p.ParameterType)
                };
            }
            return result;
        }

        private static object GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
#endif
