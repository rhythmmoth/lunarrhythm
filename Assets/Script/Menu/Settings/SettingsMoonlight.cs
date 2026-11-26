using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Player;
using YARG.Settings;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;
using YARG.Settings.Types;

namespace YARG.Menu.Settings
{
    /// <summary>
    /// The Settings screen as the Moonlight board draws it (MSettings): tabs in a pill on the right of the header,
    /// one panel of rows (text values, toggles, sliders), and a panel about the selected row. Drawn over the YARG
    /// settings menu, which still owns the tabs and settings; the rows are built here from the same metadata. All
    /// sizes are the board's (1280×720) times 1.5.
    /// </summary>
    public class SettingsMoonlight : MonoBehaviour
    {
        private const float EDGE = 42f, TOP = 135f, BOTTOM = 102f, HEADER_Y = 72f, LIST_RIGHT = 1164f;
        private const float ROW = 66f, HEAD = 60f;
        private static readonly (string Tab, string Label)[] TabLabels =
        {
            ("General", "General"), ("Sound", "Sound"), ("Graphics", "Graphics"), ("AllSettings", "All settings"),
        };
        // The board's tab blurbs, shown when the selected row has no description of its own
        private static readonly Dictionary<string, string> Blurbs = new()
        {
            ["General"] = "Scores on online songs go to the leaderboards, and your profile, unlocks and online play use your Discord account.",
            ["Sound"] = "Lead, Bass, Drums and Vocals set each part of the song. Menu music is the jukebox.",
            ["Graphics"] = "The preview shows the real highway with your current settings.",
            ["AllSettings"] = "Every other settings page, including the advanced ones.",
        };

        private enum Kind { Header, Field, Button, Tab, Preset }

        private class Row
        {
            public Kind Kind;
            public string Name;          // field, button or tab name
            public string Section;       // the header above it
            public ISettingType Setting;
            public PresetKind Preset;
            public RectTransform Rect;
            public Image Wash, Crescent;
            public TextMeshProUGUI Label, Value;
            public Image Track, Knob, Fill, Dot;
        }

        private enum PresetKind { Camera, Colours, Highway, Hud }

        private SettingsMenu _menu;
        private RectTransform _root, _viewport, _content, _tabs, _previewBox;
        private Transform _previewUI;
        private Transform _previewHome;
        private TextMeshProUGUI _kicker, _asideTitle, _asideBody, _asideButton;
        private GameObject _asideButtonObject;
        private readonly List<Row> _rows = new();
        private int _selected;
        private string _subTab; // a page opened from All settings
        private bool _schemePushed;

        /// <summary>Showing YARG's own preset editor (Manage presets) instead of this screen.</summary>
        public bool Fallback { get; private set; }

        public static SettingsMoonlight Create(SettingsMenu menu, Transform previewUI)
        {
            foreach (Transform child in menu.transform)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            // The menu doesn't cover the help bar; this covers the whole canvas, as the board does
            var canvas = (RectTransform) menu.GetComponentInParent<Canvas>().rootCanvas.transform;
            var root = new GameObject("Moonlight Settings", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            root.SetParent(menu.transform, true);
            var view = root.gameObject.AddComponent<SettingsMoonlight>();
            view._menu = menu;
            view._root = root;
            view._previewUI = previewUI;
            view._previewHome = previewUI != null ? previewUI.parent : null;
            view.Build();
            return view;
        }

        private void Build()
        {
            // Header: back, title, the tab pill on the right
            var back = Moonlight.Panel("Back", _root, 33, Moonlight.PanelFill);
            back.raycastTarget = true;
            Pin(back.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE, -HEADER_Y), new Vector2(66f, 66f));
            back.gameObject.AddComponent<Button>().onClick.AddListener(Back);
            var chevron = Graphic<Image>("Chevron", back.transform);
            chevron.sprite = Moonlight.Chevron;
            chevron.color = Moonlight.Text;
            chevron.rectTransform.sizeDelta = new Vector2(27f, 27f);
            var title = Text(_root, Moonlight.Heading, 36f, Moonlight.Text, FontWeight.Medium);
            title.text = "Settings";
            Pin(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE + 90f, -HEADER_Y), new Vector2(400f, 66f));

