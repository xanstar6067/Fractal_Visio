using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The main menu: every fractal as a card with a live preview, grouped by section, on a sheet
    /// of frosted glass over the picture. It opens when the app starts and whenever the explorer's
    /// gallery button is tapped.
    ///
    /// Built for a finger. Cards are at least <see cref="UiTheme.CardMinimumWidth"/> dp wide - two
    /// columns on a phone held upright, up to six on a tablet - and the whole card is the target.
    /// The favourite star is a separate 48 dp target in the corner of the preview. Filters are a
    /// row of chips that scrolls sideways; the grid scrolls down. A tap that becomes a scroll does
    /// not open anything (see the drag threshold in <c>UiRouter</c>).
    ///
    /// At the top of "All" sits a Continue card showing the frame on screen right now: the app
    /// starts here, and the first thing most people want is to go back to where they were.
    /// </summary>
    public sealed class GalleryScreen : UiScreen
    {
        private enum Filter
        {
            All,
            Favorites,
            Recent,
            Section
        }

        private readonly Action openSettings;
        private readonly List<CardView> cards = new();
        private readonly List<ChipView> chips = new();
        private readonly List<string> sections = new();

        private IGalleryPreferences preferences;
        private IFractalThumbnails thumbnails;

        // What the user was looking at. Kept on the screen object, which outlives its GameObjects,
        // so a rotation or a language change does not throw them back to "All".
        private Filter filter = Filter.All;
        private string filterSection;

        private RectTransform gridViewport;
        private ScrollRect gridScroll;
        private RectTransform gridContent;
        private float gridViewportHeight;
        private int preferencesVersion;
        private int shownPreferencesVersion;
        private IFractalDefinition shownCurrent;

        private RawImage continueFrame;
        private Text continueTitle;
        private float continueAspect = 1f;

        // Grid metrics, device pixels, set while building.
        private float left;
        private float gridWidth;
        private int columns;
        private float cardWidth;
        private float cardHeight;
        private float spacing;

        public GalleryScreen(Action openSettings)
        {
            this.openSettings = openSettings;
        }

        protected override void OnBuild(Transform parent)
        {
            if (preferences != null)
            {
                preferences.Changed -= OnPreferencesChanged;
            }

            preferences = Services.Get<IGalleryPreferences>();
            thumbnails = Services.Get<IFractalThumbnails>();
            if (preferences != null)
            {
                preferences.Changed += OnPreferencesChanged;
            }

            CollectSections();

            // Full screen, square corners: the screen's own corners are the panel's.
            Panel = GlassPanel.Create("Gallery", parent, 0f, UiTheme.GalleryTint, Color.clear);
            UiFactory.Stretch(Panel.Root);

            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            left = UiTheme.SafeLeft + margin;
            var right = UiTheme.SafeRight + margin;
            var top = UiTheme.SafeTop + margin;
            gridWidth = Mathf.Max(64f, Screen.width - left - right);

            var headerHeight = UiTheme.Px(UiTheme.SegmentHeight);
            BuildHeader(Panel.Content, top, right, headerHeight);

            var chipsTop = top + headerHeight + UiTheme.Px(10f);
            var chipsHeight = UiTheme.Px(UiTheme.ChipHeight);
            BuildChips(Panel.Content, chipsTop, chipsHeight, right);

            var gridTop = chipsTop + chipsHeight + UiTheme.Px(8f);
            BuildGridViewport(Panel.Content, gridTop);
            ResolveCardMetrics();
            RebuildGrid(false);
        }

        protected override void OnTick()
        {
            if (preferencesVersion != shownPreferencesVersion)
            {
                if (filter == Filter.Favorites || filter == Filter.Recent)
                {
                    // The list itself changed: a card came or went.
                    RebuildGrid(true);
                }
                else
                {
                    shownPreferencesVersion = preferencesVersion;
                    for (var i = 0; i < cards.Count; i++)
                    {
                        cards[i].RefreshStar(preferences);
                    }
                }
            }

            if (!ReferenceEquals(Services.Session.Definition, shownCurrent))
            {
                RebuildGrid(true);
            }

            // Previews are asked for every frame the gallery is on screen: that is what keeps them
            // wanted, and what picks up a redraw after a palette change or a new card size.
            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].RefreshThumbnail(thumbnails);
            }

            RefreshContinueCard();
        }

        public override void Dispose()
        {
            if (preferences != null)
            {
                preferences.Changed -= OnPreferencesChanged;
            }

            cards.Clear();
            chips.Clear();
            gridContent = null;
            gridScroll = null;
            gridViewport = null;
            continueFrame = null;
            continueTitle = null;
            base.Dispose();
        }

        private void OnPreferencesChanged()
        {
            preferencesVersion++;
        }

        private void CollectSections()
        {
            sections.Clear();
            var gallery = Services.Gallery;
            for (var i = 0; i < gallery.Count; i++)
            {
                var section = gallery[i].Section;
                if (!string.IsNullOrEmpty(section) && !sections.Contains(section))
                {
                    sections.Add(section);
                }
            }

            // A section filter from before an update that removed the section falls back to all.
            if (filter == Filter.Section && !sections.Contains(filterSection))
            {
                filter = Filter.All;
                filterSection = null;
            }
        }

        // ---------------------------------------------------------------- header and filters

        private void BuildHeader(RectTransform parent, float top, float right, float height)
        {
            var title = UiFactory.CreateText(
                "Title", parent, Strings.Get("gallery.title"), UiTheme.TitleFontSize + 4, UiTheme.Text,
                TextAnchor.MiddleLeft, fitToRect: true);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, left, -top, gridWidth - height - UiTheme.Px(8f), height);

            var button = UiFactory.CreateImage(
                "Settings", parent, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(height * 0.5f))), UiTheme.CardFill);
            button.raycastTarget = true;
            UiFactory.Anchor(
                button.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-right, -top), new Vector2(height, height));
            AttachButton(button, openSettings);

            var iconSize = Mathf.Round(height * 0.5f);
            var icon = UiFactory.CreateImage("Icon", button.transform, null, UiTheme.Text);
            icon.sprite = UiSprites.Gear(Mathf.RoundToInt(iconSize));
            icon.type = Image.Type.Simple;
            UiFactory.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
        }

        /// <summary>
        /// A row of filter chips that scrolls sideways, edge to edge: the first chip lines up with
        /// the cards, the last can scroll out from under the right margin.
        /// </summary>
        private void BuildChips(RectTransform parent, float top, float height, float right)
        {
            chips.Clear();

            var viewport = UiFactory.CreateRect("Filters", parent);
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(1f, 1f);
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.anchoredPosition = new Vector2(0f, -top);
            viewport.sizeDelta = new Vector2(0f, height);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = UiFactory.CreateRect("FiltersContent", viewport);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;

            // Catches a sideways drag that starts between two chips.
            var dragArea = UiFactory.CreateImage("DragArea", content, null, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(dragArea.rectTransform);
            dragArea.raycastTarget = true;

            var x = left;
            var gap = UiTheme.Px(8f);
            x = AddChip(content, x, height, Filter.All, null, Strings.Get("gallery.filter.all")) + gap;
            if (preferences != null)
            {
                x = AddChip(content, x, height, Filter.Favorites, null, Strings.Get("gallery.filter.favorites")) + gap;
                x = AddChip(content, x, height, Filter.Recent, null, Strings.Get("gallery.filter.recent")) + gap;
            }

            for (var i = 0; i < sections.Count; i++)
            {
                x = AddChip(content, x, height, Filter.Section, sections[i], Strings.SectionName(sections[i])) + gap;
            }

            var contentWidth = x - gap + right;
            content.sizeDelta = new Vector2(contentWidth, 0f);

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = contentWidth > Screen.width + 1f;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            RefreshChips();
        }

        /// <summary>One chip: a 48 dp touch target around a smaller capsule. Returns its right edge.</summary>
        private float AddChip(RectTransform content, float x, float height, Filter kind, string section, string label)
        {
            var text = UiFactory.CreateText(
                "Label", content, label, UiTheme.SegmentFontSize - 3, UiTheme.Text, TextAnchor.MiddleCenter);
            var width = text.preferredWidth + UiTheme.Px(32f);

            var hit = UiFactory.CreateImage("Chip_" + (section ?? kind.ToString()), content, null, new Color(0f, 0f, 0f, 0f));
            hit.raycastTarget = true;
            hit.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            hit.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            hit.rectTransform.pivot = new Vector2(0f, 0.5f);
            hit.rectTransform.anchoredPosition = new Vector2(x, 0f);
            hit.rectTransform.sizeDelta = new Vector2(width, height);

            var capsuleHeight = height * 0.8f;
            var capsule = UiFactory.CreateImage(
                "Capsule", hit.transform, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(capsuleHeight * 0.5f))), UiTheme.CardFill);
            UiFactory.Anchor(capsule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, capsuleHeight));

            text.transform.SetParent(capsule.transform, false);
            UiFactory.Stretch(text.rectTransform);

            AttachButton(capsule, () => SelectFilter(kind, section), hit);
            chips.Add(new ChipView(kind, section, capsule, text));
            return x + width;
        }

        private void SelectFilter(Filter kind, string section)
        {
            if (kind == filter && (kind != Filter.Section || section == filterSection))
            {
                return;
            }

            filter = kind;
            filterSection = kind == Filter.Section ? section : null;
            RefreshChips();
            RebuildGrid(false);
        }

        private void RefreshChips()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var chip = chips[i];
                var selected = chip.Kind == filter && (filter != Filter.Section || chip.Section == filterSection);
                chip.Capsule.color = selected ? UiTheme.SegmentSelected : UiTheme.CardFill;
                chip.Label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                chip.Label.color = selected ? UiTheme.Text : UiTheme.TextMuted;
            }
        }

        // ---------------------------------------------------------------- grid

        private void BuildGridViewport(RectTransform parent, float top)
        {
            gridViewport = UiFactory.CreateRect("Grid", parent);
            gridViewport.anchorMin = Vector2.zero;
            gridViewport.anchorMax = Vector2.one;
            gridViewport.offsetMin = Vector2.zero;
            gridViewport.offsetMax = new Vector2(0f, -top);
            gridViewport.gameObject.AddComponent<RectMask2D>();
            gridViewportHeight = Mathf.Max(1f, Screen.height - top);

            gridScroll = gridViewport.gameObject.AddComponent<ScrollRect>();
            gridScroll.viewport = gridViewport;
            gridScroll.horizontal = false;
            gridScroll.movementType = ScrollRect.MovementType.Elastic;
            gridScroll.elasticity = 0.1f;
            gridScroll.inertia = true;
            gridScroll.decelerationRate = 0.135f;
            gridScroll.scrollSensitivity = UiTheme.Px(40f);
        }

        private void ResolveCardMetrics()
        {
            spacing = UiTheme.Px(UiTheme.CardSpacing);
            var minimum = UiTheme.Px(UiTheme.CardMinimumWidth);
            columns = Mathf.Clamp(Mathf.FloorToInt((gridWidth + spacing) / (minimum + spacing)), 1, UiTheme.CardMaximumColumns);
            cardWidth = (gridWidth - spacing * (columns - 1)) / columns;

            // A square preview, then the name and three lines of description.
            cardHeight = cardWidth + UiTheme.Px(84f);
        }

        /// <summary>
        /// Lay the grid out again for the current filter. <paramref name="keepScroll"/> holds the
        /// scroll position - a star toggled halfway down must not jump the list to the top - while
        /// a new filter starts from the top.
        /// </summary>
        private void RebuildGrid(bool keepScroll)
        {
            if (gridScroll == null)
            {
                return;
            }

            var position = keepScroll ? gridScroll.verticalNormalizedPosition : 1f;
            if (gridContent != null)
            {
                UnityEngine.Object.Destroy(gridContent.gameObject);
            }

            cards.Clear();
            continueFrame = null;
            continueTitle = null;
            shownPreferencesVersion = preferencesVersion;
            shownCurrent = Services.Session.Definition;

            gridContent = UiFactory.CreateRect("GridContent", gridViewport);
            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(0.5f, 1f);
            gridContent.anchoredPosition = Vector2.zero;

            var dragArea = UiFactory.CreateImage("DragArea", gridContent, null, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(dragArea.rectTransform);
            dragArea.raycastTarget = true;

            var cursor = -UiTheme.Px(4f);
            var entries = FilteredEntries();

            if (filter == Filter.All)
            {
                cursor = AddContinueCard(cursor);
                for (var s = 0; s < sections.Count; s++)
                {
                    var inSection = new List<CatalogEntry>();
                    for (var i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].Section == sections[s])
                        {
                            inSection.Add(entries[i]);
                        }
                    }

                    if (inSection.Count == 0)
                    {
                        continue;
                    }

                    cursor = AddSectionHeader(Strings.SectionName(sections[s]), cursor);
                    cursor = AddCards(inSection, cursor) - UiTheme.Px(20f);
                }
            }
            else if (entries.Count == 0)
            {
                cursor = AddEmptyState(cursor);
            }
            else
            {
                cursor = AddCards(entries, cursor);
            }

            var height = -cursor + UiTheme.SafeBottom + UiTheme.Px(UiTheme.ScreenMargin) * 2f;
            gridContent.sizeDelta = new Vector2(0f, height);
            gridScroll.content = gridContent;
            gridScroll.vertical = height > gridViewportHeight + 1f;
            gridScroll.StopMovement();
            gridScroll.verticalNormalizedPosition = position;
        }

        private List<CatalogEntry> FilteredEntries()
        {
            var result = new List<CatalogEntry>();
            var gallery = Services.Gallery;

            if (filter == Filter.Recent)
            {
                // In the order they were opened, newest first - not in catalog order.
                var recent = preferences != null ? preferences.Recent : Array.Empty<string>();
                for (var r = 0; r < recent.Count; r++)
                {
                    for (var i = 0; i < gallery.Count; i++)
                    {
                        if (gallery[i].Id == recent[r])
                        {
                            result.Add(gallery[i]);
                            break;
                        }
                    }
                }

                return result;
            }

            for (var i = 0; i < gallery.Count; i++)
            {
                var entry = gallery[i];
                var include = filter switch
                {
                    Filter.Favorites => preferences != null && preferences.IsFavorite(entry.Id),
                    Filter.Section => entry.Section == filterSection,
                    _ => true
                };

                if (include)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private float AddSectionHeader(string text, float cursor)
        {
            var height = UiTheme.Px(34f);
            var header = UiFactory.CreateText(
                "Section_" + text, gridContent, text, UiTheme.CardTitleFontSize + 2, UiTheme.Text,
                TextAnchor.LowerLeft, fitToRect: true);
            header.fontStyle = FontStyle.Bold;
            Place(header.rectTransform, left, cursor, gridWidth, height);
            return cursor - height - UiTheme.Px(10f);
        }

        private float AddEmptyState(float cursor)
        {
            var key = filter == Filter.Favorites ? "gallery.empty.favorites" : "gallery.empty.recent";
            var height = UiTheme.Px(90f);
            var text = UiFactory.CreateText(
                "Empty", gridContent, Strings.Get(key), UiTheme.SegmentFontSize - 2, UiTheme.TextMuted,
                TextAnchor.MiddleCenter, fitToRect: true);
            Place(text.rectTransform, left, cursor - UiTheme.Px(24f), gridWidth, height);
            return cursor - UiTheme.Px(24f) - height;
        }

        private float AddCards(IReadOnlyList<CatalogEntry> entries, float cursor)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var x = left + column * (cardWidth + spacing);
                var y = cursor - row * (cardHeight + spacing);
                cards.Add(BuildCard(entries[i], x, y));
            }

            var rows = (entries.Count + columns - 1) / columns;
            return cursor - rows * cardHeight - Mathf.Max(0, rows - 1) * spacing;
        }

        private CardView BuildCard(in CatalogEntry entry, float x, float y)
        {
            var definition = entry.Definition;
            var radius = UiTheme.PxInt(UiTheme.CardRadius);
            var padding = UiTheme.Px(10f);

            var card = UiFactory.CreateImage("Card_" + entry.Id, gridContent, UiSprites.Rounded(radius), UiTheme.CardFill);
            card.raycastTarget = true;
            Place(card.rectTransform, x, y, cardWidth, cardHeight);
            var captured = entry;
            AttachButton(card, () => Open(captured));

            // The preview, clipped to the card's corners. It stays hidden until drawn: a RawImage
            // without a texture paints white, and a white flash per card is worse than a dark tile.
            var clip = UiFactory.CreateImage("PreviewClip", card.transform, UiSprites.Rounded(radius), Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Place(clip.rectTransform, 0f, 0f, cardWidth, cardWidth);
            var preview = UiFactory.CreateRawImage("Preview", clip.transform);
            UiFactory.Stretch(preview.rectTransform);
            preview.enabled = false;

            var name = UiFactory.CreateText(
                "Name", card.transform, Strings.FractalName(definition), UiTheme.CardTitleFontSize, UiTheme.Text,
                TextAnchor.MiddleLeft, fitToRect: true);
            name.fontStyle = FontStyle.Bold;
            Place(name.rectTransform, padding, -(cardWidth + UiTheme.Px(6f)), cardWidth - padding * 2f, UiTheme.Px(24f));

            var about = UiFactory.CreateText(
                "About", card.transform, Strings.FractalDescription(definition), UiTheme.CardDetailFontSize, UiTheme.TextMuted,
                TextAnchor.UpperLeft, fitToRect: true);
            Place(about.rectTransform, padding, -(cardWidth + UiTheme.Px(30f)), cardWidth - padding * 2f, UiTheme.Px(48f));

            if (ReferenceEquals(definition, Services.Session.Definition))
            {
                // The fractal open right now: the card to tap to carry on with it.
                var outline = UiFactory.CreateImage(
                    "Current", card.transform, UiSprites.RoundedOutline(radius, Mathf.Max(2f, UiTheme.Px(2f))), UiTheme.Accent);
                UiFactory.Stretch(outline.rectTransform);
            }

            Image star = null;
            if (preferences != null)
            {
                star = BuildStar(card.transform, entry.Id);
            }

            var view = new CardView(entry, preview, star, Mathf.RoundToInt(cardWidth));
            view.RefreshStar(preferences);
            return view;
        }

        /// <summary>The favourite toggle: a 48 dp target in the preview's top-right corner, with a dark disc so the star reads on any picture.</summary>
        private Image BuildStar(Transform card, string id)
        {
            var target = UiTheme.Px(48f);
            var hit = UiFactory.CreateImage("Favorite", card, null, new Color(0f, 0f, 0f, 0f));
            hit.raycastTarget = true;
            UiFactory.Anchor(hit.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(target, target));

            var discSize = UiTheme.Px(32f);
            var disc = UiFactory.CreateImage(
                "Disc", hit.transform, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(discSize * 0.5f))), UiTheme.Scrim);
            UiFactory.Anchor(disc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(discSize, discSize));

            var iconSize = Mathf.Round(UiTheme.Px(20f));
            var icon = UiFactory.CreateImage("Star", disc.transform, null, UiTheme.Text);
            icon.type = Image.Type.Simple;
            UiFactory.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));

            AttachButton(disc, () => preferences.SetFavorite(id, !preferences.IsFavorite(id)), hit);
            return icon;
        }

        /// <summary>
        /// The picture on screen right now, the fractal's name and how deep it is. Tapping it closes
        /// the gallery - the picture is already there, still rendering behind the glass.
        /// </summary>
        private float AddContinueCard(float cursor)
        {
            // Wide but never most of the view: on a phone on its side the grid below has to show.
            var height = Mathf.Min(UiTheme.Px(190f), Mathf.Min(gridWidth * 0.5f, gridViewportHeight * 0.45f));
            var radius = UiTheme.PxInt(UiTheme.CardRadius);
            continueAspect = gridWidth / Mathf.Max(1f, height);

            var card = UiFactory.CreateImage("Continue", gridContent, UiSprites.Rounded(radius), UiTheme.CardFill);
            card.raycastTarget = true;
            Place(card.rectTransform, left, cursor, gridWidth, height);
            AttachButton(card, Close);

            var clip = UiFactory.CreateImage("Clip", card.transform, UiSprites.Rounded(radius), Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            UiFactory.Stretch(clip.rectTransform);

            continueFrame = UiFactory.CreateRawImage("Frame", clip.transform);
            UiFactory.Stretch(continueFrame.rectTransform);
            continueFrame.enabled = false;

            var scrimHeight = Mathf.Max(UiTheme.Px(62f), height * 0.42f);
            var scrim = UiFactory.CreateImage("Scrim", clip.transform, null, UiTheme.Scrim);
            var scrimRect = scrim.rectTransform;
            scrimRect.anchorMin = new Vector2(0f, 0f);
            scrimRect.anchorMax = new Vector2(1f, 0f);
            scrimRect.pivot = new Vector2(0.5f, 0f);
            scrimRect.anchoredPosition = Vector2.zero;
            scrimRect.sizeDelta = new Vector2(0f, scrimHeight);

            var padding = UiTheme.Px(14f);
            var arrowSize = Mathf.Min(UiTheme.Px(40f), scrimHeight * 0.7f);
            var textWidth = gridWidth - padding * 3f - arrowSize;

            var caption = UiFactory.CreateText(
                "Caption", scrim.transform, Strings.Get("gallery.continue"), UiTheme.LabelFontSize - 1, UiTheme.TextMuted,
                TextAnchor.LowerLeft, fitToRect: true);
            Place(caption.rectTransform, padding, -scrimHeight * 0.14f, textWidth, scrimHeight * 0.34f);

            continueTitle = UiFactory.CreateText(
                "Title", scrim.transform, string.Empty, UiTheme.CardTitleFontSize + 2, UiTheme.Text,
                TextAnchor.UpperLeft, fitToRect: true);
            continueTitle.fontStyle = FontStyle.Bold;
            Place(continueTitle.rectTransform, padding, -scrimHeight * 0.5f, textWidth, scrimHeight * 0.42f);

            var arrow = UiFactory.CreateImage(
                "Go", scrim.transform, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(arrowSize * 0.5f))), UiTheme.SegmentSelected);
            UiFactory.Anchor(arrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-padding, 0f), new Vector2(arrowSize, arrowSize));
            var chevronSize = Mathf.Round(arrowSize * 0.55f);
            var chevron = UiFactory.CreateImage("Chevron", arrow.transform, null, UiTheme.Text);
            chevron.sprite = UiSprites.Chevron(Mathf.RoundToInt(chevronSize));
            chevron.type = Image.Type.Simple;
            UiFactory.Anchor(chevron.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(chevronSize, chevronSize));

            RefreshContinueCard();
            return cursor - height - UiTheme.Px(22f);
        }

        private void RefreshContinueCard()
        {
            if (continueFrame == null)
            {
                return;
            }

            var backdrop = Services.Backdrop;
            var texture = backdrop != null ? backdrop.Texture : null;
            continueFrame.texture = texture;
            continueFrame.enabled = texture != null;
            if (texture != null)
            {
                continueFrame.uvRect = CoverCrop(backdrop.UvRect, Screen.width / (float)Mathf.Max(1, Screen.height), continueAspect);
            }

            var session = Services.Session;
            var reference = session.DefaultView.scale.AsDouble;
            var scale = session.View.scale.AsDouble;
            var zoom = scale > 0d ? reference / scale : 1d;
            var depth = zoom < 1000d
                ? zoom.ToString("0.#", CultureInfo.InvariantCulture)
                : zoom.ToString("0.#e+00", CultureInfo.InvariantCulture);
            var title = Strings.Format("gallery.continue_detail", Strings.FractalName(session.Definition), depth);
            if (continueTitle.text != title)
            {
                continueTitle.text = title;
            }
        }

        /// <summary>
        /// The part of <paramref name="source"/> - a picture of the whole screen - that fills a box
        /// of aspect <paramref name="targetAspect"/> without stretching: cropped, centred.
        /// </summary>
        private static Rect CoverCrop(Rect source, float sourceAspect, float targetAspect)
        {
            if (targetAspect > sourceAspect)
            {
                var height = source.height * sourceAspect / targetAspect;
                return new Rect(source.x, source.y + (source.height - height) * 0.5f, source.width, height);
            }

            var width = source.width * targetAspect / sourceAspect;
            return new Rect(source.x + (source.width - width) * 0.5f, source.y, width, source.height);
        }

        private void Open(CatalogEntry entry)
        {
            // The same fractal keeps its view: tapping the card of what is open means "carry on",
            // and resetting a deep zoom by accident would lose it. Reset lives in the fractal panel.
            if (!ReferenceEquals(Services.Session.Definition, entry.Definition))
            {
                Services.Session.SetDefinition(entry.Definition);
            }

            Close();
        }

        /// <summary>A tap target with the same press feedback as a settings row.</summary>
        private static void AttachButton(Image graphic, Action onClick, Graphic hitArea = null)
        {
            var host = hitArea != null ? hitArea.gameObject : graphic.gameObject;
            var button = host.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1.1f),
                pressedColor = new Color(0.8f, 0.8f, 0.8f, 1.4f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.4f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }
        }

        private readonly struct ChipView
        {
            public ChipView(Filter kind, string section, Image capsule, Text label)
            {
                Kind = kind;
                Section = section;
                Capsule = capsule;
                Label = label;
            }

            public Filter Kind { get; }
            public string Section { get; }
            public Image Capsule { get; }
            public Text Label { get; }
        }

        private sealed class CardView
        {
            private readonly CatalogEntry entry;
            private readonly RawImage preview;
            private readonly Image star;
            private readonly int pixelSize;

            public CardView(CatalogEntry entry, RawImage preview, Image star, int pixelSize)
            {
                this.entry = entry;
                this.preview = preview;
                this.star = star;
                this.pixelSize = pixelSize;
            }

            public void RefreshThumbnail(IFractalThumbnails thumbnails)
            {
                if (thumbnails == null || preview == null)
                {
                    return;
                }

                var texture = thumbnails.Get(entry, pixelSize);
                if (!ReferenceEquals(texture, preview.texture))
                {
                    preview.texture = texture;
                    preview.enabled = texture != null;
                }
            }

            public void RefreshStar(IGalleryPreferences preferences)
            {
                if (star == null || preferences == null)
                {
                    return;
                }

                var favorite = preferences.IsFavorite(entry.Id);
                var size = Mathf.RoundToInt(star.rectTransform.sizeDelta.x);
                star.sprite = favorite ? UiSprites.Star(size) : UiSprites.StarOutline(size);
                star.color = favorite ? UiTheme.Favorite : UiTheme.Text;
            }
        }
    }
}
