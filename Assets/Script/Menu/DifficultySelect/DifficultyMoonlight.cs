using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Player;

namespace YARG.Menu.DifficultySelect
{
    /// <summary>
    /// The instrument and difficulty screen as the Moonlight board draws it (MDifficulty): the song in the header
    /// with the song speed, then one card per player. The player choosing gets instrument tiles (with each part's
    /// difficulty in moon dots), a difficulty bar, modifiers and Ready; the others show what they picked. The YARG
    /// menu underneath keeps its rules and its one-player-at-a-time flow. Sizes are the board's (1280×720) × 1.5.
    /// </summary>
    public class DifficultyMoonlight : MonoBehaviour
    {
        private const float EDGE = 42f, BOTTOM = 102f, TOP = 156f, HEADER_Y = 81f, GAP = 24f;
        private const int DOTS = 7, TOP_TIER = 6;
        private static readonly Color TopDot = new(1f, 0.302f, 0.369f);
        private static readonly Color EmptyDot = new(0.227f, 0.267f, 0.459f);
        private static readonly Color Track = new(0.051f, 0.075f, 0.188f); // #0d1330
        private static readonly Color Ink = new(0.043f, 0.063f, 0.149f);   // #0b1026
        private static readonly Color[] AvatarColors =
        {
            new(0.227f, 0.145f, 0.439f), new(0.122f, 0.29f, 0.416f), new(0.353f, 0.114f, 0.247f), new(0.114f, 0.29f, 0.227f),
        };
        private static readonly Dictionary<string, Sprite> Icons = new();

        private enum Stop { Speed, Instrument, Difficulty, Modifiers, Buttons }

        private DifficultySelectMenu _menu;
        private RectTransform _root, _cards;
        private TextMeshProUGUI _speed;
        private Image _speedPill;
        private Stop _stop = Stop.Instrument;
        private bool _sitOutFocused;
        private bool _modifiers;     // the card shows the modifier list
        private int _modifierIndex;  // the list's selection; the count means Done
        private bool _schemePushed;

        public static DifficultyMoonlight Create(DifficultySelectMenu menu)
        {
            foreach (Transform child in menu.transform)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            var canvas = (RectTransform) menu.GetComponentInParent<Canvas>().rootCanvas.transform;
            var root = new GameObject("Moonlight Difficulty", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            root.SetParent(menu.transform, true);
            var view = root.gameObject.AddComponent<DifficultyMoonlight>();
            view._menu = menu;
            view._root = root;
            view._cards = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            view._cards.SetParent(root, false);
            Stretch(view._cards);
            view._cards.offsetMin = new Vector2(EDGE, BOTTOM);
            view._cards.offsetMax = new Vector2(-EDGE, -TOP);
            var row = view._cards.GetComponent<HorizontalLayoutGroup>();
            row.spacing = GAP;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            return view;
        }

        private YARG.Core.Song.SongEntry _song;
        private RectTransform _header;

        /// <summary>The header shows the song being set up: rebuilt when it changes.</summary>
        private void BuildHeader()
        {
            var song = GlobalVariables.State.CurrentSong;
            if (_header != null && song == _song) return;
            if (_header != null) Destroy(_header.gameObject);
            _song = song;
            _header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            _header.SetParent(_root, false);
            Stretch(_header);
            var root = _header;
            var back = Moonlight.Panel("Back", root, 33, Moonlight.PanelFill);
            back.raycastTarget = true;
            Pin(back.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE, -HEADER_Y), new Vector2(66f, 66f));
            back.gameObject.AddComponent<Button>().onClick.AddListener(Back);
            var chevron = Graphic<Image>("Chevron", back.transform);
            chevron.sprite = Moonlight.Chevron;
            chevron.color = Moonlight.Text;
            chevron.rectTransform.sizeDelta = new Vector2(27f, 27f);

            var cover = Moonlight.Panel("Cover", root, 15, Moonlight.Raised);
            cover.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Pin(cover.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE + 66f + 24f, -HEADER_Y), new Vector2(84f, 84f));
            var art = Graphic<RawImage>("Art", cover.transform);
            Stretch(art.rectTransform);
            art.enabled = false;
            LoadCover(song, art);

