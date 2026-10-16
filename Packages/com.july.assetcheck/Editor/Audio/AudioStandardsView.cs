using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
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
        }
    }
}
