using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Input;
using YARG.Menu.Main;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Scores;
using YARG.Settings.Customization;

namespace YARG.Menu.ProfileList
{
    /// <summary>
    /// The Players screen and its Edit player page, drawn as the Moonlight boards have them (MPlayers, MPlayerEdit)
    /// over the YARG profile menu, whose list is still built (hidden) so connecting, devices and reordering keep using
    /// its code. All sizes are the boards' (1280×720) times 1.5.
    /// </summary>
    public class PlayersMoonlight : MonoBehaviour
    {
        private const float EDGE = 42f, TOP = 135f, BOTTOM = 102f, HEADER_Y = 72f;
        private const float LIST_RIGHT = 1164f;  // the left panel: 28 + 748 on the board
        private const float EDIT_RIGHT = 882f;   // the edit page's left panel: (28 + 560) × 1.5
        private static readonly Color[] AvatarColors =
        {
            new(0.227f, 0.145f, 0.439f), new(0.122f, 0.29f, 0.416f), new(0.353f, 0.114f, 0.247f), new(0.114f, 0.29f, 0.227f),
        };
        private static readonly Color[] LaneColors =
        {
            new(0.243f, 0.769f, 0.427f), new(0.91f, 0.29f, 0.373f), new(0.949f, 0.788f, 0.298f),
            new(0.29f, 0.616f, 0.949f), new(0.949f, 0.6f, 0.29f),
        };
        private static readonly Color Rule = new(0.627f, 0.686f, 1f, 0.08f);

        private ProfileListMenu _menu;
        private Transform _yargList;
        private RectTransform _root, _listPage, _editPage;

        // Header
        private TextMeshProUGUI _title, _subtitle;
        private GameObject _addButton;

        // Players page
        private RectTransform _rows;
        private readonly List<(YargProfile Profile, Image Wash, Image Crescent, TextMeshProUGUI Name)> _rowViews = new();
        private int _selected;
        private RawImage _asideAvatarPicture;
        private TextMeshProUGUI _asideInitial, _asideName, _asideSub;
        private Image _asideAvatar;
        private readonly TextMeshProUGUI[] _detailValues = new TextMeshProUGUI[2];
        private TextMeshProUGUI _primaryLabel, _secondaryLabel;

        // Edit page
        private YargProfile _editing;
        private int _editSelected;
        private int _lane;
        private readonly List<(Image Wash, Image Crescent, TextMeshProUGUI Label, TextMeshProUGUI Value)> _editRows = new();
        private readonly List<(Image Box, TextMeshProUGUI Key)> _laneViews = new();
        private readonly List<TextMeshProUGUI> _otherKeys = new();
        private GameObject _banner;
        private TextMeshProUGUI _bannerText;
        private Image _spinner;
        private CancellationTokenSource _capture;
        private bool _schemePushed;

        private enum EditRow { Name, Controls, NoteSpeed, HighwayLength }

        // The Controls row's picks, left to right: the five lanes, then these (bound the same way)
        private const int LANES = 5;
        private static readonly (string Bind, string Label, bool Menu)[] OtherBinds =
        {
            ("ProKeys.StarPower", "Overdrive", false), ("Menu.Start", "Pause", true),
            ("Menu.Up", "Menu up", true), ("Menu.Down", "Menu down", true),
        };
        private static readonly (EditRow? Row, string Label)[] EditLayout =
        {
            (null, "PLAYER"), (EditRow.Name, "Name"), (EditRow.Controls, "Controls"),
            (null, "HIGHWAY"), (EditRow.NoteSpeed, "Note speed"), (EditRow.HighwayLength, "Highway length"),

        };

        public static PlayersMoonlight Create(ProfileListMenu menu, Transform yargList)
        {
            // The YARG look goes; its objects stay (hidden) for their logic
            foreach (Transform child in menu.transform)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            var root = new GameObject("Moonlight Players", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(menu.transform, false);
            Stretch(root);
            var view = root.gameObject.AddComponent<PlayersMoonlight>();
            view._menu = menu;
            view._yargList = yargList;
            view._root = root;
            view.BuildHeader();
            view._listPage = Page("Players Page", root);
            view.BuildListPage();
            view._editPage = Page("Edit Page", root);
            view.BuildEditPage();
            view._editPage.gameObject.SetActive(false);
            return view;
        }

        // ---------- header ----------

        private void BuildHeader()
        {
            var back = Moonlight.Panel("Back", _root, 33, Moonlight.PanelFill);
            back.raycastTarget = true;
            Pin(back.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE, -HEADER_Y), new Vector2(66f, 66f));
            back.gameObject.AddComponent<Button>().onClick.AddListener(Back);
            var chevron = Graphic<Image>("Chevron", back.transform);
            chevron.sprite = Moonlight.Chevron;
            chevron.color = Moonlight.Text;
            chevron.rectTransform.sizeDelta = new Vector2(27f, 27f);

            var row = new GameObject("Title Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            row.transform.SetParent(_root, false);
            Pin((RectTransform) row.transform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE + 90f, -HEADER_Y), new Vector2(0f, 66f));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _title = Text(row.transform, Moonlight.Heading, 36f, Moonlight.Text, FontWeight.Medium);
            _title.overflowMode = TextOverflowModes.Overflow;
            _title.richText = false;
            _subtitle = Text(row.transform, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.Regular);
            _subtitle.overflowMode = TextOverflowModes.Overflow;
            _subtitle.richText = false;

            var add = Pill(_root, "Add a player", true, () => _menu.AddProfile());
            Pin(add, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-EDGE, -HEADER_Y), new Vector2(0f, 66f));
            var label = add.GetComponentInChildren<TextMeshProUGUI>();
            add.sizeDelta = new Vector2(label.GetPreferredValues("Add a player").x + 60f, 66f);
            _addButton = add.gameObject;
        }

