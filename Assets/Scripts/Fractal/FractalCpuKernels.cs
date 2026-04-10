using UnityEngine;

namespace FractalVisio.Fractal
{
    internal static class FractalCpuKernels
    {
        public static void RenderMandelbrotTile(
            Texture2D target,
            TileDescriptor tile,
            in FractalView view,
            int maxIterations,
            int sampleStep,
            Gradient gradient)
        {
            var rect = tile.PixelRect;
            var width = target.width;
            var height = target.height;
            var halfScale = view.scale.AsDouble * 0.5d;
            var aspect = (double)width / height;

            for (var y = rect.yMin; y < rect.yMax; y += sampleStep)
            {
                for (var x = rect.xMin; x < rect.xMax; x += sampleStep)
                {
                    var nx = (x + 0.5d) / width;
                    var ny = (y + 0.5d) / height;

                    var cx = view.x.AsDouble + ((nx - 0.5d) * 2d * halfScale * aspect);
                    var cy = view.y.AsDouble + ((ny - 0.5d) * 2d * halfScale);

                    var color = EvaluateMandelbrot(cx, cy, maxIterations, gradient);
                    FillBlock(target, x, y, sampleStep, sampleStep, color);
                }
            }
        }

        private static Color EvaluateMandelbrot(double cx, double cy, int maxIterations, Gradient gradient)
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
                return Color.black;
            }

            var t = iteration / (float)maxIterations;
            return gradient.Evaluate(t);
        }

        private static void FillBlock(Texture2D target, int xStart, int yStart, int blockWidth, int blockHeight, Color color)
        {
            var maxX = Mathf.Min(target.width, xStart + blockWidth);
            var maxY = Mathf.Min(target.height, yStart + blockHeight);

            for (var y = yStart; y < maxY; y++)
            {
                for (var x = xStart; x < maxX; x++)
                {
                    target.SetPixel(x, y, color);
                }
            }
        }
    }
}
