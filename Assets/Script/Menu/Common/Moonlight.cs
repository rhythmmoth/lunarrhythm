using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu
{
    /// <summary>
    /// LunarRhythm's menu look ("Moonlight"): flat night panels over the rotating background, one moon-cream highlight
    /// for whatever is selected, Unbounded headings and Barlow text. No gradients, glows or gloss.
    /// </summary>
    public static class Moonlight
    {
        public static readonly Color Night = Hex(0x02091A);
        /// <summary>Panels sit over the moving background at 80%.</summary>
        public static readonly Color PanelFill = new(6f / 255f, 10f / 255f, 28f / 255f, 0.8f);
        public static readonly Color Raised = Hex(0x131A3A);
        public static readonly Color Keycap = Hex(0x232C55);
        public static readonly Color Moon = Hex(0xF3E8CC);
        /// <summary>Behind the selected row.</summary>
        public static readonly Color MoonWash = OverDark(new Color(0.953f, 0.91f, 0.8f), 0.08f); // the boards' 8%
        public static readonly Color Text = Hex(0xEEF0FF);
        public static readonly Color Soft = Hex(0xD4D8F2);
        public static readonly Color Muted = Hex(0x9AA1C7);
        public static readonly Color MenuIdle = Hex(0x8F97C4);
        public static readonly Color Faint = Hex(0x5F6790);
        public static readonly Color Season = Hex(0xE48BE0);
        public static readonly Color Featured = Hex(0x7E96FF);
        public static readonly Color Good = Hex(0x7FE0B0);
        public static readonly Color Danger = Hex(0xFF8A9A);

        /// <summary>Unbounded, for titles and small caps labels. Set by the main menu, which always loads first.</summary>
        public static TMP_FontAsset Heading;
        /// <summary>Barlow, for everything else.</summary>
        public static TMP_FontAsset Body;

        private static readonly Dictionary<int, Sprite> Rounded = new();
        private static Sprite _crescent;

        /// <summary>
        /// A white rounded rectangle, 9-sliced so it stretches to any size. Use radius = height / 2 for a pill.
        /// </summary>
        public static Sprite RoundedSprite(int radius)
        {
            // Unity-null check: runtime textures can be unloaded between scenes
            if (!Rounded.TryGetValue(radius, out var sprite) || sprite == null)
            {
                sprite = MakeRounded(radius * 2, radius);
                Rounded[radius] = sprite;
            }
            return sprite;
        }

        /// <summary>The selection marker: a moon-cream crescent, opening to the upper right.</summary>
        public static Sprite Crescent
        {
            get
            {
                if (_crescent == null)
                {
                    _crescent = MakeCrescent(64);
                }
                return _crescent;
            }
        }

        private static Sprite _chevron, _play, _ring;
        private static readonly Dictionary<int, Sprite> Outlines = new();

        /// <summary>The boards' secondary button: a 1.5px outline, 9-sliced like <see cref="RoundedSprite"/>.</summary>
        public static Sprite OutlineSprite(int radius)
        {
            if (Outlines.TryGetValue(radius, out var sprite) && sprite != null)
            {
                return sprite;
            }
            int size = radius * 2 + 2;
            var pixels = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded square's edge (negative inside)
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float alpha = Mathf.Clamp01(0.5f - d) - Mathf.Clamp01(0.5f - (d + 1.5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) (alpha * 255));
                }
            }
            sprite = ToSprite(pixels, size, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            Outlines[radius] = sprite;
            return sprite;
        }

        /// <summary>The back button's chevron (the boards' 24-unit "M15 5 l-7 7 7 7", stroke 2.2), pointing left.</summary>
        public static Sprite Chevron => _chevron != null ? _chevron : _chevron = MakeShape(64, p =>
            Mathf.Min(Segment(p, new Vector2(15f, 5f), new Vector2(8f, 12f)),
                Segment(p, new Vector2(8f, 12f), new Vector2(15f, 19f))) <= 1.1f);

        /// <summary>The Play button's triangle (the boards' "M6 4 l14 8 -14 8z").</summary>
        public static Sprite Play => _play != null ? _play : _play = MakeShape(64, p =>
            p.x >= 6f && p.y >= 4f + (p.x - 6f) * 8f / 14f && p.y <= 20f - (p.x - 6f) * 8f / 14f);

        /// <summary>An empty difficulty dot: a ring 15% of its width thick.</summary>
        public static Sprite Ring => _ring != null ? _ring : _ring = MakeShape(64, p =>
        {
            float d = Vector2.Distance(p, new Vector2(12f, 12f));
            return d <= 12f && d >= 12f - 3.6f;
        });

        private static Sprite _star;

        /// <summary>The boards' star ("M12 2.5l2.9 6.1 6.6.8-4.9 4.6 1.3 6.6L12 17.3l-5.9 3.3 1.3-6.6-4.9-4.6 6.6-.8z").</summary>
        public static Sprite Star => _star != null ? _star : _star = MakeShape(64, p => InPolygon(p, StarPoints));

        private static Sprite _starOutline, _check, _plus, _alert, _thinRing;

        /// <summary>The score panel's progress ring (the boards' r 14, stroke 3 in a 34 box).</summary>
        public static Sprite ThinRing => _thinRing != null ? _thinRing : _thinRing = MakeShape(64, p =>
            Mathf.Abs(Vector2.Distance(p, new Vector2(12f, 12f)) - 9.88f) <= 1.06f);

        /// <summary>The toasts' icons, drawn with the boards' 2.2 stroke.</summary>
        public static Sprite StarOutline => _starOutline != null ? _starOutline : _starOutline = MakeShape(64, p =>
        {
            float d = float.MaxValue;
            for (int i = 0, j = StarPoints.Length - 1; i < StarPoints.Length; j = i++)
                d = Mathf.Min(d, Segment(p, StarPoints[j], StarPoints[i]));
            return d <= 1.1f;
        });

        public static Sprite Check => _check != null ? _check : _check = MakeShape(64, p =>
            Mathf.Min(Segment(p, new Vector2(5f, 12f), new Vector2(10f, 17f)),
                Segment(p, new Vector2(10f, 17f), new Vector2(19f, 7f))) <= 1.1f);

        public static Sprite Plus => _plus != null ? _plus : _plus = MakeShape(64, p =>
            Mathf.Min(Segment(p, new Vector2(6f, 12f), new Vector2(18f, 12f)),
                Segment(p, new Vector2(12f, 6f), new Vector2(12f, 18f))) <= 1.1f);

        public static Sprite Alert => _alert != null ? _alert : _alert = MakeShape(64, p =>
            Mathf.Abs(Vector2.Distance(p, new Vector2(12f, 12f)) - 9f) <= 1.1f ||
            Segment(p, new Vector2(12f, 7f), new Vector2(12f, 13f)) <= 1.1f ||
            Vector2.Distance(p, new Vector2(12f, 17f)) <= 1.1f);

        private static readonly Vector2[] StarPoints =
        {
            new(12f, 2.5f), new(14.9f, 8.6f), new(21.5f, 9.4f), new(16.6f, 14f), new(17.9f, 20.6f),
            new(12f, 17.3f), new(6.1f, 20.6f), new(7.4f, 14f), new(2.5f, 9.4f), new(9.1f, 8.6f),
        };

        private static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// A white shape drawn on the boards' 24-unit icon grid (y down), 4×4 supersampled for smooth edges.
        /// </summary>
        private static Sprite MakeShape(int size, System.Func<Vector2, bool> inside)
        {
            var pixels = new Color32[size * size];
            float unit = 24f / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            // Texture rows go up, the icon grid goes down
                            var p = new Vector2((x + (sx + 0.5f) / 4f) * unit, (size - y - (sy + 0.5f) / 4f) * unit);
                            hits += inside(p) ? 1 : 0;
                        }
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) (hits * 255 / 16));
                }
            }
            return ToSprite(pixels, size, Vector4.zero);
        }

        /// <summary>
        /// The boards' see-through colours are blended in sRGB; the game blends in linear space, where a light colour
        /// over a dark one reads several times stronger. This gives the alpha that looks the same over a dark panel.
        /// </summary>
        public static Color OverDark(Color color, float boardAlpha)
        {
            // The board's result over the panel (~#0a0f20), in linear light, solved for the game's alpha
            var panel = new Color(0.04f, 0.06f, 0.125f);
            var under = panel.linear;
            var target = Color.Lerp(panel, color, boardAlpha).linear;
            var top = color.linear;
            float alpha = (target.g - under.g) / Mathf.Max(0.0001f, top.g - under.g);
            return new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        /// <summary>Makes a code-built slider (a track with a fill and a dot) follow the mouse: press or drag anywhere
        /// along it. set takes the spot (0 to 1), applies it and returns where the value landed (0 to 1) after any
        /// snapping; released runs when the button comes up.</summary>
        public static void Draggable(RectTransform track, RectTransform fill, RectTransform dot, System.Func<float, float> set,
            System.Action pressed = null, System.Action released = null)
        {
            var hit = new GameObject("Drag Area", typeof(RectTransform), typeof(Image), typeof(SliderDrag));
            hit.transform.SetParent(track, false);
            var rect = (RectTransform) hit.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-15f, -21f); // the track is a thin line: grab it a little above, below and past the ends
            rect.offsetMax = new Vector2(15f, 21f);
            hit.GetComponent<Image>().color = Color.clear;
            var drag = hit.GetComponent<SliderDrag>();
            drag.Track = track;
            drag.Fill = fill;
            drag.Dot = dot;
            drag.Set = set;
            drag.Pressed = pressed;
            drag.Released = released;
        }

        /// <summary>A slider is being dragged: hover highlights shouldn't move (or rebuild the row under the mouse).</summary>
        public static bool Dragging { get; private set; }

        private class SliderDrag : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler,
            UnityEngine.EventSystems.IInitializePotentialDragHandler, UnityEngine.EventSystems.IDragHandler,
            UnityEngine.EventSystems.IPointerClickHandler
        {
            public RectTransform Track, Fill, Dot;
            public System.Func<float, float> Set;
            public System.Action Pressed, Released;

            public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
            {
                if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left) return;
                Dragging = true;
                Pressed?.Invoke();
                Apply(e);
            }

            public void OnInitializePotentialDrag(UnityEngine.EventSystems.PointerEventData e) => e.useDragThreshold = false;

            public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
            {
                if (Dragging) Apply(e);
            }

            public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e)
            {
                if (!Dragging) return;
                Dragging = false;
                Released?.Invoke();
            }

            // Swallowed, so the row under the slider doesn't take the click as "step the value"
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) { }

            private void OnDisable() => Dragging = false;

            private void Apply(UnityEngine.EventSystems.PointerEventData e)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, e.position, e.pressEventCamera, out var local)) return;
                var r = Track.rect;
                float t = Set(Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width)));
                if (Fill != null) Fill.anchorMax = new Vector2(t, Fill.anchorMax.y);
                if (Dot != null) Dot.anchorMin = Dot.anchorMax = new Vector2(t, Dot.anchorMin.y);
            }
        }

        /// <summary>Runs enter when the mouse moves onto target (and exit when it leaves): code-built menus move their
        /// highlight on hover this way, like the prefab ones.</summary>
        public static void OnHover(GameObject target, System.Action enter, System.Action exit = null)
        {
            var trigger = target.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                ?? target.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            void Add(UnityEngine.EventSystems.EventTriggerType type, System.Action action)
            {
                var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(_ => action());
                trigger.triggers.Add(entry);
            }
            Add(UnityEngine.EventSystems.EventTriggerType.PointerEnter, enter);
            if (exit != null) Add(UnityEngine.EventSystems.EventTriggerType.PointerExit, exit);
        }

        /// <summary>A faint wash over a button while the mouse is on it, for buttons whose highlight isn't a
        /// selection (tiles, pills, tabs). Sits under the button's text.</summary>
        public static void HoverWash(Graphic button, int radius)
        {
            var wash = Panel("Hover", button.transform, radius, new Color(1f, 1f, 1f, 0.07f));
            wash.transform.SetAsFirstSibling();
            var rect = wash.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            wash.gameObject.SetActive(false);
            OnHover(button.gameObject, () => wash.gameObject.SetActive(true), () => wash.gameObject.SetActive(false));
        }

        /// <summary>A flat panel with rounded corners. Panels and pills in the panel fill float over the moving
        /// background, so they get a drop shadow (board FixShadows): pills and round buttons a tight one, panels a wider
        /// one. Rows and other fills inside a panel don't.</summary>
        public static Image Panel(string name, Transform parent, int radius, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = RoundedSprite(radius);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            if (color == PanelFill)
            {
                Shadow(image.rectTransform, radius >= 30 ? PillShadow : PanelShadow, radius);
            }
            return image;
        }

        /// <summary>The boards' drop shadows × 1.5: how far down, how soft, how dark.</summary>
        public static readonly (float Offset, float Blur, float Alpha) PillShadow = (12f, 42f, 0.45f);
        public static readonly (float Offset, float Blur, float Alpha) PanelShadow = (15f, 51f, 0.5f);

        private static readonly Dictionary<(int, int, int, int), Sprite> Shadows = new();

        /// <summary>
        /// A soft drop shadow for a rounded rect of this corner radius. It's drawn as the rect's first child and is clear
        /// over the rect itself, so it shows only outside the edges; being a child, it follows the rect wherever layout
        /// puts it.
        /// </summary>
        public static void Shadow(RectTransform target, (float Offset, float Blur, float Alpha) style, int radius)
        {
            int blur = Mathf.RoundToInt(style.Blur), offset = Mathf.RoundToInt(style.Offset);
            var key = (radius, blur, offset, Mathf.RoundToInt(style.Alpha * 100f));
            if (!Shadows.TryGetValue(key, out var sprite) || sprite == null)
            {
                sprite = MakeShadow(radius, blur, offset, style.Alpha);
                Shadows[key] = sprite;
            }
            var shadow = new GameObject("Shadow", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            shadow.transform.SetParent(target, false);
            shadow.transform.SetAsFirstSibling();
            shadow.GetComponent<LayoutElement>().ignoreLayout = true;
            shadow.sprite = sprite;
            shadow.type = Image.Type.Sliced;
            shadow.color = Color.black;
            shadow.raycastTarget = false;
            var rect = shadow.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-blur, -blur - offset);
            rect.offsetMax = new Vector2(blur, blur - offset);
        }

        // The texture is the rect with blur-wide margins (offset down); alpha falls off around the offset rect and is
        // zero over the rect itself. 9-sliced so the edges stretch to any size.
        private static Sprite MakeShadow(int radius, int blur, int offset, float strength)
        {
            int size = 2 * (blur + radius) + 1;
            float Distance(float x, float y, float left, float bottom, float right, float top)
            {
                // Signed distance to a rounded rect (negative inside)
                float cx = (left + right) / 2f, cy = (bottom + top) / 2f;
                float qx = Mathf.Abs(x - cx) - ((right - left) / 2f - radius);
                float qy = Mathf.Abs(y - cy) - ((top - bottom) / 2f - radius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            }
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    // The rect sits offset higher than the shadow's centre: its bottom margin is blur + offset
                    float onRect = Distance(px, py, blur, blur + offset, size - blur, size - blur + offset);
                    float fromShadow = Mathf.Max(0f, Distance(px, py, blur, blur, size - blur, size - blur));
                    float alpha = strength * Mathf.Exp(-3f * (fromShadow / blur) * (fromShadow / blur)) * Mathf.Clamp01(onRect + 0.5f);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte) (alpha * 255f));
                }
            }
            var border = new Vector4(blur + radius, blur + offset + radius, blur + radius, blur - offset + radius);
            return ToSprite(pixels, size, border);
        }

        public static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font, float size, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = FontStyles.Normal;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A white rounded square of the given size, 9-sliced at the radius; radius = size / 2 is a circle.</summary>
        public static Sprite MakeRounded(int size, int radius)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance past the nearest corner's circle, antialiased over a pixel
                    float dx = Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius), 0f);
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) (alpha * 255));
                }
            }
            return ToSprite(pixels, size, new Vector4(radius, radius, radius, radius));
        }

        private static Sprite _wordmark;

        /// <summary>The crescent wordmark (board NextLogo), from Resources/Wordmark.png (remade with the editor menu
        /// "LunarRhythm/Make the wordmark"). Null if it's missing.</summary>
        public static Sprite Wordmark => _wordmark != null ? _wordmark : _wordmark = Resources.Load<Sprite>("Wordmark");

        /// <summary>Shows the wordmark in a logo image, <paramref name="width"/> wide, in place of the old script art.</summary>
        public static void ShowWordmark(Image logo, float width)
        {
            if (logo == null || Wordmark == null) return;
            logo.sprite = Wordmark;
            logo.color = Color.white;
            logo.type = Image.Type.Simple;
            logo.preserveAspect = true;
            logo.rectTransform.sizeDelta = new Vector2(width, width * Wordmark.rect.height / Wordmark.rect.width);
        }

        private static Sprite MakeCrescent(int size)
        {
            // A disc with a second, offset disc cut out of its upper right
            float s = size / 64f;
            var outer = new Vector2(29f, 31f) * s;
            float outerR = 25f * s;
            var cut = new Vector2(41f, 41f) * s;
            float cutR = 20f * s;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float inOuter = Mathf.Clamp01(outerR - Vector2.Distance(p, outer) + 0.5f);
                    float outCut = Mathf.Clamp01(Vector2.Distance(p, cut) - cutR + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) (inOuter * outCut * 255));
                }
            }
            return ToSprite(pixels, size, Vector4.zero);
        }

        private static Sprite ToSprite(Color32[] pixels, int size, Vector4 border)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        private static Color Hex(int rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
