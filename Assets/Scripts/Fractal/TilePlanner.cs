using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.Fractal
{
    public static class TilePlanner
    {
        public static IEnumerable<TileDescriptor> BuildTiles(int width, int height, int tileSize)
        {
            var tileIndex = 0;

            for (var y = 0; y < height; y += tileSize)
            {
                for (var x = 0; x < width; x += tileSize)
                {
                    var w = Mathf.Min(tileSize, width - x);
                    var h = Mathf.Min(tileSize, height - y);
                    yield return new TileDescriptor(new RectInt(x, y, w, h), tileIndex++);
                }
            }
        }
    }
}
