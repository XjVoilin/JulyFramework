using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace July.AssetCheck
{
    /// <summary>实现此接口的具体编辑器规则通过无参构造创建，并自动加入对应模块。</summary>
    public interface IAssetCheckRule<TContext> where TContext : AssetCheckContext
    {
        void Check(TContext context);
    }

    /// <summary>为支持保存修改的模块提供可选修复能力。</summary>
    public interface IAssetCheckFix<TContext> : IAssetCheckRule<TContext> where TContext : AssetCheckContext
    {
        void Fix(TContext context, AssetCheckIssue issue);
    }

    /// <summary>检查或修复语义变化时递增。未声明的规则使用版本 1。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AssetCheckRuleVersionAttribute : Attribute
    {
        public int Version { get; }

        public AssetCheckRuleVersionAttribute(int version)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            Version = version;
        }
    }

    internal static class AssetCheckRuleIdentity
    {
        internal static string Id(Type type) => type.Assembly.GetName().Name + ":" + type.FullName;

        internal static string Fingerprint(Type type) => Id(type) + "@" +
            (type.GetCustomAttribute<AssetCheckRuleVersionAttribute>()?.Version ?? 1).ToString(CultureInfo.InvariantCulture);
    }

    internal static class AssetCheckRules<TContext> where TContext : AssetCheckContext
    {
        // 仅共享不可变的规则发现元数据；规则实例归对应模块和工作区所有。
        private static readonly Type[] Types = TypeCache.GetTypesDerivedFrom<IAssetCheckRule<TContext>>()
            .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ThenBy(type => type.Assembly.GetName().Name, StringComparer.Ordinal).ToArray();

        internal static readonly string Fingerprint = string.Join(";", Types.Select(AssetCheckRuleIdentity.Fingerprint));

        internal static IAssetCheckRule<TContext>[] Create()
        {
            foreach (var type in Types)
                if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null) == null)
                    throw new InvalidOperationException("资源检查规则必须提供无参构造：" + type.FullName);
            return Types.Select(type => (IAssetCheckRule<TContext>)Activator.CreateInstance(type, true)).ToArray();
        }

        internal static void Check(IAssetCheckRule<TContext>[] rules, TContext context)
        {
            foreach (var rule in rules)
            {
                context.BindRule(rule);
                rule.Check(context);
            }
            context.BindRule<TContext>(null);
        }

        internal static void Fix(TContext context, AssetCheckIssue issue) =>
            ((IAssetCheckFix<TContext>)issue.Rule).Fix(context, issue);
    }
}
