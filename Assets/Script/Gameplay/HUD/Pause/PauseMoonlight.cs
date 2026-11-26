using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Menu;
using YARG.Menu.Navigation;
using YARG.Settings;
using YARG.Settings.Types;

namespace YARG.Gameplay.HUD
{
    /// <summary>
    /// The quick play pause as the Moonlight board draws it (MPause): the song dimmed behind, "Paused" with the
    /// song's time, Resume / Restart / Quick settings / Back to the library, and the quick settings beside them
    /// (volume and calibration). Lives inside YARG's pause object, so it opens and closes with it and calls the same
    /// actions. Sizes are the board's (1280×720) × 1.5.
    /// </summary>
    public class PauseMoonlight : MonoBehaviour
    {
        private static readonly (string Setting, string Label)[] Volumes =
        {
            (nameof(SettingsManager.Settings.MasterMusicVolume), "Master"),
            (nameof(SettingsManager.Settings.GuitarVolume), "Lead"),
            (nameof(SettingsManager.Settings.BassVolume), "Bass"),
            (nameof(SettingsManager.Settings.DrumsVolume), "Drums"),
            (nameof(SettingsManager.Settings.VocalsVolume), "Vocals"),
            (nameof(SettingsManager.Settings.SfxVolume), "Sound effects"),
            (nameof(SettingsManager.Settings.MusicPlayerVolume), "Menu music"),
        };
        private static readonly (string Setting, string Label)[] Calibration =
        {
            (nameof(SettingsManager.Settings.AudioCalibration), "Audio calibration"),
            (nameof(SettingsManager.Settings.VideoCalibration), "Video calibration"),
        };
        private static readonly string[] Items = { "Resume", "Restart", "Quick settings", "Back to the library" };
        private static readonly Color Track = new(0.051f, 0.075f, 0.188f);
        private static readonly Color Ink = new(0.043f, 0.063f, 0.149f);

        private GenericPause _pause;
        private RectTransform _root, _list, _card;
        private TextMeshProUGUI _subtitle;
        private int _item;
        private bool _inCard;
        private int _cardRow;   // 0 is the tab bar
        private bool _calibration;
        private readonly List<(CanvasGroup Group, float Alpha)> _hidden = new();

        public static PauseMoonlight Create(GenericPause pause)
        {
            var canvas = (RectTransform) pause.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            var root = new GameObject("Moonlight Pause", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            root.SetParent(pause.transform, true);
            // The song stays visible, well dimmed (the board's 72%, as it reads in linear blending)
            root.GetComponent<Image>().color = new Color(0.008f, 0.02f, 0.063f, 0.94f);
            // Above the gameplay HUD (lyrics, score), which the board dims too; under the help bar
            root.gameObject.AddComponent<Canvas>(); // sorted in OnEnable: an inactive canvas drops the override
            root.gameObject.AddComponent<GraphicRaycaster>();
            var view = root.gameObject.AddComponent<PauseMoonlight>();
            view._pause = pause;
            view._root = root;
            view.Build();
            return view;
        }

        private void Build()
        {
            var left = new GameObject("Paused", typeof(RectTransform)).GetComponent<RectTransform>();
            left.SetParent(_root, false);
            Pin(left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, -180f), new Vector2(600f, 600f));
            var title = Text(left, Moonlight.Heading, 51f, Moonlight.Text, FontWeight.Medium);
            title.text = "Paused";
            Pin(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(600f, 66f));
            _subtitle = Text(left, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
            _subtitle.richText = false;
            Pin(_subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -72f), new Vector2(600f, 30f));
            _list = new GameObject("Items", typeof(RectTransform)).GetComponent<RectTransform>();
            _list.SetParent(left, false);
            Pin(_list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -135f), new Vector2(600f, 400f));

