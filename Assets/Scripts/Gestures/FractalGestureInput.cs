using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Gestures
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

        /// <summary>
        /// The user is actively moving the view. <b>Not</b> "a finger is on the glass": a resting
        /// finger changes nothing, and treating it as a gesture made the renderer drop to its
        /// coarse pass the instant the screen was touched.
        /// </summary>
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
    ///
    /// Every gesture has to travel past a dead zone before it engages. A finger resting on glass
    /// wanders by several pixels a frame, and without a threshold that jitter reads as a drag: the
    /// view creeps, the renderer restarts, and the picture drops to its coarsest pass just because
    /// the screen was touched. The threshold is in <b>dp</b>, not pixels, for the same reason the
    /// interface is - the same jitter is a handful of pixels on one phone and thirty on another.
    ///
    /// Once engaged, a gesture stays engaged until the fingers lift; the dead zone is a start
    /// condition, not a per-frame filter, or a slow drag would stutter through it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FractalGestureInput : MonoBehaviour
    {
        private const float MinimumPinchDistance = 0.01f;
        private const float RotationDeadzoneRadians = 30f * Mathf.Deg2Rad;
        private const float KeyboardRotateSpeed = 1.2f; // rad/s for the Q/E desktop test path

        /// <summary>Travel before a drag engages. Android's own touch slop is about this.</summary>
        private const float TouchSlopDp = 8f;

        /// <summary>Pinch has to change the finger distance by this fraction before it engages.</summary>
        private const float PinchSlop = 0.02f;

        private Vector2 previousMousePosition;
        private bool mouseWasPressed;
        private float twistAccumulator;
        private bool rotationEngaged;

        private Vector2 dragTravel;
        private bool dragEngaged;
        private bool pinchEngaged;
        private float pinchTravel;
        private int previousTouchCount;

        public FractalGestureFrame Current { get; private set; }

        /// <summary>Dead zone in device pixels. A mouse does not shake, so it barely gets one.</summary>
        private static float DragSlop =>
            Application.isMobilePlatform ? ScreenScale.Dp(TouchSlopDp) : 2f;

        private void Update()
        {
            var touchCount = Input.touchCount;

            if (touchCount < 2)
            {
                twistAccumulator = 0f;
                rotationEngaged = false;
                pinchEngaged = false;
                pinchTravel = 0f;
            }

            // Any change in how many fingers are down starts a new gesture: the centre and the
            // spread jump, and carrying the old engagement across would send the view with them.
            if (touchCount != previousTouchCount)
            {
                dragTravel = Vector2.zero;
                dragEngaged = false;
                pinchTravel = 0f;
                pinchEngaged = false;
                previousTouchCount = touchCount;
            }

            Current = touchCount > 0 ? ReadTouches() : ReadMouse();
        }

        private FractalGestureFrame ReadTouches()
        {
            var touchCount = Input.touchCount;
            if (touchCount >= 3)
            {
                var reset = Input.GetTouch(2).phase == TouchPhase.Began;
                return new FractalGestureFrame(
                    reset, reset, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, reset);
            }

            if (touchCount == 2)
            {
                return ReadPinch();
            }

            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                dragTravel = Vector2.zero;
                dragEngaged = false;
            }

            if (!dragEngaged)
            {
                dragTravel += touch.deltaPosition;
                if (dragTravel.magnitude < DragSlop)
                {
                    // Below the dead zone: the finger is resting, not dragging. Report nothing at
                    // all, so the renderer keeps the frame it has finished.
                    return default;
                }

                // Engage without applying the travel so far - otherwise the picture jumps by the
                // width of the dead zone at the moment the drag starts.
                dragEngaged = true;
                return new FractalGestureFrame(
                    true, false, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false);
            }

            var moved = touch.deltaPosition.sqrMagnitude > 0f;
            return new FractalGestureFrame(
                true, moved, touch.deltaPosition, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false);
        }

        private FractalGestureFrame ReadPinch()
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

            // Screen space is y-up while the view rotation is applied clockwise-positive,
            // so the twist has to be negated for the fractal to follow the fingers.
            var rawTwist = ShortestAngle(
                Mathf.Atan2(second.position.y - first.position.y, second.position.x - first.position.x),
                Mathf.Atan2(previousSecond.y - previousFirst.y, previousSecond.x - previousFirst.x));

            if (!rotationEngaged)
            {
                twistAccumulator += rawTwist;
                if (Mathf.Abs(twistAccumulator) >= RotationDeadzoneRadians)
                {
                    rotationEngaged = true;
                }
            }

            var rotationDelta = rotationEngaged ? rawTwist : 0f;

            if (!pinchEngaged)
            {
                pinchTravel += Mathf.Abs(zoomRatio - 1f);
                dragTravel += currentCenter - previousCenter;

                if (pinchTravel < PinchSlop &&
                    dragTravel.magnitude < DragSlop &&
                    !rotationEngaged)
                {
                    return default;
                }

                pinchEngaged = true;
                return new FractalGestureFrame(
                    true, false, Vector2.zero, currentCenter, currentCenter, 1f, 0f, currentCenter, false);
            }

            var centerMoved = (currentCenter - previousCenter).sqrMagnitude > 0f;
            var changed = centerMoved ||
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

        private FractalGestureFrame ReadMouse()
        {
            var position = (Vector2)Input.mousePosition;
            var pressed = Input.GetMouseButton(0);

            if (!pressed)
            {
                dragTravel = Vector2.zero;
                dragEngaged = false;
            }

            var rawDelta = pressed && mouseWasPressed ? position - previousMousePosition : Vector2.zero;
            var panDelta = Vector2.zero;

            if (pressed)
            {
                if (!dragEngaged)
                {
                    dragTravel += rawDelta;
                    if (dragTravel.magnitude >= DragSlop)
                    {
                        dragEngaged = true;
                    }
                }
                else
                {
                    panDelta = rawDelta;
                }
            }

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
            var changed = panDelta.sqrMagnitude > 0f ||
                          Mathf.Abs(scroll) > 0.001f ||
                          Mathf.Abs(keyRotate) > 0f ||
                          reset;

            previousMousePosition = position;
            mouseWasPressed = pressed;

            return new FractalGestureFrame(
                changed,
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
