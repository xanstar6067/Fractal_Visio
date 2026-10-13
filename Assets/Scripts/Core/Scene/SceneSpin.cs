using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// A 3D scene still turning after a flick: the trackball keeps going about the point that was
    /// held, slowing down on its own. The speed decays as the plane's coast does (<see cref="ViewInertia"/>)
    /// - down to 0.2% after the configured length - so the inertia setting means the same in both.
    ///
    /// Pure arithmetic: time comes in as arguments and the camera goes out through
    /// <see cref="SceneNavigator.Orbit"/>.
    /// </summary>
    public sealed class SceneSpin
    {
        /// <summary>Fastest a flick may spin, degrees per second. Faster is a blur, not a turn.</summary>
        public const float MaximumSpeed = 900f;

        /// <summary>Below this the spin has stopped, degrees per second.</summary>
        private const float StopSpeed = 2f;

        /// <summary>ln(500): the speed is down to 0.2% after the configured time.</summary>
        private const float DecayToStop = 6.2146f;

        private Vector3 pivot;
        private Vector2 velocity;
        private float decay;

        public bool IsActive { get; private set; }

        /// <param name="degreesPerSecond">Trackball speed, x and y as <see cref="SceneNavigator.Orbit"/> takes them.</param>
        /// <param name="seconds">How long the spin lasts; zero or less starts nothing.</param>
        public void Start(Vector3 center, Vector2 degreesPerSecond, float seconds)
        {
            Stop();
            if (!(seconds > 0f))
            {
                return;
            }

            velocity = Vector2.ClampMagnitude(degreesPerSecond, MaximumSpeed);
            if (velocity.magnitude < StopSpeed * 4f)
            {
                return;
            }

            pivot = center;
            decay = DecayToStop / seconds;
            IsActive = true;
        }

        public void Stop()
        {
            IsActive = false;
            velocity = Vector2.zero;
        }

        /// <summary>Turn the camera by this frame's share of the spin. Returns false once it has stopped.</summary>
        public bool Step(ref SceneCamera camera, float deltaTime)
        {
            if (!IsActive || !(deltaTime > 0f))
            {
                return IsActive;
            }

            // The exact integral over the frame, so a long frame does not overshoot.
            var damping = Mathf.Exp(-decay * deltaTime);
            var travel = velocity * ((1f - damping) / decay);
            camera = SceneNavigator.Orbit(camera, pivot, travel.x, travel.y);
            velocity *= damping;
            if (velocity.magnitude < StopSpeed)
            {
                Stop();
            }

            return true;
        }
    }
}
