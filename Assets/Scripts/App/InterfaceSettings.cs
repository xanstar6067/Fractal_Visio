using UnityEngine;

namespace FractalVisio.App
{
    /// <summary>
    /// Settings about the interface rather than the picture.
    ///
    /// It lives on the session because the session is the only owner of saved, user-visible state
    /// today; stage 7's <c>SettingsModule</c> is where it belongs long-term. Keeping it here now
    /// costs one flag and means the state store will pick it up for free when it lands.
    /// </summary>
    public struct InterfaceSettings
    {
        /// <summary>
        /// Multiplier on the density-derived UI scale. The escape hatch for a device whose reported
        /// dpi does not match its physical size - see <c>UiTheme</c>.
        /// </summary>
        public float Scale;

        public static InterfaceSettings Default => new InterfaceSettings { Scale = 1f }.Sanitized();

        public InterfaceSettings Sanitized()
        {
            var result = this;
            result.Scale = Mathf.Clamp(result.Scale <= 0f ? 1f : result.Scale, 0.6f, 2f);
            return result;
        }

        public bool Equals(in InterfaceSettings other) => Mathf.Approximately(Scale, other.Scale);
    }
}
