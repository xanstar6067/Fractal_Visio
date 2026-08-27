using UnityEngine;

namespace FractalVisio.Fractal
{
    public readonly struct FractalGestureFrame
    {
        public FractalGestureFrame(
            bool isInteracting,
            bool changed,
            Vector2 panDelta,
            Vector2 previousCenter,
            Vector2 currentCenter,
            float zoomRatio,
            bool resetRequested)
        {
            IsInteracting = isInteracting;
            Changed = changed;
            PanDelta = panDelta;
            PreviousCenter = previousCenter;
            CurrentCenter = currentCenter;
            ZoomRatio = zoomRatio;
            ResetRequested = resetRequested;
        }

        public bool IsInteracting { get; }
        public bool Changed { get; }
        public Vector2 PanDelta { get; }
        public Vector2 PreviousCenter { get; }
        public Vector2 CurrentCenter { get; }
        public float ZoomRatio { get; }
        public bool ResetRequested { get; }
        public bool HasZoom => ZoomRatio > 0f && Mathf.Abs(ZoomRatio - 1f) > 0.0001f;
    }

    /// <summary>
    /// Minimal, allocation-free touch/mouse recognizer for the fractal viewport.
    /// One finger pans, two fingers pan+zoom, three fingers reset the view.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FractalGestureInput : MonoBehaviour
    {
        private const float MinimumPinchDistance = 0.01f;
        private Vector2 previousMousePosition;
        private bool mouseWasPressed;

        public FractalGestureFrame Current { get; private set; }

        private void Update()
        {
            Current = Input.touchCount > 0 ? ReadTouches() : ReadMouse();
        }

        private static FractalGestureFrame ReadTouches()
        {
            var touchCount = Input.touchCount;
            if (touchCount >= 3)
            {
                var reset = Input.GetTouch(2).phase == TouchPhase.Began;
                return new FractalGestureFrame(true, reset, Vector2.zero, Vector2.zero, Vector2.zero, 1f, reset);
            }

            if (touchCount == 2)
            {
                var first = Input.GetTouch(0);
                var second = Input.GetTouch(1);
                var currentCenter = (first.position + second.position) * 0.5f;
                var previousFirst = first.position - first.deltaPosition;
                var previousSecond = second.position - second.deltaPosition;
                var previousCenter = (previousFirst + previousSecond) * 0.5f;
                var currentDistance = Vector2.Distance(first.position, second.position);
                var previousDistance = Vector2.Distance(previousFirst, previousSecond);
                var zoomRatio = previousDistance > MinimumPinchDistance
                    ? currentDistance / previousDistance
                    : 1f;
                var changed = (currentCenter - previousCenter).sqrMagnitude > 0.01f ||
                              Mathf.Abs(zoomRatio - 1f) > 0.0001f;

                return new FractalGestureFrame(
                    true,
                    changed,
                    Vector2.zero,
                    previousCenter,
                    currentCenter,
                    zoomRatio,
                    false);
            }

            var touch = Input.GetTouch(0);
            var moved = touch.phase == TouchPhase.Moved && touch.deltaPosition.sqrMagnitude > 0.01f;
            return new FractalGestureFrame(true, moved, touch.deltaPosition, Vector2.zero, Vector2.zero, 1f, false);
        }

        private FractalGestureFrame ReadMouse()
        {
            var position = (Vector2)Input.mousePosition;
            var pressed = Input.GetMouseButton(0);
            var panDelta = pressed && mouseWasPressed ? position - previousMousePosition : Vector2.zero;
            var scroll = Input.mouseScrollDelta.y;
            var zoomRatio = Mathf.Abs(scroll) > 0.001f ? Mathf.Exp(scroll * 0.18f) : 1f;
            var reset = Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(2);
            var changed = panDelta.sqrMagnitude > 0.01f || Mathf.Abs(scroll) > 0.001f || reset;

            previousMousePosition = position;
            mouseWasPressed = pressed;

            return new FractalGestureFrame(
                pressed,
                changed,
                panDelta,
                position,
                position,
                zoomRatio,
                reset);
        }
    }
}
