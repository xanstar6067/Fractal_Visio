using System;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Where a 3D scene is looked at from: a point of attention (<see cref="Target"/>), the camera
    /// <see cref="Distance"/> behind it, the way the camera is turned and its vertical field of view.
    ///
    /// The turn is a quaternion, so the camera has no favoured "up": it can go over a pole and be
    /// rolled. The conventions are the WPF version's (<c>Fractal3DCamera</c>), so a view described
    /// there - azimuth, tilt, roll, distance, target - is the same view here: unturned, the camera
    /// looks along -Z with +X to the right and +Y up; a positive tilt puts it above the target looking
    /// down. Quaternions compose as in Unity, <c>b * a</c> being "a, then b"; WPF writes the same as
    /// <c>Concatenate(a, b)</c>.
    ///
    /// Plain floats, like the shader that draws it: a 3D fractal is drawn in fp32, and a camera more
    /// precise than its picture buys nothing.
    /// </summary>
    public readonly struct SceneCamera : IEquatable<SceneCamera>
    {
        /// <summary>Closest the camera may come to what it looks at. Below this fp32 runs out of digits.</summary>
        public const float MinimumDistance = 1e-4f;

        public const float MaximumDistance = 100f;

        public const float DefaultFieldOfView = 55f;

        public SceneCamera(Quaternion orientation, float distance, Vector3 target, float fieldOfView = DefaultFieldOfView)
        {
            Orientation = orientation;
            Distance = distance;
            Target = target;
            FieldOfView = fieldOfView;
        }

        public Quaternion Orientation { get; }

        /// <summary>From the camera to <see cref="Target"/>, along the view direction.</summary>
        public float Distance { get; }

        /// <summary>The point the camera looks at, and turns around when nothing else is under the finger.</summary>
        public Vector3 Target { get; }

        /// <summary>Vertical field of view, degrees.</summary>
        public float FieldOfView { get; }

        /// <summary>Direction of view.</summary>
        public Vector3 Forward => Orientation * Vector3.back;

        /// <summary>Right on the screen.</summary>
        public Vector3 Right => Orientation * Vector3.right;

        /// <summary>Up on the screen.</summary>
        public Vector3 Up => Orientation * Vector3.up;

        public Vector3 Position => Target - Forward * Mathf.Max(Distance, MinimumDistance);

        /// <summary>
        /// 1 / tan(half the field of view): how far ahead the screen plane is when the screen is two
        /// units high. The shader and <see cref="SceneNavigator.PixelRay"/> build rays from it alike.
        /// </summary>
        public float FieldOfViewScale => 1f / Mathf.Tan(Mathf.Clamp(FieldOfView, 5f, 160f) * Mathf.Deg2Rad * 0.5f);

        /// <summary>
        /// The camera at azimuth <paramref name="yaw"/>, tilt <paramref name="pitch"/> and roll
        /// <paramref name="roll"/>, in degrees: first the roll about the line of sight, then the tilt,
        /// then the azimuth about the world's vertical - the WPF version's <c>Orientation</c>.
        /// </summary>
        public static SceneCamera FromAngles(
            float yaw, float pitch, float roll, float distance, Vector3 target, float fieldOfView = DefaultFieldOfView)
        {
            return new SceneCamera(Orient(yaw, pitch, roll), distance, target, fieldOfView);
        }

        public static Quaternion Orient(float yaw, float pitch, float roll)
        {
            var rollTurn = Quaternion.AngleAxis(roll, Vector3.forward);
            var pitchTurn = Quaternion.AngleAxis(-pitch, Vector3.right);
            var yawTurn = Quaternion.AngleAxis(yaw, Vector3.up);
            return Normalize(yawTurn * (pitchTurn * rollTurn));
        }

        /// <summary>
        /// Azimuth, tilt and roll of the camera, degrees - the inverse of <see cref="FromAngles"/>.
        /// The tilt stays within ±90; a camera over the pole reads as an azimuth 180 further on and a
        /// roll near 180. Straight over a pole the azimuth is ambiguous and takes the whole turn.
        /// </summary>
        public (float Yaw, float Pitch, float Roll) Angles()
        {
            var back = Orientation * Vector3.forward;
            var up = Up;
            var right = Right;

            var pitch = Math.Asin(Math.Clamp((double)back.y, -1d, 1d));
            var horizontal = Math.Sqrt((double)back.x * back.x + (double)back.z * back.z);
            if (horizontal < 1e-6d)
            {
                return ((float)(Math.Atan2(-right.z, right.x) * Mathf.Rad2Deg), Math.Sign(back.y) * 90f, 0f);
            }

            var yaw = Math.Atan2(back.x, back.z);
            double sinPitch = Math.Sin(pitch), cosPitch = Math.Cos(pitch);
            double sinYaw = Math.Sin(yaw), cosYaw = Math.Cos(yaw);

            // Right and up of the same camera without roll; the roll is the angle between them and the real ones.
            var upAlongRight = up.x * cosYaw - up.z * sinYaw;
            var upAlongUp = -up.x * sinPitch * sinYaw + up.y * cosPitch - up.z * sinPitch * cosYaw;
            var roll = Math.Atan2(-upAlongRight, upAlongUp);
            return ((float)(yaw * Mathf.Rad2Deg), (float)(pitch * Mathf.Rad2Deg), (float)(roll * Mathf.Rad2Deg));
        }

        public SceneCamera WithOrientation(Quaternion value) => new(value, Distance, Target, FieldOfView);

        public SceneCamera WithDistance(float value) => new(Orientation, value, Target, FieldOfView);

        public SceneCamera WithTarget(Vector3 value) => new(Orientation, Distance, value, FieldOfView);

        public SceneCamera WithFieldOfView(float value) => new(Orientation, Distance, Target, value);

        /// <summary>
        /// The same camera kept usable: a unit quaternion, a distance in range, no NaN anywhere. A
        /// camera that cannot be repaired - from a damaged file - comes back as <paramref name="fallback"/>.
        /// </summary>
        public SceneCamera Sanitized(in SceneCamera fallback)
        {
            var q = Orientation;
            var length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (!(length > 1e-6f) || float.IsNaN(length) || float.IsInfinity(length) ||
                !IsFinite(Target) || float.IsNaN(Distance) || float.IsInfinity(Distance))
            {
                return fallback;
            }

            var fieldOfView = float.IsNaN(FieldOfView) || FieldOfView <= 0f ? DefaultFieldOfView : Mathf.Clamp(FieldOfView, 5f, 160f);
            return new SceneCamera(
                new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length),
                Mathf.Clamp(Distance, MinimumDistance, MaximumDistance),
                Target,
                fieldOfView);
        }

        public bool Equals(SceneCamera other) =>
            Orientation.x == other.Orientation.x && Orientation.y == other.Orientation.y &&
            Orientation.z == other.Orientation.z && Orientation.w == other.Orientation.w &&
            Distance == other.Distance && Target.x == other.Target.x && Target.y == other.Target.y &&
            Target.z == other.Target.z && FieldOfView == other.FieldOfView;

        public override bool Equals(object obj) => obj is SceneCamera other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Orientation, Distance, Target, FieldOfView);

        internal static Quaternion Normalize(Quaternion q)
        {
            var length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length > 1e-12f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
