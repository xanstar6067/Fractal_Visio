using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// Touch and mouse on a <see cref="PlaneMap"/>. One finger - or the left button - points: a tap
    /// puts the point where it landed, a drag carries it along. Two fingers pinch and pan the map,
    /// and so do the wheel and the right or middle button.
    ///
    /// Which of the two a touch is waits until the finger has moved past the touch slop or a second
    /// finger has landed. Deciding on contact would move the point to wherever the first finger of
    /// every pinch came down.
    ///
    /// It implements the drag handlers so the drag is the map's: the panel around it never scrolls
    /// under a finger that is placing the point.
    /// </summary>
    public sealed class PlaneMapInput : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        /// <summary>Travel, in dp, before a press counts as a drag: the gesture layer's touch slop.</summary>
        private const float SlopDp = 8f;

        /// <summary>Zoom per wheel notch.</summary>
        private const float WheelStep = 1.25f;

        private enum Mode
        {
            Idle,
            Pending,
            Pointing,
            Navigating
        }

        private readonly Dictionary<int, Vector2> pointers = new();
        private Mode mode;
        private Vector2 pressPosition;

        /// <summary>The screen point under the pointing finger, while it points - and once for a tap.</summary>
        public Action<Vector2> Pointed;

        /// <summary>The pointing finger lifted, or turned into a pinch: the point stands.</summary>
        public Action Released;

        /// <summary>Move the map: the plane point under the first pivot goes under the second, zoomed by the ratio about it.</summary>
        public Action<Vector2, Vector2, float> Navigated;

        public void OnPointerDown(PointerEventData eventData)
        {
            pointers[eventData.pointerId] = eventData.position;

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                mode = Mode.Navigating;
                return;
            }

            if (pointers.Count == 1)
            {
                if (mode == Mode.Idle)
                {
                    mode = Mode.Pending;
                    pressPosition = eventData.position;
                }

                return;
            }

            // A second finger: a pinch, whatever the first one was doing.
            if (mode == Mode.Pointing)
            {
                Released?.Invoke();
            }

            mode = Mode.Navigating;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // The slop is applied here, per mode; the event system's threshold would only delay a pinch.
            eventData.useDragThreshold = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!pointers.TryGetValue(eventData.pointerId, out var previous))
            {
                return;
            }

            var current = eventData.position;
            pointers[eventData.pointerId] = current;

            switch (mode)
            {
                case Mode.Pending:
                    if ((current - pressPosition).magnitude < Mathf.Max(4f, ScreenScale.Dp(SlopDp)))
                    {
                        return;
                    }

                    mode = Mode.Pointing;
                    Pointed?.Invoke(current);
                    return;

                case Mode.Pointing:
                    Pointed?.Invoke(current);
                    return;

                case Mode.Navigating:
                    Navigate(eventData.pointerId, previous, current);
                    return;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointers.Remove(eventData.pointerId);
            if (pointers.Count > 0)
            {
                return;
            }

            switch (mode)
            {
                case Mode.Pending:
                    // A tap: the point goes where it landed.
                    Pointed?.Invoke(eventData.position);
                    Released?.Invoke();
                    break;

                case Mode.Pointing:
                    Released?.Invoke();
                    break;
            }

            mode = Mode.Idle;
        }

        public void OnScroll(PointerEventData eventData)
        {
            var notches = Mathf.Clamp(eventData.scrollDelta.y, -3f, 3f);
            if (Mathf.Abs(notches) < 0.01f)
            {
                return;
            }

            Navigated?.Invoke(eventData.position, eventData.position, Mathf.Pow(WheelStep, notches));
        }

        private void OnDisable()
        {
            if (mode == Mode.Pointing)
            {
                Released?.Invoke();
            }

            pointers.Clear();
            mode = Mode.Idle;
        }

        /// <summary>
        /// One pointer moved. With another one down this is half a pinch: the pivot is the midpoint,
        /// and the zoom is how the distance between them changed. Alone - a mouse button, or the
        /// finger left after a pinch - it pans.
        /// </summary>
        private void Navigate(int moved, Vector2 previous, Vector2 current)
        {
            foreach (var pair in pointers)
            {
                if (pair.Key == moved)
                {
                    continue;
                }

                var other = pair.Value;
                var before = Vector2.Distance(previous, other);
                var after = Vector2.Distance(current, other);
                var ratio = before > 1f ? after / before : 1f;
                Navigated?.Invoke((previous + other) * 0.5f, (current + other) * 0.5f, ratio);
                return;
            }

            Navigated?.Invoke(previous, current, 1f);
        }
    }
}
