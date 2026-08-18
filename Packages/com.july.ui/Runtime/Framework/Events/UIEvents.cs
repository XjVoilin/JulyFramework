namespace July.UI
{
    public readonly struct UIOpenEvent
    {
        public int WindowId { get; }
        public string WindowName { get; }
        public UILayer Layer { get; }
        public object Data { get; }

        public UIOpenEvent(int windowId, string windowName, UILayer layer, object data)
        {
            WindowId = windowId;
            WindowName = windowName;
            Layer = layer;
            Data = data;
        }
    }

    public readonly struct UICloseEvent
    {
        public int WindowId { get; }
        public string WindowName { get; }
        public UILayer Layer { get; }

        public UICloseEvent(int windowId, string windowName, UILayer layer)
        {
            WindowId = windowId;
            WindowName = windowName;
            Layer = layer;
        }
    }
}
