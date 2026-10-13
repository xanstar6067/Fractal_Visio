using System;
using UnityEngine;
using FractalVisio.Core;
using FractalVisio.Rendering;

namespace FractalVisio.App
{
    /// <summary>
    /// The picture of a 3D scene. <see cref="FractalPresenter"/> hands it the frame while the session
    /// shows a scene; the plane engines rest meanwhile.
    ///
    /// Two kinds of frame, the WPF version's ladder. While anything moves - the camera, a slider, a
    /// palette - each change is drawn at once as a <b>draft</b>: a fraction of the resolution and no
    /// soft shadows, sized as it goes so the screen keeps near 30 frames a second. Once things have
    /// been still for a moment, the <b>full</b> frame is built: it starts as the draft scaled up and is
    /// marched over it a band of rows at a time, top to bottom, as many rows per screen frame as fit -
    /// so a phone is never asked for one draw long enough to stall it, and the picture sharpens where
    /// the viewer is looking instead of jumping when it is done. A full frame is never marched again
    /// for the same picture, so a still scene costs the GPU nothing.
    /// </summary>
    internal sealed class ScenePresenter : IDisposable
    {
        /// <summary>Stillness before the full frame starts, seconds: a drag that pauses briefly is still a drag.</summary>
        private const float SettleSeconds = 0.12f;

        /// <summary>Frame interval the draft is sized for: 30 a second keeps a turn feeling attached to the finger.</summary>
        private const float DraftFrameSeconds = 1f / 30f;

        /// <summary>Draft resolution as a fraction of the full frame's, the range it is adapted within.</summary>
        private const float MinimumDraftScale = 0.2f;
        private const float MaximumDraftScale = 0.6f;

        /// <summary>Steps between draft sizes: a size is a render texture, and it should not change every frame.</summary>
        private const float DraftScaleStep = 0.05f;

        /// <summary>Screen frame interval the full frame's bands are planned for.</summary>
        private const float BandFrameSeconds = 1f / 30f;

        private readonly MobileRenderProfile profile;
        private readonly SceneGpuRenderer renderer = new();

        private RenderTexture draft;
        private RenderTexture front;
        private RenderTexture back;
        private Vector2Int fullSize;
        private Vector2Int screenSize;
        private float builtRenderScale = -1f;
        private float draftScale = 0.4f;
        private int draftLevelVotes;

        private Key shownKey;
        private bool hasShown;
        private double changedAt;
        private bool refining;
        private bool complete;
        private int rowsDone;
        private float rowBudget;
        private bool draftedLastFrame;
        private bool bandedLastFrame;

        public ScenePresenter(MobileRenderProfile profile)
        {
            this.profile = profile;
        }

        /// <summary>The texture to show, and what the backdrop blur and bookmark previews read.</summary>
        public Texture Displayed { get; private set; }

        public bool IsBusy => hasShown && !complete;

        public bool Interacting { get; private set; }

        public float Progress => complete ? 1f : fullSize.y > 0 ? rowsDone / (float)fullSize.y : 0f;

        /// <summary>Pixel size of the full frame, for the HUD.</summary>
        public Vector2Int RenderSize => fullSize;

        public void Tick(FractalSession session, bool interacting, float renderScale)
        {
            var scene = session.Scene;
            if (scene == null)
            {
                return;
            }

            AdaptToLastFrame(Time.unscaledDeltaTime);
            EnsureTargets(renderScale);
            renderer.SetPalette(session.Palette);

            var now = Time.unscaledTimeAsDouble;
            var key = new Key(scene, session.Parameters, session.Camera, session.Look, session.Palette, fullSize);
            if (!hasShown || !key.Same(shownKey))
            {
                shownKey = key;
                hasShown = true;
                changedAt = now;
                DrawDraft(key);
                return;
            }

            Interacting = interacting || now - changedAt < SettleSeconds;
            if (complete || Interacting)
            {
                return;
            }

            Refine(key);
        }

        /// <summary>Forget the picture: the next tick starts from a draft. Textures stay for the next scene.</summary>
        public void Invalidate()
        {
            hasShown = false;
            complete = false;
            refining = false;
        }

        /// <summary>Give the textures back - the scene is closed - and forget the picture.</summary>
        public void Release()
        {
            Invalidate();
            ReleaseTarget(ref draft);
            ReleaseTarget(ref front);
            ReleaseTarget(ref back);
            Displayed = null;
            builtRenderScale = -1f;
        }

        public void Dispose()
        {
            Release();
            renderer.Dispose();
        }

        private void DrawDraft(in Key key)
        {
            var size = DraftSize();
            if (draft == null || draft.width != size.x || draft.height != size.y)
            {
                ReleaseTarget(ref draft);
                draft = CreateTarget(size, "Scene Draft");
            }

            var frame = new SceneFrame(key.Scene, key.Parameters, key.Camera, key.Look, draft: true);
            renderer.Render(frame, size, new RectInt(0, 0, size.x, size.y), null, 0, size.y, draft);
            Displayed = draft;
            Interacting = true;
            refining = false;
            complete = false;
            rowsDone = 0;
            draftedLastFrame = true;
        }

