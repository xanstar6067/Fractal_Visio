using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A palette drawn as a bar the width of the panel, with its colour stops as round handles under
    /// it. A tap picks the nearest stop; dragging sideways picks it and moves it. The first stop is
    /// where the ring starts and stays at 0 - the editor's offset slider turns the whole ring - and
    /// a stop never passes its neighbours, so the order and the selection stay what the user sees.
    ///
    /// Handles are rebuilt here when their number changes, not by rebuilding the panel: adding a
    /// colour must not throw the panel back to the top of its scroll.
    /// </summary>
    public sealed class GradientStrip
    {
        private const float BarHeight = 40f;
        private const float HandleRowHeight = 38f;
        private const float HandleSize = 22f;
        private const float SelectedHandleSize = 30f;

        /// <summary>Closest two stops may come, as a fraction of the ring.</summary>
        private const float MinimumGap = 0.01f;

        private readonly RectTransform handleRow;
        private readonly float trackStart;
        private readonly float trackWidth;
        private readonly List<Handle> handles = new();
        private readonly List<float> positions = new();
        private int selected = -1;
        private int dragging = -1;

        private GradientStrip(RectTransform handleRow, float trackStart, float trackWidth)
        {
            this.handleRow = handleRow;
            this.trackStart = trackStart;
            this.trackWidth = trackWidth;
        }

        /// <summary>A stop was tapped or picked up.</summary>
        public event Action<int> Selected;

        /// <summary>A stop was dragged to a new position, 0..1.</summary>
        public event Action<int, float> Moved;

        /// <summary>The finger lifted after moving a stop.</summary>
        public event Action Released;

        public static float MeasureHeight() => UiTheme.PanelPx(BarHeight + HandleRowHeight);

        public static GradientStrip Create(RectTransform parent, Texture palette, float x, float y, float width)
        {
            var barHeight = UiTheme.PanelPx(BarHeight);
            var rowHeight = UiTheme.PanelPx(HandleRowHeight);
            var inset = UiTheme.PanelPx(SelectedHandleSize) * 0.5f;

            var root = UiFactory.CreateImage("GradientStrip", parent, null, new Color(0f, 0f, 0f, 0f));
            root.raycastTarget = true;
            Place(root.rectTransform, x, y, width, barHeight + rowHeight);

            // Rounded by a mask rather than drawn square: the rest of the panel has no square corners.
            var radius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(UiTheme.PanelPx(UiTheme.SegmentRadius), barHeight * 0.4f)));
            var clip = UiFactory.CreateImage("BarClip", root.transform, UiSprites.Rounded(radius), Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Place(clip.rectTransform, inset, 0f, width - inset * 2f, barHeight);

            var bar = UiFactory.CreateRawImage("Bar", clip.transform);
            bar.texture = palette;
            UiFactory.Stretch(bar.rectTransform);

            var handleRow = UiFactory.CreateRect("Handles", root.transform);
            Place(handleRow, 0f, -barHeight, width, rowHeight);

            var strip = new GradientStrip(handleRow, inset, width - inset * 2f);

            var surface = root.gameObject.AddComponent<DragSurface>();
            surface.Horizontal = true;
            surface.Tapped = point => strip.Pick(point.x * width);
            surface.Dragged = point => strip.Drag(point.x * width);
            surface.Released = strip.EndDrag;
            return strip;
        }

        /// <summary>Show these stops, <paramref name="selectedIndex"/> highlighted (-1 for none).</summary>
        public void SetStops(IReadOnlyList<float> stopPositions, IReadOnlyList<Color32> colors, int selectedIndex)
        {
            while (handles.Count < stopPositions.Count)
            {
                handles.Add(Handle.Create(handleRow, handles.Count));
            }

            for (var i = 0; i < handles.Count; i++)
            {
                handles[i].Root.gameObject.SetActive(i < stopPositions.Count);
            }

            positions.Clear();
            for (var i = 0; i < stopPositions.Count; i++)
            {
                positions.Add(stopPositions[i]);
                handles[i].Fill.color = colors[i];
            }

            selected = selectedIndex;
            Layout();
        }

        private void Layout()
        {
            var rowHeight = handleRow.rect.height;
            for (var i = 0; i < positions.Count; i++)
            {
                var isSelected = i == selected;
                var size = UiTheme.PanelPx(isSelected ? SelectedHandleSize : HandleSize);
                var handle = handles[i];
                handle.Root.anchoredPosition = new Vector2(trackStart + positions[i] * trackWidth, -rowHeight * 0.5f);
                handle.Root.sizeDelta = new Vector2(size, size);
                handle.Ring.color = isSelected ? UiTheme.Text : new Color(1f, 1f, 1f, 0.55f);

                // The selected one on top, so a neighbour dragged close does not hide it.
                if (isSelected)
                {
                    handle.Root.SetAsLastSibling();
                }
            }
        }

        private int Nearest(float x)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < positions.Count; i++)
            {
                var distance = Mathf.Abs(trackStart + positions[i] * trackWidth - x);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void Pick(float x)
        {
            var index = Nearest(x);
            if (index >= 0)
            {
                Selected?.Invoke(index);
            }
        }

        private void Drag(float x)
        {
            if (dragging < 0)
            {
                dragging = Nearest(x);
                if (dragging < 0)
                {
                    return;
                }

                Selected?.Invoke(dragging);
            }

            if (dragging == 0 || trackWidth <= 0f)
            {
                return;
            }

            var lower = positions[dragging - 1] + MinimumGap;
            var upper = (dragging + 1 < positions.Count ? positions[dragging + 1] : 1f) - MinimumGap;
            var position = Mathf.Clamp((x - trackStart) / trackWidth, lower, Mathf.Max(lower, upper));
            if (!Mathf.Approximately(position, positions[dragging]))
            {
                Moved?.Invoke(dragging, position);
            }
        }

        private void EndDrag()
        {
            if (dragging >= 0)
            {
                dragging = -1;
                Released?.Invoke();
            }
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private sealed class Handle
        {
            public RectTransform Root;
            public Image Fill;
            public Image Ring;

            public static Handle Create(RectTransform parent, int index)
            {
                var size = UiTheme.PanelPx(SelectedHandleSize);
                var radius = Mathf.Max(1, Mathf.RoundToInt(size * 0.5f));

                // A dark halo under the white ring keeps the handle visible on a white stop as well
                // as on a black one.
                var halo = UiFactory.CreateImage("Stop" + index, parent, UiSprites.Rounded(radius), new Color(0f, 0f, 0f, 0.6f));
                var root = halo.rectTransform;
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(0f, 1f);
                root.pivot = new Vector2(0.5f, 0.5f);

                var fill = UiFactory.CreateImage("Fill", root, UiSprites.Rounded(radius), Color.white);
                UiFactory.Stretch(fill.rectTransform, Mathf.Max(2f, UiTheme.PanelPx(3.5f)));

                var ring = UiFactory.CreateImage(
                    "Ring", root, UiSprites.RoundedOutline(radius, Mathf.Max(1.5f, UiTheme.PanelPx(2.5f))), UiTheme.Text);
                UiFactory.Stretch(ring.rectTransform, Mathf.Max(1f, UiTheme.PanelPx(1f)));

                return new Handle { Root = root, Fill = fill, Ring = ring };
            }
        }
    }
}
