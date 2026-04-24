using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FractalVisio.Fractal
{
    internal static class FractalCpuKernels
    {
        public static void RenderMandelbrotTile(
            Color32[] tileBuffer,
            int targetWidth,
            int targetHeight,
            TileDescriptor tile,
            in FractalView view,
            int maxIterations,
            int sampleStep,
            Gradient gradient)
        {
            var rect = tile.PixelRect;
            var width = targetWidth;
            var height = targetHeight;
            var halfScale = view.scale.AsDouble * 0.5d;
            var aspect = (double)width / height;

            var rectWidth = rect.width;
            var rectHeight = rect.height;
            ClearTile(tileBuffer, rectWidth, rectHeight);

            for (var y = rect.yMin; y < rect.yMax; y += sampleStep)
            {
                for (var x = rect.xMin; x < rect.xMax; x += sampleStep)
                {
                    var nx = (x + 0.5d) / width;
                    var ny = (y + 0.5d) / height;

                    var cx = view.x.AsDouble + ((nx - 0.5d) * 2d * halfScale * aspect);
                    var cy = view.y.AsDouble + ((ny - 0.5d) * 2d * halfScale);

                    var color = EvaluateMandelbrot(cx, cy, maxIterations, gradient);
                    FillBlock(tileBuffer, rectWidth, rectHeight, x - rect.xMin, y - rect.yMin, sampleStep, sampleStep, color);
                }
            }
        }

        public static void BlitTile(Texture2D target, TileDescriptor tile, Color32[] tileBuffer)
        {
            var rect = tile.PixelRect;
            target.SetPixels32(rect.x, rect.y, rect.width, rect.height, tileBuffer);
        }

        public static void RenderMandelbrotTileBurst(
            Color32[] managedTileBuffer,
            ref NativeArray<Color32> nativeTileBuffer,
            NativeArray<Color32> palette,
            int targetWidth,
            int targetHeight,
            TileDescriptor tile,
            in FractalView view,
            int maxIterations)
        {
            var rect = tile.PixelRect;
            var requiredLength = rect.width * rect.height;
            if (!nativeTileBuffer.IsCreated || nativeTileBuffer.Length != requiredLength)
            {
                if (nativeTileBuffer.IsCreated)
                {
                    nativeTileBuffer.Dispose();
                }

                nativeTileBuffer = new NativeArray<Color32>(requiredLength, Allocator.Persistent);
            }

            var job = new MandelbrotTileJob
            {
                Output = nativeTileBuffer,
                Palette = palette,
                TargetWidth = targetWidth,
                TargetHeight = targetHeight,
                RectX = rect.x,
                RectY = rect.y,
                RectWidth = rect.width,
                CenterX = view.x.AsDouble,
                CenterY = view.y.AsDouble,
                Scale = view.scale.AsDouble,
                MaxIterations = maxIterations
            };

            job.Schedule(requiredLength, 64).Complete();
            nativeTileBuffer.CopyTo(managedTileBuffer);
        }

        private static Color32 EvaluateMandelbrot(double cx, double cy, int maxIterations, Gradient gradient)
        {
            var zx = 0d;
            var zy = 0d;
            var iteration = 0;

            while (zx * zx + zy * zy <= 4d && iteration < maxIterations)
            {
                var xt = zx * zx - zy * zy + cx;
                zy = 2d * zx * zy + cy;
                zx = xt;
                iteration++;
            }

            if (iteration >= maxIterations)
            {
                return new Color32(0, 0, 0, 255);
            }

            var t = iteration / (float)maxIterations;
            return (Color32)gradient.Evaluate(t);
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct MandelbrotTileJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<Color32> Output;
            [ReadOnly] public NativeArray<Color32> Palette;

            public int TargetWidth;
            public int TargetHeight;
            public int RectX;
            public int RectY;
            public int RectWidth;
            public double CenterX;
            public double CenterY;
            public double Scale;
            public int MaxIterations;

            public void Execute(int index)
            {
                var localX = index % RectWidth;
                var localY = index / RectWidth;
                var x = RectX + localX;
                var y = RectY + localY;

                var halfScale = Scale * 0.5d;
                var aspect = (double)TargetWidth / TargetHeight;
                var nx = (x + 0.5d) / TargetWidth;
                var ny = (y + 0.5d) / TargetHeight;
                var cx = CenterX + ((nx - 0.5d) * 2d * halfScale * aspect);
                var cy = CenterY + ((ny - 0.5d) * 2d * halfScale);

                var zx = 0d;
                var zy = 0d;
                var iteration = 0;

                while (zx * zx + zy * zy <= 4d && iteration < MaxIterations)
                {
                    var xt = zx * zx - zy * zy + cx;
                    zy = 2d * zx * zy + cy;
                    zx = xt;
                    iteration++;
                }

                if (iteration >= MaxIterations)
                {
                    Output[index] = new Color32(0, 0, 0, 255);
                    return;
                }

                var palettePosition = (iteration * (double)(Palette.Length - 1)) / MaxIterations;
                var paletteIndex = (int)palettePosition;
                var nextPaletteIndex = paletteIndex + 1;
                if (nextPaletteIndex >= Palette.Length)
                {
                    nextPaletteIndex = paletteIndex;
                }

                var t = palettePosition - paletteIndex;
                var a = Palette[paletteIndex];
                var b = Palette[nextPaletteIndex];
                Output[index] = new Color32(
                    LerpByte(a.r, b.r, t),
                    LerpByte(a.g, b.g, t),
                    LerpByte(a.b, b.b, t),
                    LerpByte(a.a, b.a, t));
            }

            private static byte LerpByte(byte a, byte b, double t)
            {
                return (byte)(a + ((b - a) * t) + 0.5d);
            }
        }

        private static void FillBlock(Color32[] tileBuffer, int tileWidth, int tileHeight, int xStart, int yStart, int blockWidth, int blockHeight, Color32 color)
        {
            var maxX = Mathf.Min(tileWidth, xStart + blockWidth);
            var maxY = Mathf.Min(tileHeight, yStart + blockHeight);

            for (var y = yStart; y < maxY; y++)
            {
                var row = y * tileWidth;
                for (var x = xStart; x < maxX; x++)
                {
                    tileBuffer[row + x] = color;
                }
            }
        }

        private static void ClearTile(Color32[] tileBuffer, int tileWidth, int tileHeight)
        {
            var length = tileWidth * tileHeight;
            for (var i = 0; i < length; i++)
            {
                tileBuffer[i] = new Color32(0, 0, 0, 255);
            }
        }
    }
}
