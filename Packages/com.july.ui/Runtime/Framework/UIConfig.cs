using System;
using UnityEngine;

namespace July.UI
{
    [Serializable]
    public struct UIConfig
    {
        [Tooltip("设计分辨率")]
        public Vector2Int DesignResolution;

        [Tooltip("屏幕适配模式：0 = 宽度适配，1 = 高度适配，0~1 之间按比例混合")]
        [Range(0f, 1f)]
        public float ScreenMatchMode;

        [Tooltip("UI 相机渲染深度（需高于主相机，默认 10）")]
        public float UICameraDepth;

        [Tooltip("UI 相机正交大小")]
        public float CameraOrthographicSize;

        [Tooltip("UI 相机近裁剪面")]
        public float CameraNearClip;

        [Tooltip("Canvas 与 UI 相机的距离")]
        public float PlaneDistance;

        [Tooltip("Mask 的透明度")]
        [Range(0f, 1f)]
        public float MaskAlpha;

        public static UIConfig Default => new()
        {
            DesignResolution = new Vector2Int(1080, 1920),
            ScreenMatchMode = 0f,
            UICameraDepth = 10f,
            CameraOrthographicSize = 9.6f,
            CameraNearClip = 0.3f,
            PlaneDistance = 100f,
            MaskAlpha = 0.97f,
        };
    }
}
