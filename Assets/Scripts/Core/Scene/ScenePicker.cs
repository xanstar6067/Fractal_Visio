using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// What is under the finger in a 3D scene: one ray marched on the CPU with the scene's own distance
    /// estimate, the way the shader marches every pixel. The WPF version asks the GPU for a 1x1 frame
    /// and waits for it; one ray of a few hundred steps is far below a millisecond on the CPU, and the
    /// answer is there the frame the finger lands - no gesture has to wait for a read-back.
    ///
    /// Turning, approaching and rolling all happen about this point, so the place the finger holds
    /// stays under it.
    /// </summary>
    public static class ScenePicker
    {
        /// <summary>Steps one ray may take. More than the shader's: a missed pivot is worse than a slow one.</summary>
        private const int MaximumSteps = 400;

        /// <summary>
        /// Rays that run out of steps within this many pixels of the surface count as hitting it - the
        /// shader's grazing-ray rule, so the CPU sees what the picture shows.
        /// </summary>
        private const float GrazingRatio = 16f;

        /// <summary>
        /// The point of the surface the ray through <paramref name="screenPoint"/> meets first, or false
        /// when it leaves into the background. Screen pixels, y up, over a screen
        /// <paramref name="width"/> by <paramref name="height"/>.
        /// </summary>
        public static bool TryPick(
            in SceneCamera camera, ISceneDistance distance, float boundingRadius, float traceDistance,
            Vector2 screenPoint, float width, float height, out Vector3 hit)
        {
            hit = default;
            if (distance == null || width < 1f || height < 1f)
            {
                return false;
            }

            var origin = camera.Position;
            var direction = SceneNavigator.PixelRay(camera, screenPoint, width, height);
            var pixelRadius = 2f / Mathf.Max(height * camera.FieldOfViewScale, 1f);

            // The shader's entry into the bounding sphere, with the same allowance for vertices
            // lying on it.
            var radius = Mathf.Max(boundingRadius, 1e-3f);
            var bound = radius * 1.001f + 2f * pixelRadius * (origin.magnitude + radius);
            var closest = -Vector3.Dot(origin, direction);
            var closestPoint = origin + direction * closest;
            var squared = closestPoint.sqrMagnitude;
            if (squared > bound * bound)
            {
                return false;
            }

            var halfChord = Mathf.Sqrt(Mathf.Max(bound * bound - squared, 0f));
            if (closest + halfChord < 0f)
            {
                return false;
            }

            var travelled = Mathf.Max(0f, closest - halfChord);
            var limit = Mathf.Max(traceDistance, closest + halfChord);
            var bestRatio = float.MaxValue;
            var bestTravelled = travelled;
            var exhausted = true;

            for (var i = 0; i < MaximumSteps; i++)
            {
                var estimate = distance.Estimate(origin + direction * travelled, out var stepScale);
                var epsilon = Mathf.Max(pixelRadius * travelled, 1e-7f);
                var ratio = estimate / epsilon;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestTravelled = travelled;
                }

                if (estimate < epsilon)
                {
                    hit = origin + direction * travelled;
                    return true;
                }

                var relax = Mathf.Clamp01(2f * i / MaximumSteps - 1f);
                travelled += Mathf.Max(estimate * Mathf.Lerp(Mathf.Max(stepScale, 0.05f), 1f, relax), epsilon);
                if (travelled > limit)
                {
                    exhausted = false;
                    break;
                }
            }

            if (exhausted && bestRatio < GrazingRatio)
            {
                hit = origin + direction * bestTravelled;
                return true;
            }

            return false;
        }
    }
}
