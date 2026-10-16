using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    internal sealed class AssetCheckObjectLocation
    {
        internal int[] Hierarchy;
        internal int Component = -1;

        internal static AssetCheckObjectLocation Capture(Object owner)
        {
            var component = owner as Component;
            var gameObject = component != null ? component.gameObject : owner as GameObject;
            if (gameObject == null) return null;
            var indices = new System.Collections.Generic.List<int>();
            for (var transform = gameObject.transform; transform != null; transform = transform.parent)
                indices.Add(transform.GetSiblingIndex());
            indices.Reverse();
            return new AssetCheckObjectLocation { Hierarchy = indices.ToArray(), Component = component != null
                ? Array.IndexOf(gameObject.GetComponents<Component>(), component) : -1 };
        }

        internal Object Resolve(GameObject[] roots)
        {
            // 扫描后目标对象可能被删除，此时交由调用方定位资源文件。
            if (Hierarchy.Length == 0 || Hierarchy[0] >= roots.Length) return null;
            var transform = roots[Hierarchy[0]].transform;
            for (var index = 1; index < Hierarchy.Length; index++)
            {
                if (Hierarchy[index] >= transform.childCount) return null;
                transform = transform.GetChild(Hierarchy[index]);
            }
            if (Component < 0) return transform.gameObject;
            var components = transform.GetComponents<Component>();
            return Component < components.Length && components[Component] != null ? components[Component] : transform.gameObject;
        }
    }

}