        // ---------- players page ----------

        private void BuildListPage()
        {
            var panel = PanelRect(_listPage, EDGE, LIST_RIGHT);
            _rows = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            _rows.SetParent(panel, false);
            Stretch(_rows);
            var column = _rows.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(15, 15, 12, 12);
            column.spacing = 3f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            var aside = PanelRect(_listPage, LIST_RIGHT + 30f, 1920f - EDGE);
            var stack = aside.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(33, 33, 33, 33);
            stack.spacing = 21f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var who = Box("Who", aside, 84f);
            (_asideAvatar, _asideInitial, _asideAvatarPicture) = Avatar(who, 84f, 30f);
            _asideAvatar.rectTransform.anchorMin = _asideAvatar.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _asideAvatar.rectTransform.pivot = new Vector2(0f, 0.5f);
            _asideName = Text(who, Moonlight.Heading, 30f, Moonlight.Text, FontWeight.Medium);
            _asideName.richText = false;
            Line(_asideName.rectTransform, 84f + 21f, 14f, 40f);
            _asideSub = Text(who, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.Regular);
            Line(_asideSub.rectTransform, 84f + 21f, 50f, 28f);

            string[] keys = { "Note speed", "Highway length" };
            var details = new GameObject("Details", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<VerticalLayoutGroup>();
            details.transform.SetParent(aside, false);
            details.spacing = 3f;
            details.childControlWidth = details.childControlHeight = true;
            details.childForceExpandHeight = false;
            for (int i = 0; i < keys.Length; i++)
            {
                var line = Box(keys[i], details.transform, 60f);
                RuleUnder(line);
                var k = Text(line, Moonlight.Body, 22.5f, Moonlight.Soft, FontWeight.Regular);
                k.text = keys[i];
                Stretch(k.rectTransform);
                _detailValues[i] = Text(line, Moonlight.Body, 22.5f, Moonlight.Text, FontWeight.SemiBold);
                _detailValues[i].alignment = TextAlignmentOptions.MidlineRight;
                Stretch(_detailValues[i].rectTransform);
            }

            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(aside, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

            var buttons = Box("Buttons", aside, 66f);
            var pair = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            pair.spacing = 15f;
            pair.childControlWidth = pair.childControlHeight = true;
            pair.childForceExpandWidth = pair.childForceExpandHeight = true;
            _primaryLabel = Pill(buttons, "Edit player", true, Primary).GetComponentInChildren<TextMeshProUGUI>();
            _secondaryLabel = Pill(buttons, "Disconnect", false, Secondary).GetComponentInChildren<TextMeshProUGUI>();
        }

        /// <summary>Rebuilds the rows after the YARG list does (connect, reorder, add...), keeping a selection.</summary>
        public void Rebuild(YargProfile select = null)
        {
            if (_rows == null)
            {
                return;
            }
            var keep = select ?? (_selected < _rowViews.Count ? _rowViews[_selected].Profile : null);
            for (int i = _rows.childCount - 1; i >= 0; i--)
            {
                Destroy(_rows.GetChild(i).gameObject);
            }
            _rowViews.Clear();

            var active = PlayerContainer.Players.Select(p => p.Profile).ToList();
            var idle = PlayerContainer.Profiles.Except(active).Where(p => !p.IsBot).OrderBy(p => p.Name).ToList();
            if (active.Count > 0)
            {
                Heading(_rows, "PLAYING NOW", 18f);
                foreach (var profile in active) PlayerRow(profile, true);
            }
            if (idle.Count > 0)
            {
                Heading(_rows, "NOT CONNECTED", 27f);
                foreach (var profile in idle) PlayerRow(profile, false);
            }
            if (_rowViews.Count == 0)
            {
                var empty = Text(_rows, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
                empty.text = "No players yet. Add one to start.";
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            }

            int index = _rowViews.FindIndex(r => r.Profile == keep);
            Select(index >= 0 ? index : 0);
            if (_editing != null && !PlayerContainer.Profiles.Contains(_editing))
            {
                CloseEdit();
            }
        }

        private void PlayerRow(YargProfile profile, bool connected)
        {
            var row = new GameObject(profile.Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(_rows, false);
            row.GetComponent<LayoutElement>().minHeight = row.GetComponent<LayoutElement>().preferredHeight = 90f;
            var wash = row.GetComponent<Image>();
            wash.sprite = Moonlight.RoundedSprite(15);
            wash.type = Image.Type.Sliced;
            int index = _rowViews.Count;
            row.GetComponent<Button>().transition = Selectable.Transition.None;
            row.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_selected == index) Primary();
                else Select(index);
            });
            Moonlight.OnHover(row, () => { if (_selected != index) Select(index); });

            float left = connected ? 21f : 60f;
            var crescent = Graphic<Image>("Crescent", row.transform);
            crescent.sprite = Moonlight.Crescent;
            crescent.color = Moonlight.Moon;
            Pin(crescent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(21f, 0f), new Vector2(21f, 21f));

            var player = connected ? PlayerContainer.GetPlayerFromProfile(profile) : null;
            int number = player != null ? PlayerContainer.GetPlayerIndex(player) + 1 : 0;
            var (disc, _, _) = Avatar(row.transform, 57f, 21f, profile, connected, number == 1);
            Pin(disc.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(connected ? 60f : left, 0f), new Vector2(57f, 57f));
            float textLeft = (connected ? 60f : left) + 57f + 18f;

            var name = Text(row.transform, Moonlight.Body, 25.5f, connected ? Moonlight.Text : Moonlight.Muted,
                connected ? FontWeight.Bold : FontWeight.SemiBold);
            name.text = profile.Name;
            name.richText = false;
            Line(name.rectTransform, textLeft, 15f, 33f, -330f);
            var sub = Text(row.transform, Moonlight.Body, 19.5f, connected ? Moonlight.Muted : Moonlight.Faint, FontWeight.Regular);
            sub.text = connected
                ? number == 1 && Leaderboards() ? $"Player {number} · your signed-in account" : $"Player {number} · local profile"
                : "Local profile";
            Line(sub.rectTransform, textLeft, 48f, 27f, -330f);

            if (connected)
            {
                var chip = Moonlight.Panel("Device", row.transform, 22, Moonlight.Raised);
                var device = Text(chip.transform, Moonlight.Body, 19.5f, Moonlight.Soft, FontWeight.Regular);
                device.text = DeviceName(player);
                device.alignment = TextAlignmentOptions.Center;
                Stretch(device.rectTransform);
                float width = device.GetPreferredValues(device.text).x + 36f;
                Pin(chip.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-21f, 0f), new Vector2(width, 45f));
            }
            else
            {
                var hint = Text(row.transform, Moonlight.Body, 19.5f, Moonlight.Muted, FontWeight.Regular);
                hint.text = "Choose to connect";
                hint.alignment = TextAlignmentOptions.MidlineRight;
                Stretch(hint.rectTransform);
                hint.rectTransform.offsetMax = new Vector2(-21f, 0f);
            }
            _rowViews.Add((profile, wash, crescent, name));
        }

