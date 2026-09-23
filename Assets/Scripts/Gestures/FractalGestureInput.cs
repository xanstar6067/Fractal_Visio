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
            bool resetRequested,
            bool touching = false,
            bool hasFling = false,
            FlingVelocity fling = default,
            bool tapped = false,
            Vector2 tapPosition = default)
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
            Touching = touching;
            HasFling = hasFling;
            Fling = fling;
            Tapped = tapped;
            TapPosition = tapPosition;
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

        /// <summary>
        /// A finger (or the mouse button) is down on the picture, moving or not. This - not
        /// <see cref="IsInteracting"/> - is what stops a coasting view: catching it with a still
        /// finger is how the user says "stop here".
        /// </summary>
        public bool Touching { get; }

        /// <summary>The gesture ended this frame while moving; <see cref="Fling"/> says how fast.</summary>
        public bool HasFling { get; }

        public FlingVelocity Fling { get; }

        /// <summary>
        /// One finger (or the mouse button) went down and came up this frame without ever engaging
        /// a drag, and quickly: a tap. The picture has not moved; the interface decides what a tap
        /// means - close a panel, show or hide the controls.
        /// </summary>
        public bool Tapped { get; }

        /// <summary>Screen point of the tap, in the same pixels as the touches.</summary>
        public Vector2 TapPosition { get; }

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
    ///
    /// It also measures how fast an engaged gesture moves over its last
    /// <see cref="VelocityWindowSeconds"/>, and reports that as a fling the frame the gesture ends.
    /// Only the last moment counts: a finger that stopped before lifting flings nothing.
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

        /// <summary>How much of the end of a gesture its fling velocity is measured over. The reference app's figure.</summary>
        private const float VelocityWindowSeconds = 0.1f;

        /// <summary>A press held longer than this is not a tap, even if it never moved.</summary>
        private const float TapMaximumSeconds = 0.3f;

        /// <summary>
        /// Mouse taps are ignored this long after a touch. A phone also reports every touch as a
        /// simulated mouse click, and one tap must not count twice.
        /// </summary>
        private const float MouseAfterTouchSeconds = 0.5f;

        private const int SampleCapacity = 32;

        private enum GestureKind
        {
            None,
            Drag,
            Pinch
        }

        private readonly float[] sampleTime = new float[SampleCapacity];
        private readonly Vector2[] samplePan = new Vector2[SampleCapacity];
        private readonly float[] sampleZoom = new float[SampleCapacity];
        private readonly float[] sampleRotation = new float[SampleCapacity];
        private int sampleHead;
        private int sampleCount;

        private GestureKind engagedKind;
        private float engagedSince;
        private Vector2 lastPivot;

        /// <summary>
        /// After a pinch ends by lifting one finger, the other is ignored until it lifts too. The
        /// zoom is coasting by then, and a leftover finger that starts panning would stop it -
        /// fingers never leave the glass at exactly the same moment.
        /// </summary>
        private bool ignoreUntilRelease;

        private Vector2 previousMousePosition;
        private bool mouseWasPressed;
        private float twistAccumulator;
        private bool rotationEngaged;

        private Vector2 dragTravel;
        private bool dragEngaged;
        private bool pinchEngaged;
        private float pinchTravel;
        private int previousTouchCount;

        // Tap candidates. A touch stays a candidate only while it is the only finger, has not
        // engaged a drag and has not been down too long.
        private bool touchTapCandidate;
        private float touchTapStart;
        private bool mouseTapCandidate;
        private float mouseTapStart;
        private float lastTouchTime = -10f;

        public FractalGestureFrame Current { get; private set; }

        /// <summary>Dead zone in device pixels. A mouse does not shake, so it barely gets one.</summary>
        private static float DragSlop =>
            Application.isMobilePlatform ? ScreenScale.Dp(TouchSlopDp) : 2f;

        private void Update()
        {
            var touchCount = Input.touchCount;
            var fling = default(FractalGestureFrame);

            if (touchCount > 0)
            {
                lastTouchTime = Time.unscaledTime;
            }

            if (touchCount >= 2)
            {
                // A second finger makes it a pinch, whatever happens next.
                touchTapCandidate = false;
            }

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
                if (touchCount < previousTouchCount)
                {
                    fling = EndGesture(touchCount);
                }
                else
                {
                    ClearSamples();
                    engagedKind = GestureKind.None;
                    ignoreUntilRelease = false;
                }

                dragTravel = Vector2.zero;
                dragEngaged = false;
                pinchTravel = 0f;
                pinchEngaged = false;
                previousTouchCount = touchCount;
            }

            if (touchCount == 0)
            {
                ignoreUntilRelease = false;
            }

            if (fling.HasFling)
            {
                Current = fling;
                return;
            }

            if (ignoreUntilRelease)
            {
                Current = default;
                return;
            }

            Current = touchCount > 0 ? ReadTouches() : ReadMouse();
        }

        /// <summary>
        /// Fingers lifted. A drag flings when the last finger leaves; a pinch flings as soon as it
        /// stops being a pinch, and the finger left behind is ignored until it lifts.
        /// </summary>
        private FractalGestureFrame EndGesture(int remainingTouches)
        {
            var kind = engagedKind;
            var ended = kind == GestureKind.Pinch
                ? remainingTouches < 2
                : kind == GestureKind.Drag && remainingTouches == 0;

            if (!ended)
            {
                return default;
            }

            var frame = BuildFling(kind);
            engagedKind = GestureKind.None;
            ClearSamples();

            if (kind == GestureKind.Pinch && remainingTouches > 0 && frame.HasFling)
            {
                ignoreUntilRelease = true;
            }

            return frame;
        }

        private FractalGestureFrame ReadTouches()
        {
            var touchCount = Input.touchCount;
            if (touchCount >= 3)
            {
                var reset = Input.GetTouch(2).phase == TouchPhase.Began;
                return new FractalGestureFrame(
                    reset, reset, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, reset, touching: true);
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
                touchTapCandidate = true;
                touchTapStart = Time.unscaledTime;
            }

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                // Lifted without ever leaving the dead zone, and quickly: a tap, not a drag that
                // happened to be short. A cancelled touch (a system gesture took it) is neither.
                var still = !dragEngaged && (dragTravel + touch.deltaPosition).magnitude < DragSlop;
                var quick = Time.unscaledTime - touchTapStart <= TapMaximumSeconds;
                var tapped = touchTapCandidate && touch.phase == TouchPhase.Ended && still && quick;
                touchTapCandidate = false;
                if (tapped)
                {
                    return Tap(touch.position);
                }
            }

            if (!dragEngaged)
            {
                dragTravel += touch.deltaPosition;
                if (dragTravel.magnitude < DragSlop)
                {
                    // Below the dead zone: the finger is resting, not dragging. Report nothing but
                    // the touch itself, so the renderer keeps the frame it has finished.
                    return Resting();
                }

                // Engage without applying the travel so far - otherwise the picture jumps by the
                // width of the dead zone at the moment the drag starts.
                dragEngaged = true;
                Engage(GestureKind.Drag, touch.position);
                return new FractalGestureFrame(
                    true, false, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false, touching: true);
            }

            AddSample(touch.deltaPosition, 0f, 0f);
            var moved = touch.deltaPosition.sqrMagnitude > 0f;
            return new FractalGestureFrame(
                true, moved, touch.deltaPosition, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false, touching: true);
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
                    return Resting();
                }

                pinchEngaged = true;
                Engage(GestureKind.Pinch, currentCenter);
                return new FractalGestureFrame(
                    true, false, Vector2.zero, currentCenter, currentCenter, 1f, 0f, currentCenter, false, touching: true);
            }

            lastPivot = currentCenter;
            AddSample(Vector2.zero, zoomRatio > 0f ? Mathf.Log(zoomRatio) : 0f, rotationDelta);

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
                false,
                touching: true);
        }

        private FractalGestureFrame ReadMouse()
        {
            var position = (Vector2)Input.mousePosition;
            var pressed = Input.GetMouseButton(0);
            var now = Time.unscaledTime;

            // A click is a tap under the same rules as a finger. Decided before the release below
            // clears the drag state it depends on.
            var tapped = false;
            if (pressed && !mouseWasPressed)
            {
                mouseTapCandidate = now - lastTouchTime > MouseAfterTouchSeconds;
                mouseTapStart = now;
            }
            else if (!pressed && mouseWasPressed)
            {
                tapped = mouseTapCandidate && !dragEngaged && now - mouseTapStart <= TapMaximumSeconds;
                mouseTapCandidate = false;
            }

            if (!pressed)
            {
                if (mouseWasPressed && engagedKind == GestureKind.Drag)
                {
                    var fling = BuildFling(GestureKind.Drag);
                    engagedKind = GestureKind.None;
                    ClearSamples();
                    dragTravel = Vector2.zero;
                    dragEngaged = false;
                    previousMousePosition = position;
                    mouseWasPressed = false;
                    if (fling.HasFling)
                    {
                        return fling;
                    }
                }

                dragTravel = Vector2.zero;
                dragEngaged = false;
            }

            var rawDelta = pressed && mouseWasPressed ? position - previousMousePosition : Vector2.zero;
            var panDelta = Vector2.zero;

            if (pressed)
            {
                if (!mouseWasPressed)
                {
                    ClearSamples();
                    engagedKind = GestureKind.None;
                }

                if (!dragEngaged)
                {
                    dragTravel += rawDelta;
                    if (dragTravel.magnitude >= DragSlop)
                    {
                        dragEngaged = true;
                        Engage(GestureKind.Drag, position);
                    }
                }
                else
                {
                    panDelta = rawDelta;
                    AddSample(rawDelta, 0f, 0f);
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
                reset,
                touching: pressed,
                tapped: tapped,
                tapPosition: position);
        }

        private static FractalGestureFrame Resting() =>
            new(false, false, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false, touching: true);

        private static FractalGestureFrame Tap(Vector2 position) =>
            new(false, false, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false,
                tapped: true, tapPosition: position);

        private void Engage(GestureKind kind, Vector2 pivot)
        {
            engagedKind = kind;
            engagedSince = Time.unscaledTime;
            lastPivot = pivot;
            ClearSamples();
        }

        private void AddSample(Vector2 pan, float zoomLog, float rotation)
        {
            sampleTime[sampleHead] = Time.unscaledTime;
            samplePan[sampleHead] = pan;
            sampleZoom[sampleHead] = zoomLog;
            sampleRotation[sampleHead] = rotation;
            sampleHead = (sampleHead + 1) % SampleCapacity;
            sampleCount = Mathf.Min(SampleCapacity, sampleCount + 1);
        }

        private void ClearSamples()
        {
            sampleHead = 0;
            sampleCount = 0;
        }

        /// <summary>
        /// Velocity over the end of the gesture: the motion inside the window divided by the window.
        /// Dividing by the whole window even when the samples do not fill it is deliberate - a
        /// finger that paused before lifting leaves the window mostly empty and flings little.
        /// </summary>
        private FractalGestureFrame BuildFling(GestureKind kind)
        {
            if (kind == GestureKind.None || sampleCount == 0)
            {
                return default;
            }

            var now = Time.unscaledTime;
            var gestureSeconds = now - engagedSince;
            var window = Mathf.Clamp(gestureSeconds, Time.unscaledDeltaTime, VelocityWindowSeconds);
            if (!(window > 0f))
            {
                return default;
            }

            var pan = Vector2.zero;
            var zoom = 0d;
            var rotation = 0d;
            for (var i = 0; i < sampleCount; i++)
            {
                var index = (sampleHead - 1 - i + SampleCapacity) % SampleCapacity;
                if (now - sampleTime[index] > window)
                {
                    break;
                }

                pan += samplePan[index];
                zoom += sampleZoom[index];
                rotation += sampleRotation[index];
            }

            var velocity = kind == GestureKind.Drag
                ? new FlingVelocity(pan / window, 0d, 0d, lastPivot, gestureSeconds)
                : new FlingVelocity(Vector2.zero, zoom / window, rotation / window, lastPivot, gestureSeconds);

            return velocity.IsZero
                ? default
                : new FractalGestureFrame(false, false, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 0f, Vector2.zero, false,
                    hasFling: true, fling: velocity);
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
