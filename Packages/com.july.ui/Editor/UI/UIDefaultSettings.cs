using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
[InitializeOnLoad]
public static partial class UIDefaultSettings
{
    static UIDefaultSettings()
    {
        ObjectFactory.componentWasAdded += OnComponentAdded;
    }

    private static void OnComponentAdded(Component component)
    {
        if (component is Graphic graphic)
        {
            graphic.raycastTarget = false;
        }

        if (component is Selectable)
        {
            var targetGraphic = component.GetComponent<Graphic>();
            if (targetGraphic != null)
            {
                targetGraphic.raycastTarget = true;
            }
        }
    }
}
#endif