using System;

namespace July.Guide
{
    /// <summary>Immutable options for one UGUI guide presentation.</summary>
    public sealed class GuideViewData
    {
        public string TextKey { get; }
        public int MaskType { get; }
        public int PointerType { get; }
        public int PlacementType { get; }
        public string ConfirmTextKey { get; }
        public string SkipTextKey { get; }

        public GuideViewData(string textKey = null, int maskType = GuideMaskTypes.None,
            int pointerType = GuidePointerTypes.None, int placementType = GuidePlacementTypes.Center,
            string confirmTextKey = "Confirm", string skipTextKey = "Skip")
        {
            if (maskType != GuideMaskTypes.None && maskType != GuideMaskTypes.Rectangle)
                throw new ArgumentOutOfRangeException(nameof(maskType));
            if (pointerType < GuidePointerTypes.None || pointerType > GuidePointerTypes.Drag)
                throw new ArgumentOutOfRangeException(nameof(pointerType));
            if (placementType < GuidePlacementTypes.Center || placementType > GuidePlacementTypes.Right)
                throw new ArgumentOutOfRangeException(nameof(placementType));
            TextKey = textKey;
            MaskType = maskType;
            PointerType = pointerType;
            PlacementType = placementType;
            ConfirmTextKey = confirmTextKey;
            SkipTextKey = skipTextKey;
        }
    }

    public static class GuideMaskTypes
    {
        public const int None = 0;
        public const int Rectangle = 1;
    }

    public static class GuidePointerTypes
    {
        public const int None = 0;
        public const int Click = 1;
        public const int Drag = 2;
    }

    public static class GuidePlacementTypes
    {
        public const int Center = 0;
        public const int Top = 1;
        public const int Bottom = 2;
        public const int Left = 3;
        public const int Right = 4;
    }

    /// <summary>Only UGUI raycasts. These modes never own keyboard, controller or gameplay input.</summary>
    public static class GuideRaycastModes
    {
        public const int BlockAll = 0;
        public const int BlockOutsideTarget = 1;
        public const int AllowAll = 2;
    }
}
