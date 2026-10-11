using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BigWorld.Gameplay
{
    /// <summary>Each control owns one pointer until release or explicit cancellation.</summary>
    public sealed class HunterTouchControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, IInitializePotentialDragHandler
    {
        private readonly HunterPointerStick pointer = new HunterPointerStick();
        public bool IsStick { get; set; }
        public bool Held => pointer.HasPointer;
        public Vector2 Value => new Vector2(pointer.X, pointer.Y);
        public RectTransform Knob { get; set; }
        public Func<bool> AcceptInput;
        public Action Pressed;
        public Text Label { get; set; }

        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !isActiveAndEnabled || (AcceptInput != null && !AcceptInput())) return;
            Vector2 value;
            if (!Read(data, out value) || !pointer.TryPress(data.pointerId, value.x, value.y)) return;
            Refresh(); Pressed?.Invoke();
        }
        public void OnDrag(PointerEventData data)
        {
            Vector2 value;
            if (Read(data, out value) && pointer.TryDrag(data.pointerId, value.x, value.y)) Refresh();
        }
        public void OnPointerUp(PointerEventData data)
        { if (pointer.TryRelease(data.pointerId)) Refresh(); }
        public void Cancel() { pointer.Cancel(); Refresh(); }
        private void OnDisable() { Cancel(); }
        private bool Read(PointerEventData data, out Vector2 value)
        {
            value = Vector2.zero;
            if (!IsStick) return true;
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out value)) return false;
            value = (value - rect.rect.center) / Mathf.Max(1, Mathf.Min(rect.rect.width, rect.rect.height) * .5f);
            return true;
        }
        private void Refresh()
        {
            if (Knob) Knob.anchoredPosition = Value * (((RectTransform)transform).rect.width * .5f - Knob.rect.width * .5f);
            var graphic = GetComponent<Image>();
            if (graphic) graphic.color = Held ? new Color(.18f, .62f, .55f, .9f) : new Color(.07f, .15f, .22f, .88f);
        }
    }
}
