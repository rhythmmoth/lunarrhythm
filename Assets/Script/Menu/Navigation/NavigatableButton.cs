using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YARG.Menu.Navigation
{
    public sealed class NavigatableButton : NavigatableBehaviour
    {
        [SerializeField]
        private Button.ButtonClickedEvent _onClick = new();

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            Confirm();
        }

        public override void Confirm()
        {
            _onClick.Invoke();
        }

        public void RemoveOnClickListeners()
        {
            _onClick.RemoveAllListeners();
        }

        /// <summary>Like <see cref="SetOnClickEvent"/>, but also drops the listeners set in the editor (for copies).</summary>
        public void ReplaceOnClickEvent(UnityAction a)
        {
            _onClick = new Button.ButtonClickedEvent();
            _onClick.AddListener(a);
        }

        public void SetOnClickEvent(UnityAction a)
        {
            _onClick.RemoveAllListeners();
            _onClick.AddListener(a);
        }
    }
}