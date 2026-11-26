using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.Persistent;

namespace YARG.Menu.MusicLibrary
{
    /// <summary>
    /// Lays the music library's prefabs out as the Moonlight board draws it (MLibrary, MSongOptions): a header with
    /// the title, a count and the search pill on the right; the song list in one panel; the song's panel on the
    /// right (<see cref="LibrarySidebar"/>). All sizes are the board's (1280×720) times 1.5. Lookups are by name and
    /// skip anything missing, so a prefab change only loses style.
    /// </summary>
    internal static class LibraryMoonlight
    {
        public const float EDGE = 42f;     // screen edge to panels
        public const float TOP = 135f;     // screen top to panels
        public const float BOTTOM = 102f;  // panels to screen bottom
        public const float GAP = 30f;      // between the two panels
        public const float SIDEBAR = 684f;
        private const float HELP_BAR = 75f;
        private const float HEADER_Y = 72f; // the header row's centre: the board's 22 + 52 / 2, ×1.5
        private const float SCREEN_W = 1920f;
        private const float SCREEN_H = 1080f;

        /// <summary>Lays out the header, list panel and columns. Returns the count line beside the title.</summary>
        public static TextMeshProUGUI StyleMenu(Transform menu)
        {
            // Columns: the list (its panel inset 42 from the left) and the 684 sidebar, 42 from the right
            if (menu.Find("Horizontal Layout")?.GetComponent<HorizontalLayoutGroup>() is { } columns)
            {
                columns.padding = new RectOffset(0, (int) EDGE, 0, 0);
                columns.spacing = GAP;
            }
            var sidebar = menu.Find("Horizontal Layout/Sidebar");
            if (sidebar != null)
            {
                Hide(sidebar);
                if (sidebar.TryGetComponent(out LayoutElement width)) width.minWidth = width.preferredWidth = SIDEBAR;
            }
            if (menu.Find("Horizontal Layout/Left")?.GetComponent<LayoutElement>() is { } left)
            {
                left.minWidth = left.preferredWidth = SCREEN_W - SIDEBAR - EDGE - GAP;
            }

            var main = menu.Find("Horizontal Layout/Left/Main Area");
            if (main == null)
            {
                return null;
            }

            TextMeshProUGUI subtitle = null;
            var header = main.Find("Header");
            if (header != null)
            {
                if (header.TryGetComponent(out LayoutElement height)) height.minHeight = height.preferredHeight = TOP + 15f;
                Hide(header.Find("Background"));
                StyleBack(header.Find("Button"));
                subtitle = StyleTitle(header);
                StyleSearch(header.Find("Search Field"));
            }

            // The board has no sort bar: its count is the line beside the title
            main.Find("Status Subheader")?.gameObject.SetActive(false);

            // The list's panel; the rows sit inside it with the board's 15px padding
            var list = Panel(main, 27);
            list.offsetMin = new Vector2(EDGE, BOTTOM - HELP_BAR);
            list.offsetMax = new Vector2(0f, -TOP);
            var songs = main.Find("Songs Container And Scrollbar");
            if (songs != null)
            {
                if (songs.TryGetComponent(out HorizontalOrVerticalLayoutGroup pad))
                {
                    pad.padding = new RectOffset((int) EDGE + 15, 15, 0, (int) (BOTTOM - HELP_BAR) + 15);
                }
                if (songs.TryGetComponent(out LayoutElement fill))
                {
                    fill.preferredHeight = SCREEN_H - HELP_BAR - (TOP + 15f);
                    fill.flexibleHeight = 1f;
                }
                StyleScrollbar(songs.Find("Scrollbar"));
            }
            // The list centres its rows on its view's height: settle the new layout before it first does
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform) menu);
            return subtitle;
        }

        private static void StyleBack(Transform back)
        {
            if (back == null || !back.TryGetComponent(out Image circle))
            {
                return;
            }
            var rect = (RectTransform) back;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(EDGE, -HEADER_Y);
            rect.sizeDelta = new Vector2(66f, 66f);
            circle.sprite = Moonlight.RoundedSprite(33);
            circle.type = Image.Type.Sliced;
            circle.color = Moonlight.PanelFill;
            Moonlight.Shadow(circle.rectTransform, Moonlight.PillShadow, 33);
            var chevron = new GameObject("Chevron", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            chevron.transform.SetParent(back, false);
            chevron.sprite = Moonlight.Chevron;
            chevron.color = Moonlight.Text;
            chevron.raycastTarget = false;
            chevron.rectTransform.sizeDelta = new Vector2(27f, 27f);
        }

        /// <summary>"Music library" in Unbounded and the muted count after it, in one row.</summary>
        private static TextMeshProUGUI StyleTitle(Transform header)
        {
            var row = new GameObject("Title Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            row.transform.SetParent(header, false);
            var rect = (RectTransform) row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(EDGE + 66f + 24f, -HEADER_Y);
            rect.sizeDelta = new Vector2(0f, 66f);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (header.Find("Double Header Text/Header")?.GetComponent<TextMeshProUGUI>() is { } title)
            {
                title.transform.SetParent(row.transform, false);
                title.text = "Music library"; // set in the prefab, not localised
                if (Moonlight.Heading != null) title.font = Moonlight.Heading;
                title.fontStyle = FontStyles.Normal;
                title.fontWeight = FontWeight.Medium;
                title.fontSize = 36f;
                title.color = Moonlight.Text;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.enableAutoSizing = false;
            }
            header.Find("Double Header Text")?.gameObject.SetActive(false);

            var subtitle = Moonlight.Label("Count", row.transform, Moonlight.Body, 21f, Moonlight.Muted);
            subtitle.overflowMode = TextOverflowModes.Overflow;
            return subtitle;
        }

        private static void StyleSearch(Transform field)
        {
            if (field == null || !field.TryGetComponent(out Image pill))
            {
                return;
            }
            // Right of the header, over the sidebar's column: its right edge 42 from the screen's
            var rect = (RectTransform) field;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(SIDEBAR + GAP, -HEADER_Y);
            rect.sizeDelta = new Vector2(495f, 66f);
            pill.sprite = Moonlight.RoundedSprite(33);
            pill.type = Image.Type.Sliced;
            pill.color = Moonlight.PanelFill;
            Moonlight.Shadow(pill.rectTransform, Moonlight.PillShadow, 33);
            if (field.Find("Text Area") is RectTransform area)
            {
                area.anchorMin = Vector2.zero;
                area.anchorMax = Vector2.one;
                area.offsetMin = new Vector2(66f, 0f);
                area.offsetMax = new Vector2(-27f, 0f);
            }
            if (field.TryGetComponent(out TMP_InputField input))
            {
                if (input.textComponent != null)
                {
                    input.textComponent.color = Moonlight.Text;
                    input.textComponent.fontSize = 22.5f;
                    input.textComponent.fontStyle = FontStyles.Normal;
                    if (Moonlight.Body != null) input.textComponent.font = Moonlight.Body;
                }
                if (input.placeholder is TextMeshProUGUI placeholder)
                {
                    placeholder.text = "Search songs, artists";
                    placeholder.color = Moonlight.Muted;
                    placeholder.fontSize = 22.5f;
                    placeholder.fontStyle = FontStyles.Normal;
                    if (Moonlight.Body != null) placeholder.font = Moonlight.Body;
                }
            }
            if (field.Find("Search Icon") is RectTransform icon)
            {
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = new Vector2(27f + 12f, 0f);
                icon.sizeDelta = new Vector2(24f, 24f);
                Tint(icon, Moonlight.Muted);
            }
        }

        /// <summary>The parts of a list row this look draws itself.</summary>
        public class Row
        {
            public TextMeshProUGUI Title, Meta;
            public Image Crescent, Rule;
        }

        /// <summary>
        /// Hides the prefab row's own text (still there, so clicks on it work) and adds the board's: a crescent when
        /// selected, the title with the artist after it, and the length on the right.
        /// </summary>
        public static Row StyleRow(Transform row)
        {
            // Rows are pooled and the spare ones never shown; size them all, or the list's centring sums 70s and 72s
            ((RectTransform) row).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 72f);
            Hide(row.Find("Backgrounds/Normal Background"));
            Hide(row.Find("Backgrounds/Category Background"));
            Hide(row.Find("Backgrounds/Subcategory Background"));
            Hide(row.Find("Backgrounds/Button Header Background"));
            Hide(row.Find("Background Gradient/Normal Gradient Background"));
            Hide(row.Find("Background Gradient/Category Gradient Background"));
            if (row.Find("Backgrounds/Selected Background")?.GetComponent<Image>() is { } wash)
            {
                wash.sprite = Moonlight.RoundedSprite(15);
                wash.type = Image.Type.Sliced;
                wash.color = Moonlight.MoonWash;
            }
            if (row.Find("Container") is { } container)
            {
                container.gameObject.AddComponent<CanvasGroup>().alpha = 0f; // invisible; its buttons still take clicks
            }

            var moon = new Row
            {
                Crescent = Overlay<Image>("Crescent", row),
                Title = Moonlight.Label("Moon Title", row, Moonlight.Body, 25.5f, Moonlight.Text),
                Meta = Moonlight.Label("Moon Meta", row, Moonlight.Body, 19.5f, Moonlight.Faint),
                Rule = Overlay<Image>("Rule", row),
            };
            moon.Crescent.sprite = Moonlight.Crescent;
            moon.Crescent.color = Moonlight.Moon;
            var c = moon.Crescent.rectTransform;
            c.anchorMin = c.anchorMax = new Vector2(0f, 0.5f);
            c.anchoredPosition = new Vector2(21f + 10.5f, 0f);
            c.sizeDelta = new Vector2(21f, 21f);

            moon.Title.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(moon.Title.rectTransform);
            moon.Title.rectTransform.offsetMin = new Vector2(60f, 0f);
            moon.Title.rectTransform.offsetMax = new Vector2(-21f - 120f, 0f);

            moon.Meta.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            moon.Meta.alignment = TextAlignmentOptions.MidlineRight;
            var m = moon.Meta.rectTransform;
            m.anchorMin = new Vector2(1f, 0f);
            m.anchorMax = Vector2.one;
            m.pivot = new Vector2(1f, 0.5f);
            m.anchoredPosition = new Vector2(-21f, 0f);
            m.sizeDelta = new Vector2(240f, 0f);

            var r = moon.Rule.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(0f, 1.5f);
            return moon;
        }

        /// <summary>Fills a row for what it shows: a song, a section head, or anything else in the list.</summary>
        public static void FillRow(Row moon, ViewType view, bool selected)
        {
            if (moon == null)
            {
                return;
            }
            moon.Crescent.gameObject.SetActive(selected);
            if (view is SortHeaderViewType head)
            {
                // Small spaced capitals with a line under them
                moon.Title.font = Moonlight.Heading != null ? Moonlight.Heading : Moonlight.Body;
                moon.Title.fontSize = 18f;
                moon.Title.fontWeight = FontWeight.Medium;
                moon.Title.characterSpacing = 10f;
                var tint = head.Tint ?? Moonlight.Muted;
                moon.Title.color = selected ? Moonlight.Moon : tint;
                moon.Title.rectTransform.offsetMin = new Vector2(21f, 0f);
                moon.Title.text = "<noparse>" + Clean(head.HeaderText).ToUpperInvariant() + "</noparse>";
                moon.Meta.text = "<noparse>" + Clean(head.Note) + "</noparse>";
                moon.Meta.color = tint;
                moon.Rule.gameObject.SetActive(true);
                var rule = tint;
                rule.a = head.Tint != null ? 0.35f : 0.2f;
                moon.Rule.color = rule;
                moon.Crescent.gameObject.SetActive(false);
                return;
            }

            moon.Title.font = Moonlight.Body;
            moon.Title.fontSize = 25.5f;
            moon.Title.fontWeight = FontWeight.Regular;
            moon.Title.characterSpacing = 0f;
            moon.Title.color = selected ? Moonlight.Moon : Moonlight.Text;
            moon.Title.rectTransform.offsetMin = new Vector2(60f, 0f);
            moon.Rule.gameObject.SetActive(false);
            moon.Meta.color = selected ? Moonlight.Soft : Moonlight.Faint;
            if (view is SongViewType song)
            {
                var entry = song.SongEntry;
                string name = entry.Name;
                string artist = entry.Artist;
                moon.Title.text = $"<font-weight={(selected ? 700 : 600)}><noparse>{Clean(name)}</noparse></font-weight>" +
                    $"<space=18><size=21><font-weight=400><color=#9AA1C7><noparse>{Clean(artist)}</noparse></color></font-weight></size>";
                var time = TimeSpan.FromMilliseconds(entry.SongLengthMilliseconds);
                moon.Meta.text = time.Hours > 0 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
            }
            else
            {
                moon.Title.text = view.GetPrimaryText(selected);
                moon.Meta.text = string.Empty;
            }
        }

        private static string Clean(string text) => text?.Replace("</noparse>", "") ?? string.Empty;

        /// <summary>The song options popup: the board's card, without YARG's header and footer caps.</summary>
        public static void StylePopup(Transform popup)
        {
            // The board dims by 60% in sRGB; blending in linear space needs ~87% to darken as much
            if (popup.TryGetComponent(out Image dim)) dim.color = new Color(0.008f, 0.02f, 0.063f, 0.87f);
            var menu = popup.Find("Menu");
            if (menu == null)
            {
                return;
            }
            menu.localScale = Vector3.one; // the prefab scaled it to 110%
            // Submenus switch YARG's header on for their name; that name is drawn in the Moonlight header instead
            Hide(menu.Find("Header"));
            foreach (var old in menu.Find("Header")?.GetComponentsInChildren<Graphic>(true) ?? Array.Empty<Graphic>())
            {
                old.enabled = false;
            }
            Hide(menu.Find("Footer"));
            Hide(menu.Find("Container/ScrollView"));
            if (menu.Find("Container/ScrollView/Scrollbar") is { } bar)
            {
                bar.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
            }
            if (menu.Find("Container/ScrollView/Viewport/Content")?.GetComponent<VerticalLayoutGroup>() is { } items)
            {
                items.spacing = 6f;
            }

            var card = Moonlight.Panel("Moonlight Card", menu, 33, new Color(0.024f, 0.04f, 0.11f, 1f));
            card.transform.SetAsFirstSibling();
            Stretch(card.rectTransform);

            // The song at the top: cover, title and artist, a rule under them
            var head = new GameObject("Moonlight Header", typeof(RectTransform)).GetComponent<RectTransform>();
            head.SetParent(menu, false);
            head.anchorMin = new Vector2(0f, 1f);
            head.anchorMax = Vector2.one;
            head.pivot = new Vector2(0.5f, 1f);
            var cover = Moonlight.Panel("Cover", head, 15, Moonlight.Raised);
            cover.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            cover.rectTransform.anchorMin = cover.rectTransform.anchorMax = new Vector2(0f, 1f);
            cover.rectTransform.pivot = new Vector2(0f, 1f);
            cover.rectTransform.anchoredPosition = new Vector2(36f, -30f);
            cover.rectTransform.sizeDelta = new Vector2(78f, 78f);
            var art = new GameObject("Art", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            art.transform.SetParent(cover.transform, false);
            art.raycastTarget = false;
            Stretch(art.rectTransform);
            var title = Moonlight.Label("Title", head, Moonlight.Heading != null ? Moonlight.Heading : Moonlight.Body, 24f, Moonlight.Text);
            title.fontWeight = FontWeight.Medium;
            Moonlight.Label("Artist", head, Moonlight.Body, 21f, Moonlight.Muted);
            var rule = Overlay<Image>("Rule", head);
            rule.color = new Color(0.627f, 0.686f, 1f, 0.08f);
            rule.rectTransform.anchorMin = Vector2.zero;
            rule.rectTransform.anchorMax = new Vector2(1f, 0f);
            rule.rectTransform.pivot = new Vector2(0.5f, 0f);
            rule.rectTransform.offsetMin = new Vector2(21f, 0f);
            rule.rectTransform.offsetMax = new Vector2(-21f, 1.5f);
        }

        /// <summary>
        /// Sizes and fills the popup after its items are made: the song (or the submenu's name), then the items, the
        /// card wrapped around them like the board's.
        /// </summary>
        public static void LayoutPopup(Transform popup, Transform items, SongViewType song, Texture cover, string heading)
        {
            var menu = popup.Find("Menu") as RectTransform;
            var head = menu != null ? menu.Find("Moonlight Header") as RectTransform : null;
            if (menu == null || head == null)
            {
                return;
            }

            var art = head.Find("Cover");
            var title = head.Find("Title").GetComponent<TextMeshProUGUI>();
            var artist = head.Find("Artist").GetComponent<TextMeshProUGUI>();
            float headHeight;
            if (heading == null && song != null)
            {
                art.gameObject.SetActive(true);
                var raw = art.GetComponentInChildren<RawImage>();
                raw.texture = cover;
                raw.uvRect = new Rect(0f, 0f, 1f, -1f); // covers load upside down, as in the sidebar
                raw.color = cover != null ? Color.white : Color.clear;
                title.text = "<noparse>" + Clean(song.SongEntry.Name) + "</noparse>";
                artist.text = "<noparse>" + Clean(song.SongEntry.Artist) + "</noparse>";
                artist.gameObject.SetActive(true);
                Line(title.rectTransform, 36f + 78f + 21f, -30f - 39f + 14f);
                Line(artist.rectTransform, 36f + 78f + 21f, -30f - 39f - 15f);
                headHeight = 30f + 78f + 21f;
            }
            else
            {
                art.gameObject.SetActive(false);
                artist.gameObject.SetActive(false);
                title.text = heading ?? string.Empty;
                Line(title.rectTransform, 36f, -30f - 18f);
                headHeight = heading != null ? 30f + 36f + 21f : 0f;
            }
            head.gameObject.SetActive(headHeight > 0f);
            head.sizeDelta = new Vector2(0f, headHeight);

            int count = 0;
            foreach (Transform item in items)
            {
                if (item.gameObject.activeSelf) count++;
            }
            float itemsTop = headHeight > 0f ? headHeight + 1.5f + 12f : 30f;
            float height = Mathf.Min(itemsTop + count * 69f + Mathf.Max(0, count - 1) * 6f + 21f, 750f);
            menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 1f);
            menu.pivot = new Vector2(0.5f, 1f);
            menu.anchoredPosition = new Vector2(0f, -225f);
            menu.sizeDelta = new Vector2(630f, height);

            if (menu.Find("Container") is RectTransform container)
            {
                container.offsetMin = new Vector2(0f, 21f);
                container.offsetMax = new Vector2(0f, -itemsTop);
            }
            if (menu.Find("Container/ScrollView/Viewport") is RectTransform viewport)
            {
                viewport.offsetMin = new Vector2(21f, viewport.offsetMin.y);
                viewport.offsetMax = new Vector2(-6f, viewport.offsetMax.y); // its scrollbar's 15 is the rest
            }
        }

        private static void Line(RectTransform rect, float left, float centreY)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(left, centreY - 16f);
            rect.offsetMax = new Vector2(-36f, centreY + 16f);
        }

        /// <summary>One popup item: the board's 46px row, a crescent, the wash and cream text when selected, a hint.</summary>
        public static void StylePopupItem(PopupMenuItem item, string hint = null)
        {
            var row = item.transform;
            var size = (RectTransform) row;
            size.sizeDelta = new Vector2(size.sizeDelta.x, 69f);
            Hide(row.Find("Background"));
            if (row.Find("Selected Background")?.GetComponent<Image>() is { } selected)
            {
                selected.sprite = Moonlight.RoundedSprite(15);
                selected.type = Image.Type.Sliced;
                selected.color = Moonlight.MoonWash;
                Stretch(selected.rectTransform);
            }
            var body = row.Find("Body")?.GetComponent<TextMeshProUGUI>();
            if (body == null)
            {
                return;
            }
            if (Moonlight.Body != null) body.font = Moonlight.Body;
            body.fontSize = 25.5f;
            body.fontStyle = FontStyles.Normal;
            body.alignment = TextAlignmentOptions.MidlineLeft;
            body.margin = new Vector4(58.5f, 0f, 21f + 200f, 0f);
            Stretch(body.rectTransform);

            var crescent = Overlay<Image>("Crescent", row);
            crescent.sprite = Moonlight.Crescent;
            crescent.color = Moonlight.Moon;
            crescent.rectTransform.anchorMin = crescent.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            crescent.rectTransform.anchoredPosition = new Vector2(21f + 9.75f, 0f);
            crescent.rectTransform.sizeDelta = new Vector2(19.5f, 19.5f);

            if (!string.IsNullOrEmpty(hint))
            {
                var note = Moonlight.Label("Hint", row, Moonlight.Body, 19.5f, Moonlight.Muted);
                note.fontWeight = FontWeight.SemiBold;
                note.alignment = TextAlignmentOptions.MidlineRight;
                note.text = hint;
                Stretch(note.rectTransform);
                note.rectTransform.offsetMax = new Vector2(-21f, 0f);
            }

            void Show(bool on)
            {
                crescent.gameObject.SetActive(on);
                body.color = on ? Moonlight.Moon : Moonlight.Text;
                body.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
            }
            Show(item.Button.Selected);
            item.Button.SelectionStateChanged += (_, on, _) => Show(on);
        }

        /// <summary>The library's Play button as the board's cream pill.</summary>
        public static void StylePlayButton(HelpBarButton button) => button?.MakePrimary();

        /// <summary>A thin faint handle on no track; the colour tint is off, it painted the handle blue.</summary>
        private static void StyleScrollbar(Transform bar)
        {
            if (bar == null || !bar.TryGetComponent(out Scrollbar scrollbar))
            {
                return;
            }
            scrollbar.transition = Selectable.Transition.None;
            Tint(bar, Color.clear);
            // The board's list has no scrollbar: still there for dragging, just not drawn
            if (!bar.TryGetComponent(out CanvasGroup hidden)) hidden = bar.gameObject.AddComponent<CanvasGroup>();
            hidden.alpha = 0f;
            if (scrollbar.handleRect != null && scrollbar.handleRect.TryGetComponent(out Image handle))
            {
                handle.color = Moonlight.Faint;
                handle.sprite = Moonlight.RoundedSprite(4);
                handle.type = Image.Type.Sliced;
            }
        }

        /// <summary>A graphic that ignores its parent's layout and takes no clicks.</summary>
        private static T Overlay<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(parent, false);
            var graphic = go.GetComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static RectTransform Panel(Transform parent, int radius)
        {
            var panel = Moonlight.Panel("Moonlight Panel", parent, radius, Moonlight.PanelFill);
            panel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            panel.transform.SetAsFirstSibling();
            var rect = panel.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Hide(Transform t)
        {
            if (t != null && t.TryGetComponent(out Image image)) image.enabled = false;
        }

        private static void Tint(Transform t, Color color)
        {
            if (t != null && t.TryGetComponent(out Image image)) image.color = color;
        }
    }
}