            var pill = Moonlight.Panel("Tabs", _root, 36, Moonlight.PanelFill);
            _tabs = pill.rectTransform;
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 6, 6);
            row.spacing = 6f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            Pin(_tabs, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-EDGE, -HEADER_Y), new Vector2(0f, 66f));
            foreach (var (tab, label) in TabLabels)
            {
                var tabButton = Moonlight.Panel(tab, _tabs, 27, Color.clear);
                tabButton.raycastTarget = true;
                tabButton.gameObject.AddComponent<Button>().onClick.AddListener(() => OpenTab(tab));
                var text = Text(tabButton.transform, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.SemiBold);
                text.text = label;
                text.alignment = TextAlignmentOptions.Center;
                text.overflowMode = TextOverflowModes.Overflow;
                Stretch(text.rectTransform);
                var size = tabButton.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = text.GetPreferredValues(label).x + 48f;
                size.preferredHeight = 54f;
            }

            // The list, scrolled by keeping the selected row in view
            var list = PanelRect(_root, EDGE, LIST_RIGHT);
            _viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            _viewport.SetParent(list, false);
            Stretch(_viewport);
            _viewport.offsetMin = new Vector2(15f, 12f);
            _viewport.offsetMax = new Vector2(-15f, -12f);
            _content = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1f);
            list.GetComponent<Image>().raycastTarget = true; // catches the mouse wheel
            var scroller = list.gameObject.AddComponent<ScrollCatcher>();
            scroller.Scrolled = delta => Select(_selected - Math.Sign(delta));

            // The aside: section, row name, its description, a preview (Graphics) and the tab's button
            var aside = PanelRect(_root, LIST_RIGHT + 30f, 1920f - EDGE);
            var stack = aside.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(33, 33, 33, 33);
            stack.spacing = 18f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            _kicker = Text(aside, Moonlight.Heading, 18f, Moonlight.Muted, FontWeight.Medium);
            _kicker.characterSpacing = 12f;
            _asideTitle = Text(aside, Moonlight.Heading, 33f, Moonlight.Text, FontWeight.Medium);
            _asideTitle.textWrappingMode = TextWrappingModes.Normal;
            _asideBody = Text(aside, Moonlight.Body, 22.5f, Moonlight.Soft, FontWeight.Regular);
            _asideBody.textWrappingMode = TextWrappingModes.Normal;
            _asideBody.lineSpacing = 12f;
            _previewBox = Moonlight.Panel("Preview", aside, 18, Moonlight.Night).rectTransform;
            _previewBox.gameObject.AddComponent<LayoutElement>().preferredHeight = 375f;
            _previewBox.gameObject.AddComponent<RectMask2D>();
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(aside, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;
            var button = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            button.transform.SetParent(aside, false);
            button.GetComponent<LayoutElement>().preferredHeight = 66f;
            var outline = button.GetComponent<Image>();
            outline.sprite = Moonlight.OutlineSprite(33);
            outline.type = Image.Type.Sliced;
            outline.color = new Color(0.627f, 0.686f, 1f, 0.35f);
            button.GetComponent<Button>().onClick.AddListener(AsideAction);
            _asideButton = Text(button.transform, Moonlight.Body, 24f, Moonlight.Text, FontWeight.SemiBold);
            _asideButton.alignment = TextAlignmentOptions.Center;
            Stretch(_asideButton.rectTransform);
            _asideButtonObject = button;
        }

        private string CurrentTabName => _subTab ?? _menu.CurrentTab?.Name;

        // ---------- rows ----------

        /// <summary>Rebuilds the rows for the current tab (the YARG menu calls this whenever it rebuilds).</summary>
        public void Rebuild(bool keepPosition = false)
        {
            if (Fallback) return;
            int keep = keepPosition ? _selected : 0;
            foreach (Transform child in _content) Destroy(child.gameObject);
            _rows.Clear();
            var tab = _menu.CurrentTab;
            if (_subTab == null && tab != null && tab.Name != null && tab is not MetadataTab)
            {
                if (tab.Name == "Presets") BuildPresets();
                else BuildAllSettings();
            }
            else if (tab is MetadataTab metadata)
            {
                BuildMetadata(metadata);
            }
            UpdateTabs();
            UpdatePreview();
            Select(keep, true);
        }

        private void BuildMetadata(MetadataTab tab)
        {
            bool advanced = _menu.ShowAdvanced;
            var shown = tab.Settings.Where(s => (advanced || !s.IsAdvanced) &&
                (s is not ButtonRowMetadata { Visible: { } visible } || visible())).ToList();
            string section = null;
            for (int i = 0; i < shown.Count; i++)
            {
                switch (shown[i])
                {
                    case HeaderMetadata header:
                        if (i + 1 == shown.Count || shown[i + 1] is HeaderMetadata) break; // nothing under it
                        section = Localize.Key("Settings.Header", header.HeaderName);
                        AddRow(new Row { Kind = Kind.Header, Name = header.HeaderName, Section = section });
                        break;
                    case ButtonRowMetadata buttons:
                        foreach (string name in buttons.Buttons)
                        {
                            AddRow(new Row { Kind = Kind.Button, Name = name, Section = section });
                        }
                        break;
                    case FieldMetadata field:
                        ISettingType setting;
                        try { setting = SettingsManager.GetSettingByName(field.FieldName); }
                        catch (Exception) { continue; }
                        // Resolutions and the like fill their list when shown
                        setting?.GetType().GetMethod("UpdateValues")?.Invoke(setting, null);
                        AddRow(new Row { Kind = Kind.Field, Name = field.FieldName, Setting = setting, Section = section });
                        break;
                }
            }
        }

        private void BuildPresets()
        {
            string section = "PRESETS";
            AddRow(new Row { Kind = Kind.Header, Name = "Presets", Section = section });
            foreach (var kind in new[] { PresetKind.Camera, PresetKind.Colours, PresetKind.Highway, PresetKind.Hud })
            {
                AddRow(new Row { Kind = Kind.Preset, Preset = kind, Section = section });
            }
        }

        private void BuildAllSettings()
        {
            string section = "ALL SETTINGS";
            AddRow(new Row { Kind = Kind.Header, Name = "AllSettings", Section = section });
            foreach (var tab in SettingsManager.AllSettingsTabs.Except(SettingsManager.DisplayedSettingsTabs))
            {
                if (tab is MetadataTab) AddRow(new Row { Kind = Kind.Tab, Name = tab.Name, Section = section });
            }
        }

        private void AddRow(Row row)
        {
            float y = _rows.Count == 0 ? 0f : -(_rows.Last().Rect.anchoredPosition.y * -1f + _rows.Last().Rect.sizeDelta.y);
            float height = row.Kind == Kind.Header ? HEAD : ROW;
            var go = new GameObject(row.Name ?? row.Preset.ToString(), typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_content, false);
            row.Rect = (RectTransform) go.transform;
            row.Rect.anchorMin = new Vector2(0f, 1f);
            row.Rect.anchorMax = Vector2.one;
            row.Rect.pivot = new Vector2(0.5f, 1f);
            row.Rect.anchoredPosition = new Vector2(0f, y);
            row.Rect.sizeDelta = new Vector2(0f, height);
            row.Wash = go.GetComponent<Image>();
            row.Wash.sprite = Moonlight.RoundedSprite(15);
            row.Wash.type = Image.Type.Sliced;
            row.Wash.color = Color.clear;
            int index = _rows.Count;
            go.GetComponent<Button>().transition = Selectable.Transition.None;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_selected == index) Activate();
                else Select(index);
            });
            if (row.Kind != Kind.Header) Moonlight.OnHover(go, () => { if (_selected != index && !Moonlight.Dragging) Select(index); });

            if (row.Kind == Kind.Header)
            {
                row.Wash.raycastTarget = false;
                row.Label = Text(go.transform, Moonlight.Heading, 18f, Moonlight.Muted, FontWeight.Medium);
                row.Label.characterSpacing = 12f;
                row.Label.text = (row.Section ?? "").ToUpperInvariant();
                row.Label.alignment = TextAlignmentOptions.BottomLeft;
                Stretch(row.Label.rectTransform);
                row.Label.margin = new Vector4(21f, 0f, 21f, 12f);
            }
            else
            {
                row.Crescent = Graphic<Image>("Crescent", go.transform);
                row.Crescent.sprite = Moonlight.Crescent;
                row.Crescent.color = Moonlight.Moon;
                Pin(row.Crescent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(21f, 0f), new Vector2(21f, 21f));
                row.Label = Text(go.transform, Moonlight.Body, 24f, Moonlight.Text, FontWeight.SemiBold);
                Stretch(row.Label.rectTransform);
                row.Label.rectTransform.offsetMin = new Vector2(60f, 0f);
                row.Label.rectTransform.offsetMax = new Vector2(-420f, 0f);
                row.Value = Text(go.transform, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
                row.Value.alignment = TextAlignmentOptions.MidlineRight;
                Stretch(row.Value.rectTransform);
                row.Value.rectTransform.offsetMax = new Vector2(-21f, 0f);
                row.Value.rectTransform.offsetMin = new Vector2(420f, 0f);
                if (row.Setting is ToggleSetting)
                {
                    row.Track = Moonlight.Panel("Track", go.transform, 18, Moonlight.Keycap);
                    Pin(row.Track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-21f, 0f), new Vector2(66f, 36f));
                    row.Knob = Moonlight.Panel("Knob", row.Track.transform, 14, Moonlight.Muted);
                    row.Knob.rectTransform.sizeDelta = new Vector2(27f, 27f);
                }
                else if (row.Setting is SliderSetting slider && !float.IsInfinity(slider.Min) && !float.IsInfinity(slider.Max))
                {
                    row.Track = Moonlight.Panel("Track", go.transform, 3, Moonlight.Keycap);
                    Pin(row.Track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-21f - 60f - 18f, 0f), new Vector2(270f, 6f));
                    row.Fill = Moonlight.Panel("Fill", row.Track.transform, 3, Moonlight.Moon);
                    row.Fill.rectTransform.anchorMin = Vector2.zero;
                    row.Fill.rectTransform.pivot = new Vector2(0f, 0.5f);
                    row.Fill.rectTransform.offsetMin = row.Fill.rectTransform.offsetMax = Vector2.zero;
                    row.Dot = Moonlight.Panel("Dot", row.Track.transform, 14, Moonlight.Moon);
                    row.Dot.rectTransform.sizeDelta = new Vector2(27f, 27f);
                    row.Value.rectTransform.offsetMin = new Vector2(-81f, 0f);
                    row.Value.rectTransform.anchorMin = new Vector2(1f, 0f);
                    Moonlight.Draggable(row.Track.rectTransform, row.Fill.rectTransform, row.Dot.rectTransform, t =>
                    {
                        slider.Value = Mathf.Clamp(Mathf.Round(Mathf.Lerp(slider.Min, slider.Max, t) * 100f) / 100f, slider.Min, slider.Max);
                        FillRow(row);
                        UpdateAside();
                        return Mathf.InverseLerp(slider.Min, slider.Max, slider.Value);
                    }, () => { if (_selected != index) Select(index); });
                }
            }
            _rows.Add(row);
            FillRow(row);
            _content.sizeDelta = new Vector2(0f, -y + height);
        }

        private void FillRow(Row row)
        {
            switch (row.Kind)
            {
                case Kind.Field:
                    row.Label.text = Shorter(Sentence(Localize.Key("Settings.Setting", row.Name, "Name")));
                    FillValue(row);
                    break;
                case Kind.Button:
                    if (row.Name == nameof(SettingsManager.Settings.SignOutOfLeaderboards))
                    {
                        row.Label.text = "Signed in as " + YARG.Core.Song.Leaderboards.PlayerName;
                        row.Label.richText = false;
                        row.Value.text = "Sign out ›";
                    }
                    else
                    {
                        row.Label.text = Sentence(Localize.Key("Settings.Button", row.Name));
                        row.Value.text = "›";
                    }
                    break;
                case Kind.Tab:
                    row.Label.text = Sentence(Localize.Key("Settings.Tab", row.Name));
                    row.Value.text = "›";
                    break;
                case Kind.Preset:
                    row.Label.text = row.Preset switch
                    {
                        PresetKind.Camera => "Camera", PresetKind.Colours => "Colours",
                        PresetKind.Highway => "Highway", _ => "HUD layout",
                    };
                    var profile = PlayerOne;
                    row.Value.text = profile == null ? "No player connected" : row.Preset switch
                    {
                        PresetKind.Camera  => Name(CustomContentManager.CameraSettings.GetPresetById(profile.CameraPreset)),
                        PresetKind.Colours => Name(CustomContentManager.ColorProfiles.GetPresetById(profile.ColorProfile)),
                        PresetKind.Highway => Name(CustomContentManager.HighwayPresets.GetPresetById(profile.HighwayPreset)),
                        _                  => Name(CustomContentManager.RockMeterPresets.GetPresetById(profile.RockMeterPreset)),
                    };
                    break;
            }
        }

        private void FillValue(Row row)
        {
            switch (row.Setting)
            {
                case ToggleSetting toggle:
                    row.Value.text = string.Empty;
                    row.Track.color = toggle.Value ? Moonlight.Moon : Moonlight.Keycap;
                    row.Knob.color = toggle.Value ? new Color(0.043f, 0.063f, 0.149f) : Moonlight.Muted;
                    Pin(row.Knob.rectTransform, new Vector2(toggle.Value ? 1f : 0f, 0.5f), new Vector2(toggle.Value ? 1f : 0f, 0.5f),
                        new Vector2(toggle.Value ? -4.5f : 4.5f, 0f), new Vector2(27f, 27f));
                    break;
                case SliderSetting slider when row.Track != null:
                    float t = Mathf.InverseLerp(slider.Min, slider.Max, slider.Value);
                    row.Fill.rectTransform.anchorMax = new Vector2(t, 1f);
                    Pin(row.Dot.rectTransform, new Vector2(t, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(27f, 27f));
                    bool percent = slider is VolumeSetting || (slider.Min == 0f && slider.Max == 1f);
                    row.Value.text = percent ? Mathf.RoundToInt(slider.Value * 100f) + "%" : slider.Value.ToString("0.##");
                    break;
                case SliderSetting slider:
                    row.Value.text = slider.Value.ToString("0.##");
                    break;
                case IntSetting number:
                    row.Value.text = number.Value + (row.Name.Contains("Calibration") ? " ms" : "");
                    break;
                case DurationSetting duration:
                    row.Value.text = duration.Value.ToString("0.##") + " " + duration.PreferredUnit.ToString().ToLowerInvariant();
                    break;
                case IDropdownSetting dropdown:
                    string raw = dropdown.CurrentIndex >= 0 ? dropdown.IndexToString(dropdown.CurrentIndex) : "";
                    row.Value.text = dropdown.Localizable ? Localize.Key("Settings.Setting", row.Name, "Dropdown", raw) : raw;
                    break;
                default:
                    row.Value.text = row.Setting?.ValueAsObject?.ToString() ?? "";
                    break;
            }
        }

        /// <summary>A setting changed (here, or from a button such as sign-in): refresh what the rows show.</summary>
        public void RefreshValues()
        {
            foreach (var row in _rows) FillRow(row);
            UpdateAside();
        }

        // ---------- selection and actions ----------

        private void Select(int index, bool snap = false)
        {
            if (_rows.Count == 0)
            {
                _selected = 0;
                UpdateAside();
                PushScheme();
                return;
            }
            int step = index < _selected ? -1 : 1;
            index = Mathf.Clamp(index, 0, _rows.Count - 1);
            // Headers aren't selectable: carry on in the direction of travel
            while (index >= 0 && index < _rows.Count && _rows[index].Kind == Kind.Header) index += step;
            if (index < 0 || index >= _rows.Count)
            {
                index = _rows.FindIndex(r => r.Kind != Kind.Header);
                if (snap || index < 0) index = Math.Max(0, index);
                else index = _selected;
            }
            _selected = index;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Kind == Kind.Header) continue;
                bool on = i == _selected;
                row.Wash.color = on ? Moonlight.MoonWash : Color.clear;
                row.Crescent.gameObject.SetActive(on);
                row.Label.color = on ? Moonlight.Moon : Moonlight.Text;
                row.Label.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
            }
            KeepInView();
            UpdateAside();
            PushScheme();
        }

        private void KeepInView()
        {
            if (_rows.Count == 0) return;
            var rect = _rows[_selected].Rect;
            float top = -rect.anchoredPosition.y, bottom = top + rect.sizeDelta.y;
            float view = _viewport.rect.height, y = _content.anchoredPosition.y;
            // Bring the section header into view with the first row under it
            if (_selected > 0 && _rows[_selected - 1].Kind == Kind.Header) top -= HEAD;
            if (top < y) y = top;
            else if (bottom > y + view) y = bottom - view;
            _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, Mathf.Max(0f, _content.sizeDelta.y - view)));
        }

        private Row Current => _rows.Count > 0 ? _rows[_selected] : null;

        private void Activate()
        {
            var row = Current;
            if (row == null) return;
            switch (row.Kind)
            {
                case Kind.Button:
                    SettingsManager.InvokeButton(row.Name);
                    RefreshValues();
                    break;
                case Kind.Tab:
                    _subTab = row.Name;
                    _menu.SelectTabByName(row.Name);
                    break;
                case Kind.Field:
                case Kind.Preset:
                    Change(1);
                    break;
            }
        }

        private void Change(int direction)
        {
            var row = Current;
            if (row == null) return;
            if (row.Kind == Kind.Preset)
            {
                ChangePreset(row.Preset, direction);
                RefreshValues();
                return;
            }
            if (row.Kind != Kind.Field) return;
            switch (row.Setting)
            {
                case ToggleSetting toggle:
                    toggle.Value = !toggle.Value;
                    break;
                case VolumeSetting volume:
                    volume.Value = Mathf.Clamp01(Mathf.Round((volume.Value + direction * 0.05f) * 20f) / 20f);
                    break;
                case SliderSetting slider:
                    float span = float.IsInfinity(slider.Max - slider.Min) ? 1f : slider.Max - slider.Min;
                    slider.Value = Mathf.Clamp(slider.Value + direction * span / 20f, slider.Min, slider.Max);
                    break;
                case IntSetting number:
                    number.Value = Mathf.Clamp(number.Value + direction, number.Min, number.Max);
                    break;
                case DurationSetting duration:
                    duration.Value = Math.Clamp(duration.Value + direction, 0d, duration.Max);
                    break;
                case IDropdownSetting dropdown when dropdown.Count > 0:
                    dropdown.SelectIndex(((dropdown.CurrentIndex < 0 ? 0 : dropdown.CurrentIndex) + direction + dropdown.Count) % dropdown.Count);
                    break;
            }
            FillRow(row);
            UpdateAside();
        }

        private static YargProfile PlayerOne => PlayerContainer.Players.Count > 0 ? PlayerContainer.Players[0].Profile : null;

        private static void ChangePreset(PresetKind kind, int direction)
        {
            var profile = PlayerOne;
            if (profile == null) return;
            switch (kind)
            {
                case PresetKind.Camera:
                    profile.CameraPreset = Cycle(CustomContentManager.CameraSettings, profile.CameraPreset, direction);
                    break;
                case PresetKind.Colours:
                    profile.ColorProfile = Cycle(CustomContentManager.ColorProfiles, profile.ColorProfile, direction);
                    break;
                case PresetKind.Highway:
                    profile.HighwayPreset = Cycle(CustomContentManager.HighwayPresets, profile.HighwayPreset, direction);
                    break;
                default:
                    profile.RockMeterPreset = Cycle(CustomContentManager.RockMeterPresets, profile.RockMeterPreset, direction);
                    break;
            }
        }

        private static Guid Cycle(CustomContent content, Guid current, int direction)
        {
            var list = content.DefaultBasePresets.Concat(content.CustomBasePresets).ToList();
            if (list.Count == 0) return current;
            int index = list.FindIndex(p => p.Id == current);
            return list[((index < 0 ? 0 : index) + direction + list.Count) % list.Count].Id;
        }

        private static string Name(BasePreset preset) => preset?.Name ?? "Default";

        /// <summary>
        /// The board's sentence case for YARG's Title Case names ("Open Calibrator" → "Open calibrator"), keeping
        /// acronyms and mixed-case words (VSync, HUD, FPS); a unit in brackets goes, as the value shows it.
        /// </summary>
        /// <summary>The board's short Sound names: "Lead" for "Lead volume", "Menu music" for the music player.</summary>
        private static string Shorter(string name) => name switch
        {
            "Master music volume" => "Master",
            "SFX volume" => "Sound effects",
            "Music player volume" => "Menu music",
            _ when name.EndsWith(" volume") => name[..^" volume".Length],
            _ => name,
        };

        private static readonly HashSet<string> ProperNouns = new() { "Discord", "YARG", "LunarRhythm", "Windows", "Mac" };

        internal static string Sentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\((ms|s|%|Hz)\)$", "");
            // Every Title-case word after the first, also inside "Backgrounds/Venue" and "Per-Song"; acronyms
            // and mixed case (VSync, HUD, FPS) are left alone
            return System.Text.RegularExpressions.Regex.Replace(text, @"\b[A-Z][a-z]+\b", m =>
                m.Index == 0 || ProperNouns.Contains(m.Value) ? m.Value : m.Value.ToLowerInvariant());
        }

        // ---------- aside ----------

        private void UpdateAside()
        {
            string tab = _subTab ?? _menu.CurrentTab?.Name ?? "";
            var row = Current;
            string tabName = Localize.Key("Settings.Tab", tab);
            _kicker.text = (row?.Section ?? tabName).ToUpperInvariant();
            _asideTitle.text = row == null || row.Kind == Kind.Header ? tabName : row.Label.text;
            _asideTitle.richText = false;
            string description = null;
            if (row?.Kind == Kind.Field && LocalizationManager.TryGetLocalizedKey($"Settings.Setting.{row.Name}.Description", out var text))
            {
                description = text;
            }
            _asideBody.text = description ?? (Blurbs.TryGetValue(tab, out var blurb) ? blurb : "");

            _asideButtonObject.SetActive(true);
            _asideButton.text = tab switch
            {
                "General" => YARG.Core.Song.Leaderboards.SignedIn ? "Sign out" : "Sign in with Discord",
                "Presets" => "Manage presets",
                "AllSettings" => "",
                _ => _subTab != null ? "Back to all settings" : "Reset this tab",
            };
            _asideButtonObject.SetActive(_asideButton.text.Length > 0);
        }

        private void AsideAction()
        {
            string tab = _subTab ?? _menu.CurrentTab?.Name ?? "";
            switch (tab)
            {
                case "General":
                    SettingsManager.InvokeButton(YARG.Core.Song.Leaderboards.SignedIn
                        ? nameof(SettingsManager.Settings.SignOutOfLeaderboards)
                        : nameof(SettingsManager.Settings.SignInWithDiscord));
                    break;
                case "Presets":
                    ShowFallback();
                    break;
                default:
                    if (_subTab != null) BackToAll();
                    else ResetTab();
                    break;
            }
        }

        private static SettingsManager.SettingContainer _defaults;

        /// <summary>Puts every setting on this tab back to its default.</summary>
        private void ResetTab()
        {
            if (_menu.CurrentTab is not MetadataTab tab) return;
            _defaults ??= new SettingsManager.SettingContainer();
            foreach (var field in tab.Settings.OfType<FieldMetadata>())
            {
                var property = typeof(SettingsManager.SettingContainer).GetProperty(field.FieldName);
                if (property?.GetValue(_defaults) is ISettingType fresh && property.GetValue(SettingsManager.Settings) is ISettingType live
                    && !live.ValueEquals(fresh.ValueAsObject))
                {
                    live.ValueAsObject = fresh.ValueAsObject;
                }
            }
            RefreshValues();
        }

        /// <summary>The highway preview (Graphics) sits in the aside's box, like the board's.</summary>
        private void UpdatePreview()
        {
            bool show = (_subTab ?? _menu.CurrentTab?.Name) == "Graphics" && _previewUI != null;
            _previewBox.gameObject.SetActive(show);
            if (_previewUI == null) return;
            if (show && _previewUI.parent != _previewBox)
            {
                _previewUI.SetParent(_previewBox, false);
                var rect = (RectTransform) _previewUI;
                if (rect.TryGetComponent(out LayoutElement ignore)) ignore.ignoreLayout = true;
                else rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                // The preview renders 860×720: fit the box's height, centred, on the board's dark box (not
                // YARG's moving checkerboard, which is this container's own material)
                Stretch(rect); // the crop is set to the box's shape in LateUpdate
                rect.localScale = Vector3.one;
                // It masks the preview: keep it masking, just don't draw it
                if (rect.TryGetComponent(out Mask mask)) mask.showMaskGraphic = false;
                else if (rect.TryGetComponent(out RawImage checker)) checker.enabled = false;
            }
            else if (!show && _previewUI.parent != _previewHome && _previewHome != null)
            {
                _previewUI.SetParent(_previewHome, false);
                _previewUI.localScale = Vector3.one;
                if (_previewUI.TryGetComponent(out Mask mask)) mask.showMaskGraphic = true;
                else if (_previewUI.TryGetComponent(out RawImage checker)) checker.enabled = true;
            }
        }

        private void UpdateTabs()
        {
            string active = _subTab != null ? "AllSettings" : _menu.CurrentTab?.Name;
            foreach (Transform tab in _tabs)
            {
                if (!tab.TryGetComponent(out Button _)) continue; // the pill's drop shadow isn't a tab
                bool on = tab.name == active;
                tab.GetComponent<Image>().color = on ? Moonlight.Moon : Color.clear;
                var label = tab.GetComponentInChildren<TextMeshProUGUI>();
                label.color = on ? new Color(0.043f, 0.063f, 0.149f) : Moonlight.Muted;
                label.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
            }
        }

        private void OpenTab(string tab)
        {
            _subTab = null;
            _menu.SelectTabByName(tab);
            Rebuild();
        }

        private void NextTab()
        {
            string current = _subTab != null ? "AllSettings" : _menu.CurrentTab?.Name;
            int index = Array.FindIndex(TabLabels, t => t.Tab == current);
            OpenTab(TabLabels[(index + 1) % TabLabels.Length].Tab);
        }

        private void BackToAll()
        {
            _subTab = null;
            _menu.SelectTabByName("AllSettings");
            Rebuild();
        }

        private void Back()
        {
            if (_subTab != null) BackToAll();
            else _menu.gameObject.SetActive(false);
        }

        private void LateUpdate() => FramePreview();

        /// <summary>
        /// The preview camera renders the whole screen; YARG crops a slice as tall as the screen, which squashes it in
        /// a wide box. Crop to the box's own shape instead, around the highway: the whole highway, undistorted, as
        /// on the board. Every frame, since the preview rebuilds (and resets its crop) when settings change.
        /// </summary>
        private void FramePreview()
        {
            if (_previewUI == null || !_previewBox.gameObject.activeInHierarchy) return;
            var box = _previewBox.rect;
            foreach (var image in _previewUI.GetComponentsInChildren<RawImage>())
            {
                if (image.texture == null || box.height <= 0f) continue;
                // The highway fills the bottom two thirds of the screen (as in a song): show just that, so it runs the
                // box's height like the board's
                const float HIGHWAY = 0.66f;
                float width = Mathf.Min(1f, box.width / box.height * HIGHWAY * image.texture.height / image.texture.width);
                image.uvRect = new Rect(0.5f - width / 2f, 0f, width, HIGHWAY);
            }
        }

        // ---------- YARG's preset editor ----------

        private void ShowFallback()
        {
            Fallback = true;
            _schemePushed = false; // the menu pops it as it pushes its own
            foreach (Transform child in _menu.transform)
            {
                if (child == transform) continue;
                if (child.TryGetComponent(out CanvasGroup group))
                {
                    group.alpha = 1f;
                    group.blocksRaycasts = true;
                }
            }
            _root.gameObject.SetActive(false);
            _menu.RefreshNavigationScheme(); // YARG's own controls while editing presets
        }

        private readonly List<(CanvasGroup Group, float Alpha, bool Blocks)> _covered = new();

        /// <summary>Called when the settings menu closes: the menu it opened over comes back.</summary>
        public void OnHidden()
        {
            foreach (var (group, alpha, blocks) in _covered)
            {
                if (group == null) continue;
                group.alpha = alpha;
                group.blocksRaycasts = blocks;
            }
            _covered.Clear();
        }

        /// <summary>Called when the settings menu opens: always this screen, on the first tab.</summary>
        public void OnShown()
        {
            // Settings opens over the current menu (the title screen); the board shows only the background
            OnHidden();
            foreach (var other in FindObjectsByType<MenuObject>(FindObjectsSortMode.None))
            {
                if (!other.gameObject.activeInHierarchy || other.transform.IsChildOf(_menu.transform) ||
                    _menu.transform.IsChildOf(other.transform))
                {
                    continue;
                }
                if (!other.TryGetComponent(out CanvasGroup group)) group = other.gameObject.AddComponent<CanvasGroup>();
                _covered.Add((group, group.alpha, group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            if (Fallback)
            {
                Fallback = false;
                foreach (Transform child in _menu.transform)
                {
                    if (child == transform) continue;
                    if (child.TryGetComponent(out CanvasGroup group))
                    {
                        group.alpha = 0f;
                        group.blocksRaycasts = false;
                    }
                }
                _root.gameObject.SetActive(true);
            }
            _subTab = null;
            Rebuild();
        }

        // ---------- navigation ----------

        /// <summary>Pushes this screen's controls (one scheme; the YARG menu pops it when it closes).</summary>
        public void PushScheme()
        {
            if (Fallback || !isActiveAndEnabled) return;
            if (_schemePushed)
            {
                Navigator.Instance.PopScheme();
            }
            var row = Current;
            var entries = new List<NavigationScheme.Entry>
            {
                new(MenuAction.Up, "Menu.Common.Up", () => Select(_selected - 1)),
                new(MenuAction.Down, "Menu.Common.Down", () => Select(_selected + 1)),
                new(MenuAction.Green, row?.Kind is Kind.Button or Kind.Tab ? "Menu.Common.Confirm" : "Menu.Players.Change", Activate),
                new(MenuAction.Red, "Menu.Common.Back", Back),
                new(MenuAction.Orange, "Menu.Settings.SwitchTab", NextTab),
                new(MenuAction.Blue, _menu.ShowAdvanced ? "Menu.Settings.HideAdvanced" : "Menu.Settings.ShowAdvanced",
                    () => _menu.ToggleAdvancedFromMoonlight()),
                new(MenuAction.Left, "Menu.Players.Adjust", () => Change(-1), hide: true),
                new(MenuAction.Right, "Menu.Players.Adjust", () => Change(1), hide: true),
            };
            Navigator.Instance.PushScheme(new NavigationScheme(entries, true)); // the jukebox keeps playing
            _schemePushed = true;
        }

        /// <summary>The YARG menu popped this screen's scheme (it closed).</summary>
        public void SchemePopped() => _schemePushed = false;

        // ---------- helpers ----------

        private static RectTransform PanelRect(Transform parent, float left, float right)
        {
            var panel = Moonlight.Panel("Panel", parent, 27, Moonlight.PanelFill).rectTransform;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.offsetMin = new Vector2(left, BOTTOM);
            panel.offsetMax = new Vector2(right, -TOP);
            return panel;
        }

        private static T Graphic<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(parent, false);
            var graphic = go.GetComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
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

        /// <summary>Mouse wheel over the list moves the selection, like the library.</summary>
        private class ScrollCatcher : MonoBehaviour, UnityEngine.EventSystems.IScrollHandler
        {
            public Action<float> Scrolled;
            public void OnScroll(UnityEngine.EventSystems.PointerEventData eventData) => Scrolled?.Invoke(eventData.scrollDelta.y);
        }
    }
}
