using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// The camera moves of a 3D scene, as pure arithmetic on <see cref="SceneCamera"/>: turn around a
    /// point, slide, approach a point, roll. Ported from the WPF version's <c>Fractal3DCamera</c> and
    /// <c>Fractal3DWindow.Navigation</c>; what was the mouse there is the finger here.
    ///
    /// Screen coordinates are Unity's - pixels from the bottom-left corner, y up - and a ray through a
    /// pixel is built exactly as the shader builds it (<c>SceneCommon.hlsl</c>), so a point picked on
    /// the CPU lies on the ray the picture shows under the finger.
    /// </summary>
    public static class SceneNavigator
    {
        /// <summary>
        /// Turn the camera, and the point it looks at, about a fixed point by a turn given in world
        /// axes. The point stays where it is on the screen.
        /// </summary>
        public static SceneCamera RotateAround(in SceneCamera camera, Vector3 pivot, Quaternion turn)
        {
            return new SceneCamera(
                SceneCamera.Normalize(turn * camera.Orientation),
                camera.Distance,
                pivot + turn * (camera.Target - pivot),
                camera.FieldOfView);
        }

        /// <summary>
        /// The trackball: the scene turns about <paramref name="pivot"/> the way the finger drags it -
        /// right moves the near side right, down moves it down. The axes are the screen's, not the
        /// world's, so the camera passes over a pole without stopping. Degrees; y up, as on screen.
        /// </summary>
        public static SceneCamera Orbit(in SceneCamera camera, Vector3 pivot, float degreesX, float degreesY)
        {
            var turn = Quaternion.AngleAxis(degreesY, camera.Right) * Quaternion.AngleAxis(-degreesX, camera.Up);
            return RotateAround(camera, pivot, turn);
        }

        /// <summary>
        /// Roll about the line through <paramref name="pivot"/> along the view: the picture turns about
        /// that point on the screen. Positive turns the picture clockwise.
        /// </summary>
        public static SceneCamera RollAround(in SceneCamera camera, Vector3 pivot, float degrees)
        {
            return RotateAround(camera, pivot, Quaternion.AngleAxis(-degrees, camera.Forward));
        }

        /// <summary>
        /// The same line of sight without roll: the top of the screen faces the world's up again. A
        /// camera turned over a pole turns half round about the line of sight.
        /// </summary>
        public static SceneCamera Level(in SceneCamera camera)
        {
            var (yaw, pitch, _) = camera.Angles();
            return camera.WithOrientation(SceneCamera.Orient(yaw, pitch, 0f));
        }

        /// <summary>
        /// Slide the camera and its target across the screen so that a point at depth
        /// <paramref name="depth"/> follows the finger by <paramref name="delta"/> pixels.
        /// </summary>
        public static SceneCamera Pan(in SceneCamera camera, Vector2 delta, float depth, float screenHeight)
        {
            var unit = WorldUnitsPerPixel(depth, camera.FieldOfView, screenHeight);
            return camera.WithTarget(camera.Target - (camera.Right * delta.x + camera.Up * delta.y) * unit);
        }

        /// <summary>
        /// Approach <paramref name="pivot"/> - or back away from it, for a ratio below one - by
        /// <paramref name="ratio"/>: the camera moves along the line to the point, which therefore
        /// stays put on the screen and can be approached for ever but never passed through, since the
        /// way left is divided rather than subtracted. The target moves onto the point's depth, so the
        /// camera's distance is what is left of the way to it - the WPF wheel zoom.
        /// </summary>
        public static SceneCamera Approach(in SceneCamera camera, Vector3 pivot, float ratio)
        {
            if (!(ratio > 0f) || float.IsInfinity(ratio))
            {
                return camera;
            }

            var forward = camera.Forward;
            var position = camera.Position;
            var depth = Vector3.Dot(pivot - position, forward);
            if (!(depth > SceneCamera.MinimumDistance * 0.5f))
            {
                return camera;
            }

            var nextDepth = Mathf.Clamp(depth / ratio, SceneCamera.MinimumDistance, SceneCamera.MaximumDistance);
            var keep = nextDepth / depth;
            var nextPosition = pivot + (position - pivot) * keep;
            return new SceneCamera(camera.Orientation, nextDepth, nextPosition + forward * nextDepth, camera.FieldOfView);
        }

        /// <summary>The point at <paramref name="depth"/> along the view ray through a screen point.</summary>
        public static Vector3 PointAtDepth(in SceneCamera camera, Vector2 screenPoint, float width, float height, float depth)
        {
            var ray = PixelRay(camera, screenPoint, width, height);
            return camera.Position + ray * (depth / Mathf.Max(Vector3.Dot(ray, camera.Forward), 1e-3f));
        }

        /// <summary>
        /// Unit direction of the ray through a screen point. The screen is two units high at the
        /// field-of-view scale ahead of the camera - the same formula as the shader's.
        /// </summary>
        public static Vector3 PixelRay(in SceneCamera camera, Vector2 screenPoint, float width, float height)
        {
            var half = 0.5f * Mathf.Max(height, 1f);
            var planeX = (screenPoint.x - 0.5f * width) / half;
            var planeY = (screenPoint.y - 0.5f * height) / half;
            return (camera.Forward * camera.FieldOfViewScale + camera.Right * planeX + camera.Up * planeY).normalized;
        }

        /// <summary>Where on the screen a point in front of the camera appears - the inverse of <see cref="PixelRay"/>.</summary>
        public static Vector2 Project(in SceneCamera camera, Vector3 point, float width, float height)
        {
            var offset = point - camera.Position;
            var depth = Vector3.Dot(offset, camera.Forward) / camera.FieldOfViewScale;
            var half = 0.5f * Mathf.Max(height, 1f);
            return new Vector2(
                0.5f * width + Vector3.Dot(offset, camera.Right) / depth * half,
                0.5f * height + Vector3.Dot(offset, camera.Up) / depth * half);
        }

        /// <summary>World units one screen pixel spans at a depth: the step that makes a slide follow the finger.</summary>
        public static float WorldUnitsPerPixel(float depth, float fieldOfView, float screenHeight)
        {
            return 2f * Mathf.Max(depth, SceneCamera.MinimumDistance) *
                   Mathf.Tan(Mathf.Clamp(fieldOfView, 5f, 160f) * Mathf.Deg2Rad * 0.5f) / Mathf.Max(screenHeight, 1f);
        }

        /// <summary>
        /// Light direction in world space from a light given the way the relief's is - an azimuth on
        /// the screen (0 is right, 90 is up) and a height above the screen's plane - so the light keeps
        /// its place on the screen as the camera turns, like a lamp on the viewer's shoulder.
        /// </summary>
        public static Vector3 LightDirection(in SceneCamera camera, float azimuth, float height)
        {
            var a = azimuth * Mathf.Deg2Rad;
            var h = Mathf.Clamp(height, -89.9f, 89.9f) * Mathf.Deg2Rad;
            var horizontal = Mathf.Cos(h);
            return (camera.Right * (horizontal * Mathf.Cos(a)) +
                    camera.Up * (horizontal * Mathf.Sin(a)) -
                    camera.Forward * Mathf.Sin(h)).normalized;
        }
    }
}
