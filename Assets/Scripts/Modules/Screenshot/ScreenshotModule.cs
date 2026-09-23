using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// Saves the picture as a PNG: waits for the render to finish so the file is the sharp frame
    /// rather than a coarse pass, copies exactly what the viewer sees out of the displayed texture,
    /// and encodes it off the main thread.
    ///
    /// The image is the fractal alone - it reads <see cref="IBackdropSource"/>, the texture under
    /// the interface - at the resolution the picture is rendered in. On Android 10 and later it is
    /// also added to the gallery (Pictures/FractalVisio) through MediaStore, which needs no storage
    /// permission; older devices keep it in the app's own folder.
    /// </summary>
    public sealed class ScreenshotModule : IAppModule, IScreenshotService
    {
        /// <summary>Give up waiting for a sharp frame after this long and save what there is.</summary>
        private const float RenderTimeoutSeconds = 30f;

        /// <summary>Frames the renderer must stay idle before capture, so a render starting next frame is not caught half-way.</summary>
        private const int IdleFramesRequired = 3;

        private const string GalleryFolder = "Pictures/FractalVisio";

        private AppServices services;
        private float requestedAt;
        private int idleFrames;
        private Task<EncodedImage> encoding;

        public string Id => "screenshot";

        public ScreenshotState State { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public event Action<ScreenshotState> StateChanged;

        public void Initialize(AppServices context)
        {
            services = context;
            services.Provide<IScreenshotService>(this);
        }

        public void Request()
        {
            if (services == null || State == ScreenshotState.WaitingForRender || encoding != null)
            {
                return;
            }

            requestedAt = Time.unscaledTime;
            idleFrames = 0;
            SetState(ScreenshotState.WaitingForRender, services.Strings.Get("screenshot.rendering"));
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

            if (State != ScreenshotState.WaitingForRender)
            {
                return;
            }

            var status = services.Render.Status;
            idleFrames = status.IsBusy || status.Interacting ? 0 : idleFrames + 1;
            if (idleFrames < IdleFramesRequired && Time.unscaledTime - requestedAt < RenderTimeoutSeconds)
            {
                return;
            }

            Capture();
        }

        public void Shutdown()
        {
            services = null;
        }

        private void Capture()
        {
            var source = services.Backdrop?.Texture;
            if (source == null)
            {
                SetState(ScreenshotState.Failed, services.Strings.Get("screenshot.nothing_to_save"));
                return;
            }

            var uv = services.Backdrop.UvRect;
            var width = Mathf.Clamp(Mathf.RoundToInt(source.width * uv.width), 16, 8192);
            var height = Mathf.Clamp(Mathf.RoundToInt(source.height * uv.height), 16, 8192);

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            byte[] pixels;
            try
            {
                Graphics.Blit(source, target, uv.size, uv.position);
                RenderTexture.active = target;

                var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readback.Apply(false, false);
                pixels = readback.GetRawTextureData();
                UnityEngine.Object.Destroy(readback);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            var fileName = "FractalVisio_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            var directory = services.Storage?.ExportDirectory ?? Application.persistentDataPath;
            var format = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? GraphicsFormat.R8G8B8A8_SRGB
                : GraphicsFormat.R8G8B8A8_UNorm;

            // PNG encoding of a couple of megapixels takes a noticeable fraction of a second; on the
            // main thread that is a visible freeze right after the user tapped the button.
            encoding = Task.Run(() =>
            {
                var png = ImageConversion.EncodeArrayToPNG(pixels, format, (uint)width, (uint)height);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, fileName);
                File.WriteAllBytes(path, png);
                return new EncodedImage(fileName, path, png);
            });
        }

        private void FinishEncoding()
        {
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
            var message = TryAddToGallery(image)
                ? services.Strings.Format("screenshot.saved_to_gallery", GalleryFolder)
                : services.Strings.Format("screenshot.saved_to_file", image.Path);
            SetState(ScreenshotState.Saved, message);
        }

        /// <summary>
        /// MediaStore insert with a relative path: the Android 10+ way to put a file in the shared
        /// gallery without a storage permission. Plain JNI, so no plugin or package is needed.
        /// </summary>
        private static bool TryAddToGallery(EncodedImage image)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return false;
            }

            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") < 29)
                {
                    return false;
                }

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", image.FileName);
                values.Call("put", "mime_type", "image/png");
                values.Call("put", "relative_path", GalleryFolder);

                using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                using var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                using var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                if (uri == null)
                {
                    return false;
                }

                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
                if (stream == null)
                {
                    return false;
                }

                stream.Call("write", image.Png);
                stream.Call("close");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Adding the image to the gallery failed: " + exception.Message);
                return false;
            }
        }

        private void SetState(ScreenshotState state, string message)
        {
            State = state;
            LastMessage = message ?? string.Empty;
            StateChanged?.Invoke(state);
        }

        private readonly struct EncodedImage
        {
            public EncodedImage(string fileName, string path, byte[] png)
            {
                FileName = fileName;
                Path = path;
                Png = png;
            }

            public string FileName { get; }
            public string Path { get; }
            public byte[] Png { get; }
        }
    }
}
