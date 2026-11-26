using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Menu.Main
{
    /// <summary>
    /// The profile popup (MProfile), opened from the title screen's card: Overview (stats), Customise (wear looks) and
    /// Unlocks (recent and next). Everything comes from the server's /lb/profile; equipping goes through /lb/equip,
    /// which checks ownership. Sizes are the board's (1280×720) × 1.5.
    /// </summary>
    public class ProfilePopup : MonoBehaviour
    {
        private const float WIDTH = 1560f, HEIGHT = 915f, BANNER = 192f, PAD = 45f;
        private static readonly (string Type, string Label)[] Categories =
        {
            ("alias", "Alias"), ("frame", "Avatar frame"), ("color", "Name colour"), ("banner", "Banner"),
            ("highway", "Highway theme"), ("notes", "Note colours"), ("noteshape", "Note shape"), ("hiteffect", "Hit effect"),
        };
        private static readonly (string Api, string Name)[] Instruments =
            { ("guitar", "Lead"), ("bass", "Bass"), ("drums", "Drums"), ("vocals", "Vocals") };
        private static readonly string[] Tabs = { "Overview", "Customise", "Unlocks" };
        private static readonly Color Ink = new(0.043f, 0.063f, 0.149f);
        private static readonly Color Tile = Moonlight.Raised;

        private TMP_FontAsset _font;
        private RectTransform _dialog, _header, _tabs, _content;
        private List<(CanvasGroup Group, float Alpha, bool Blocks)> _hidden;
        private int _tab = 1; // the board opens on Customise
        private int _category;
        private int _item;
        private bool _busy;

        // Someone else's profile (board FixOtherProfile): only the overview, nothing to equip, no XP numbers
        private bool _viewing;
        private long _playerId;
        private string _playerName, _playerAvatar;
        private Leaderboards.Profile _player;
        private bool _playerMissing;

        private Leaderboards.Profile Data => _viewing ? _player : ProfileCard.Profile;

        public static void Open(Transform parent, TMP_FontAsset font) => Create(parent, font, null);

        /// <summary>Another player's overview, from a board row (its id, name and picture path).</summary>
        public static void OpenPlayer(Transform parent, TMP_FontAsset font, long id, string name, string avatar) =>
            Create(parent, font, popup =>
            {
                popup._viewing = true;
                popup._tab = 0;
                popup._playerId = id;
                popup._playerName = name;
                popup._playerAvatar = avatar;
            });

        private static void Create(Transform parent, TMP_FontAsset font, Action<ProfilePopup> setUp)
        {
            var hidden = new List<(CanvasGroup Group, float Alpha, bool Blocks)>();
            foreach (Transform child in parent)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                hidden.Add((group, Moonrise.Settle(group), group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            var root = new GameObject("Profile Popup", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            var dim = root.GetComponent<Image>();
            dim.color = new Color(0.008f, 0.02f, 0.063f, 0.55f);
            Stretch(dim.rectTransform);
            Moonrise.Rise(root);

            var popup = root.AddComponent<ProfilePopup>();
            popup._font = font;
            popup._hidden = hidden;
            setUp?.Invoke(popup);
            popup.Build(dim.rectTransform);
        }

        private void Build(RectTransform root)
        {
            var dialog = Moonlight.Panel("Dialog", root, 36, new Color(6f / 255f, 10f / 255f, 28f / 255f, 0.97f));
            dialog.raycastTarget = true;
            dialog.gameObject.AddComponent<Mask>().showMaskGraphic = true; // the banner keeps the rounded corners
            _dialog = dialog.rectTransform;
            _dialog.anchorMin = _dialog.anchorMax = _dialog.pivot = new Vector2(0f, 1f);
            _dialog.anchoredPosition = new Vector2(180f, -42f);
            _dialog.sizeDelta = new Vector2(WIDTH, HEIGHT);

            _header = Child("Header", _dialog);
            Top(_header, 0f, BANNER, 0f, 0f);
            _tabs = Child("Tabs", _dialog);
            Top(_tabs, BANNER + 24f, 54f, PAD, PAD);
            _content = Child("Content", _dialog);
            Stretch(_content);
            _content.offsetMin = new Vector2(PAD, 36f);
            _content.offsetMax = new Vector2(-PAD, -(BANNER + 24f + (_viewing ? 0f : 54f + 27f)));

            if (_viewing)
            {
                Navigator.Instance.PushScheme(new NavigationScheme(new()
                {
                    new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Close", Close),
                }, true));
                Redraw();
                LoadPlayer().Forget();
                return;
            }

            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Profile.Equip", () => EquipSelected().Forget()),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Close", Close),
                new NavigationScheme.Entry(MenuAction.Orange, "Menu.Settings.SwitchTab", () => SetTab((_tab + 1) % Tabs.Length)),
                new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Profile.Challenges", OpenChallenges),
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", () => Move(0, -1), hide: true),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", () => Move(0, 1), hide: true),
                new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.Left", () => Move(-1, 0), hide: true),
                new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.Right", () => Move(1, 0), hide: true),
            }, true));

            Redraw();
            Load().Forget();
        }

        private async UniTaskVoid Load()
        {
            await ProfileCard.Reload(); // the latest unlocks
            if (this != null) Redraw();
        }

        private async UniTaskVoid LoadPlayer()
        {
            var player = await Leaderboards.GetPlayer(_playerId);
            if (this == null) return;
            _player = player;
            _playerMissing = player == null;
            Redraw();
        }

        private void Close() => Destroy(gameObject);

        private bool _closed;

        private void OnDestroy() => Restore();

        // Undo now rather than at the end of the frame, so a screen opened straight after starts from the real menu
        private void Restore()
        {
            if (_closed) return;
            _closed = true;
            Navigator.Instance?.PopScheme();
            foreach (var (group, alpha, blocks) in _hidden)
            {
                if (group != null)
                {
                    Moonrise.Return(group, alpha);
                    group.blocksRaycasts = blocks;
                }
            }
        }

        private void OpenChallenges()
        {
            var parent = transform.parent;
            Restore();
            Destroy(gameObject);
            ChallengesMenu.Open(parent, _font);
        }

        private void SetTab(int tab)
        {
            _tab = tab;
            Redraw();
        }

        // Customise: up and down walk the looks, left and right the categories
        private void Move(int category, int item)
        {
            if (_tab != 1 || Data == null) return;
            if (category != 0)
            {
                _category = (_category + category + Categories.Length) % Categories.Length;
                _item = 0;
            }
            int count = ItemsOf(Categories[_category].Type).Count;
            if (item != 0 && count > 0) _item = (_item + item + count) % count;
            Redraw();
        }

        private List<Leaderboards.CosmeticItem> ItemsOf(string type) =>
            Data?.Items.Where(i => i.Type == type).ToList() ?? new List<Leaderboards.CosmeticItem>();

        private async UniTaskVoid EquipSelected()
        {
            if (_tab != 1 || Data == null) return;
            var items = ItemsOf(Categories[_category].Type);
            if (_item < items.Count) await Equip(items[_item]);
        }

        private async UniTask Equip(Leaderboards.CosmeticItem item)
        {
            if (_busy || item.Equipped) return;
            if (!item.Owned)
            {
                ToastManager.ToastMessage($"Locked: {item.Requirement}");
                return;
            }
            _busy = true;
            bool ok = await Leaderboards.Equip(item.Type, item.Id);
            if (ok) await ProfileCard.Reload();
            _busy = false;
            if (this == null) return;
            if (!ok) ToastManager.ToastWarning("Couldn't wear that right now. Try again in a moment.");
            Redraw();
        }

        // ---------- drawing ----------

        private void Redraw()
        {
            Clear(_header);
            Clear(_tabs);
            Clear(_content);
            var data = Data;
            DrawHeader(data);
            if (_viewing)
            {
                if (data == null)
                {
                    Label(_content, _playerMissing ? "This profile can't be shown right now." : $"Loading {_playerName}'s profile…",
                        24f, Moonlight.Muted).richText = false;
                    return;
                }
                DrawOverview(data);
                var note = Label(_content, "Only the overview: what they own and their progress toward unlocks stay private.",
                    19.5f, Moonlight.Faint);
                Pin(note.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(WIDTH - 2 * PAD, 27f));
                return;
            }
            DrawTabs();
            if (data == null)
            {
                Label(_content, Leaderboards.SignedIn ? "Loading your profile…" : "Sign in to see your profile.", 24f, Moonlight.Muted);
                return;
            }
            switch (_tab)
            {
                case 0: DrawOverview(data); break;
                case 1: DrawCustomise(data); break;
                default: DrawUnlocks(data); break;
            }
        }

        private void DrawHeader(Leaderboards.Profile data)
        {
            var banner = new GameObject("Banner", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            banner.transform.SetParent(_header, false);
            banner.color = Parse(data?.Look.Banner, new Color(0.094f, 0.125f, 0.29f));
            banner.raycastTarget = false;
            Stretch(banner.rectTransform);

            // Avatar inside its frame: a ring in the frame's colour, or none
            var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            frame.transform.SetParent(_header, false);
            frame.sprite = Moonlight.ThinRing;
            frame.color = Parse(data?.Look.Frame, Color.clear);
            frame.raycastTarget = false;
            Pin(frame.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(PAD, 27f), new Vector2(126f, 126f));
            var disc = Moonlight.Panel("Avatar", frame.rectTransform, 50, new Color(0.227f, 0.145f, 0.439f));
            disc.rectTransform.anchorMin = disc.rectTransform.anchorMax = disc.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            disc.rectTransform.sizeDelta = new Vector2(99f, 99f);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            string name = data?.Name ?? (_viewing ? _playerName : Leaderboards.PlayerName) ?? "";
            var initial = Label(disc.rectTransform, name.Length > 0 ? name[..1].ToUpperInvariant() : "?", 36f, Moonlight.Text, Heading());
            initial.alignment = TextAlignmentOptions.Center;
            if ((_viewing ? _playerAvatar : ProfileCard.Avatar) is { } avatar)
            {
                var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                picture.transform.SetParent(disc.rectTransform, false);
                picture.raycastTarget = false;
                picture.enabled = false;
                Stretch(picture.rectTransform);
                MusicLibrary.LeaderboardCard.LoadAvatar(avatar, picture).Forget();
            }

            var title = Label(_header, name, 39f, Parse(data?.Look.Color, Moonlight.Text), Heading());
            title.richText = false;
            title.fontWeight = FontWeight.Medium;
            Pin(title.rectTransform, Vector2.zero, Vector2.zero, new Vector2(PAD + 126f + 27f, 69f), new Vector2(800f, 54f));
            var alias = Label(_header, data?.Look.Alias ?? "", 24f, Moonlight.Soft);
            alias.richText = false;
            Pin(alias.rectTransform, Vector2.zero, Vector2.zero, new Vector2(PAD + 126f + 27f, 36f), new Vector2(800f, 33f));

            if (data != null)
            {
                // The level ring on the right
                var track = new GameObject("Level", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                track.transform.SetParent(_header, false);
                track.sprite = Moonlight.ThinRing;
                track.color = new Color(0.627f, 0.686f, 1f, 0.2f);
                track.raycastTarget = false;
                Pin(track.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-PAD - 270f, 36f), new Vector2(81f, 81f));
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                fill.transform.SetParent(track.rectTransform, false);
                fill.sprite = Moonlight.ThinRing;
                fill.color = Moonlight.Moon;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int) Image.Origin360.Top;
                fill.fillAmount = _viewing ? 1f : data.Need > 0 ? Mathf.Clamp01(data.Into / (float) data.Need) : 0f;
                fill.raycastTarget = false;
                Stretch(fill.rectTransform);
                var number = Label(track.rectTransform, data.Level.ToString(), 22.5f, Moonlight.Moon, Heading());
                number.alignment = TextAlignmentOptions.Center;
                var line1 = Label(_header, $"Level {data.Level}", 22.5f, Moonlight.Text);
                line1.fontWeight = FontWeight.Bold;
                Pin(line1.rectTransform, new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-PAD - 171f, _viewing ? 62f : 78f), new Vector2(171f, 30f));
                if (!_viewing)
                {
                    var line2 = Label(_header, $"{data.Into:N0} / {data.Need:N0} XP", 19.5f, Moonlight.Soft);
                    Pin(line2.rectTransform, new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-PAD - 171f, 48f), new Vector2(171f, 27f));
                }
            }

            var close = Moonlight.Panel("Close", _header, 30, new Color(0.024f, 0.04f, 0.11f, 0.6f));
            close.raycastTarget = true;
            Pin(close.rectTransform, Vector2.one, Vector2.one, new Vector2(-24f, -21f), new Vector2(60f, 60f));
            close.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            var x = Label(close.rectTransform, "×", 39f, Moonlight.Text);
            x.alignment = TextAlignmentOptions.Center;
        }

        private void DrawTabs()
        {
            var pill = Moonlight.Panel("Pill", _tabs, 33, new Color(0.051f, 0.075f, 0.188f));
            pill.rectTransform.anchorMin = Vector2.zero;
            pill.rectTransform.anchorMax = new Vector2(0f, 1f);
            pill.rectTransform.pivot = new Vector2(0f, 0.5f);
            pill.rectTransform.offsetMin = pill.rectTransform.offsetMax = Vector2.zero; // the tabs row's height, width from the fitter
            var row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 6, 6);
            row.spacing = 6f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int tab = i;
                bool on = i == _tab;
                var button = Moonlight.Panel(Tabs[i], pill.rectTransform, 27, on ? Moonlight.Moon : Color.clear);
                button.raycastTarget = true;
                button.gameObject.AddComponent<Button>().onClick.AddListener(() => SetTab(tab));
                var label = Label(button.rectTransform, Tabs[i], 22.5f, on ? Ink : Moonlight.Muted);
                label.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
                label.alignment = TextAlignmentOptions.Center;
                var size = button.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = label.GetPreferredValues(Tabs[i]).x + 54f;
                size.preferredHeight = 42f;
            }
        }

        private void DrawOverview(Leaderboards.Profile data)
        {
            var s = data.Stats;
            var stats = new[]
            {
                ("Global rank", s.Rank is { } rank ? $"#{rank}" : "—"), ("Total score", s.TotalScore.ToString("N0")),
                ("Full combos", s.Fcs.ToString("N0")), ("Songs played", s.Plays.ToString("N0")),
            };
            float width = (_content.rect.width - 3 * 18f) / 4f;
            if (width <= 0f) width = (WIDTH - 2 * PAD - 54f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var tile = Moonlight.Panel(stats[i].Item1, _content, 21, Tile).rectTransform;
                Pin(tile, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * (width + 18f), 0f), new Vector2(width, 108f));
                var key = Label(tile, stats[i].Item1, 19.5f, Moonlight.Muted);
                Pin(key.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -18f), new Vector2(width - 48f, 27f));
                var value = Label(tile, stats[i].Item2, 33f, Moonlight.Text, Heading());
                value.fontWeight = FontWeight.Medium;
                Pin(value.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -48f), new Vector2(width - 48f, 45f));
            }
            for (int i = 0; i < 4; i++)
            {
                var (api, instrumentName) = Instruments[i];
                var info = data.Instruments.TryGetValue(api, out var found) ? found : new Leaderboards.InstrumentStats();
                var tile = Moonlight.Panel(instrumentName, _content, 21, Tile).rectTransform;
                Pin(tile, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * (width + 18f), -126f), new Vector2(width, 96f));
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(tile, false);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.color = Moonlight.Text;
                Pin(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(45f, 45f));
                Addressables.LoadAssetAsync<Sprite>($"InstrumentIcons[{api}]").Completed += h =>
                {
                    if (icon != null) icon.sprite = h.Result;
                };
                var key = Label(tile, $"{instrumentName} · level {info.Level}", 19.5f, Moonlight.Muted);
                Pin(key.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(87f, 0f), new Vector2(width - 100f, 27f));
                var value = Label(tile, $"{info.Stars:N0} stars", 27f, Moonlight.Text);
                value.fontWeight = FontWeight.Bold;
                Pin(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(87f, 0f), new Vector2(width - 100f, 33f));
            }
            string favourite = Instruments.FirstOrDefault(i => i.Api == s.Favourite).Name;
            var parts = new List<string>();
            if (favourite != null) parts.Add($"Favourite instrument: {favourite}");
            if (s.Created > 0) parts.Add($"Member since {DateTimeOffset.FromUnixTimeSeconds(s.Created).UtcDateTime:MMMM yyyy}");
            if (data.Season is { } season) parts.Add($"Season {season.Number} level {season.Level}");
            var line = Label(_content, string.Join(" · ", parts), 21f, Moonlight.Muted);
            Pin(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -243f), new Vector2(WIDTH - 2 * PAD, 30f));
        }

        private void DrawCustomise(Leaderboards.Profile data)
        {
            for (int i = 0; i < Categories.Length; i++)
            {
                int category = i;
                bool on = i == _category;
                var row = Moonlight.Panel(Categories[i].Label, _content, 15, on ? Moonlight.MoonWash : Color.clear);
                row.raycastTarget = true;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _category = category;
                    _item = 0;
                    Redraw();
                });
                Pin(row.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * 69f), new Vector2(300f, 66f));
                if (on)
                {
                    var crescent = new GameObject("Crescent", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    crescent.transform.SetParent(row.rectTransform, false);
                    crescent.sprite = Moonlight.Crescent;
                    crescent.color = Moonlight.Moon;
                    crescent.raycastTarget = false;
                    Pin(crescent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(21f, 0f), new Vector2(18f, 18f));
                }
                var label = Label(row.rectTransform, Categories[i].Label, 24f, on ? Moonlight.Moon : Moonlight.Text);
                label.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
                label.rectTransform.offsetMin = new Vector2(54f, 0f);
            }

            var items = ItemsOf(Categories[_category].Type);
            float left = 330f;
            float width = (WIDTH - 2 * PAD - left - 2 * 15f) / 3f;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                bool selected = i == _item;
                var card = Moonlight.Panel(item.Label, _content, 21, item.Equipped ? Moonlight.MoonWash : Tile);
                card.raycastTarget = true;
                card.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _item = items.IndexOf(item);
                    Equip(item).Forget();
                });
                Pin(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(left + i % 3 * (width + 15f), -(i / 3) * (114f + 15f)), new Vector2(width, 114f));
                if (item.Equipped || selected)
                {
                    var edge = Moonlight.Panel("Edge", card.rectTransform, 21, item.Equipped ? Moonlight.Moon : Moonlight.Muted);
                    edge.sprite = Moonlight.OutlineSprite(21);
                    Stretch(edge.rectTransform);
                }
                float textLeft = 21f;
                if (!string.IsNullOrEmpty(item.Color))
                {
                    var swatch = Moonlight.Panel("Swatch", card.rectTransform, 15, Parse(item.Color, Color.clear));
                    if (!item.Owned) swatch.color = new Color(swatch.color.r, swatch.color.g, swatch.color.b, 0.4f);
                    Pin(swatch.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(21f, -24f), new Vector2(30f, 30f));
                    textLeft = 66f;
                }
                var label = Label(card.rectTransform, item.Label, 24f, item.Owned ? Moonlight.Text : Moonlight.Faint);
                label.fontWeight = FontWeight.Bold;
                label.richText = false;
                Pin(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -21f), new Vector2(width - textLeft - 15f, 36f));
                string note = item.Equipped ? "Equipped" : item.Owned ? item.Requirement : $"Locked · {item.Requirement}";
                var line = Label(card.rectTransform, note, 19.5f, item.Equipped ? Moonlight.Moon : Moonlight.Muted);
                Pin(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(21f, -66f), new Vector2(width - 36f, 27f));
            }
        }

        private void DrawUnlocks(Leaderboards.Profile data)
        {
            float width = (WIDTH - 2 * PAD - 36f) / 2f;
            Kicker("RECENTLY UNLOCKED", 0f);
            Kicker("NEXT UP", width + 36f);
            if (data.Recent.Count == 0)
            {
                var none = Label(_content, "Nothing yet: looks unlock from your runs, levels and seasons.", 21f, Moonlight.Muted);
                Pin(none.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -39f), new Vector2(width, 30f));
            }
            for (int i = 0; i < data.Recent.Count; i++)
            {
                var r = data.Recent[i];
                var row = Moonlight.Panel(r.Label, _content, 18, Tile).rectTransform;
                Pin(row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -39f - i * 87f), new Vector2(width, 78f));
                var name = Label(row, r.Label, 24f, Moonlight.Text);
                name.fontWeight = FontWeight.Bold;
                name.richText = false;
                Pin(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -9f), new Vector2(width - 200f, 33f));
                var type = Label(row, TypeName(r.Type), 19.5f, Moonlight.Muted);
                Pin(type.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -42f), new Vector2(width - 200f, 27f));
                var when = Label(row, When(r.Date), 21f, Moonlight.Muted);
                when.alignment = TextAlignmentOptions.MidlineRight;
                when.rectTransform.offsetMax = new Vector2(-24f, 0f);
            }
            for (int i = 0; i < data.Next.Count; i++)
            {
                var n = data.Next[i];
                var row = Moonlight.Panel(n.Label, _content, 18, Tile).rectTransform;
                Pin(row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(width + 36f, -39f - i * 120f), new Vector2(width, 108f));
                var name = Label(row, n.Label, 24f, Moonlight.Text);
                name.fontWeight = FontWeight.Bold;
                name.richText = false;
                Pin(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(width - 230f, 33f));
                var progress = Label(row, $"{n.Have:N0} / {n.Need:N0}", 21f, Moonlight.Muted);
                progress.alignment = TextAlignmentOptions.MidlineRight;
                Pin(progress.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -12f), new Vector2(200f, 33f));
                var bar = Moonlight.Panel("Bar", row, 3, Moonlight.Keycap).rectTransform;
                Pin(bar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -54f), new Vector2(width - 48f, 6f));
                var fill = Moonlight.Panel("Fill", bar, 3, Moonlight.Moon).rectTransform;
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(n.Need > 0 ? Mathf.Clamp01(n.Have / (float) n.Need) : 0f, 1f);
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                var req = Label(row, $"{TypeName(n.Type)} · {n.Requirement}", 19.5f, Moonlight.Muted);
                Pin(req.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -69f), new Vector2(width - 48f, 27f));
            }
        }

        private void Kicker(string text, float left)
        {
            var label = Label(_content, text, 16.5f, Moonlight.Muted, Heading());
            label.characterSpacing = 12f;
            label.fontWeight = FontWeight.Medium;
            Pin(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, 0f), new Vector2(600f, 27f));
        }

        private static string TypeName(string type) =>
            Categories.FirstOrDefault(c => c.Type == type).Label ?? type;

        private static string When(long? unix)
        {
            if (unix == null) return "";
            var then = DateTimeOffset.FromUnixTimeSeconds(unix.Value).UtcDateTime.Date;
            int days = (int) (OnlineSongs.ServerNow.Date - then).TotalDays;
            return days <= 0 ? "Today" : days == 1 ? "Yesterday" : days < 7 ? $"{days} days ago" : then.ToString("MMM d");
        }

        // ---------- helpers ----------

        private TMP_FontAsset Heading() => Moonlight.Heading ? Moonlight.Heading : _font;

        /// <summary>A server colour (#rrggbb), or the fallback when it's missing or malformed.</summary>
        private static Color Parse(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var color) ? color : fallback;

        private TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TMP_FontAsset font = null)
        {
            var label = Moonlight.Label("Text", parent, font ? font : _font, size, color);
            label.text = text;
            Stretch(label.rectTransform);
            return label;
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Clear(Transform parent)
        {
            foreach (Transform child in parent) Destroy(child.gameObject);
        }

        private static void Top(RectTransform rect, float top, float height, float left, float right)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
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
