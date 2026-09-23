using System;
using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.UI
{
    /// <summary>
    /// A panel made of blocks - option lists, sliders, actions, a paragraph - laid out in one to
    /// three balanced columns. The fractal, colour and settings panels are all this with different
    /// blocks: a screen lists its blocks and reads and writes the session, and layout, scrolling and
    /// touch targets are handled here once.
    ///
    /// Adding a setting is one block plus the lines that read and write it on the session. A new
    /// control type is a widget in <c>UI/Widgets</c> and a block wrapping it, never rows laid out by
    /// hand in a screen.
    /// </summary>
    public abstract class BlockScreen : UiScreen
    {
        /// <summary>Most columns to split into. Past three the rows get too narrow to read.</summary>
        protected virtual int MaximumColumns => 3;

        /// <summary>Title at the top of the panel, read while building.</summary>
        protected abstract string BuildTitle();

        /// <summary>The panel's blocks, top to bottom; the planner balances them across columns.</summary>
        protected abstract void CollectBlocks(List<Block> blocks);

        protected override void OnBuild(Transform parent)
        {
            var blocks = new List<Block>();
            CollectBlocks(blocks);

            var width = ResolvePanelWidth(MaximumColumns, out var columns);
            var padding = UiTheme.PanelInset(width, UiTheme.PanelPadding, 0.06f);
            var sectionGap = Mathf.Min(UiTheme.PanelPx(UiTheme.SectionSpacing), UiTheme.AvailablePanelHeight * 0.04f);
            var titleHeight = UiTheme.PanelPx(28f);
            var columnWidth = (width - padding * 2f - padding * (columns - 1)) / columns;

            // Shortest column first, so blocks of different lengths end up balanced instead of one
            // column running off the bottom while the next is half empty.
            var cursors = new float[columns];
            var top = -(padding + titleHeight + sectionGap);
            for (var i = 0; i < columns; i++)
            {
                cursors[i] = top;
            }

            var plan = new int[blocks.Count];
            for (var i = 0; i < blocks.Count; i++)
            {
                var column = ShortestColumn(cursors);
                plan[i] = column;
                cursors[column] -= blocks[i].Measure(columnWidth) + sectionGap;
            }

            var tallest = 0f;
            for (var i = 0; i < columns; i++)
            {
                tallest = Mathf.Max(tallest, -cursors[i]);
            }

            var contentHeight = tallest - sectionGap + padding;
            var content = CreateScrollingPanel(parent, GetType().Name, width, contentHeight);
            AddTitle(content, BuildTitle(), padding, width);

            for (var i = 0; i < columns; i++)
            {
                cursors[i] = top;
            }

            for (var i = 0; i < blocks.Count; i++)
            {
                var column = plan[i];
                var x = padding + column * (columnWidth + padding);
                blocks[i].Build(content, x, cursors[column], columnWidth);
                cursors[column] -= blocks[i].Measure(columnWidth) + sectionGap;
            }
        }

        private static int ShortestColumn(float[] cursors)
        {
            var best = 0;
            for (var i = 1; i < cursors.Length; i++)
            {
                if (cursors[i] > cursors[best])
                {
                    best = i;
                }
            }

            return best;
        }

        protected static string[] Names<T>(IReadOnlyList<T> items, Func<T, string> name)
        {
            var names = new string[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                names[i] = name(items[i]);
            }

            return names;
        }

        /// <summary>Index of the value in <paramref name="values"/> within 0.01 of <paramref name="target"/>, or -1.</summary>
        protected static int NearestIndex(float[] values, float target)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < values.Length; i++)
            {
                var distance = Mathf.Abs(values[i] - target);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = i;
            }

            return bestDistance <= 0.01f ? best : -1;
        }

        /// <summary>One piece of a panel that the column planner can measure and place.</summary>
        protected abstract class Block
        {
            /// <summary>Height in device pixels at the given column width.</summary>
            public abstract float Measure(float width);

            public abstract void Build(RectTransform content, float x, float y, float width);
        }

        /// <summary>
        /// A <see cref="SettingsSection"/> - a caption over mutually exclusive rows - optionally
        /// followed by one action row. <paramref name="swatches"/>, if given, draws a colour strip
        /// in each row: the palette list shows the palettes, not only their names.
        /// </summary>
        protected sealed class OptionsBlock : Block
        {
            private readonly string label;
            private readonly IReadOnlyList<string> options;
            private readonly Action<int> onSelect;
            private readonly Action<SettingsSection> onBuilt;
            private readonly string actionLabel;
            private readonly Action action;
            private readonly IReadOnlyList<Texture> swatches;

            public OptionsBlock(
                string label,
                IReadOnlyList<string> options,
                Action<int> onSelect,
                Action<SettingsSection> onBuilt,
                string actionLabel = null,
                Action action = null,
                IReadOnlyList<Texture> swatches = null)
            {
                this.label = label;
                this.options = options;
                this.onSelect = onSelect;
                this.onBuilt = onBuilt;
                this.actionLabel = actionLabel;
                this.action = action;
                this.swatches = swatches;
            }

            private bool HasAction => actionLabel != null && action != null;

            public override float Measure(float width)
            {
                var height = SettingsSection.MeasureHeight(options.Count);
                if (HasAction)
                {
                    height += UiTheme.PanelPx(UiTheme.RowSpacing) + ActionRow.MeasureHeight();
                }

                return height;
            }

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var section = SettingsSection.Create(content, label, options, onSelect, x, y, width, swatches);
                onBuilt?.Invoke(section);

                if (HasAction)
                {
                    var actionY = y - section.Height - UiTheme.PanelPx(UiTheme.RowSpacing);
                    ActionRow.Create(content, actionLabel, x, actionY, width, action, ActionStyle.Accent);
                }
            }
        }

        /// <summary>One full-width command.</summary>
        protected sealed class ActionBlock : Block
        {
            private readonly string label;
            private readonly Action action;
            private readonly ActionStyle style;

            public ActionBlock(string label, Action action, ActionStyle style = ActionStyle.Normal)
            {
                this.label = label;
                this.action = action;
                this.style = style;
            }

            public override float Measure(float width) => ActionRow.MeasureHeight();

            public override void Build(RectTransform content, float x, float y, float width)
            {
                ActionRow.Create(content, label, x, y, width, action, style);
            }
        }

        /// <summary>A paragraph in the muted style: what this fractal is, what a section does.</summary>
        protected sealed class TextBlock : Block
        {
            private static readonly TextGenerator Generator = new();

            private readonly string text;

            public TextBlock(string text)
            {
                this.text = text ?? string.Empty;
            }

            public override float Measure(float width)
            {
                // Exactly what the Text component will lay out, measured before it exists: the
                // planner needs heights before anything is built.
                var settings = new TextGenerationSettings
                {
                    font = UiTheme.Font,
                    fontSize = FontSize,
                    fontStyle = FontStyle.Normal,
                    lineSpacing = 1f,
                    richText = false,
                    scaleFactor = 1f,
                    textAnchor = TextAnchor.UpperLeft,
                    horizontalOverflow = HorizontalWrapMode.Wrap,
                    verticalOverflow = VerticalWrapMode.Overflow,
                    generationExtents = new Vector2(Mathf.Max(1f, width), 0f),
                    pivot = Vector2.zero,
                    generateOutOfBounds = true,
                    color = Color.white
                };

                return Mathf.Ceil(Generator.GetPreferredHeight(text, settings)) + UiTheme.PanelPx(2f);
            }

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var paragraph = UiFactory.CreateText(
                    "Paragraph", content, text, UiTheme.LabelFontSize + 1, UiTheme.TextMuted,
                    TextAnchor.UpperLeft, panelScale: true);
                paragraph.horizontalOverflow = HorizontalWrapMode.Wrap;
                Place(paragraph.rectTransform, x, y, width, Measure(width));
            }

            private static int FontSize => Mathf.Max(8, Mathf.RoundToInt(UiTheme.PanelPx(UiTheme.LabelFontSize + 1)));
        }
    }
}
