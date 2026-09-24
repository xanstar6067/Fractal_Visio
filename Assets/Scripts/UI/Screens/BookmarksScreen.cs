using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using FractalVisio.App;

namespace FractalVisio.UI
{
    /// <summary>
    /// Saved pictures: save the current one, tap one to go back to it, and its "more" button for
    /// the rest - rename, delete (<see cref="BookmarkActionsScreen"/>). Each row shows the
    /// bookmark's preview; a preview still being taken appears when it is ready. A view of
    /// <see cref="IBookmarkService"/>; when the list changes the panel is rebuilt, because its
    /// height is its content.
    /// </summary>
    public sealed class BookmarksScreen : UiScreen
    {
        private readonly Action<Bookmark> showActions;
        private readonly List<(Bookmark Bookmark, ActionRow Row)> rows = new();
        private IBookmarkService bookmarks;
        private int builtVersion;
        private int version;

        /// <param name="showActions">Opens the actions of one bookmark.</param>
        public BookmarksScreen(Action<Bookmark> showActions)
        {
            this.showActions = showActions;
        }

        protected override void OnBuild(Transform parent)
        {
            bookmarks = Services.Get<IBookmarkService>();
            if (bookmarks != null)
            {
                bookmarks.Changed -= OnChanged;
                bookmarks.Changed += OnChanged;
            }

            builtVersion = version;
            rows.Clear();

            var width = ResolvePanelWidth(1, out _);
            var padding = UiTheme.PanelInset(width, UiTheme.PanelPadding, 0.06f);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            var titleHeight = UiTheme.PanelPx(28f);
            var rowWidth = width - padding * 2f;

            var count = bookmarks?.Items.Count ?? 0;
            var contentHeight = padding + titleHeight + UiTheme.PanelPx(UiTheme.SectionSpacing) +
                                ActionRow.MeasureHeight() + gap +
                                (count > 0
                                    ? UiTheme.PanelPx(20f) + count * (ActionRow.MeasureHeight(true) + gap)
                                    : UiTheme.PanelPx(40f)) +
                                padding;

            var content = CreateScrollingPanel(parent, "BookmarksPanel", width, contentHeight);
            AddTitle(content, Strings.Get("bookmarks.title"), padding, width);

            var cursor = -(padding + titleHeight + UiTheme.PanelPx(UiTheme.SectionSpacing));
            if (bookmarks == null)
            {
                AddCaption(content, Strings.Get("bookmarks.unavailable"), padding, cursor, rowWidth);
                return;
            }

            ActionRow.Create(content, Strings.Get("bookmarks.save_current"), padding, cursor, rowWidth, () => bookmarks.AddCurrent(), ActionStyle.Accent);
            cursor -= ActionRow.MeasureHeight() + gap;

            if (count == 0)
            {
                AddCaption(content, Strings.Get("bookmarks.empty"), padding, cursor - UiTheme.PanelPx(10f), rowWidth);
                return;
            }

            cursor -= AddCaption(content, Strings.Get("bookmarks.saved"), padding, cursor, rowWidth);

            for (var i = 0; i < count; i++)
            {
                cursor -= gap;
                var item = bookmarks.Items[i];
                var row = ActionRow.Create(
                    content,
                    item.name,
                    padding,
                    cursor,
                    rowWidth,
                    () => bookmarks.Open(item),
                    detail: DescribeDate(item.createdTicks),
                    onTrailing: () => showActions?.Invoke(item),
                    trailingGlyph: TrailingGlyph.More,
                    leadingImage: true);
                row.SetLeading(bookmarks.GetPreview(item));
                rows.Add((item, row));
                cursor -= ActionRow.MeasureHeight(true);
            }
        }

        protected override void OnTick()
        {
            if (version != builtVersion)
            {
                NeedsRebuild = true;
                return;
            }

            // A preview taken since the build - the bookmark just saved - shows up in its row.
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Row.SetLeading(bookmarks.GetPreview(rows[i].Bookmark));
            }
        }

        public override void Dispose()
        {
            if (bookmarks != null)
            {
                bookmarks.Changed -= OnChanged;
            }

            rows.Clear();
            base.Dispose();
        }

        private void OnChanged()
        {
            version++;
            if (!IsVisible)
            {
                // Hidden: nothing to redraw now, but the next open must not show the old list.
                NeedsRebuild = true;
            }
        }

        /// <summary>"2026-09-24  18:30" in local time, the same in every language.</summary>
        internal static string DescribeDate(long ticks)
        {
            if (ticks <= 0)
            {
                return string.Empty;
            }

            var local = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            return local.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
