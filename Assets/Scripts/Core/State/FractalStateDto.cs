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

        /// <summary>
        /// Camera and look when <see cref="fractal"/> names a 3D scene (<see cref="ISceneDefinition"/>);
        /// the centre, scale and rotation above are then unused. JsonUtility writes it for a plane
        /// fractal too, empty - and files from before 3D read as empty: a scene with no camera starts
        /// at its default one.
        /// </summary>
        public SceneStateDto scene;
    }

    /// <summary>A 3D scene's camera and look. A zero <see cref="distance"/> means "none saved".</summary>
    [Serializable]
    public sealed class SceneStateDto
    {
        /// <summary>The camera's turn as a unit quaternion: exact, where angles would round.</summary>
        public float qx;
        public float qy;
        public float qz;
        public float qw;
        public float distance;
        public float targetX;
        public float targetY;
        public float targetZ;
        public float fieldOfView;

        public SceneLookDto look;
    }

    /// <summary><see cref="SceneLook"/>. A zero <see cref="version"/> means "none saved".</summary>
    [Serializable]
    public sealed class SceneLookDto
    {
        public int version;
        public int shading;
        public int coloring;
        public float lightAngle;
        public float lightHeight;
        public float ambient;
        public float specular;
        public bool shadows;
        public bool occlusion;
        public float effect;
        public float colorScale;
        public float colorOffset;
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

        /// <summary>
        /// <see cref="ColoringSettings.Relief"/> and its light. Absent in files from before the relief
        /// existed: off, and a <see cref="lightHeight"/> of 0 - below its minimum - reads as "use the
        /// default light" for all four.
        /// </summary>
        public bool relief;
        public float lightAngle;
        public float lightHeight;
        public float reliefDepth;
        public float reliefShine;
    }

    /// <summary>A palette as its stops. See <see cref="PaletteData.Stops"/>.</summary>
    [Serializable]
    public sealed class PaletteDto
    {
        public string id;
        public string name;
        public PaletteStopDto[] stops;

        /// <summary><see cref="PaletteData.Bands"/>. Absent in files from before it existed, which reads as a ramp.</summary>
        public bool bands;
    }

    [Serializable]
    public sealed class PaletteStopDto
    {
        public float position;

        /// <summary>"#RRGGBB".</summary>
        public string color;
    }
}