            float left = EDGE + 66f + 24f + 84f + 24f;
            var title = Text(root, Moonlight.Heading, 33f, Moonlight.Text, FontWeight.Medium);
            title.text = song != null ? (string) song.Name : "";
            title.richText = false;
            Pin(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(left, -HEADER_Y + 16f), new Vector2(1000f, 42f));
            var sub = Text(root, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
            var time = TimeSpan.FromMilliseconds(song?.SongLengthMilliseconds ?? 0);
            sub.text = $"{(song != null ? (string) song.Artist : "")} · {(time.Hours > 0 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"))}";
            sub.richText = false;
            Pin(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(left, -HEADER_Y - 20f), new Vector2(1000f, 30f));

            _speedPill = Moonlight.Panel("Song Speed", root, 30, Moonlight.PanelFill);
            _speedPill.raycastTarget = true;
            _speedPill.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                _stop = Stop.Speed;
                Show();
            });
            Moonlight.HoverWash(_speedPill, 30);
            Pin(_speedPill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-EDGE, -HEADER_Y), new Vector2(0f, 60f));
            _speed = Text(_speedPill.transform, Moonlight.Body, 22.5f, Moonlight.Soft, FontWeight.Regular);
            _speed.alignment = TextAlignmentOptions.Center;
            Stretch(_speed.rectTransform);
        }

        private static async void LoadCover(YARG.Core.Song.SongEntry song, RawImage art)
        {
            if (song == null) return;
            using var image = await Cysharp.Threading.Tasks.UniTask.RunOnThreadPool(song.LoadAlbumData);
            if (image == null || art == null) return;
            art.texture = image.LoadTexture(false);
            art.uvRect = new Rect(0f, 0f, 1f, -1f); // covers load upside down, as in the library
            art.enabled = true;
        }

        // ---------- cards ----------

        /// <summary>Redraws the cards (the YARG menu calls this whenever the player or their choices change).</summary>
        public void Show()
        {
            if (_cards == null) return;
            BuildHeader();
            foreach (Transform child in _cards) Destroy(child.gameObject);
            var players = PlayerContainer.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (i == _menu.PlayerIndex) ActiveCard(players[i]);
                else OtherCard(players[i], i < _menu.PlayerIndex);
            }
            // The board's grid has two columns: one player still gets half the width
            if (players.Count == 1)
            {
                var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
                spacer.transform.SetParent(_cards, false);
                spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }
            _speed.text = $"Song speed <b><color=#EEF0FF>{_menu.SpeedPercent}%</color></b>";
            _speedPill.sprite = _stop == Stop.Speed ? Moonlight.OutlineSprite(30) : Moonlight.RoundedSprite(30);
            _speedPill.color = _stop == Stop.Speed ? Moonlight.Moon : Moonlight.PanelFill;
            var width = _speed.GetPreferredValues(_speed.text).x + 48f;
            _speedPill.rectTransform.sizeDelta = new Vector2(width, 60f);
            PushScheme();
        }

        private RectTransform Card(YargPlayer player, bool active, string status, Color statusColor)
        {
            var card = Moonlight.Panel(player.Profile.Name, _cards, 27, Moonlight.PanelFill);
            var column = card.gameObject.AddComponent<LayoutElement>();
            column.minWidth = column.preferredWidth = 0f; // equal columns, as the board's grid, whatever's inside
            column.flexibleWidth = 1f;
            var stack = card.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(33, 33, 30, 30);
            stack.spacing = 21f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            if (active)
            {
                // The board's cream edge on the card being chosen
                var edge = Graphic<Image>("Edge", card.transform);
                edge.sprite = Moonlight.OutlineSprite(27);
                edge.type = Image.Type.Sliced;
                edge.color = new Color(0.953f, 0.91f, 0.8f, 0.5f);
                Stretch(edge.rectTransform);
            }

            var head = Box("Player", card.transform, 54f);
            var disc = Moonlight.Panel("Avatar", head, 27, AvatarColors[Math.Abs(player.Profile.Name.GetHashCode()) % AvatarColors.Length]);
            Pin(disc.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
            var initial = Text(disc.transform, Moonlight.Heading, 21f, Moonlight.Text, FontWeight.Regular);
            initial.text = string.IsNullOrEmpty(player.Profile.Name) ? "?" : player.Profile.Name[..1].ToUpperInvariant();
            initial.alignment = TextAlignmentOptions.Center;
            Stretch(initial.rectTransform);
            var name = Text(head, Moonlight.Body, 27f, Moonlight.Text, FontWeight.Bold);
            name.text = player.Profile.Name;
            name.richText = false;
            name.overflowMode = TextOverflowModes.Overflow;
            float nameWidth = Mathf.Min(name.GetPreferredValues(name.text).x, 360f);
            Pin(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(54f + 18f, 0f), new Vector2(nameWidth, 54f));
            var device = Text(head, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.Regular);
            device.text = DeviceName(player);
            Pin(device.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(54f + 18f + nameWidth + 18f, 0f), new Vector2(240f, 54f));
            var state = Text(head, Moonlight.Body, 21f, statusColor, FontWeight.Regular);
            state.text = status;
            state.alignment = TextAlignmentOptions.MidlineRight;
            Stretch(state.rectTransform);
            return card.rectTransform;
        }

        private void ActiveCard(YargPlayer player)
        {
            string warning = player.IsMissingMicrophone ? "No microphone"
                : player.IsMissingInputDevice ? "No controller" : null;
            var card = Card(player, true, warning ?? "Choosing", warning != null ? Moonlight.Danger : Moonlight.Moon);
            if (_modifiers)
            {
                ModifierList(card, player.Profile);
                return;
            }
            if (_menu.PossibleInstruments.Count == 0)
            {
                var none = Text(card, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
                none.text = "This song has no part for this player's controller.";
                Spacer(card);
                Buttons(card, false);
                return;
            }

            Label(card, "INSTRUMENT", _stop == Stop.Instrument);
            var tiles = Box("Instruments", card, 156f);
            var grid = tiles.gameObject.AddComponent<HorizontalLayoutGroup>();
            grid.spacing = 12f;
            grid.childControlWidth = grid.childControlHeight = true;
            grid.childForceExpandWidth = grid.childForceExpandHeight = true;
            var song = GlobalVariables.State.CurrentSong;
            foreach (var instrument in _menu.PossibleInstruments)
            {
                bool on = player.Profile.CurrentInstrument == instrument;
                var tile = Moonlight.Panel(instrument.ToString(), tiles, 21, on ? Moonlight.MoonWash : Moonlight.Raised);
                tile.raycastTarget = true;
                var pick = instrument;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _stop = Stop.Instrument;
                    _menu.PickInstrument(pick);
                });
                Moonlight.HoverWash(tile, 21);
                if (on)
                {
                    var edge = Graphic<Image>("Edge", tile.transform);
                    edge.sprite = Moonlight.OutlineSprite(21);
                    edge.type = Image.Type.Sliced;
                    edge.color = Moonlight.Moon;
                    Stretch(edge.rectTransform);
                }
                var icon = Graphic<Image>("Icon", tile.transform);
                icon.preserveAspect = true;
                icon.color = on ? Moonlight.Moon : Moonlight.Text;
                Pin(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -27f), new Vector2(42f, 42f));
                SetIcon(icon, IconName(instrument));
                var label = Text(tile.transform, Moonlight.Body, 22.5f, on ? Moonlight.Moon : Moonlight.Text, FontWeight.Bold);
                label.text = instrument.ToLocalizedName();
                label.alignment = TextAlignmentOptions.Center;
                Pin(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(200f, 30f));
                Dots(tile.transform, song != null ? song[instrument].Intensity : -1, on);
            }

            Label(card, "DIFFICULTY", _stop == Stop.Difficulty);
            var bar = Moonlight.Panel("Difficulties", card, 33, Track);
            var barSize = bar.gameObject.AddComponent<LayoutElement>();
            barSize.minHeight = barSize.preferredHeight = 69f;
            barSize.flexibleHeight = 0f; // or its segments lend it their flexibility
            var segments = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            segments.padding = new RectOffset(6, 6, 6, 6);
            segments.spacing = 6f;
            segments.childControlWidth = segments.childControlHeight = true;
            segments.childForceExpandWidth = segments.childForceExpandHeight = true;
            foreach (var difficulty in _menu.PossibleDifficulties)
            {
                bool on = player.Profile.CurrentDifficulty == difficulty;
                var segment = Moonlight.Panel(difficulty.ToString(), bar.transform, 28, on ? Moonlight.Moon : Color.clear);
                segment.raycastTarget = true;
                var pick = difficulty;
                segment.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _stop = Stop.Difficulty;
                    _menu.PickDifficulty(pick);
                });
                Moonlight.HoverWash(segment, 28);
                var text = Text(segment.transform, Moonlight.Body, 22.5f, on ? Ink : Moonlight.Muted, on ? FontWeight.Bold : FontWeight.SemiBold);
                text.text = difficulty.ToLocalizedName();
                text.alignment = TextAlignmentOptions.Center;
                Stretch(text.rectTransform);
            }

            if (_menu.CanPickModifiers)
            {
                bool focused = _stop == Stop.Modifiers;
                var row = Moonlight.Panel("Modifiers", card, 15, focused ? Moonlight.MoonWash : Moonlight.Raised);
                row.raycastTarget = true;
                var rowSize = row.gameObject.AddComponent<LayoutElement>();
                rowSize.minHeight = rowSize.preferredHeight = 66f;
                row.gameObject.AddComponent<Button>().onClick.AddListener(OpenModifiers);
                Moonlight.HoverWash(row, 15);
                if (focused) Crescent(row.transform, 21f);
                var key = Text(row.transform, Moonlight.Body, 22.5f, focused ? Moonlight.Moon : Moonlight.Text, FontWeight.SemiBold);
                key.text = "Modifiers";
                Stretch(key.rectTransform);
                key.rectTransform.offsetMin = new Vector2(focused ? 51f : 21f, 0f);
                var value = Text(row.transform, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
                value.text = ModifierSummary(player.Profile) + " ›";
                value.alignment = TextAlignmentOptions.MidlineRight;
                Stretch(value.rectTransform);
                value.rectTransform.offsetMax = new Vector2(-21f, 0f);
            }

            Spacer(card);
            Buttons(card, true);
        }

        private void OtherCard(YargPlayer player, bool done)
        {
            var current = PlayerContainer.Players[_menu.PlayerIndex].Profile.Name;
            string status = !done ? "Waiting" : player.SittingOut ? "Sitting out" : "Ready";
            var card = Card(player, false, status, done && !player.SittingOut ? Moonlight.Good : Moonlight.Muted);
            Spacer(card);
            var middle = Box("Choice", card, 210f);
            var icon = Graphic<Image>("Icon", middle);
            icon.preserveAspect = true;
            icon.color = Moonlight.Text;
            Pin(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(84f, 84f));
            SetIcon(icon, IconName(player.Profile.CurrentInstrument));
            var what = Text(middle, Moonlight.Heading, 30f, Moonlight.Text, FontWeight.Medium);
            what.alignment = TextAlignmentOptions.Center;
            what.text = !done ? "Not chosen yet"
                : player.SittingOut ? "Sitting out"
                : $"{player.Profile.CurrentInstrument.ToLocalizedName()} · {player.Profile.CurrentDifficulty.ToLocalizedName()}";
            Pin(what.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(600f, 42f));
            var note = Text(middle, Moonlight.Body, 21f, Moonlight.Muted, FontWeight.Regular);
            note.alignment = TextAlignmentOptions.Center;
            note.richText = false;
            string mods = ModifierSummary(player.Profile);
            note.text = (mods == "None" ? "No modifiers" : mods) + " · waiting for " + current;
            Pin(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -153f), new Vector2(600f, 30f));
            Spacer(card);
        }

        private void ModifierList(Transform card, YargProfile profile)
        {
            Label(card, "MODIFIERS", true);
            // Upscroll is the only modifier (a display preference), then Done
            for (int i = 0; i <= 1; i++)
            {
                bool done = i == 1;
                bool focused = i == _modifierIndex;
                var row = Moonlight.Panel(done ? "Done" : "Upscroll", card, 15, focused ? Moonlight.MoonWash : Color.clear);
                row.raycastTarget = true;
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
                int index = i;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _modifierIndex = index;
                    ChooseModifier();
                });
                Moonlight.OnHover(row.gameObject, () =>
                {
                    if (_modifierIndex == index) return;
                    _modifierIndex = index;
                    Show();
                });
                if (focused) Crescent(row.transform, 21f);
                var name = Text(row.transform, Moonlight.Body, 22.5f, focused ? Moonlight.Moon : Moonlight.Text,
                    focused ? FontWeight.Bold : FontWeight.SemiBold);
                name.text = done ? "Done" : "Upscroll";
                Stretch(name.rectTransform);
                name.rectTransform.offsetMin = new Vector2(60f, 0f);
                if (!done)
                {
                    bool active = profile.Upscroll;
                    var track = Moonlight.Panel("Toggle", row.transform, 18, active ? Moonlight.Moon : Moonlight.Keycap);
                    Pin(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-21f, 0f), new Vector2(66f, 36f));
                    var knob = Moonlight.Panel("Knob", track.transform, 14, active ? Ink : Moonlight.Muted);
                    Pin(knob.rectTransform, new Vector2(active ? 1f : 0f, 0.5f), new Vector2(active ? 1f : 0f, 0.5f),
                        new Vector2(active ? -4.5f : 4.5f, 0f), new Vector2(27f, 27f));
                }
            }
        }

        private void Buttons(Transform card, bool canReady)
        {
            var row = Box("Buttons", card, 69f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 15f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            bool focused = _stop == Stop.Buttons;
            if (canReady)
            {
                var ready = Pill(row, "Ready", true, focused && !_sitOutFocused, () => _menu.Ready());
                ready.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
            if (_menu.CanSitOut)
            {
                var sit = Pill(row, "Sit out", false, focused && (_sitOutFocused || !canReady), () => _menu.SitOut());
                sit.gameObject.AddComponent<LayoutElement>().preferredWidth = 180f;
            }
        }

        // ---------- navigation ----------

        private List<Stop> Stops()
        {
            var stops = new List<Stop> { Stop.Speed };
            if (_menu.PossibleInstruments.Count > 0)
            {
                stops.Add(Stop.Instrument);
                stops.Add(Stop.Difficulty);
                if (_menu.CanPickModifiers) stops.Add(Stop.Modifiers);
            }
            stops.Add(Stop.Buttons);
            return stops;
        }

        private void Move(int direction)
        {
            if (_modifiers)
            {
                _modifierIndex = Mathf.Clamp(_modifierIndex + direction, 0, 1);
                Show();
                return;
            }
            var stops = Stops();
            int index = Mathf.Clamp(stops.IndexOf(_stop) + direction, 0, stops.Count - 1);
            _stop = stops[Mathf.Max(0, index)];
            Show();
        }

        private void Adjust(int direction)
        {
            if (_modifiers) return;
            var profile = _menu.Player.Profile;
            switch (_stop)
            {
                case Stop.Speed:
                    _menu.SpeedPercent += direction * 5;
                    Show();
                    break;
                case Stop.Instrument:
                    _menu.PickInstrument(Next(_menu.PossibleInstruments, profile.CurrentInstrument, direction));
                    break;
                case Stop.Difficulty:
                    _menu.PickDifficulty(Next(_menu.PossibleDifficulties, profile.CurrentDifficulty, direction));
                    break;
                case Stop.Buttons:
                    _sitOutFocused = _menu.CanSitOut && direction > 0;
                    Show();
                    break;
            }
        }

        private static T Next<T>(IReadOnlyList<T> list, T current, int direction)
        {
            if (list.Count == 0) return current;
            int index = list.ToList().IndexOf(current);
            return list[Mathf.Clamp((index < 0 ? 0 : index) + direction, 0, list.Count - 1)];
        }

        private void Choose()
        {
            if (_modifiers)
            {
                ChooseModifier();
                return;
            }
            switch (_stop)
            {
                case Stop.Modifiers:
                    OpenModifiers();
                    break;
                case Stop.Buttons when _sitOutFocused || _menu.PossibleInstruments.Count == 0:
                    _menu.SitOut();
                    break;
                default:
                    _menu.Ready(); // anywhere else, A readies up: the board's main action
                    break;
            }
        }

        private void OpenModifiers()
        {
            _modifiers = true;
            _modifierIndex = 0;
            Show();
        }

        private void ChooseModifier()
        {
            if (_modifierIndex >= 1)
            {
                _modifiers = false;
                _stop = Stop.Modifiers;
                Show();
            }
            else
            {
                var profile = PlayerContainer.Players[_menu.PlayerIndex].Profile;
                profile.Upscroll = !profile.Upscroll;
                Show();
            }
        }

        private void Back()
        {
            if (_modifiers)
            {
                _modifiers = false;
                _stop = Stop.Modifiers;
                Show();
                return;
            }
            _stop = Stop.Instrument;
            _menu.Back();
        }

        public void PushScheme()
        {
            if (!isActiveAndEnabled) return;
            if (_schemePushed) Navigator.Instance.PopScheme();
            var entries = new List<NavigationScheme.Entry>
            {
                new(MenuAction.Up, "Menu.Common.Up", () => Move(-1)),
                new(MenuAction.Down, "Menu.Common.Down", () => Move(1)),
                new(MenuAction.Green, "Menu.MusicLibrary.Footer.Choose", Choose),
                new(MenuAction.Red, "Menu.Common.Back", Back),
                new(MenuAction.Left, "Menu.Players.Adjust", () => Adjust(-1), hide: true),
                new(MenuAction.Right, "Menu.Players.Adjust", () => Adjust(1), hide: true),
            };
            if (_menu.CanSitOut && !_modifiers)
            {
                entries.Add(new(MenuAction.Yellow, "Menu.DifficultySelect.SitOut", () => _menu.SitOut()));
            }
            Navigator.Instance.PushScheme(new NavigationScheme(entries, false));
            _schemePushed = true;
        }

        public void SchemePopped() => _schemePushed = false;

        private void OnEnable()
        {
            _modifiers = false;
            _stop = Stop.Instrument;
            _sitOutFocused = false;
        }

        // ---------- helpers ----------

        private string ModifierSummary(YargProfile profile)
        {
            return profile.Upscroll ? "Upscroll" : "None";
        }

        private static string IconName(Instrument instrument) => instrument switch
        {
            // The pad game's parts, as the library names their icons
            Instrument.FiveFretGuitar => "guitar",
            Instrument.FiveFretBass => "bass",
            Instrument.FiveFretRhythm => "drums",
            Instrument.Keys => "vocals",
            _ => instrument.ToResourceName(),
        };

        private static void SetIcon(Image icon, string name)
        {
            if (Icons.TryGetValue(name, out var sprite) && sprite != null)
            {
                icon.sprite = sprite;
                return;
            }
            icon.enabled = false;
            // Asynchronously: a blocking load can run inside the player loop and recurse it
            Addressables.LoadAssetAsync<Sprite>($"InstrumentIcons[{name}]").Completed += handle =>
            {
                Icons[name] = handle.Result;
                if (icon == null) return;
                icon.sprite = handle.Result;
                icon.enabled = handle.Result != null;
            };
        }

        private static void Dots(Transform tile, int intensity, bool on)
        {
            var row = new GameObject("Dots", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<HorizontalLayoutGroup>();
            row.transform.SetParent(tile, false);
            row.spacing = 4.5f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = false;
            Pin((RectTransform) row.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 21f), new Vector2(120f, 9f));
            for (int i = 0; i < DOTS; i++)
            {
                // diff 0-6 shows as 1-7 dots; a 7 is all red
                bool filled = intensity >= 0 && i <= intensity;
                var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                dot.transform.SetParent(row.transform, false);
                dot.rectTransform.sizeDelta = new Vector2(9f, 9f);
                dot.sprite = filled ? Moonlight.RoundedSprite(8) : Moonlight.Ring;
                dot.color = !filled ? EmptyDot : intensity >= TOP_TIER ? TopDot : on ? Moonlight.Moon : Moonlight.Muted;
                dot.raycastTarget = false;
            }
        }

        private static void Label(Transform card, string text, bool focused)
        {
            var box = Box(text, card, 24f);
            if (focused) Crescent(box, 0f);
            var label = Text(box, Moonlight.Heading, 16.5f, focused ? Moonlight.Moon : Moonlight.Muted, FontWeight.Medium);
            label.characterSpacing = 12f;
            label.text = text;
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(focused ? 27f : 0f, 0f);
        }

        private static void Crescent(Transform parent, float left)
        {
            var crescent = Graphic<Image>("Crescent", parent);
            crescent.sprite = Moonlight.Crescent;
            crescent.color = Moonlight.Moon;
            Pin(crescent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left, 0f), new Vector2(19.5f, 19.5f));
        }

        private static RectTransform Pill(Transform parent, string text, bool primary, bool focused, Action click)
        {
            var pill = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
            pill.transform.SetParent(parent, false);
            pill.sprite = primary ? Moonlight.RoundedSprite(34) : Moonlight.OutlineSprite(34);
            pill.type = Image.Type.Sliced;
            pill.color = primary ? Moonlight.Moon : focused ? Moonlight.Moon : new Color(0.627f, 0.686f, 1f, 0.35f);
            pill.GetComponent<Button>().onClick.AddListener(() => click());
            Moonlight.HoverWash(pill, 33);
            if (primary && focused)
            {
                // A focus ring outside the cream pill
                var ring = Graphic<Image>("Focus", pill.transform);
                ring.sprite = Moonlight.OutlineSprite(34);
                ring.type = Image.Type.Sliced;
                ring.color = Moonlight.Moon;
                Stretch(ring.rectTransform);
                ring.rectTransform.offsetMin = new Vector2(-6f, -6f);
                ring.rectTransform.offsetMax = new Vector2(6f, 6f);
            }
            var label = Text(pill.transform, Moonlight.Body, 24f, primary ? Ink : Moonlight.Text, primary ? FontWeight.Bold : FontWeight.SemiBold);
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return pill.rectTransform;
        }

        private static string DeviceName(YargPlayer player)
        {
            var device = player?.Bindings.InputDevices.FirstOrDefault();
            if (device != null) return device is UnityEngine.InputSystem.Keyboard ? "Keyboard"
                : device is UnityEngine.InputSystem.Gamepad ? "Gamepad" : device.displayName;
            return player?.Bindings.Microphone != null ? "Microphone" : "";
        }

        private static void Spacer(Transform parent)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(parent, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private static RectTransform Box(string name, Transform parent, float height)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            box.transform.SetParent(parent, false);
            var size = box.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            size.flexibleHeight = 0f;
            return (RectTransform) box.transform;
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
    }
}
