using UnityEngine;

namespace FractalVisio.App
{
    /// <summary>
    /// Settings about the interface rather than the picture.
    ///
    /// It lives on the session because the session is the one owner of user-visible state, and
    /// the state store module saves it from there alongside the render resolution. Stage 7 was
    /// to give it a settings module of its own; with nothing for that module to do beyond what the
    /// session and the state store already do, it was not added.
    /// </summary>
    public struct InterfaceSettings
    {
        /// <summary>
        /// Multiplier on the density-derived UI scale. The escape hatch for a device whose reported
        /// dpi does not match its physical size - see <c>UiTheme</c>.
        /// </summary>
        public float Scale;

        /// <summary>
        /// 1.0 means "whatever the screen's density says", which is now the right answer: the
        /// device test that asked for 2.5 was measuring a UI a stray CanvasScaler was shrinking by
        /// about 0.3 (see AppBootstrap.EnsureUi). With that fixed the density figure stands on its
        /// own and this is a taste multiplier on top of it.
        /// </summary>
        public static InterfaceSettings Default => new InterfaceSettings { Scale = 1f }.Sanitized();

        public InterfaceSettings Sanitized()
        {
            var result = this;
            result.Scale = Mathf.Clamp(result.Scale <= 0f ? 1f : result.Scale, 0.6f, 2.5f);
            return result;
        }

        public bool Equals(in InterfaceSettings other) => Mathf.Approximately(Scale, other.Scale);
    }
}
