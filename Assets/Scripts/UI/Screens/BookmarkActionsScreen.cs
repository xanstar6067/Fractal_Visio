using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// What can be done with one bookmark: rename it, open it, delete it. Opened by the "more"
    /// button of a bookmark - on a card in the gallery's "Saved", or on a row of the bookmarks panel
    /// - and it goes back to where it came from when it is done.
    ///
    /// Delete does not ask first: it removes at once and the toast offers Undo. A confirmation
    /// dialog is a second tap on every delete to protect the rare mistake; Undo costs nothing until
    /// the mistake happens.
    /// </summary>
    public sealed class BookmarkActionsScreen : BlockScreen
    {
        private const int NameLimit = 60;

        private readonly Action<Bookmark> open;
        private readonly Action<string, Action> showUndo;
        private IBookmarkService bookmarks;
        private Bookmark target;
        private Action back;
        private InputField nameInput;

        /// <param name="open">Opens a bookmark and gets the interface out of the way.</param>
        /// <param name="showUndo">Shows a message with an Undo that runs the action.</param>
        public BookmarkActionsScreen(Action<Bookmark> open, Action<string, Action> showUndo)
        {
            this.open = open;
            this.showUndo = showUndo;
        }

        /// <summary>
        /// Aim the sheet at <paramref name="bookmark"/>; the caller then opens it. <paramref name="returnTo"/>
        /// runs after a rename or a delete - back to the bookmarks panel it was opened from - or is null
        /// when what is underneath is still there, like the gallery.
        /// </summary>
        public void Show(Bookmark bookmark, Action returnTo)
        {
            target = bookmark;
            back = returnTo;
            NeedsRebuild = true;
        }

        protected override int MaximumColumns => 1;

        protected override string BuildTitle() => target != null ? target.name : Strings.Get("bookmarks.title");

        protected override void CollectBlocks(List<Block> blocks)
        {
            bookmarks = Services.Get<IBookmarkService>();
            nameInput = null;
            if (target == null || bookmarks == null)
            {
                return;
            }

            blocks.Add(new TextBlock(Describe(target)));
            blocks.Add(new RenameBlock(this));
            blocks.Add(new ActionBlock(Strings.Get("bookmarks.open"), OpenTarget, ActionStyle.Accent));
            blocks.Add(new ActionBlock(Strings.Get("bookmarks.delete"), Delete, ActionStyle.Danger));
        }

        protected override void OnTick()
        {
            // Removed from somewhere else meanwhile: nothing left to act on.
            if (target != null && bookmarks != null && !Contains(target))
            {
                target = null;
                Close();
            }
        }

        private bool Contains(Bookmark bookmark)
        {
            var items = bookmarks.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], bookmark))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>"Mandelbrot · 2026-09-24 18:30": what it is and when it was saved.</summary>
        private string Describe(Bookmark bookmark)
        {
            var name = bookmark.state?.fractal ?? string.Empty;
            var catalog = Services.Catalog;
            for (var i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Id == name)
                {
                    name = Strings.FractalName(catalog[i]);
                    break;
                }
            }

            var date = BookmarksScreen.DescribeDate(bookmark.createdTicks);
            return string.IsNullOrEmpty(date) ? name : name + "  ·  " + date;
        }

        private void OpenTarget()
        {
            var bookmark = target;
            Close();
            open?.Invoke(bookmark);
        }

        private void Rename()
        {
            if (target != null && nameInput != null)
            {
                bookmarks.Rename(target.id, nameInput.text);
            }

            Close();
            back?.Invoke();
        }

        private void Delete()
        {
            var bookmark = target;
            var index = bookmarks.Remove(bookmark.id);
            target = null;
            Close();
            back?.Invoke();
            if (index >= 0)
            {
                showUndo?.Invoke(
                    Strings.Format("bookmarks.removed", bookmark.name),
                    () => bookmarks.Restore(bookmark, index));
            }
        }

        /// <summary>The name, editable, and the button that keeps the edit.</summary>
        private sealed class RenameBlock : Block
        {
            private readonly BookmarkActionsScreen owner;

            public RenameBlock(BookmarkActionsScreen owner)
            {
                this.owner = owner;
            }

            public override float Measure(float width)
            {
                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                return UiTheme.PanelPx(20f) + gap * 0.5f + TextInputRow.MeasureHeight() + gap + ActionRow.MeasureHeight();
            }

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                var cursor = y - AddCaption(content, owner.Strings.Get("bookmarks.name"), x, y, width) - gap * 0.5f;
                owner.nameInput = TextInputRow.Create(content, owner.target.name, x, cursor, width, NameLimit);
                cursor -= TextInputRow.MeasureHeight() + gap;
                ActionRow.Create(content, owner.Strings.Get("bookmarks.rename"), x, cursor, width, owner.Rename);
            }
        }
    }
}
