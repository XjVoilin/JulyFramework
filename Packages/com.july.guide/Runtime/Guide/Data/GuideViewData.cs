using System;

namespace July.Guide
{
    [Serializable]
    public sealed class GuideViewData
    {
        public int Type;
        public int ParamId;
        public int MaskType;
        public int PointerType;
        public string TextKey;
        public int PlacementType;
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

    public static class GuideInputModes
    {
        public const int BlockAll = 0;
        public const int BlockOutsideTarget = 1;
        public const int AllowAll = 2;
    }
}