        private static bool Leaderboards() => YARG.Core.Song.Leaderboards.SignedIn;

        private void Select(int index)
        {
            if (_rowViews.Count == 0)
            {
                _selected = 0;
                UpdateAside(null);
                return;
            }
            _selected = Mathf.Clamp(index, 0, _rowViews.Count - 1);
            for (int i = 0; i < _rowViews.Count; i++)
            {
                bool on = i == _selected;
                var (profile, wash, crescent, name) = _rowViews[i];
                wash.color = on ? Moonlight.MoonWash : Color.clear;
                crescent.gameObject.SetActive(on);
                bool connected = PlayerContainer.IsProfileTaken(profile);
                name.color = on ? Moonlight.Moon : connected ? Moonlight.Text : Moonlight.Muted;
            }
            UpdateAside(_rowViews[_selected].Profile);
            PushScheme();
        }

        private YargProfile Current => _rowViews.Count > 0 ? _rowViews[_selected].Profile : null;

        private void UpdateAside(YargProfile profile)
        {
            _asideAvatar.transform.parent.parent.gameObject.SetActive(profile != null);
            if (profile == null)
            {
                return;
            }
            var player = PlayerContainer.GetPlayerFromProfile(profile);
            int number = player != null ? PlayerContainer.GetPlayerIndex(player) + 1 : 0;
            FillAvatar(_asideAvatar, _asideInitial, _asideAvatarPicture, profile, player != null, number == 1);
            _asideName.text = profile.Name;
            _asideSub.text = player != null ? $"{DeviceName(player)} · player {number}" : "Not connected";
            _detailValues[0].text = profile.NoteSpeed.ToString("0.0");
            _detailValues[1].text = profile.HighwayLength.ToString("0.0");
            _primaryLabel.text = player != null ? "Edit player" : "Connect";
            _secondaryLabel.text = player != null ? "Disconnect" : "Delete";
        }

