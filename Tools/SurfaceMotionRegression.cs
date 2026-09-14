// Pipeline eval_file script. Checks the actual composite pixels, not only view arithmetic.
// No GameObjects or scene changes. The source image stays fixed during the entire drag.
var shader = UnityEngine.Shader.Find("FractalVisio/FrameComposite");
var settings = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
var included = settings.FindProperty("m_AlwaysIncludedShaders");
var inBuild = false;
for (var i = 0; i < included.arraySize; i++)
    inBuild |= included.GetArrayElementAtIndex(i).objectReferenceValue == shader && shader != null;
if (!inBuild) throw new System.Exception("The surface compositor would be stripped from Player builds.");
if (!System.IO.File.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath,
    "../ProjectSettings/GraphicsSettings.asset")).Contains("06652061ea50e374bbefc2053e20b14d"))
    throw new System.Exception("Compositor inclusion has not been saved to GraphicsSettings.");

var viewport = new FractalVisio.Core.Viewport(128, 96);
var view = new FractalVisio.Core.ViewState { x = -0.743643887037151m, y = 0.131825904205330m, scale = 0.000000000000001m };
var source = new UnityEngine.Texture2D(128, 96, UnityEngine.TextureFormat.RGBA32, false);
source.filterMode = UnityEngine.FilterMode.Bilinear;
source.wrapMode = UnityEngine.TextureWrapMode.Clamp;
var pixels = new UnityEngine.Color32[128 * 96];
for (var y = 0; y < 96; y++)
for (var x = 0; x < 128; x++)
    pixels[y * 128 + x] = new UnityEngine.Color32((byte)(x * 2), (byte)(y * 2), 80, 255);
source.SetPixels32(pixels);
source.Apply(false, false);
var readback = new UnityEngine.Texture2D(128, 96, UnityEngine.TextureFormat.RGBA32, false);
var compositor = new FractalVisio.Rendering.FrameCompositor(UnityEngine.Color.black);
var previousTarget = UnityEngine.RenderTexture.active;
var maxError = 0;
var checks = 0;
try
{
    var identity = FractalVisio.Core.FramePlacement.Resolve(view, viewport.Aspect, view, viewport.Aspect);
    compositor.Compose(viewport, source, identity, null, FractalVisio.Core.FramePlacement.Invalid);
    UnityEngine.RenderTexture.active = (UnityEngine.RenderTexture)compositor.Texture;
    readback.ReadPixels(new UnityEngine.Rect(0, 0, 128, 96), 0, 0);
    readback.Apply(false, false);
    var baseline = readback.GetPixels32();
    // Ensure a working, oriented image; an all-black result must not pass the motion test.
    if (baseline[48 * 128 + 96].r <= baseline[48 * 128 + 32].r + 80)
        throw new System.Exception("Composite did not draw the source gradient.");
    for (var frame = 0; frame <= 12; frame++)
    {
        var current = view;
        FractalVisio.Core.ViewNavigator.Pan(ref current, viewport, new UnityEngine.Vector2(frame * 2, frame));
        var placement = FractalVisio.Core.FramePlacement.Resolve(view, viewport.Aspect, current, viewport.Aspect);
        compositor.Compose(viewport, source, placement, null, FractalVisio.Core.FramePlacement.Invalid);
        UnityEngine.RenderTexture.active = (UnityEngine.RenderTexture)compositor.Texture;
        readback.ReadPixels(new UnityEngine.Rect(0, 0, 128, 96), 0, 0);
        readback.Apply(false, false);
        var result = readback.GetPixels32();
        for (var y = 32; y < 64; y += 8)
        for (var x = 48; x < 96; x += 8)
        {
            var actual = result[y * 128 + x];
            var expected = baseline[(y - frame) * 128 + x - frame * 2];
            maxError = Math.Max(maxError, Math.Abs(actual.r - expected.r));
            maxError = Math.Max(maxError, Math.Abs(actual.g - expected.g));
            checks++;
        }
    }
    if (maxError > 2) throw new System.Exception("Surface moved incorrectly; channel error = " + maxError);
    return new { passed = true, includedInPlayer = inBuild, framesWithoutRecalculation = 13, checks, maxChannelError = maxError,
        sceneDirty = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty };
}
finally
{
    UnityEngine.RenderTexture.active = previousTarget;
    compositor.Dispose();
    UnityEngine.Object.DestroyImmediate(source);
    UnityEngine.Object.DestroyImmediate(readback);
}
