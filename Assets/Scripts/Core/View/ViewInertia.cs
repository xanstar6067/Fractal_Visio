using System;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>How fast a gesture was moving the view at the moment the fingers lifted.</summary>
    public readonly struct FlingVelocity
    {
        public FlingVelocity(Vector2 panPixelsPerSecond, double zoomLogPerSecond, double rotationPerSecond, Vector2 pivot, float gestureSeconds)
        {
            PanPixelsPerSecond = panPixelsPerSecond;
            ZoomLogPerSecond = zoomLogPerSecond;
            RotationPerSecond = rotationPerSecond;
            Pivot = pivot;
            GestureSeconds = gestureSeconds;
        }

        /// <summary>Finger travel in screen pixels per second, in the same sense as a pan delta.</summary>
        public Vector2 PanPixelsPerSecond { get; }

        /// <summary>ln(finger spread ratio) per second. Positive spreads the fingers, i.e. zooms in.</summary>
        public double ZoomLogPerSecond { get; }

        /// <summary>Radians per second, in the sense of a gesture rotation delta.</summary>
        public double RotationPerSecond { get; }

        /// <summary>Screen point zoom and rotation turn around: where the pinch last was.</summary>
        public Vector2 Pivot { get; }

        /// <summary>How long the gesture had been engaged. A short one is a tap, and needs a harder flick to count.</summary>
        public float GestureSeconds { get; }

        public bool IsZero => PanPixelsPerSecond == Vector2.zero && ZoomLogPerSecond == 0d && RotationPerSecond == 0d;
    }

    /// <summary>Something that knows where the view is going. The renderer uses it to aim a frame at where it will be seen.</summary>
    public interface IViewForecast
    {
        bool IsActive { get; }

        /// <summary>The view <paramref name="seconds"/> from now, starting from <paramref name="current"/>.</summary>
        ViewState Predict(in ViewState current, double seconds);
    }

    /// <summary>
    /// The view coasting after a flick: pan, or zoom and rotation about the pinch point, slowing
    /// down on its own. Ported from the reference app's <c>FadeMoveController</c> and
    /// <c>SmoothMovementDeceleration</c>.
    ///
    /// Speed decays exponentially, <c>v(t) = v0 e^(-kt)</c>, with k chosen so the speed is down to
    /// 0.2% after the configured length. Zoom decays in log space, so a zoom-in slows as a ratio
    /// rather than as a linear amount, which is what makes it feel like one continuous motion.
    /// Because the motion is a closed-form function of time, the future view is <b>known</b>, not
    /// guessed - <see cref="Predict"/> is exact, and a render can be aimed at where the view will
    /// be by the time the frame arrives.
    ///
    /// Pure arithmetic: time comes in as arguments, the view goes out through
    /// <see cref="ViewNavigator"/>, and nothing here reads the screen.
    /// </summary>
    public sealed class ViewInertia : IViewForecast
    {
        /// <summary>Speed left at the end of the configured length. The reference app's figure.</summary>
        private const double ResidualSpeed = 0.002d;

        // Thresholds, in short screen edges per second for pan. A pan must be at least this fast
        // to coast, and a tap - a gesture shorter than TapSeconds - much faster, so lifting a
        // finger after a small adjustment does not send the view drifting.
        private const float TapSeconds = 0.3f;
        private const double PanStart = 0.1d;
        private const double PanStartAfterTap = 0.5d;
        private const double PanStop = 0.02d;
        private const double PanMaximum = 10d;

        // ln(ratio) per second: 1.2x/s to start, 1.02x/s to stop, 100x/s at most.
        private static readonly double ZoomStart = Math.Log(1.2d);
        private static readonly double ZoomStop = Math.Log(1.02d);
        private static readonly double ZoomMaximum = Math.Log(100d);

        // Radians per second.
        private const double RotationStart = 0.05d;
        private const double RotationStop = 0.02d;
        private const double RotationMaximum = 10d;

        private Vector2 panVelocity;
        private double zoomVelocity;
        private double rotationVelocity;
        private Vector2 pivot;
        private Viewport viewport;
        private double decay;
        private double startTime;
        private double lastTime;
        private float zoomExponent = 1f;
        private double minimumScale;
        private double maximumScale;

        public bool IsActive { get; private set; }

        /// <summary>
        /// Start coasting from <paramref name="velocity"/>, or do nothing if it is too slow to be a
        /// flick. <paramref name="lengthSeconds"/> of zero or less disables inertia.
        /// </summary>
        /// <param name="zoomSpeed">The same exponent the live pinch applies, so coasting matches the gesture.</param>
        public bool Start(
            in FlingVelocity velocity,
            in Viewport display,
            double now,
            double lengthSeconds,
            float zoomSpeed,
            double minimumViewScale,
            double maximumViewScale)
        {
            Stop();
            if (!(lengthSeconds > 0d) || velocity.IsZero)
            {
                return false;
            }

            viewport = display;
            var shortEdge = Math.Max(1d, Math.Min(display.Width, display.Height));

            var pan = velocity.PanPixelsPerSecond;
            var panSpeed = pan.magnitude / shortEdge;
            var panStart = velocity.GestureSeconds < TapSeconds ? PanStartAfterTap : PanStart;
            panVelocity = panSpeed >= panStart
                ? pan * (float)(Math.Min(panSpeed, PanMaximum) / panSpeed)
                : Vector2.zero;

            var zoom = velocity.ZoomLogPerSecond;
            zoomVelocity = Math.Abs(zoom) >= ZoomStart ? Math.Clamp(zoom, -ZoomMaximum, ZoomMaximum) : 0d;

            var rotation = velocity.RotationPerSecond;
            rotationVelocity = Math.Abs(rotation) >= RotationStart
                ? Math.Clamp(rotation, -RotationMaximum, RotationMaximum)
                : 0d;

            if (panVelocity == Vector2.zero && zoomVelocity == 0d && rotationVelocity == 0d)
            {
                return false;
            }

            pivot = velocity.Pivot;
            decay = -Math.Log(ResidualSpeed) / lengthSeconds;
            startTime = now;
            lastTime = now;
            zoomExponent = zoomSpeed;
            minimumScale = minimumViewScale;
            maximumScale = maximumViewScale;
            IsActive = true;
            return true;
        }

        public void Stop()
        {
            IsActive = false;
            panVelocity = Vector2.zero;
            zoomVelocity = 0d;
            rotationVelocity = 0d;
        }

        /// <summary>
        /// Move <paramref name="view"/> along the coast from the last step to <paramref name="now"/>.
        /// Returns false once the motion has died out, or was stopped by reaching a zoom limit.
        /// </summary>
        public bool Step(ref ViewState view, double now)
        {
            if (!IsActive)
            {
                return false;
            }

            var t0 = lastTime - startTime;
            var t1 = Math.Max(t0, now - startTime);
            lastTime = now;

            var scaleBefore = view.scale.AsDouble;
            Advance(ref view, t0, t1);

            // Pinned against a zoom limit: nothing more will happen on that axis.
            if (zoomVelocity != 0d && view.scale.AsDouble == scaleBefore)
            {
                zoomVelocity = 0d;
            }

            var remaining = Math.Exp(-decay * t1);
            var shortEdge = Math.Max(1d, Math.Min(viewport.Width, viewport.Height));
            if (panVelocity.magnitude * remaining / shortEdge < PanStop)
            {
                panVelocity = Vector2.zero;
            }

            if (Math.Abs(zoomVelocity) * remaining < ZoomStop)
            {
                zoomVelocity = 0d;
            }

            if (Math.Abs(rotationVelocity) * remaining < RotationStop)
            {
                rotationVelocity = 0d;
            }

            if (panVelocity == Vector2.zero && zoomVelocity == 0d && rotationVelocity == 0d)
            {
                Stop();
            }

            return true;
        }

        public ViewState Predict(in ViewState current, double seconds)
        {
            var result = current;
            if (!IsActive || !(seconds > 0d))
            {
                return result;
            }

            var t0 = lastTime - startTime;
            Advance(ref result, t0, t0 + seconds);
            return result;
        }

        /// <summary>Apply the distance covered between coast times <paramref name="t0"/> and <paramref name="t1"/>.</summary>
        private void Advance(ref ViewState view, double t0, double t1)
        {
            // Integral of e^(-kt) over [t0, t1]: seconds of "initial speed" the interval is worth.
            var travel = (Math.Exp(-decay * t0) - Math.Exp(-decay * t1)) / decay;
            if (!(travel > 0d))
            {
                return;
            }

            if (panVelocity != Vector2.zero)
            {
                ViewNavigator.Pan(ref view, viewport, panVelocity * (float)travel);
            }

            if (zoomVelocity != 0d || rotationVelocity != 0d)
            {
                ViewNavigator.PinchZoomRotate(
                    ref view,
                    viewport,
                    pivot,
                    pivot,
                    (float)Math.Exp(zoomVelocity * travel),
                    zoomExponent,
                    (float)(rotationVelocity * travel),
                    minimumScale,
                    maximumScale);
            }
        }
    }
}
