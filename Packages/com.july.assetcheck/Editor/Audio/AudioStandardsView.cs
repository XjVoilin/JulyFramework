using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using static July.AssetCheck.AssetCheckStandardFields;

namespace July.AssetCheck
{
    internal static class AudioStandardsView
    {
        internal static void Build(VisualElement line, PlatformStandards standards, Action<Action<PlatformStandards>> edit)
        {
            AddInt(line, "采样率上限 Hz", standards.audio.maxSampleRate, v => edit(s => s.audio.maxSampleRate = v));
            AddInt(line, "声道数上限", standards.audio.maxChannels, v => edit(s => s.audio.maxChannels = v));
            AddRequired(line, "MiniGame 覆盖", standards.audio.requireMiniGameOverride,
                value => edit(s => s.audio.requireMiniGameOverride = value));
            var loadTypes = new VisualElement();
            loadTypes.AddToClassList("audio-load-types");
            line.Add(loadTypes);
            AddLoadTypes(loadTypes, "BGM 允许加载方式", standards.audio.BgmLoadTypes,
                value => edit(s => s.audio.bgmLoadTypes = value));
            AddLoadTypes(loadTypes, "SFX 允许加载方式", standards.audio.SfxLoadTypes,
                value => edit(s => s.audio.sfxLoadTypes = value));
        }

        private static void AddLoadTypes(VisualElement line, string label, AudioLoadTypes current, Action<AudioLoadTypes> change)
        {
            var field = new EnumFlagsField(label, current)
            {
                tooltip = "按 Project 资源标签 BGM 或 SFX 检查。选择空集合时使用本类别的默认标准。"
            };
            field.RegisterValueChangedCallback(evt => change((AudioLoadTypes)evt.newValue &
                (AudioLoadTypes.DecompressOnLoad | AudioLoadTypes.CompressedInMemory | AudioLoadTypes.Streaming)));
            field.AddToClassList("audio-load-type");
            Add(line, field);
        }
    }
}
