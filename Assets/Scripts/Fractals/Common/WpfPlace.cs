using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// A point of interest of the WPF version, in this app's terms. WPF frames a view by its
    /// <c>Zoom</c>, the view's width being 3 / Zoom; a <see cref="PlaceOfInterest"/> is framed by its
    /// height on a wide screen, like a default view. The WPF canvas is about 4:3, so the height is
    /// three quarters of the width: 2.25 / Zoom. The coordinates carry over unchanged - the WPF
    /// Burning Ship has the same orientation since 2026-09-24.
    /// </summary>
    internal static class WpfPlace
    {
        public static PlaceOfInterest At(string id, decimal x, decimal y, decimal zoom, params ParameterValue[] parameters)
        {
            return new PlaceOfInterest(id, x, y, 2.25m / zoom, parameters);
        }
    }
}
