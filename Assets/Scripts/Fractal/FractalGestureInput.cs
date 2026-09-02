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
            float rotationDelta,
            Vector2 rotationPivot,
            bool resetRequested)
        {
            IsInteracting = isInteracting;
            Changed = changed;
            PanDelta = panDelta;
            PreviousCenter = previousCenter;
            CurrentCenter = currentCenter;
            ZoomRatio = zoomRatio;
            RotationDelta = rotationDelta;
            RotationPivot = rotationPivot;
            ResetRequested = resetRequested;
        }

        public bool IsInteracting { get; }
        public bool Changed { get; }
        public Vector2 PanDelta { get; }
        public Vector2 PreviousCenter { get; }
        public Vector2 CurrentCenter { get; }
        public float ZoomRatio { get; }

        /// <summary>Radians to add to the view rotation this frame (already past the dead zone).</summary>
        public float RotationDelta { get; }

        /// <summary>Screen-space point the rotation turns around (two-finger midpoint / cursor).</summary>
        public Vector2 RotationPivot { get; }
        public bool ResetRequested { get; }
        public bool HasZoom => ZoomRatio > 0f && Mathf.Abs(ZoomRatio - 1f) > 0.0001f;
        public bool HasRotation => Mathf.Abs(RotationDelta) > 0f;
    }

    /// <summary>
    /// Minimal, allocation-free touch/mouse recognizer for the fractal viewport.
    /// One finger pans, two fingers pan+zoom+rotate, three fingers reset the view.
    /// Rotation only engages after the twist passes a dead zone, so an incidental
    /// twist during a pinch does not spin the fractal.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FractalGestureInput : MonoBehaviour
    {
        private const float MinimumPinchDistance = 0.01f;
        private const float RotationDeadzoneRadians = 30f * Mathf.Deg2Rad;
        private const float KeyboardRotateSpeed = 1.2f; // rad/s for the Q/E desktop test path

        private Vector2 previousMousePosition;
        private bool mouseWasPressed;
        private float twistAccumulator;
        private bool rotationEngaged;

        public FractalGestureFrame Current { get; private set; }

        private void Update()
        {
            if (Input.touchCount < 2)
            {
                twistAccumulator = 0f;
                rotationEngaged = false;
            }

            Current = Input.touchCount > 0 ? ReadTouches() : ReadMouse();
        }

        private FractalGestureFrame ReadTouches()
        {
            var touchCount = Input.touchCount;
            if (touchCount >= 3)
            {
                var reset = Input.GetTouch(2).phase == TouchPhase.Began;
                return new FractalGestureFrame(
                    true, reset, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, reset);
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

                var rawTwist = ShortestAngle(
                    Mathf.Atan2(previousSecond.y - previousFirst.y, previousSecond.x - previousFirst.x),
                    Mathf.Atan2(second.position.y - first.position.y, second.position.x - first.position.x));

                if (!rotationEngaged)
                {
                    twistAccumulator += rawTwist;
                    if (Mathf.Abs(twistAccumulator) >= RotationDeadzoneRadians)
                    {
                        rotationEngaged = true;
                    }
                }

                var rotationDelta = rotationEngaged ? rawTwist : 0f;
                var changed = (currentCenter - previousCenter).sqrMagnitude > 0.01f ||
                              Mathf.Abs(zoomRatio - 1f) > 0.0001f ||
                              Mathf.Abs(rotationDelta) > 0f;

                return new FractalGestureFrame(
                    true,
                    changed,
                    Vector2.zero,
                    previousCenter,
                    currentCenter,
                    zoomRatio,
                    rotationDelta,
                    currentCenter,
                    false);
            }

            var touch = Input.GetTouch(0);
            var moved = touch.phase == TouchPhase.Moved && touch.deltaPosition.sqrMagnitude > 0.01f;
            return new FractalGestureFrame(
                true, moved, touch.deltaPosition, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false);
        }

        private FractalGestureFrame ReadMouse()
        {
            var position = (Vector2)Input.mousePosition;
            var pressed = Input.GetMouseButton(0);
            var panDelta = pressed && mouseWasPressed ? position - previousMousePosition : Vector2.zero;
            var scroll = Input.mouseScrollDelta.y;
            var zoomRatio = Mathf.Abs(scroll) > 0.001f ? Mathf.Exp(scroll * 0.18f) : 1f;

            var keyRotate = 0f;
            if (Input.GetKey(KeyCode.Q))
            {
                keyRotate += KeyboardRotateSpeed * Time.unscaledDeltaTime;
            }

            if (Input.GetKey(KeyCode.E))
            {
                keyRotate -= KeyboardRotateSpeed * Time.unscaledDeltaTime;
            }

            var reset = Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(2);
            var changed = panDelta.sqrMagnitude > 0.01f ||
                          Mathf.Abs(scroll) > 0.001f ||
                          Mathf.Abs(keyRotate) > 0f ||
                          reset;

            previousMousePosition = position;
            mouseWasPressed = pressed;

            return new FractalGestureFrame(
                pressed || Mathf.Abs(keyRotate) > 0f,
                changed,
                panDelta,
                position,
                position,
                zoomRatio,
                keyRotate,
                position,
                reset);
        }

        private static float ShortestAngle(float fromRadians, float toRadians)
        {
            var delta = toRadians - fromRadians;
            while (delta > Mathf.PI)
            {
                delta -= 2f * Mathf.PI;
            }

            while (delta < -Mathf.PI)
            {
                delta += 2f * Mathf.PI;
            }

            return delta;
        }
    }
}
