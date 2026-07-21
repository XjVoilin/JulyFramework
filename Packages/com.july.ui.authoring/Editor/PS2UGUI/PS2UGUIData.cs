using System.Collections.Generic;

namespace July.UI.Authoring.Editor
{
    internal class UnityData
    {
        public UnityCanvas canvas;
        public List<UnityNode> children;
    }

    internal class UnityCanvas
    {
        public int width;
        public int height;
    }

    internal class UnityNode
    {
        public string name = "";
        public string type = "";
        public string spritePath = "";
        public string prefabPath = "";
        public string fillSpritePath = "";
        public float x;
        public float y;
        public float width;
        public float height;
        public string anchorPreset = "middle-center";
        public bool active = true;
        public int order;
        public List<UnityNode> children = new List<UnityNode>();
        public string text = "";
        public float fontSize;
        public string colorHex = "";
        public string alignment = "";
        public int opacity = 100;
        public bool useNativeSize;
        public string imageType = "simple";
        public bool raycastTarget;
        public bool addUIButton;
        public bool addUIButtonEffect;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public float rotationZ;
        public string fillDirection = "horizontal";
        public string strokeColor = "";
        public float strokeWidth;
    }
}
