using System;

namespace FractalVisio.Core
{
    /// <summary>
    /// Everything needed to put a picture back on screen, in a form <c>JsonUtility</c> can write.
    ///
    /// Two rules shape it. The centre and scale are <b>decimal strings</b>: JsonUtility cannot write
    /// a decimal, and a double would silently lose a deep view. Parameters are stored <b>by key</b>,
    /// so a fractal that gains or reorders parameters still reads an old file. The iteration budget
    /// is not stored at all - the session derives it from the scale, and a stored one would
    /// override a better budget after an update.
    ///
    /// Raise <see cref="CurrentVersion"/> on any incompatible change and teach
    /// <see cref="StateCodec.Upgrade"/> to read the old shape. Version 2 (2026-09-24): the Burning
    /// Ship was mirrored top to bottom, so its saved views are too.
    /// </summary>
    [Serializable]
    public sealed class FractalStateDto
    {
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;

        /// <summary><see cref="IFractalDefinition.Id"/>.</summary>
        public string fractal;

        public string centerX;
        public string centerY;
        public string scale;
        public double rotation;

        /// <summary><see cref="PaletteData.Id"/>.</summary>
        public string palette;

        public ColoringDto coloring;
        public ParameterValueDto[] parameters;
    }

    [Serializable]
    public sealed class ParameterValueDto
    {
        public string key;
        public double value;
    }

    [Serializable]
    public sealed class ColoringDto
    {
        public bool smooth;
        public int mode;
        public float cycleLength;
        public float offset;

        /// <summary>"#RRGGBB".</summary>
        public string interior;
    }

    /// <summary>A palette as its stops. See <see cref="PaletteData.Stops"/>.</summary>
    [Serializable]
    public sealed class PaletteDto
    {
        public string id;
        public string name;
        public PaletteStopDto[] stops;
    }

    [Serializable]
    public sealed class PaletteStopDto
    {
        public float position;

        /// <summary>"#RRGGBB".</summary>
        public string color;
    }
}
