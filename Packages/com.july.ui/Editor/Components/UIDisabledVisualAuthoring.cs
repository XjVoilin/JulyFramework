using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI.Editor
{
    internal static class UIDisabledVisualAuthoring
    {
        internal static readonly string[] BindingArrays = { "_sprites", "_activeObjects", "_colors", "_textMaterials" };

        internal static List<(Object target, string path)> OwnedProperties(UIDisabledVisual visual)
        {
            var result = new List<(Object, string)>();
            var source = new SerializedObject(visual);
            foreach (var arrayName in BindingArrays)
            {
                var array = source.FindProperty(arrayName);
                for (var i = 0; i < array.arraySize; i++)
                {
                    var target = array.GetArrayElementAtIndex(i).FindPropertyRelative("_target").objectReferenceValue;
                    if (!target) continue;
                    var path = arrayName switch
                    {
                        "_sprites" => "m_Sprite",
                        "_activeObjects" => "m_IsActive",
                        "_colors" => target is TMP_Text ? "m_fontColor" : "m_Color",
                        "_textMaterials" => "m_sharedMaterial",
                        _ => throw new ArgumentOutOfRangeException(nameof(arrayName))
                    };
                    result.Add((target, path));
                }
            }
            return result;
        }

        internal static List<string> ConfigurationErrors(UIDisabledVisual visual)
        {
            var errors = new List<string>();
            visual.CollectConfigurationErrors(errors);
            var properties = new HashSet<(Object, string)>(OwnedProperties(visual));
            var root = visual.transform.root;
            foreach (var other in root.GetComponentsInChildren<UIDisabledVisual>(true))
            {
                if (other == visual) continue;
                foreach (var property in OwnedProperties(other))
                    if (properties.Contains(property))
                        errors.Add($"{property.target.name}.{property.path} is also controlled by UIDisabledVisual on {other.name}.");
            }

            var sourceCount = 0;
            foreach (var button in root.GetComponentsInChildren<UISmartButton>(true))
            {
                if (new SerializedObject(button).FindProperty("_disabledVisual").objectReferenceValue != visual) continue;
                sourceCount++;
                if (button is UISmartButtonGray || button is UISmartButtonSprite)
                    errors.Add($"{button.name} uses a legacy gray/sprite button. Use UISmartButton to avoid overlapping visuals.");
            }
            if (sourceCount > 1) errors.Add("A visual cannot be bound to multiple button state sources.");

            foreach (var property in properties)
            {
                var component = property.Item1 as Component;
                if (!component) continue;
                foreach (var gray in component.GetComponentsInParent<UIGrayGroup>(true))
                    errors.Add($"{component.name} is also controlled by UIGrayGroup on {gray.name}. Remove the overlapping control.");
                foreach (var animator in component.GetComponentsInParent<Animator>(true))
                {
                    if (!animator.runtimeAnimatorController) continue;
                    var path = AnimationUtility.CalculateTransformPath(component.transform, animator.transform);
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    {
                        if (Animates(clip, path, component.GetType(), property.Item2))
                            errors.Add($"{component.name}.{property.Item2} is also animated by {clip.name}.");
                    }
                }
            }
            return errors;
        }

        private static bool Animates(AnimationClip clip, string path, Type targetType, string property)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (Matches(binding, path, targetType, property)) return true;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (Matches(binding, path, targetType, property)) return true;
            return false;
        }

        private static bool Matches(EditorCurveBinding binding, string path, Type type, string property)
            => binding.path == path && binding.type.IsAssignableFrom(type)
                && (binding.propertyName == property || binding.propertyName.StartsWith(property + ".", StringComparison.Ordinal));

        internal static void CaptureNormal(UIDisabledVisual visual)
        {
            var source = new SerializedObject(visual);
            foreach (var arrayName in BindingArrays)
            {
                var array = source.FindProperty(arrayName);
                for (var i = 0; i < array.arraySize; i++)
                {
                    var item = array.GetArrayElementAtIndex(i);
                    var target = item.FindPropertyRelative("_target").objectReferenceValue;
                    if (!target) continue; // 作者可以先配置一部分条目，再采集。
                    var normal = item.FindPropertyRelative("_normal");
                    switch (arrayName)
                    {
                        case "_sprites": normal.objectReferenceValue = ((Image)target).sprite; break;
                        case "_activeObjects": normal.boolValue = ((GameObject)target).activeSelf; break;
                        case "_colors": normal.colorValue = ((Graphic)target).color; break;
                        case "_textMaterials": normal.objectReferenceValue = ((TMP_Text)target).fontSharedMaterial; break;
                    }
                }
            }
            source.ApplyModifiedProperties();
        }
    }

    /// <summary>借助 Unity 动画预览记录临时属性；停止、保存、切换选择和重载前均恢复。</summary>
    [InitializeOnLoad]
    internal static class UIDisabledVisualPreview
    {
        internal static UIDisabledVisual Owner { get; private set; }
        private static bool _ownsAnimationMode;

        static UIDisabledVisualPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
            EditorSceneManager.sceneSaving += (_, _) => Stop();
            PrefabStage.prefabSaving += _ => Stop();
            Undo.undoRedoPerformed += Stop;
        }

        internal static void Show(UIDisabledVisual visual, bool disabled)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Inspector preview is only available outside Play Mode.");
            var errors = UIDisabledVisualAuthoring.ConfigurationErrors(visual);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));

            if (Owner != visual || !_ownsAnimationMode || !AnimationMode.InAnimationMode())
            {
                Stop();
                if (AnimationMode.InAnimationMode())
                    throw new InvalidOperationException("Stop the other animation preview first.");
                AnimationMode.StartAnimationMode();
                _ownsAnimationMode = true;
                Owner = visual;
                try
                {
                    foreach (var property in UIDisabledVisualAuthoring.OwnedProperties(visual))
                        Record(visual, property.target, property.path);
                }
                catch
                {
                    Stop();
                    throw;
                }
            }

            try
            {
                visual.ApplyState(disabled); // 不改组件的初始状态，也不调用按钮或业务。
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch
            {
                Stop();
                throw;
            }
        }

        private static void Record(UIDisabledVisual visual, Object target, string propertyPath)
        {
            var property = new SerializedObject(target).FindProperty(propertyPath);
            if (property.propertyType == SerializedPropertyType.Color)
            {
                foreach (var channel in new[] { "r", "g", "b", "a" })
                    Record(visual, target, propertyPath + "." + channel);
                return;
            }

            var targetTransform = target is GameObject go ? go.transform : ((Component)target).transform;
            var binding = new EditorCurveBinding
            {
                path = AnimationUtility.CalculateTransformPath(targetTransform, visual.transform),
                type = target.GetType(),
                propertyName = propertyPath
            };
            var modification = new PropertyModification { target = target, propertyPath = propertyPath };
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    modification.objectReference = property.objectReferenceValue;
                    break;
                case SerializedPropertyType.Boolean:
                    modification.value = property.boolValue ? "1" : "0";
                    break;
                case SerializedPropertyType.Float:
                    modification.value = property.floatValue.ToString("R", CultureInfo.InvariantCulture);
                    break;
                default: throw new InvalidOperationException($"Unsupported preview property: {propertyPath}.");
            }
            AnimationMode.AddPropertyModification(binding, modification, property.prefabOverride);
        }

        internal static void Stop()
        {
            if (_ownsAnimationMode && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            _ownsAnimationMode = false;
            Owner = null;
            SceneView.RepaintAll();
        }
    }

    internal sealed class UIDisabledVisualPreviewSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            UIDisabledVisualPreview.Stop();
            return paths;
        }
    }
}
