using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// Reports a finger on a rectangle as a point in 0..1 across it: pressed, dragged, released,
    /// tapped. The input half of the palette editor's gradient and colour picker.
    ///
    /// A control inside a scrolling panel takes the drag away from the panel. For one that only
    /// moves sideways (<see cref="Horizontal"/>) that would leave a dead strip the panel cannot be
    /// scrolled by, so a drag that starts mostly vertical is handed to the enclosing
    /// <see cref="ScrollRect"/> instead.
    /// </summary>
    public sealed class DragSurface : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ScrollRect forwardTo;
        private bool dragging;

        /// <summary>Only sideways drags are ours; vertical ones scroll the panel.</summary>
        public bool Horizontal { get; set; }

        /// <summary>The finger went down. For a 2D control that answers at once.</summary>
        public Action<Vector2> Pressed { get; set; }

        /// <summary>The finger moved, after the drag threshold, on a drag this control kept.</summary>
        public Action<Vector2> Dragged { get; set; }

        /// <summary>The finger lifted after a drag this control kept.</summary>
        public Action Released { get; set; }

        /// <summary>The finger lifted without having dragged.</summary>
        public Action<Vector2> Tapped { get; set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragging = false;
            Pressed?.Invoke(Normalized(eventData.position, eventData.pressEventCamera));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!eventData.dragging)
            {
                Tapped?.Invoke(Normalized(eventData.position, eventData.pressEventCamera));
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            forwardTo = null;
            if (Horizontal)
            {
                GetComponentInParent<ScrollRect>()?.OnInitializePotentialDrag(eventData);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            var travel = eventData.position - eventData.pressPosition;
            if (Horizontal && Mathf.Abs(travel.y) > Mathf.Abs(travel.x))
            {
                forwardTo = GetComponentInParent<ScrollRect>();
                if (forwardTo != null)
                {
                    forwardTo.OnBeginDrag(eventData);
                    return;
                }
            }

            dragging = true;
            Dragged?.Invoke(Normalized(eventData.position, eventData.pressEventCamera));
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (forwardTo != null)
            {
                forwardTo.OnDrag(eventData);
                return;
            }

            if (dragging)
            {
                Dragged?.Invoke(Normalized(eventData.position, eventData.pressEventCamera));
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (forwardTo != null)
            {
                forwardTo.OnEndDrag(eventData);
                forwardTo = null;
                return;
            }

            if (dragging)
            {
                dragging = false;
                Released?.Invoke();
            }
        }

        private Vector2 Normalized(Vector2 screenPoint, Camera eventCamera)
        {
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out var local);
            var bounds = rect.rect;
            return new Vector2(
                bounds.width > 0f ? (local.x - bounds.xMin) / bounds.width : 0f,
                bounds.height > 0f ? (local.y - bounds.yMin) / bounds.height : 0f);
        }
    }
}
