using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FractalVisio.App
{
    /// <summary>
    /// Copies a rectangle of a texture into RGBA32 pixels of a chosen size: a blit into a temporary
    /// render texture - which scales and crops in one step - then a read back to the CPU,
    /// asynchronous where the GPU supports it so the frame does not stall on it, synchronous where it
    /// does not. One grab at a time; <see cref="Tick"/> it every frame until it reports done.
    ///
    /// The pixels are in texture order - bottom row first - and in the colour space the render
    /// target stores, which is sRGB for an 8-bit target: ready for a PNG, and for a
    /// <c>Texture2D</c> created with <c>linear: false</c>.
    /// </summary>
    public sealed class FrameGrab : IDisposable
    {
        private RenderTexture target;
        private AsyncGPUReadbackRequest request;
        private bool waiting;

        public bool IsBusy => waiting;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>The copied pixels once a grab has finished, or null after a failure.</summary>
        public byte[] Pixels { get; private set; }

        /// <summary>Start copying <paramref name="uv"/> of <paramref name="source"/> at <paramref name="width"/> by <paramref name="height"/>.</summary>
        public bool Begin(Texture source, Rect uv, int width, int height)
        {
            if (waiting || source == null || width < 1 || height < 1)
            {
                return false;
            }

            Width = width;
            Height = height;
            Pixels = null;
            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, target, uv.size, uv.position);

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
                waiting = true;
                return true;
            }

            // Old graphics drivers need a synchronous read back.
            Pixels = ReadNow(target, width, height);
            ReleaseTarget();
            waiting = false;
            return true;
        }

        /// <summary>True on the frame the grab finishes - also for a synchronous one, which finishes in <see cref="Begin"/>.</summary>
        public bool Tick()
        {
            if (!waiting)
            {
                return false;
            }

            if (!request.done)
            {
                return false;
            }

            waiting = false;
            try
            {
                Pixels = request.hasError ? null : request.GetData<byte>().ToArray();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Reading back a frame failed: " + exception.Message);
                Pixels = null;
            }

            ReleaseTarget();
            return true;
        }

        public void Dispose()
        {
            if (waiting)
            {
                request.WaitForCompletion();
                waiting = false;
            }

            ReleaseTarget();
        }

        /// <summary>The <paramref name="side"/>-pixel square in the middle of <paramref name="uv"/>, for a source <paramref name="width"/> by <paramref name="height"/>.</summary>
        public static Rect CentreSquare(Rect uv, int width, int height)
        {
            var visibleWidth = width * uv.width;
            var visibleHeight = height * uv.height;
            if (!(visibleWidth > 0f) || !(visibleHeight > 0f))
            {
                return uv;
            }

            var side = Mathf.Min(visibleWidth, visibleHeight);
            var croppedWidth = uv.width * side / visibleWidth;
            var croppedHeight = uv.height * side / visibleHeight;
            return new Rect(
                uv.x + (uv.width - croppedWidth) * 0.5f,
                uv.y + (uv.height - croppedHeight) * 0.5f,
                croppedWidth,
                croppedHeight);
        }

        private static byte[] ReadNow(RenderTexture source, int width, int height)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = source;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                return texture.GetRawTextureData();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Reading back a frame failed: " + exception.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(texture);
            }
        }

        private void ReleaseTarget()
        {
            if (target != null)
            {
                RenderTexture.ReleaseTemporary(target);
                target = null;
            }
        }
    }
}
