using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// Saves the picture as a PNG or a JPEG, in one of two ways.
    ///
    /// At the screen's own size and without supersampling it copies the screen: it waits for the
    /// render to finish so the file is the sharp frame rather than a coarse pass, copies exactly
    /// what the viewer sees out of the displayed texture (<see cref="IBackdropSource"/>, the fractal
    /// alone, under the interface) and encodes it off the main thread. Any other size, or any
    /// supersampling, is rendered again at that size by <see cref="IFrameExport"/> - a 4K image is
    /// 4K of detail, not the screen stretched - with progress and Cancel in the toast.
    ///
    /// On Android 10 and later the file is also added to the gallery (Pictures/FractalVisio)
    /// through MediaStore, which needs no storage permission, and can then be shared; older devices
    /// keep it in the app's own folder.
    /// </summary>
    public sealed class ScreenshotModule : IAppModule, IScreenshotService
    {
        /// <summary>Give up waiting for a sharp frame after this long and save what there is.</summary>
        private const float RenderTimeoutSeconds = 30f;

        /// <summary>Frames the renderer must stay idle before capture, so a render starting next frame is not caught half-way.</summary>
        private const int IdleFramesRequired = 3;

        private const int MaximumSide = 8192;
        private const long MaximumPixels = 16000000;
        private const int JpegQuality = 95;

        private const string GalleryFolder = "Pictures/FractalVisio";

        private AppServices services;
        private float requestedAt;
        private int idleFrames;
        private Task<EncodedImage> encoding;
        private AsyncGPUReadbackRequest readbackRequest;
        private RenderTexture readbackTarget;
        private IFrameExportJob exportJob;
        private int captureWidth;
        private int captureHeight;
        private bool captureJpeg;
        private float renderProgress;
        private float phaseStartedAt;
        private string savedUri;
        private string savedMime;

        public string Id => "screenshot";

        public ScreenshotState State { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public float RenderProgress => renderProgress;

        public float PhaseSeconds => Time.unscaledTime - phaseStartedAt;

        public bool CanCancel => State == ScreenshotState.WaitingForRender;

        public bool CanShare => State == ScreenshotState.Saved && !string.IsNullOrEmpty(savedUri);

        public event Action<ScreenshotState> StateChanged;

        public void Initialize(AppServices context)
        {
            services = context;
            services.Provide<IScreenshotService>(this);
        }

        public void Request()
        {
            if (services == null || State == ScreenshotState.WaitingForRender ||
                State == ScreenshotState.ReadingPixels || State == ScreenshotState.Encoding ||
                State == ScreenshotState.AddingToGallery || encoding != null)
            {
                return;
            }

            requestedAt = Time.unscaledTime;
            idleFrames = 0;
            renderProgress = 0f;
            savedUri = null;

            var settings = services.Session.Interface;
            captureJpeg = settings.ScreenshotJpeg;
            if (settings.ScreenshotWidth > 0 || settings.ScreenshotSupersampling > 1)
            {
                BeginExport(settings);
                return;
            }

            SetState(ScreenshotState.WaitingForRender, services.Strings.Get("screenshot.rendering"));
        }

        public void Cancel()
        {
            if (!CanCancel)
            {
                return;
            }

            exportJob?.Dispose();
            exportJob = null;
            SetState(ScreenshotState.Cancelled, services.Strings.Get("screenshot.cancelled"));
        }

        public void Tick()
        {
            if (services == null)
            {
                return;
            }

            if (encoding != null)
            {
                if (encoding.IsCompleted)
                {
                    FinishEncoding();
                }

                return;
            }

            if (State == ScreenshotState.ReadingPixels)
            {
                if (readbackRequest.done)
                {
                    FinishReadback();
                }

                return;
            }

            if (State != ScreenshotState.WaitingForRender)
            {
                return;
            }

            if (exportJob != null)
            {
                TickExport();
                return;
            }

            var status = services.Render.Status;
            renderProgress = status.IsBusy ? Mathf.Clamp01(status.Progress) : 1f;
            idleFrames = status.IsBusy || status.Interacting ? 0 : idleFrames + 1;
            if (idleFrames < IdleFramesRequired && Time.unscaledTime - requestedAt < RenderTimeoutSeconds)
            {
                return;
            }

            Capture();
        }

        public void Shutdown()
        {
            exportJob?.Dispose();
            exportJob = null;

            if (readbackTarget != null)
            {
                readbackRequest.WaitForCompletion();
                RenderTexture.ReleaseTemporary(readbackTarget);
                readbackTarget = null;
            }

            services = null;
        }

        /// <summary>
        /// The size of the picture as it is rendered on screen: what "Screen size" saves, and the
        /// base a supersampled image of that size is rendered at.
        /// </summary>
        private bool TryGetScreenSize(out int width, out int height)
        {
            width = height = 0;
            var source = services.Backdrop?.Texture;
            if (source == null)
            {
                return false;
            }

            var uv = services.Backdrop.UvRect;
            width = Mathf.Clamp(Mathf.RoundToInt(source.width * uv.width), 16, MaximumSide);
            height = Mathf.Clamp(Mathf.RoundToInt(source.height * uv.height), 16, MaximumSide);
            return true;
        }

        private static bool IsSupportedSize(int width, int height) =>
            width <= MaximumSide && height <= MaximumSide && (long)width * height <= MaximumPixels;

        private void BeginExport(in InterfaceSettings settings)
        {
            int width, height;
            if (settings.ScreenshotWidth > 0)
            {
                width = settings.ScreenshotWidth;
                height = settings.ScreenshotHeight;
            }
            else if (!TryGetScreenSize(out width, out height))
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.nothing_to_save"));
                return;
            }

            if (!IsSupportedSize(width, height))
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.size_unsupported"));
                return;
            }

            exportJob = services.Get<IFrameExport>()?.Begin(width, height, settings.ScreenshotSupersampling);
            if (exportJob == null)
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.failed"));
                return;
            }

            captureWidth = width;
            captureHeight = height;
            SetState(ScreenshotState.WaitingForRender, services.Strings.Get("screenshot.rendering"));
        }

        private void TickExport()
        {
            renderProgress = exportJob.Progress;
            if (!exportJob.IsDone)
            {
                return;
            }

            var pixels = exportJob.Pixels;
            exportJob.Dispose();
            exportJob = null;
            Debug.Log($"Screenshot export rendered in {PhaseSeconds:F2}s.");
            if (pixels == null)
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.failed"));
                return;
            }

            StartEncoding(pixels, captureWidth, captureHeight);
        }

        private void Capture()
        {
            Debug.Log($"Screenshot waited {Time.unscaledTime - requestedAt:F2}s for the render.");
            if (!TryGetScreenSize(out var width, out var height))
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.nothing_to_save"));
                return;
            }

            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize || !IsSupportedSize(width, height))
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.size_unsupported"));
                return;
            }

            var source = services.Backdrop.Texture;
            var uv = services.Backdrop.UvRect;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, target, uv.size, uv.position);
                captureWidth = width;
                captureHeight = height;
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    readbackRequest = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
                    readbackTarget = target;
                    SetState(ScreenshotState.ReadingPixels, services.Strings.Get("screenshot.reading"));
                    return;
                }

                // Old graphics drivers need a synchronous readback.
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    texture.Apply(false, false);
                    var pixels = texture.GetRawTextureData();
                    UnityEngine.Object.Destroy(texture);
                    StartEncoding(pixels, width, height);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Capturing the image failed: " + exception.Message);
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.failed"));
            }
            finally
            {
                if (readbackTarget != target)
                {
                    RenderTexture.ReleaseTemporary(target);
                }
            }
        }

        private void FinishReadback()
        {
            Debug.Log($"Screenshot GPU readback took {PhaseSeconds:F2}s.");
            var target = readbackTarget;
            readbackTarget = null;
            try
            {
                if (readbackRequest.hasError)
                {
                    SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.failed"));
                    return;
                }

                StartEncoding(readbackRequest.GetData<byte>().ToArray(), captureWidth, captureHeight);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Reading image pixels failed: " + exception.Message);
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.failed"));
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void StartEncoding(byte[] pixels, int width, int height)
        {
            var jpeg = captureJpeg;
            var fileName = "FractalVisio_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + (jpeg ? ".jpg" : ".png");
            var directory = services.Storage?.ExportDirectory ?? Application.persistentDataPath;
            var format = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? GraphicsFormat.R8G8B8A8_SRGB
                : GraphicsFormat.R8G8B8A8_UNorm;

            SetState(ScreenshotState.Encoding, services.Strings.Format("screenshot.encoding", jpeg ? "JPEG" : "PNG"));
            encoding = Task.Run(() =>
            {
                var bytes = jpeg
                    ? ImageConversion.EncodeArrayToJPG(pixels, format, (uint)width, (uint)height, 0, JpegQuality)
                    : ImageConversion.EncodeArrayToPNG(pixels, format, (uint)width, (uint)height);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, fileName);
                File.WriteAllBytes(path, bytes);
                return new EncodedImage(fileName, path, bytes, jpeg ? "image/jpeg" : "image/png");
            });
        }

        private void FinishEncoding()
        {
            Debug.Log($"Screenshot encoding and file write took {PhaseSeconds:F2}s.");
            var task = encoding;
            encoding = null;

            if (task.IsFaulted || task.IsCanceled)
            {
                var reason = task.Exception?.GetBaseException().Message ?? "cancelled";
                Debug.LogWarning("Saving the image failed: " + reason);
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.failed"));
                return;
            }

            var image = task.Result;
            if (Application.platform == RuntimePlatform.Android)
            {
                SetState(ScreenshotState.AddingToGallery, services.Strings.Get("screenshot.gallery"));
            }

            savedUri = TryAddToGallery(image);
            savedMime = image.Mime;
            var message = savedUri != null
                ? services.Strings.Format("screenshot.saved_to_gallery", GalleryFolder)
                : services.Strings.Format("screenshot.saved_to_file", image.Path);
            if (Application.platform == RuntimePlatform.Android)
            {
                Debug.Log($"Screenshot gallery write took {PhaseSeconds:F2}s.");
            }
            SetState(ScreenshotState.Saved, message);
        }

        /// <summary>
        /// MediaStore insert with a relative path: the Android 10+ way to put a file in the shared
        /// gallery without a storage permission. Plain JNI, so no plugin or package is needed.
        /// Returns the new item's content URI - what sharing hands to other apps - or null.
        /// </summary>
        private static string TryAddToGallery(EncodedImage image)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return null;
            }

            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") < 29)
                {
                    return null;
                }

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", image.FileName);
                values.Call("put", "mime_type", image.Mime);
                values.Call("put", "relative_path", GalleryFolder);

                using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                using var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                using var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                if (uri == null)
                {
                    return null;
                }

                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
                if (stream == null)
                {
                    return null;
                }

                stream.Call("write", image.Bytes);
                stream.Call("close");
                return uri.Call<string>("toString");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Adding the image to the gallery failed: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// ACTION_SEND with the gallery item's content URI, through the system chooser. The URI is the
        /// app's own MediaStore item, so a read grant on the intent is all the receiving app needs.
        /// </summary>
        public void Share()
        {
            if (!CanShare || Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent", intentClass.GetStatic<string>("ACTION_SEND"));
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", savedUri);
                intent.Call<AndroidJavaObject>("setType", savedMime ?? "image/png");
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_STREAM"), uri);
                intent.Call<AndroidJavaObject>("addFlags", intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION"));

                using var chooser = intentClass.CallStatic<AndroidJavaObject>(
                    "createChooser", intent, services.Strings.Get("screenshot.share"));
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("startActivity", chooser);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Sharing the image failed: " + exception.Message);
            }
        }

        private void SetState(ScreenshotState state, string message)
        {
            State = state;
            phaseStartedAt = Time.unscaledTime;
            LastMessage = message ?? string.Empty;
            StateChanged?.Invoke(state);
        }

        private readonly struct EncodedImage
        {
            public EncodedImage(string fileName, string path, byte[] bytes, string mime)
            {
                FileName = fileName;
                Path = path;
                Bytes = bytes;
                Mime = mime;
            }

            public string FileName { get; }
            public string Path { get; }
            public byte[] Bytes { get; }
            public string Mime { get; }
        }
    }
}