        private void Primary()
        {
            var profile = Current;
            if (profile == null) return;
            if (PlayerContainer.IsProfileTaken(profile)) OpenEdit(profile);
            else if (_menu.CanConnectProfile && ViewFor(profile) is { } view) view.Connect(true).Forget();
        }

        private void Secondary()
        {
            var profile = Current;
            if (profile == null) return;
            if (PlayerContainer.IsProfileTaken(profile)) ViewFor(profile)?.Disconnect();
            else ViewFor(profile)?.RemoveProfile();
        }

        private void Move(int direction)
        {
            var profile = Current;
            if (profile == null || !PlayerContainer.IsProfileTaken(profile)) return;
            if (direction < 0) _menu.MoveProfileUp(profile);
            else _menu.MoveProfileDown(profile);
        }

        private ProfileView ViewFor(YargProfile profile) =>
            _yargList.GetComponentsInChildren<ProfileView>(true).LastOrDefault(v => v.Profile == profile);

        // ---------- edit page ----------

        private void BuildEditPage()
        {
            var panel = PanelRect(_editPage, EDGE, EDIT_RIGHT);
            var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(15, 15, 12, 12);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            foreach (var (row, label) in EditLayout)
            {
                if (row == null)
                {
                    Heading(panel, label, 0f, 60f);
                    continue;
                }
                var line = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                line.transform.SetParent(panel, false);
                line.GetComponent<LayoutElement>().minHeight = line.GetComponent<LayoutElement>().preferredHeight = 66f;
                var wash = line.GetComponent<Image>();
                wash.sprite = Moonlight.RoundedSprite(15);
                wash.type = Image.Type.Sliced;
                int index = _editRows.Count;
                line.GetComponent<Button>().transition = Selectable.Transition.None;
                line.GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (_editSelected == index) ActivateEdit();
                    else SelectEdit(index);
                });
                Moonlight.OnHover(line, () => { if (_editSelected != index) SelectEdit(index); });
                var crescent = Graphic<Image>("Crescent", line.transform);
                crescent.sprite = Moonlight.Crescent;
                crescent.color = Moonlight.Moon;
                Pin(crescent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(21f, 0f), new Vector2(21f, 21f));
                var name = Text(line.transform, Moonlight.Body, 24f, Moonlight.Text, FontWeight.SemiBold);
                name.text = label;
                Stretch(name.rectTransform);
                name.rectTransform.offsetMin = new Vector2(21f + 21f + 18f, 0f);
                var value = Text(line.transform, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
                value.alignment = TextAlignmentOptions.MidlineRight;
                value.richText = false;
                Stretch(value.rectTransform);
                value.rectTransform.offsetMax = new Vector2(-21f, 0f);
                _editRows.Add((wash, crescent, name, value));
            }

            // Controls: the five lanes and their keys, then the other binds
            var controls = PanelRect(_editPage, EDIT_RIGHT + 30f, 1920f - EDGE);
            var stack = controls.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(36, 36, 33, 33);
            stack.spacing = 24f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var head = Box("Heading", controls, 36f);
            var title = Text(head, Moonlight.Heading, 27f, Moonlight.Text, FontWeight.Medium);
            title.text = "Controls";
            Stretch(title.rectTransform);
            var tip = Text(head, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.Regular);
            tip.text = "Pick a lane or key, then press the key to use";
            tip.alignment = TextAlignmentOptions.MidlineRight;
            Stretch(tip.rectTransform);

