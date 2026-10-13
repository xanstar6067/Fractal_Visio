using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>What a 3D frame shows and how hard it tries. See <see cref="SceneGpuRenderer.Render"/>.</summary>
    public readonly struct SceneFrame
    {
        public SceneFrame(
            ISceneDefinition scene, in FractalParameterSet parameters, in SceneCamera camera, in SceneLook look, bool draft)
        {
            Scene = scene;
            Parameters = parameters;
            Camera = camera;
            Look = look;
            Draft = draft;
        }

        public ISceneDefinition Scene { get; }
        public FractalParameterSet Parameters { get; }
        public SceneCamera Camera { get; }
        public SceneLook Look { get; }

        /// <summary>
        /// A frame drawn while the picture moves: no soft shadows, the most expensive effect by far
        /// (the WPF version's default motion quality). Everything else is as settled.
        /// </summary>
        public bool Draft { get; }
    }

    /// <summary>
    /// Ray marches a 3D scene on the GPU. Owns the uniforms every scene shares - camera, light,
    /// colouring, the palette - and knows nothing about any scene: the definition names its shader
    /// and sets its own shape (<see cref="ISceneDefinition.BindMaterial"/>).
    ///
    /// A draw marches one band of rows of a target and copies the rest from the previous frame
    /// (<c>_MainTex</c>), so a large frame can be built over several screen frames - a phone driver
    /// treats one draw that runs too long as a hung GPU - and an export tile is the same draw with a
    /// window into a larger image. Nothing here allocates per frame.
    /// </summary>
    public sealed class SceneGpuRenderer : IDisposable
    {
        private const float ShadowSharpness = 16f;
        private const float OcclusionStrength = 0.7f;

        private static readonly int PaletteId = Shader.PropertyToID("_PaletteTex");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int WindowId = Shader.PropertyToID("_Window");
        private static readonly int BandId = Shader.PropertyToID("_Band");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int CameraRightId = Shader.PropertyToID("_CameraRight");
        private static readonly int CameraUpId = Shader.PropertyToID("_CameraUp");
        private static readonly int CameraForwardId = Shader.PropertyToID("_CameraForward");
        private static readonly int MarchId = Shader.PropertyToID("_March");
        private static readonly int LightDirId = Shader.PropertyToID("_LightDir");
        private static readonly int LookId = Shader.PropertyToID("_Look");
        private static readonly int StyleId = Shader.PropertyToID("_Style");
        private static readonly int ColorMapId = Shader.PropertyToID("_ColorMap");
        private static readonly int ViewId = Shader.PropertyToID("_View");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SkyTopId = Shader.PropertyToID("_SkyTop");
        private static readonly int SkyBottomId = Shader.PropertyToID("_SkyBottom");
        private static readonly int LightColorId = Shader.PropertyToID("_LightColor");

        // The WPF version's environment: a dark sky, a white lamp and a warm material.
        private static readonly Color32 SurfaceColor = new(228, 206, 180, 255);
        private static readonly Color32 SkyTopColor = new(18, 22, 34, 255);
        private static readonly Color32 SkyBottomColor = new(6, 7, 11, 255);
        private static readonly Color32 LampColor = new(255, 255, 255, 255);

        private readonly Texture2D palette;
        private readonly Color32[] palettePixels = new Color32[PaletteData.Resolution];
        private readonly Dictionary<string, Material> materials = new();
        private PaletteData paletteData;

        // The CPU estimate that measures how close the camera is, kept while the shape is the same.
        private ISceneDefinition distanceScene;
        private FractalParameterSet distanceParameters;
        private ISceneDistance distance;
        private float homeDistance;

        public SceneGpuRenderer()
        {
            palette = new Texture2D(PaletteData.Resolution, 1, TextureFormat.RGBA32, false, true)
            {
                name = "Scene Palette",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            SetPalette(PaletteLibrary.Default);
        }

        /// <summary>Whether this scene can be drawn on this device at all.</summary>
        public bool Supports(ISceneDefinition scene) => scene != null && ResolveMaterial(scene.ShaderName) != null;

        /// <summary>The palette the colouring sources sample. Rewritten in place, like the plane renderer's.</summary>
        public void SetPalette(PaletteData value)
        {
            var next = value ?? PaletteLibrary.Default;
            if (ReferenceEquals(next, paletteData))
            {
                return;
            }

            paletteData = next;
            for (var i = 0; i < palettePixels.Length; i++)
            {
                palettePixels[i] = next[i * next.Count / palettePixels.Length];
            }

            palette.SetPixels32(palettePixels);
            palette.Apply(false, false);
        }

        /// <summary>
        /// Draw rows <paramref name="bandStart"/> to <paramref name="bandEnd"/> (from the bottom) of
        /// <paramref name="target"/> and copy the others from <paramref name="previous"/>, which must
        /// be the same size; with no previous frame the whole target is drawn.
        ///
        /// The target is the window <paramref name="window"/> of an image <paramref name="frameSize"/>
        /// pixels large: the screen's frame is its own window, an export tile a part of the image.
        /// Returns false, drawing nothing, when the scene has no shader here.
        /// </summary>
        public bool Render(
            in SceneFrame frame, Vector2Int frameSize, RectInt window, RenderTexture previous,
            int bandStart, int bandEnd, RenderTexture target)
        {
            var scene = frame.Scene;
            if (scene == null || target == null)
            {
                return false;
            }

            var material = ResolveMaterial(scene.ShaderName);
            if (material == null)
            {
                return false;
            }

            if (previous == null)
            {
                bandStart = 0;
                bandEnd = window.height;
            }

            var camera = frame.Camera;
            var look = frame.Look;
            var parameters = frame.Parameters;

            material.SetVector(FrameId, new Vector4(frameSize.x, frameSize.y, 0f, 0f));
            material.SetVector(WindowId, new Vector4(window.x, window.y, window.width, window.height));
            material.SetVector(BandId, new Vector4(bandStart, bandEnd, 0f, 0f));

            var position = camera.Position;
            material.SetVector(CameraPositionId, new Vector4(position.x, position.y, position.z, camera.FieldOfViewScale));
            material.SetVector(CameraRightId, camera.Right);
            material.SetVector(CameraUpId, camera.Up);
            material.SetVector(CameraForwardId, camera.Forward);
            material.SetVector(MarchId, new Vector4(
                SceneMarching.MaximumSteps, SceneMarching.Detail, SceneMarching.TraceDistance,
                Mathf.Max(scene.BoundingRadius(parameters), 1e-3f)));

            var light = SceneNavigator.LightDirection(camera, look.LightAngle, look.LightHeight);
            material.SetVector(LightDirId, new Vector4(light.x, light.y, light.z, look.Specular));
            material.SetVector(LookId, new Vector4(
                (int)look.Coloring,
                look.Shadows && !frame.Draft ? ShadowSharpness : 0f,
                look.Occlusion ? 1f : 0f,
                look.Ambient));
            material.SetVector(StyleId, new Vector4((int)look.Shading, look.Effect, 0f, OcclusionStrength));
            material.SetVector(ColorMapId, new Vector4(look.ColorScale, look.ColorOffset, 0f, 0f));
            material.SetVector(ViewId, new Vector4(ViewScale(scene, parameters, position), 0f, 0f, 0f));
            material.SetVector(SurfaceId, Linear(SurfaceColor));
            material.SetVector(SkyTopId, Linear(SkyTopColor));
            material.SetVector(SkyBottomId, Linear(SkyBottomColor));
            material.SetVector(LightColorId, Linear(LampColor));

            scene.BindMaterial(material, parameters);

            Graphics.Blit(previous != null ? previous : Texture2D.blackTexture, target, material, 0);
            return true;
        }

        public void Dispose()
        {
            foreach (var material in materials.Values)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            materials.Clear();
            if (palette != null)
            {
                UnityEngine.Object.Destroy(palette);
            }
        }

        /// <summary>
        /// How much smaller the scene is around the camera than around the default one: the distance
        /// from the camera to the surface against the same from the default camera. Haze, depth
        /// colouring and shaded folds shrink with it, so a self-similar shape seen up close looks
        /// like the default view made small. It starts to shrink when the camera is twice as close
        /// as in the default view, so orbiting at the old distance changes nothing. (WPF:
        /// <c>ViewScale</c>, two estimates per pixel; here once per frame.)
        /// </summary>
        private float ViewScale(ISceneDefinition scene, in FractalParameterSet parameters, Vector3 cameraPosition)
        {
            if (!ReferenceEquals(scene, distanceScene) || !parameters.SameValues(distanceParameters) || distance == null)
            {
                distanceScene = scene;
                distanceParameters = parameters;
                distance = scene.CreateDistance(parameters);
                homeDistance = distance != null ? distance.Estimate(scene.DefaultCamera.Position, out _) : 1f;
            }

            if (distance == null)
            {
                return 1f;
            }

            var here = distance.Estimate(cameraPosition, out _);
            return Mathf.Clamp(2f * Mathf.Max(here, 0f) / Mathf.Max(homeDistance, 1e-9f), 1e-6f, 1f);
        }

        private static Vector4 Linear(Color32 color) =>
            new(ToLinear(color.r), ToLinear(color.g), ToLinear(color.b), 1f);

        private static float ToLinear(byte value)
        {
            var channel = value / 255f;
            return channel <= 0.04045f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        private Material ResolveMaterial(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return null;
            }

            if (materials.TryGetValue(shaderName, out var cached))
            {
                return cached;
            }

            Material material = null;
            var shader = Shader.Find(shaderName);
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture(PaletteId, palette);
            }
            else
            {
                // As for the plane renderer: only ever seen on a device, logged once per renderer.
                Debug.LogWarning(shader == null
                    ? "Shader '" + shaderName + "' is not in this build: it is missing from Always Included Shaders."
                    : "Shader '" + shaderName + "' is not supported by this GPU.");
            }

            materials[shaderName] = material;
            return material;
        }
    }
}
