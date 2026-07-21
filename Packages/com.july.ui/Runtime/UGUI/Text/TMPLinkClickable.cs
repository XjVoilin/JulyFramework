using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace July.UI
{
    [RequireComponent(typeof(TMP_Text))]
    public class TMPLinkClickable : MonoBehaviour, IPointerClickHandler
    {
        public event Action<string> OnLinkClicked;

        private TMP_Text _text;
        private Camera _camera;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                _camera = canvas.worldCamera;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, _camera);
            if (linkIndex < 0) return;

            var linkInfo = _text.textInfo.linkInfo[linkIndex];
            string linkId = linkInfo.GetLinkID();
            OnLinkClicked?.Invoke(linkId);
        }
    }
}
