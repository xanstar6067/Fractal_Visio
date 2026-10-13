using UnityEngine;
using FractalVisio.App;
using FractalVisio.Core;
using FractalVisio.Gestures;

namespace FractalVisio.Bootstrap
{
    /// <summary>
    /// Fingers on a 3D scene, the touch form of the WPF version's CAD navigation:
    /// <list type="bullet">
    /// <item>one finger turns the scene about the point of the surface it went down on - a trackball
    /// - or, over the background, about the point the camera looks at; a flick keeps it turning;</item>
    /// <item>two fingers hold the point between them: spreading approaches it (never through it -
    /// the way left is divided), moving slides the scene with them, twisting rolls it;</item>
    /// <item>three fingers return to the start; the mouse wheel approaches the point under the cursor.</item>
    /// </list>
    /// What is under a finger is found when the gesture begins, by marching one ray on the CPU
    /// (<see cref="ScenePicker"/>), so the held point stays under the finger for the whole gesture.
    ///
    /// Bootstrap is the one assembly that sees both the gestures and the session; the arithmetic is
    /// <see cref="SceneNavigator"/>'s, in Core.
    /// </summary>
    internal sealed class SceneGestures
    {
        /// <summary>
        /// Trackball speed, degrees per dp of finger travel: a swipe across a phone held upright
        /// (about 400 dp) turns the scene about 120 degrees. The WPF figure, 0.35 per mouse pixel,
        /// is about 0.2 per dp - slow for a thumb.
        /// </summary>
        private const float DegreesPerDp = 0.3f;

        private readonly SceneSpin spin = new();

        private ISceneDefinition distanceScene;
        private FractalParameterSet distanceParameters;
        private ISceneDistance distance;

        private Vector3 pivot;
        private bool hasPivot;
        private SceneCamera placed;

        /// <summary>The scene is turning on its own after a flick.</summary>
        public bool IsSpinning => spin.IsActive;

        public void Stop() => spin.Stop();

        /// <summary>
        /// Apply this frame's gesture and spin to the session's camera. Returns whether the camera
        /// was moved by a finger or the spin.
        /// </summary>
        public bool Update(FractalSession session, in FractalGestureFrame gesture, Viewport display, float inertiaSeconds)
        {
            var camera = session.Camera;

            // A finger on the picture catches the spin, and so does any move it did not make - a
            // place, a reset, a bookmark.
            if (spin.IsActive && (gesture.Touching || !camera.Equals(placed)))
            {
                spin.Stop();
            }

            if (gesture.ResetRequested)
            {
                spin.Stop();
                hasPivot = false;
                return false;
            }

            var width = Mathf.Max(1f, display.Width);
            var height = Mathf.Max(1f, display.Height);
            var degreesPerPixel = DegreesPerDp / Mathf.Max(ScreenScale.Dp(1f), 0.01f);
            var moved = false;

            if (gesture.Began)
            {
                hasPivot = true;
                pivot = Pick(session, camera, gesture.Pointer, width, height);
            }

            if (gesture.HasFling)
            {
                var velocity = gesture.Fling.PanPixelsPerSecond * degreesPerPixel;
                if (hasPivot && velocity != Vector2.zero)
                {
                    spin.Start(pivot, velocity, inertiaSeconds);
                }

                hasPivot = false;
            }
            else if (gesture.Changed)
            {
                if (gesture.Pointers == 2 || gesture.HasZoom || gesture.HasRotation)
                {
                    camera = Pinch(session, camera, gesture, width, height);
                    moved = true;
                }
                else if (gesture.PanDelta.sqrMagnitude > 0f)
                {
                    if (!hasPivot)
                    {
                        hasPivot = true;
                        pivot = camera.Target;
                    }

                    camera = SceneNavigator.Orbit(
                        camera, pivot, gesture.PanDelta.x * degreesPerPixel, gesture.PanDelta.y * degreesPerPixel);
                    moved = true;
                }
            }

            if (!gesture.Touching && !gesture.HasFling && gesture.Pointers == 0 && !gesture.HasZoom)
            {
                hasPivot = false;
            }

            if (spin.IsActive)
            {
                spin.Step(ref camera, Time.unscaledDeltaTime);
                moved = true;
            }

            if (moved)
            {
                session.SetCamera(camera);
            }

            placed = session.Camera;
            return moved;
        }

        /// <summary>
        /// Two fingers: the held point follows their middle, their spread approaches it, their twist
        /// rolls about it. The wheel arrives here too - a zoom with no fingers, about the cursor.
        /// </summary>
        private SceneCamera Pinch(
            FractalSession session, SceneCamera camera, in FractalGestureFrame gesture, float width, float height)
        {
            if (!hasPivot || gesture.Pointers == 0)
            {
                // The wheel and the keyboard begin nothing: each notch picks its own point.
                pivot = Pick(session, camera, gesture.Pointers == 0 ? gesture.Pointer : gesture.CurrentCenter, width, height);
                hasPivot = gesture.Pointers != 0;
            }

            var depth = Vector3.Dot(pivot - camera.Position, camera.Forward);
            var slide = gesture.CurrentCenter - gesture.PreviousCenter;
            if (gesture.Pointers == 2 && slide.sqrMagnitude > 0f && depth > 0f)
            {
                // The camera moves, not the scene: the held point stays where it is in the world and
                // therefore follows the fingers on the screen.
                camera = SceneNavigator.Pan(camera, slide, depth, height);
            }

            if (gesture.HasZoom)
            {
                camera = SceneNavigator.Approach(camera, pivot, gesture.ZoomRatio);
            }

            if (gesture.HasRotation)
            {
                camera = SceneNavigator.RollAround(camera, pivot, gesture.RotationDelta * Mathf.Rad2Deg);
            }

            return camera;
        }

        /// <summary>
        /// The point under a screen position: the surface, or over the background the point at the
        /// depth the camera looks at, so turning there turns about what is in front of it.
        /// </summary>
        private Vector3 Pick(FractalSession session, in SceneCamera camera, Vector2 screenPoint, float width, float height)
        {
            var scene = session.Scene;
            var parameters = session.Parameters;
            if (!ReferenceEquals(scene, distanceScene) || !parameters.SameValues(distanceParameters) || distance == null)
            {
                distanceScene = scene;
                distanceParameters = parameters;
                distance = scene?.CreateDistance(parameters);
            }

            if (distance != null && ScenePicker.TryPick(
                    camera, distance, scene.BoundingRadius(parameters), SceneMarching.TraceDistance,
                    screenPoint, width, height, out var hit))
            {
                return hit;
            }

            return SceneNavigator.PointAtDepth(camera, screenPoint, width, height, camera.Distance);
        }
    }
}
