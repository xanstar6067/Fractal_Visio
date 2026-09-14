using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FractalVisio.UI
{
    /// <summary>
    /// Reports the finger lifting off a control. Only that: an <c>EventTrigger</c> would do it too,
    /// but it implements every pointer interface, and claiming drag and scroll events on a slider
    /// row stops the panel around it from scrolling when a drag starts there.
    /// </summary>
    public sealed class PointerUpRelay : MonoBehaviour, IPointerUpHandler
    {
        public Action Released { get; set; }

        public void OnPointerUp(PointerEventData eventData) => Released?.Invoke();
    }
}
