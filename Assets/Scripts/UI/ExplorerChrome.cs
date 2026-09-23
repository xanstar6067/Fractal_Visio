using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The controls that float over the picture while exploring: the gallery button in the top-left
    /// corner, which also names the fractal on screen, and the toolbar - along the bottom in
    /// portrait, down the right edge in landscape (<see cref="UiTheme.ToolbarVertical"/>).
    ///
    /// Every toolbar button carries a word under its icon. Four bare icons in a corner were the
    /// first version; a gear, a star and a camera do not say "palette" or "parameters", and a
    /// touch screen has no hover to explain them.
    ///
    /// Not a <see cref="UiScreen"/>: it does not open and close, it is simply there - unless the
    /// gallery covers it or a tap on the picture hid it.
    /// </summary>
    public sealed class ExplorerChrome
    {
        private const float FadeSeconds = 0.18f;

        private readonly List<GlassPanel> surfaces = new();
        private readonly List<ItemView> itemViews = new();

        private AppServices services;
        private RectTransform root;
        private CanvasGroup group;
        private GlassPanel pill;
        private Text pillLabel;
        private IFractalDefinition labelledDefinition;
        private GlassPanel toolbar;
        private float visibility = 1f;
        private float target = 1f;

        /// <summary>One toolbar button.</summary>
        public readonly struct Item
        {
            public Item(string labelKey, Func<int, Sprite> icon, Action onClick, Func<bool> isActive = null)
            {
                LabelKey = labelKey;
                Icon = icon;
                OnClick = onClick;
                IsActive = isActive;
            }

            public string LabelKey { get; }
            public Func<int, Sprite> Icon { get; }
            public Action OnClick { get; }

            /// <summary>Lit while this is true: the button whose panel is open.</summary>
            public Func<bool> IsActive { get; }
        }

        /// <summary>True while any of it is on screen, the fade included.</summary>
        public bool IsVisible => visibility > 0.001f;

        public void Build(RectTransform parent, AppServices appServices, IReadOnlyList<Item> items, Action openGallery)
        {
            services = appServices;
            surfaces.Clear();
            itemViews.Clear();
            labelledDefinition = null;

            root = UiFactory.CreateRect("ExplorerChrome", parent);
            UiFactory.Stretch(root);
            group = root.gameObject.AddComponent<CanvasGroup>();

            BuildPill(openGallery);
            BuildToolbar(items);
            Apply();
        }

        public void SetVisible(bool visible)
        {
            target = visible ? 1f : 0f;
        }

        public void Tick(float deltaTime, Texture backdrop)
        {
            if (root == null)
            {
                return;
            }

            if (!Mathf.Approximately(visibility, target))
            {
                visibility = Mathf.MoveTowards(visibility, target, deltaTime / FadeSeconds);
                Apply();
            }

            if (!IsVisible)
            {
                return;
            }

            for (var i = 0; i < surfaces.Count; i++)
            {
                surfaces[i].SetBackdrop(backdrop);
            }

            if (!ReferenceEquals(services.Session.Definition, labelledDefinition))
            {
                LabelPill();
            }

            for (var i = 0; i < itemViews.Count; i++)
            {
                itemViews[i].Refresh();
            }
        }

        public bool ContainsScreenPoint(Vector2 point)
        {
            if (visibility < 0.5f)
            {
                return false;
            }

            for (var i = 0; i < surfaces.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(surfaces[i].Root, point, null))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (root != null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
            }

            root = null;
            group = null;
            pill = null;
            pillLabel = null;
            toolbar = null;
            surfaces.Clear();
            itemViews.Clear();
        }

        private void Apply()
        {
            var eased = visibility * visibility * (3f - 2f * visibility);
            group.alpha = eased;
            group.blocksRaycasts = eased > 0.5f;
            group.interactable = eased > 0.5f;
            root.gameObject.SetActive(eased > 0.001f);
        }

        /// <summary>Top-left capsule: the gallery icon and the fractal's name. The way back to the menu.</summary>
        private void BuildPill(Action openGallery)
        {
            var height = UiTheme.TopBarHeight;
            pill = GlassPanel.Create("GalleryButton", root, UiTheme.SegmentHeight * 0.5f, UiTheme.ButtonTint, UiTheme.ButtonBorder);
            surfaces.Add(pill);

            var iconSize = Mathf.Round(height * 0.4f);
            var icon = UiFactory.CreateImage("Icon", pill.Content, null, UiTheme.Text);
            icon.sprite = UiSprites.Grid(Mathf.RoundToInt(iconSize));
            icon.type = Image.Type.Simple;
            UiFactory.Anchor(
                icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(height * 0.34f, 0f), new Vector2(iconSize, iconSize));

            pillLabel = UiFactory.CreateText(
                "Name", pill.Content, string.Empty, UiTheme.SegmentFontSize - 1, UiTheme.Text, TextAnchor.MiddleLeft);
            pillLabel.fontStyle = FontStyle.Bold;

            AddHitArea(pill.Root, UiTheme.SegmentHeight * 0.5f, openGallery);
            LabelPill();
        }

        /// <summary>
        /// Name the fractal on screen and fit the capsule to it, up to most of the screen width. A
        /// name longer than that is set smaller, on one line: best fit would rather wrap it, and a
        /// two-line name in a one-line capsule reads as a mistake.
        /// </summary>
        private void LabelPill()
        {
            labelledDefinition = services.Session.Definition;
            pillLabel.text = services.Strings.FractalName(labelledDefinition);

            var height = UiTheme.TopBarHeight;
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var textStart = height * 0.34f + Mathf.Round(height * 0.4f) + height * 0.22f;
            var textEnd = height * 0.42f;
            var room = Mathf.Max(height * 1.6f, (Screen.width - UiTheme.SafeLeft - UiTheme.SafeRight) * 0.62f);

            var fullSize = Mathf.Max(8, Mathf.RoundToInt(UiTheme.Px(UiTheme.SegmentFontSize - 1)));
            pillLabel.fontSize = fullSize;
            var textWidth = pillLabel.preferredWidth;
            var textRoom = room - textStart - textEnd;
            if (textWidth > textRoom && textWidth > 0f)
            {
                pillLabel.fontSize = Mathf.Max(Mathf.RoundToInt(fullSize * 0.6f), Mathf.FloorToInt(fullSize * textRoom / textWidth));
                textWidth = Mathf.Min(textRoom, pillLabel.preferredWidth);
            }

            var width = Mathf.Clamp(textStart + textWidth + textEnd, height * 1.6f, room);

            UiFactory.Anchor(
                pill.Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(UiTheme.SafeLeft + margin, -(UiTheme.SafeTop + margin)),
                new Vector2(width, height));

            var labelRect = pillLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(textStart, 0f);
            labelRect.offsetMax = new Vector2(-textEnd, 0f);
        }

        private void BuildToolbar(IReadOnlyList<Item> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            var vertical = UiTheme.ToolbarVertical;
            var itemSize = UiTheme.ToolbarItemSize(items.Count);
            var padding = UiTheme.Px(UiTheme.ToolbarPadding);
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var size = vertical
                ? new Vector2(itemSize.x + padding * 2f, itemSize.y * items.Count + padding * 2f)
                : new Vector2(itemSize.x * items.Count + padding * 2f, itemSize.y + padding * 2f);

            toolbar = GlassPanel.Create("Toolbar", root, UiTheme.ToolbarRadius, UiTheme.ButtonTint, UiTheme.ButtonBorder);
            surfaces.Add(toolbar);

            if (vertical)
            {
                UiFactory.Anchor(
                    toolbar.Root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-(UiTheme.SafeRight + margin), UiTheme.SafeBottom + margin), size);
            }
            else
            {
                UiFactory.Anchor(
                    toolbar.Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2((UiTheme.SafeLeft - UiTheme.SafeRight) * 0.5f, UiTheme.SafeBottom + margin), size);
            }

            for (var i = 0; i < items.Count; i++)
            {
                var x = padding + (vertical ? 0f : i * itemSize.x);
                var y = -(padding + (vertical ? i * itemSize.y : 0f));
                itemViews.Add(BuildItem(items[i], x, y, itemSize));
            }
        }

        private ItemView BuildItem(in Item item, float x, float y, Vector2 size)
        {
            var slot = UiFactory.CreateRect("Item_" + item.LabelKey, toolbar.Content);
            slot.anchorMin = new Vector2(0f, 1f);
            slot.anchorMax = new Vector2(0f, 1f);
            slot.pivot = new Vector2(0f, 1f);
            slot.anchoredPosition = new Vector2(x, y);
            slot.sizeDelta = size;

            // Lit behind the button whose panel is open, so the toolbar says where the user is.
            var inset = Mathf.Min(size.x, size.y) * 0.06f;
            var radius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(UiTheme.Px(UiTheme.SegmentRadius + 2f), size.y * 0.3f)));
            var selection = UiFactory.CreateImage("Selection", slot, UiSprites.Rounded(radius), UiTheme.SegmentSelected);
            UiFactory.Stretch(selection.rectTransform, inset);
            selection.enabled = false;

            // Icon over label, the pair centred in the button with a little more air above than
            // between them.
            var iconSize = Mathf.Round(Mathf.Min(UiTheme.Px(26f), size.y * 0.42f));
            var labelHeight = size.y * 0.3f;
            var bottomGap = size.y * 0.08f;
            var topGap = (size.y - iconSize - labelHeight - bottomGap) * 0.55f;

            var icon = UiFactory.CreateImage("Icon", slot, null, UiTheme.Text);
            icon.sprite = item.Icon(Mathf.RoundToInt(iconSize));
            icon.type = Image.Type.Simple;
            UiFactory.Anchor(
                icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -topGap), new Vector2(iconSize, iconSize));

            var label = UiFactory.CreateText(
                "Label", slot, services.Strings.Get(item.LabelKey), UiTheme.ToolbarLabelFontSize, UiTheme.TextMuted,
                TextAnchor.MiddleCenter, fitToRect: true);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, bottomGap);
            labelRect.sizeDelta = new Vector2(-inset * 2f, labelHeight);

            AddHitArea(slot, UiTheme.SegmentRadius, item.OnClick);
            return new ItemView(selection, label, item.IsActive);
        }

        /// <summary>
        /// Invisible hit area over a control. Transparent at rest, a white flash while pressed: the
        /// glass under it already is the button, it only needs to answer the finger.
        /// </summary>
        private static void AddHitArea(RectTransform parent, float referenceRadius, Action onClick)
        {
            var hit = UiFactory.CreateImage(
                "Hit", parent, UiSprites.Rounded(UiTheme.PxInt(referenceRadius)), new Color(1f, 1f, 1f, 0.12f));
            UiFactory.Stretch(hit.rectTransform);
            hit.raycastTarget = true;

            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = new Color(1f, 1f, 1f, 0.6f),
                pressedColor = new Color(1f, 1f, 1f, 1.6f),
                selectedColor = new Color(1f, 1f, 1f, 0f),
                disabledColor = new Color(1f, 1f, 1f, 0f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }
        }

        private sealed class ItemView
        {
            private readonly Image selection;
            private readonly Text label;
            private readonly Func<bool> isActive;
            private bool lit;

            public ItemView(Image selection, Text label, Func<bool> isActive)
            {
                this.selection = selection;
                this.label = label;
                this.isActive = isActive;
            }

            public void Refresh()
            {
                var active = isActive != null && isActive();
                if (active == lit)
                {
                    return;
                }

                lit = active;
                selection.enabled = active;
                label.color = active ? UiTheme.Text : UiTheme.TextMuted;
            }
        }
    }
}