            _card = Moonlight.Panel("Quick Settings", _root, 33, new Color(0.024f, 0.04f, 0.11f, 0.95f)).rectTransform;
            Pin(_card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(930f, -180f), new Vector2(780f, 0f));
            var stack = _card.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(36, 36, 33, 33);
            stack.spacing = 18f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            _card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void OnEnable()
        {
            if (_pause == null) return;
            var layer = GetComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = 90;
            // Only this screen: the YARG pause's own look and the shared song header hide while it's open
            foreach (Transform child in _pause.transform)
            {
                if (child != transform) Hide(child);
            }
            foreach (Transform child in _pause.transform.parent)
            {
                if (child.GetComponent<PauseMenuObject>() == null) Hide(child);
            }
            var game = FindAnyObjectByType<GameManager>();
            if (game != null)
            {
                _subtitle.text = $"{(string) game.Song.Name} · {Time(game.SongTime)} / {Time(game.SongLength)}";
            }
            _item = 0;
            _inCard = false;
            _cardRow = 0;
            _calibration = false;
            Draw();
            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new(MenuAction.Up, "Menu.Common.Up", () => Move(-1)),
                new(MenuAction.Down, "Menu.Common.Down", () => Move(1)),
                new(MenuAction.Green, "Menu.MusicLibrary.Footer.Choose", Choose),
                new(MenuAction.Red, "Menu.Pause.Resume", Back),
                new(MenuAction.Left, "Menu.Players.Adjust", () => Adjust(-1), hide: true),
                new(MenuAction.Right, "Menu.Players.Adjust", () => Adjust(1), hide: true),
            }, false));
        }

        private void OnDisable()
        {
            if (_pause == null) return;
            Navigator.Instance.PopScheme();
            foreach (var (group, alpha) in _hidden)
            {
                if (group != null) group.alpha = alpha;
            }
            _hidden.Clear();
            SettingsManager.SaveSettings();
        }

        private void Hide(Transform t)
        {
            if (!t.TryGetComponent(out CanvasGroup group)) group = t.gameObject.AddComponent<CanvasGroup>();
            _hidden.Add((group, group.alpha));
            group.alpha = 0f;
        }

        private static string Time(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return time.Hours > 0 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        // ---------- drawing ----------

        private void Draw()
        {
            foreach (Transform child in _list) Destroy(child.gameObject);
            for (int i = 0; i < Items.Length; i++)
            {
                bool on = !_inCard && i == _item || (i == 2 && _inCard);
                var row = Moonlight.Panel(Items[i], _list, 18, on ? Moonlight.MoonWash : Color.clear);
                row.raycastTarget = true;
                int index = i;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _inCard = false;
                    _item = index;
                    Choose();
                });
                Moonlight.OnHover(row.gameObject, () =>
                {
                    if (!_inCard && _item == index) return;
                    _inCard = false;
                    _item = index;
                    Draw();
                });
                Pin(row.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * 87f), new Vector2(600f, 78f));
                if (on) Crescent(row.transform, 24f);
                var label = Text(row.transform, Moonlight.Body, 30f, on ? Moonlight.Moon : Moonlight.Text,
                    on ? FontWeight.Bold : FontWeight.SemiBold);
                label.text = Items[i];
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(24f + 21f + 21f, 0f);
            }

            foreach (Transform child in _card) Destroy(child.gameObject);
            // The tab pill hugs its two tabs; a moon edge when it has focus
            var tabsRow = new GameObject("Tabs Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tabsRow.transform.SetParent(_card, false);
            var rowSize = tabsRow.GetComponent<LayoutElement>();
            rowSize.minHeight = rowSize.preferredHeight = 63f;
            var rowLayout = tabsRow.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            bool tabsFocused = _inCard && _cardRow == 0;
            var tabs = Moonlight.Panel("Tabs", tabsRow.transform, 32, Track);
            if (tabsFocused)
            {
                var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
                edge.GetComponent<LayoutElement>().ignoreLayout = true;
                edge.transform.SetParent(tabs.transform, false);
                edge.sprite = Moonlight.OutlineSprite(32);
                edge.type = Image.Type.Sliced;
                edge.color = Moonlight.Moon;
                edge.raycastTarget = false;
                Stretch(edge.rectTransform);
            }
            var tabRow = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabRow.padding = new RectOffset(6, 6, 6, 6);
            tabRow.spacing = 6f;
            tabRow.childControlWidth = tabRow.childControlHeight = true;
            tabRow.childForceExpandWidth = false;
            tabRow.childForceExpandHeight = true;
            foreach (var (name, calibration) in new[] { ("Volume", false), ("Calibration", true) })
            {
                bool on = _calibration == calibration;
                var tab = Moonlight.Panel(name, tabs.transform, 25, on ? Moonlight.Moon : Color.clear);
                tab.raycastTarget = true;
                tab.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _calibration = calibration;
                    _inCard = true;
                    _cardRow = 0;
                    Draw();
                });
                var text = Text(tab.transform, Moonlight.Body, 21f, on ? Ink : Moonlight.Muted, on ? FontWeight.Bold : FontWeight.SemiBold);
                text.text = name;
                text.alignment = TextAlignmentOptions.Center;
                Stretch(text.rectTransform);
                tab.gameObject.AddComponent<LayoutElement>().preferredWidth = text.GetPreferredValues(name).x + 48f;
            }

            var rows = _calibration ? Calibration : Volumes;
            for (int i = 0; i < rows.Length; i++)
            {
                bool on = _inCard && _cardRow == i + 1;
                SettingRow(rows[i].Setting, rows[i].Label, on, i + 1);
            }
        }

        private void SettingRow(string name, string label, bool on, int index)
        {
            var setting = SettingsManager.GetSettingByName(name);
            var row = Moonlight.Panel(label, _card, 12, on ? Moonlight.MoonWash : Color.clear);
            row.raycastTarget = true;
            row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                _inCard = true;
                _cardRow = index;
                Draw();
            });
            Moonlight.OnHover(row.gameObject, () =>
            {
                if ((_inCard && _cardRow == index) || Moonlight.Dragging) return;
                _inCard = true;
                _cardRow = index;
                Draw();
            });
            var size = row.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 57f;
            var key = Text(row.transform, Moonlight.Body, 22.5f, on ? Moonlight.Moon : Moonlight.Text, FontWeight.SemiBold);
            key.text = label;
            Pin(key.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(195f, 57f));
            var value = Text(row.transform, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.Regular);
            value.alignment = TextAlignmentOptions.MidlineRight;
            Pin(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(66f, 57f));
            if (setting is VolumeSetting volume)
            {
                value.text = Mathf.RoundToInt(volume.Value * 100f) + "%";
                var track = Moonlight.Panel("Track", row.transform, 3, Moonlight.Keycap);
                track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                track.rectTransform.offsetMin = new Vector2(195f + 21f, -3f);
                track.rectTransform.offsetMax = new Vector2(-66f - 21f, 3f);
                var fill = Moonlight.Panel("Fill", track.transform, 3, Moonlight.Moon);
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = new Vector2(volume.Value, 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var dot = Moonlight.Panel("Dot", track.transform, 12, Moonlight.Moon);
                Pin(dot.rectTransform, new Vector2(volume.Value, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
                // Drawn again only on release: Draw rebuilds the rows, which would drop the drag
                Moonlight.Draggable(track.rectTransform, fill.rectTransform, dot.rectTransform, t =>
                {
                    volume.Value = Mathf.Round(t * 100f) / 100f;
                    value.text = Mathf.RoundToInt(volume.Value * 100f) + "%";
                    return volume.Value;
                }, released: () =>
                {
                    _inCard = true;
                    _cardRow = index;
                    Draw();
                });
            }
            else if (setting is IntSetting number)
            {
                value.text = number.Value + " ms";
            }
        }

        // ---------- navigation ----------

        private void Move(int direction)
        {
            if (_inCard)
            {
                int count = (_calibration ? Calibration : Volumes).Length;
                _cardRow = Mathf.Clamp(_cardRow + direction, 0, count);
            }
            else
            {
                _item = Mathf.Clamp(_item + direction, 0, Items.Length - 1);
            }
            Draw();
        }

        private void Adjust(int direction)
        {
            if (!_inCard) return;
            if (_cardRow == 0)
            {
                _calibration = direction > 0;
            }
            else
            {
                var rows = _calibration ? Calibration : Volumes;
                switch (SettingsManager.GetSettingByName(rows[_cardRow - 1].Setting))
                {
                    case VolumeSetting volume:
                        volume.Value = Mathf.Clamp01(Mathf.Round((volume.Value + direction * 0.05f) * 20f) / 20f);
                        break;
                    case IntSetting number:
                        number.Value = Mathf.Clamp(number.Value + direction, number.Min, number.Max);
                        break;
                }
            }
            Draw();
        }

        private void Choose()
        {
            if (_inCard)
            {
                if (_cardRow == 0) _calibration = !_calibration;
                else Adjust(1);
                Draw();
                return;
            }
            switch (_item)
            {
                case 0: _pause.Back(); break;          // resume
                case 1: _pause.Restart(); break;
                case 2:
                    _inCard = true;
                    _cardRow = 1;
                    Draw();
                    break;
                case 3: _pause.BackToLibrary(); break;
            }
        }

        private void Back()
        {
            if (_inCard)
            {
                _inCard = false;
                _item = 2;
                Draw();
                return;
            }
            _pause.Back();
        }

        // ---------- helpers ----------

        private static Image Crescent(Transform parent, float left)
        {
            var crescent = new GameObject("Crescent", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            crescent.GetComponent<LayoutElement>().ignoreLayout = true;
            crescent.transform.SetParent(parent, false);
            crescent.sprite = Moonlight.Crescent;
            crescent.color = Moonlight.Moon;
            crescent.raycastTarget = false;
            Pin(crescent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left, 0f), new Vector2(21f, 21f));
            return crescent;
        }

        private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, float size, Color color, FontWeight weight)
        {
            var label = Moonlight.Label("Text", parent, font != null ? font : Moonlight.Body, size, color);
            label.fontWeight = weight;
            return label;
        }

        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