            var lanes = Box("Lanes", controls, 180f);
            var grid = lanes.gameObject.AddComponent<HorizontalLayoutGroup>();
            grid.spacing = 15f;
            grid.childControlWidth = grid.childControlHeight = true;
            grid.childForceExpandWidth = grid.childForceExpandHeight = true;
            for (int i = 0; i < 5; i++)
            {
                var lane = Moonlight.Panel($"Lane {i + 1}", lanes, 21, Moonlight.Raised);
                lane.raycastTarget = true;
                int index = i;
                lane.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    SelectEdit(_editRows.Count > 1 ? 1 : 0);
                    _lane = index;
                    RefreshEdit();
                    Capture();
                });
                var bar = Moonlight.Panel("Colour", lane.transform, 4, LaneColors[i]);
                Pin(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(0f, 9f));
                bar.rectTransform.anchorMin = new Vector2(0f, 1f);
                bar.rectTransform.anchorMax = Vector2.one;
                bar.rectTransform.offsetMin = new Vector2(15f, -24f);
                bar.rectTransform.offsetMax = new Vector2(-15f, -15f);
                var key = Text(lane.transform, Moonlight.Heading, 36f, Moonlight.Text, FontWeight.Medium);
                key.alignment = TextAlignmentOptions.Center;
                key.enableAutoSizing = true; // "Num 7" and the like
                key.fontSizeMin = 16f;
                key.fontSizeMax = 36f;
                key.margin = new Vector4(9f, 0f, 9f, 0f);
                key.richText = false;
                Stretch(key.rectTransform);
                var name = Text(lane.transform, Moonlight.Body, 19.5f, Moonlight.Muted, FontWeight.Regular);
                name.text = $"Lane {i + 1}";
                name.alignment = TextAlignmentOptions.Bottom;
                Stretch(name.rectTransform);
                name.rectTransform.offsetMin = new Vector2(0f, 18f);
                _laneViews.Add((lane, key));
            }

            var banner = Moonlight.Panel("Listening", controls, 21, Moonlight.MoonWash);
            banner.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
            _banner = banner.gameObject;
            // The board's spinner: a dark track ring with a cream arc turning on it (about its centre)
            var track = Graphic<Image>("Track", banner.transform);
            track.sprite = Moonlight.Ring;
            track.color = Moonlight.Keycap;
            Pin(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24f + 16.5f, 0f), new Vector2(33f, 33f));
            _spinner = Graphic<Image>("Spinner", banner.transform);
            _spinner.sprite = Moonlight.Ring;
            _spinner.type = Image.Type.Filled;
            _spinner.fillMethod = Image.FillMethod.Radial360;
            _spinner.fillAmount = 0.4f;
            _spinner.color = Moonlight.Moon;
            Pin(_spinner.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24f + 16.5f, 0f), new Vector2(33f, 33f));
            _bannerText = Text(banner.transform, Moonlight.Body, 22.5f, Moonlight.Text, FontWeight.Regular);
            Stretch(_bannerText.rectTransform);
            _bannerText.rectTransform.offsetMin = new Vector2(24f + 33f + 18f, 0f);
            _banner.SetActive(false);

            var others = OtherBinds.Select(o => o.Label).ToArray();
            var list = new GameObject("Other Binds", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<VerticalLayoutGroup>();
            list.transform.SetParent(controls, false);
            list.spacing = 3f;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandHeight = false;
            for (int o = 0; o < others.Length; o++)
            {
                string other = others[o];
                var line = Box(other, list.transform, 63f);
                RuleUnder(line);
                // Clicked like a lane: picks it and waits for the key
                var hit = line.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                int pick = LANES + o;
                line.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    SelectEdit(_editRows.Count > 1 ? 1 : 0);
                    _lane = pick;
                    RefreshEdit();
                    Capture();
                });
                var k = Text(line, Moonlight.Body, 22.5f, Moonlight.Text, FontWeight.Regular);
                k.text = other;
                Stretch(k.rectTransform);
                var chip = Moonlight.Panel("Key", line, 21, Moonlight.Keycap);
                var v = Text(chip.transform, Moonlight.Body, 19.5f, Moonlight.Text, FontWeight.Bold);
                v.alignment = TextAlignmentOptions.Center;
                v.richText = false;
                Stretch(v.rectTransform);
                Pin(chip.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(42f, 42f));
                _otherKeys.Add(v);
            }
        }

        private void OpenEdit(YargProfile profile)
        {
            _editing = profile;
            _listPage.gameObject.SetActive(false);
            _editPage.gameObject.SetActive(true);
            _addButton.SetActive(false);
            SelectEdit(1); // Controls, as on the board
        }

        private void CloseEdit()
        {
            CancelCapture();
            var profile = _editing;
            _editing = null;
            _editPage.gameObject.SetActive(false);
            _listPage.gameObject.SetActive(true);
            _addButton.SetActive(true);
            PlayerContainer.SaveProfiles();
            Rebuild(profile);
            SetTitle();
        }

        private void SelectEdit(int index)
        {
            _editSelected = Mathf.Clamp(index, 0, _editRows.Count - 1);
            RefreshEdit();
            PushScheme();
        }

        private EditRow RowAt(int index) => EditLayout.Where(e => e.Row != null).Select(e => e.Row.Value).ElementAt(index);

        private void RefreshEdit()
        {
            if (_editing == null)
            {
                return;
            }
            var player = PlayerContainer.GetPlayerFromProfile(_editing);
            SetTitle();
            for (int i = 0; i < _editRows.Count; i++)
            {
                bool on = i == _editSelected;
                var (wash, crescent, label, value) = _editRows[i];
                wash.color = on ? Moonlight.MoonWash : Color.clear;
                crescent.gameObject.SetActive(on);
                label.color = on ? Moonlight.Moon : Moonlight.Text;
                label.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
                value.text = RowAt(i) switch
                {
                    EditRow.Name          => _editing.Name,
                    EditRow.Controls      => player != null ? DeviceName(player) : "Not connected",
                    EditRow.NoteSpeed     => _editing.NoteSpeed.ToString("0.0"),
                    EditRow.HighwayLength => _editing.HighwayLength.ToString("0.0"),
                    _                     => "",
                };
            }

            bool lanesActive = RowAt(_editSelected) == EditRow.Controls;
            var binds = player?.Bindings[_editing.GameMode];
            for (int i = 0; i < _laneViews.Count; i++)
            {
                var (box, key) = _laneViews[i];
                bool on = lanesActive && i == _lane;
                box.sprite = on ? Moonlight.OutlineSprite(21) : Moonlight.RoundedSprite(21);
                box.color = on ? Moonlight.Moon : Moonlight.Raised;
                key.text = _capture != null && on ? "…" : KeyName(binds, $"ProKeys.{new[] { "Green", "Red", "Yellow", "Blue", "Orange" }[i]}Key");
            }
            var menuBinds = player?.Bindings.MenuBindings;
            for (int i = 0; i < _otherKeys.Count; i++)
            {
                var chip = _otherKeys[i];
                bool on = lanesActive && _lane == LANES + i;
                chip.text = _capture != null && on ? "…" : KeyName(OtherBinds[i].Menu ? menuBinds : binds, OtherBinds[i].Bind);
                var box = chip.transform.parent.GetComponent<Image>();
                box.sprite = on ? Moonlight.OutlineSprite(21) : Moonlight.RoundedSprite(21);
                box.color = on ? Moonlight.Moon : Moonlight.Keycap;
                var rect = (RectTransform) chip.transform.parent;
                rect.sizeDelta = new Vector2(Mathf.Max(42f, chip.GetPreferredValues(chip.text).x + 30f), 42f);
            }
        }

        private void ChangeEdit(int direction)
        {
            if (_editing == null) return;
            switch (RowAt(_editSelected))
            {
                case EditRow.Controls:
                    _lane = (_lane + direction + LANES + OtherBinds.Length) % (LANES + OtherBinds.Length);
                    break;
                case EditRow.NoteSpeed:
                    _editing.NoteSpeed = Mathf.Clamp(Mathf.Round((_editing.NoteSpeed + direction * 0.5f) * 2f) / 2f, 0f, 100f);
                    break;
                case EditRow.HighwayLength:
                    _editing.HighwayLength = Mathf.Clamp(Mathf.Round((_editing.HighwayLength + direction * 0.1f) * 10f) / 10f, 0.1f, 10f);
                    break;
            }
            RefreshEdit();
        }

        private void ActivateEdit()
        {
            if (_editing == null) return;
            switch (RowAt(_editSelected))
            {
                case EditRow.Name:
                    DialogManager.Instance.ShowRenameDialog("Rename player", name =>
                    {
                        if (string.IsNullOrWhiteSpace(name)) return;
                        _editing.Name = name.Trim();
                        ScoreContainer.RecordPlayerInfo(_editing.Id, _editing.Name);
                        RefreshEdit();
                    });
                    break;
                case EditRow.Controls:
                    var player = PlayerContainer.GetPlayerFromProfile(_editing);
                    if (player != null && !player.Bindings.Empty) Capture();
                    else if (ViewFor(_editing) is { } view) view.PromptAddDevice().ContinueWith(_ => RefreshEdit()).Forget();
                    break;
                default:
                    ChangeEdit(1);
                    break;
            }
        }

        /// <summary>Waits for the next key or button and binds it to the pick: a lane, Overdrive, Pause or a menu key
        /// (Escape cancels).</summary>
        private void Capture()
        {
            var player = _editing != null ? PlayerContainer.GetPlayerFromProfile(_editing) : null;
            if (player == null || _capture != null)
            {
                return;
            }
            bool lane = _lane < LANES;
            var other = lane ? default : OtherBinds[_lane - LANES];
            var group = !lane && other.Menu ? player.Bindings.MenuBindings : player.Bindings[_editing.GameMode];
            string name = lane ? $"ProKeys.{new[] { "Green", "Red", "Yellow", "Blue", "Orange" }[_lane]}Key" : other.Bind;
            var binding = group.FirstOrDefault(b => b.Key == name);
            if (binding == null)
            {
                return;
            }
            CaptureAsync(player, binding, group, lane ? $"lane {_lane + 1}" : other.Label).Forget();
        }

        // group: the binds a key is taken from when it's given to this one (game keys among game keys, menu keys among
        // menu keys: a pad button can both play a lane and move through menus)
        private async UniTaskVoid CaptureAsync(YargPlayer player, ControlBinding binding, IEnumerable<ControlBinding> group, string label)
        {
            _capture = new CancellationTokenSource();
            Navigator.Instance.PushScheme(new NavigationScheme(new(), null)); // keys go to the bind, not the menu
            _banner.SetActive(true);
            _bannerText.text = $"Press a key for <b><color=#F3E8CC>{label}</color></b>… <color=#9AA1C7>(Esc cancels)</color>";
            RefreshEdit();
            List<InputControl> controls;
            try
            {
                controls = await InputControlBindingHelper.Instance.GetControl(player, _capture.Token, binding);
            }
            finally
            {
                Navigator.Instance.PopScheme();
            }
            if (this == null) return;
            bool cancelled = _capture == null || _capture.IsCancellationRequested;
            _capture?.Dispose();
            _capture = null;
            _banner.SetActive(false);
            var control = controls.FirstOrDefault();
            if (!cancelled && control != null && control != Keyboard.current?.escapeKey)
            {
                // It gets this key in place of its old ones on the same device
                foreach (var other in group)
                {
                    other.RemoveControl(control); // a key can't do two things in the same place
                }
                binding.ClearBindingsForDevice(control.device);
                binding.AddControl(new ActuationSettings(), control);
            }
            RefreshEdit();
        }

        private void CancelCapture()
        {
            _capture?.Cancel();
        }

        private void Update()
        {
            if (_capture != null)
            {
                _spinner.rectTransform.Rotate(0f, 0f, -360f * Time.unscaledDeltaTime);
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    CancelCapture();
                }
            }
        }

        // ---------- navigation ----------

        public void PushScheme()
        {
            if (!isActiveAndEnabled) return;
            PopScheme();
            var entries = new List<NavigationScheme.Entry>
            {
                new(MenuAction.Up, "Menu.Common.Up", () => { if (_editing != null) SelectEdit(_editSelected - 1); else Select(_selected - 1); }),
                new(MenuAction.Down, "Menu.Common.Down", () => { if (_editing != null) SelectEdit(_editSelected + 1); else Select(_selected + 1); }),
            };
            if (_editing != null)
            {
                entries.Add(new(MenuAction.Green, "Menu.Players.Change", ActivateEdit));
                entries.Add(new(MenuAction.Red, "Menu.Players.Done", CloseEdit));
                entries.Add(new(MenuAction.Left, "Menu.Players.Adjust", () => ChangeEdit(-1), hide: true));
                entries.Add(new(MenuAction.Right, "Menu.Players.Adjust", () => ChangeEdit(1), hide: true));
            }
            else
            {
                bool connected = Current != null && PlayerContainer.IsProfileTaken(Current);
                entries.Add(new(MenuAction.Green, connected ? "Menu.Players.Edit" : "Menu.Players.Connect", Primary));
                entries.Add(new(MenuAction.Red, "Menu.Common.Back", Back));
                entries.Add(new(MenuAction.Blue, "Menu.Players.Add", () => _menu.AddProfile()));
                entries.Add(new(MenuAction.Yellow, connected ? "Menu.Players.Disconnect" : "Menu.Players.Delete", Secondary,
                    hide: Current == null));
                entries.Add(new(MenuAction.Left, "Menu.Players.Move", () => Move(-1), hide: !connected));
                entries.Add(new(MenuAction.Right, "Menu.Players.Move", () => Move(1), hide: !connected));
            }
            Navigator.Instance.PushScheme(new NavigationScheme(entries, true)); // the jukebox keeps playing
            _schemePushed = true;
        }

        public void PopScheme()
        {
            if (_schemePushed)
            {
                Navigator.Instance?.PopScheme();
                _schemePushed = false;
            }
        }

        private void Back()
        {
            if (_editing != null) CloseEdit();
            else MenuManager.Instance.PopMenu();
        }

        public void OnShown()
        {
            CloseEditQuietly();
            SetTitle();
            Rebuild();
            PushScheme();
        }

        private void CloseEditQuietly()
        {
            CancelCapture();
            _editing = null;
            _editPage.gameObject.SetActive(false);
            _listPage.gameObject.SetActive(true);
            _addButton.SetActive(true);
        }

        private void SetTitle()
        {
            if (_editing != null)
            {
                var player = PlayerContainer.GetPlayerFromProfile(_editing);
                _title.text = _editing.Name;
                _subtitle.text = player != null
                    ? $"{DeviceName(player)} · player {PlayerContainer.GetPlayerIndex(player) + 1}"
                    : "Not connected";
            }
            else
            {
                _title.text = "Players";
                _subtitle.text = "Who's playing on this computer, and with which controller";
            }
        }

        // ---------- helpers ----------

        private static string DeviceName(YargPlayer player)
        {
            var device = player?.Bindings.InputDevices.FirstOrDefault();
            if (device != null) return device is Keyboard ? "Keyboard" : device is Gamepad ? "Gamepad" : device.displayName;
            return player?.Bindings.Microphone != null ? "Microphone" : "No controller";
        }

        private static string KeyName(BindingCollection binds, string key)
        {
            if (binds?.FirstOrDefault(b => b.Key == key) is ButtonBinding button && button.Bindings.Count > 0)
            {
                var control = button.Bindings[0].Control;
                string name = string.IsNullOrEmpty(control.shortDisplayName) ? control.displayName : control.shortDisplayName;
                return name switch
                {
                    "Up Arrow" or "Up" => "↑", "Down Arrow" or "Down" => "↓",
                    "Left Arrow" or "Left" => "←", "Right Arrow" or "Right" => "→",
                    "Escape" => "Esc",
                    _ => name,
                };
            }
            return "–";
        }

        private (Image Disc, TextMeshProUGUI Initial, RawImage Picture) Avatar(Transform parent, float size, float font,
            YargProfile profile = null, bool connected = true, bool account = false)
        {
            var disc = Moonlight.Panel("Avatar", parent, Mathf.RoundToInt(size / 2f), Moonlight.Keycap);
            disc.rectTransform.sizeDelta = new Vector2(size, size);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var initial = Text(disc.transform, Moonlight.Heading, font, Moonlight.Text, FontWeight.Regular);
            initial.alignment = TextAlignmentOptions.Center;
            Stretch(initial.rectTransform);
            var picture = Graphic<RawImage>("Picture", disc.transform);
            Stretch(picture.rectTransform);
            picture.enabled = false;
            if (profile != null) FillAvatar(disc, initial, picture, profile, connected, account);
            return (disc, initial, picture);
        }

        /// <summary>Player 1 is the signed-in account: their Discord picture. Others get a coloured initial.</summary>
        private static void FillAvatar(Image disc, TextMeshProUGUI initial, RawImage picture, YargProfile profile,
            bool connected, bool account)
        {
            string name = string.IsNullOrEmpty(profile.Name) ? "?" : profile.Name;
            initial.text = name[..1].ToUpperInvariant();
            initial.color = connected ? Moonlight.Text : Moonlight.Muted;
            disc.color = connected ? AvatarColors[Math.Abs(name.GetHashCode()) % AvatarColors.Length] : Moonlight.Keycap;
            picture.enabled = false;
            string avatar = account && Leaderboards() ? ProfileCard.Avatar : null;
            if (avatar != null)
            {
                LeaderboardCard.LoadAvatar(avatar, picture).Forget();
            }
        }

        private TextMeshProUGUI Heading(Transform parent, string text, float top, float height = 0f)
        {
            var label = Text(parent, Moonlight.Heading, 18f, Moonlight.Muted, FontWeight.Medium);
            label.characterSpacing = 12f;
            label.text = text;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.margin = new Vector4(21f, 0f, 21f, 9f);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = height > 0f ? height : top + 18f + 9f + 6f;
            return label;
        }

        private RectTransform Pill(Transform parent, string text, bool primary, Action click)
        {
            var pill = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
            pill.transform.SetParent(parent, false);
            pill.sprite = primary ? Moonlight.RoundedSprite(33) : Moonlight.OutlineSprite(33);
            pill.type = Image.Type.Sliced;
            pill.color = primary ? Moonlight.Moon : new Color(0.627f, 0.686f, 1f, 0.35f);
            pill.GetComponent<Button>().onClick.AddListener(() => click());
            Moonlight.HoverWash(pill, 33);
            var label = Text(pill.transform, Moonlight.Body, 24f, primary ? Moonlight.Night : Moonlight.Text,
                primary ? FontWeight.Bold : FontWeight.SemiBold);
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return pill.rectTransform;
        }

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

        private static RectTransform Page(string name, Transform parent)
        {
            var page = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            page.SetParent(parent, false);
            Stretch(page);
            return page;
        }

        private static RectTransform Box(string name, Transform parent, float height)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            box.transform.SetParent(parent, false);
            var size = box.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            size.flexibleHeight = 0f; // or a layout group on it lends its children's flexibility
            return (RectTransform) box.transform;
        }

        private static void RuleUnder(Transform line)
        {
            var rule = Graphic<Image>("Rule", line);
            rule.color = Rule;
            rule.rectTransform.anchorMin = Vector2.zero;
            rule.rectTransform.anchorMax = new Vector2(1f, 0f);
            rule.rectTransform.pivot = new Vector2(0.5f, 0f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 1.5f);
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

        private static void Line(RectTransform rect, float left, float top, float height, float right = -21f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(right, -top);
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