        private void Refine(in Key key)
        {
            if (!refining)
            {
                // The draft, scaled up, is the canvas the bands are marched over.
                Graphics.Blit(draft, front);
                refining = true;
                rowsDone = 0;
                if (rowBudget <= 0f)
                {
                    rowBudget = Mathf.Max(8f, fullSize.y / 8f);
                }
            }

            var frame = new SceneFrame(key.Scene, key.Parameters, key.Camera, key.Look, draft: false);
            var window = new RectInt(0, 0, fullSize.x, fullSize.y);
            var rows = Mathf.Clamp(Mathf.RoundToInt(rowBudget), 4, fullSize.y);
            var top = fullSize.y - rowsDone;
            var bottom = Mathf.Max(0, top - rows);

            renderer.Render(frame, fullSize, window, front, bottom, top, back);
            (front, back) = (back, front);
            rowsDone = fullSize.y - bottom;
            Displayed = front;
            bandedLastFrame = true;

            if (bottom == 0)
            {
                complete = true;
                refining = false;
            }
        }

        /// <summary>
        /// Size the next draft and band by how long the last frame took. A frame that drew a draft or
        /// a band waited for the GPU to finish it, so its length says what that work cost here.
        /// </summary>
        private void AdaptToLastFrame(float deltaTime)
        {
            if (!(deltaTime > 0f))
            {
                return;
            }

            if (draftedLastFrame)
            {
                // Votes, not single frames: one hitch must not resize the target.
                var vote = deltaTime > DraftFrameSeconds * 1.3f ? -1 : deltaTime < DraftFrameSeconds * 0.7f ? 1 : 0;
                draftLevelVotes = vote == 0 ? 0 : Math.Sign(draftLevelVotes) == vote ? draftLevelVotes + vote : vote;
                if (Math.Abs(draftLevelVotes) >= 3)
                {
                    draftScale = Mathf.Clamp(draftScale + Math.Sign(draftLevelVotes) * DraftScaleStep, MinimumDraftScale, MaximumDraftScale);
                    draftLevelVotes = 0;
                }
            }

            if (bandedLastFrame)
            {
                var ratio = BandFrameSeconds / deltaTime;
                rowBudget = Mathf.Clamp(rowBudget * Mathf.Clamp(ratio, 0.5f, 1.5f), 4f, Mathf.Max(4f, fullSize.y));
            }

            draftedLastFrame = false;
            bandedLastFrame = false;
        }

        private Vector2Int DraftSize()
        {
            var levels = Mathf.Round(draftScale / DraftScaleStep) * DraftScaleStep;
            return new Vector2Int(
                Mathf.Max(32, Mathf.RoundToInt(fullSize.x * levels)),
                Mathf.Max(32, Mathf.RoundToInt(fullSize.y * levels)));
        }

        private void EnsureTargets(float renderScale)
        {
            var width = Mathf.Max(64, Screen.width);
            var height = Mathf.Max(64, Screen.height);
            if (front != null && screenSize.x == width && screenSize.y == height &&
                Mathf.Approximately(renderScale, builtRenderScale))
            {
                return;
            }

            screenSize = new Vector2Int(width, height);
            builtRenderScale = renderScale;
            var viewport = profile.ResolveViewport(width, height, false, renderScale);
            fullSize = new Vector2Int(viewport.Width, viewport.Height);
            rowBudget = 0f;

            ReleaseTarget(ref front);
            ReleaseTarget(ref back);
            front = CreateTarget(fullSize, "Scene Full A");
            back = CreateTarget(fullSize, "Scene Full B");
            Invalidate();
        }

        private static RenderTexture CreateTarget(Vector2Int size, string name)
        {
            var texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        private static void ReleaseTarget(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        /// <summary>Everything a frame depends on. A frame of one key is a picture of nothing else.</summary>
        private readonly struct Key
        {
            public Key(
                ISceneDefinition scene, in FractalParameterSet parameters, in SceneCamera camera, in SceneLook look,
                PaletteData palette, Vector2Int size)
            {
                Scene = scene;
                Parameters = parameters;
                Camera = camera;
                Look = look;
                Palette = palette;
                Size = size;
            }

            public ISceneDefinition Scene { get; }
            public FractalParameterSet Parameters { get; }
            public SceneCamera Camera { get; }
            public SceneLook Look { get; }
            public PaletteData Palette { get; }
            public Vector2Int Size { get; }

            public bool Same(in Key other) =>
                ReferenceEquals(Scene, other.Scene) && Parameters.SameValues(other.Parameters) &&
                Camera.Equals(other.Camera) && Look.Equals(other.Look) &&
                ReferenceEquals(Palette, other.Palette) && Size == other.Size;
        }
    }
}
