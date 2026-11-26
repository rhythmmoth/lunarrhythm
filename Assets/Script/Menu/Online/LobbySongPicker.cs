using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Song;

namespace YARG.Menu.Online
{
    /// <summary>
    /// Picking the player's lobby song (board MSongPicker): online songs only, with Season and Featured tabs, and for
    /// the highlighted song each lobby player's part and how hard it is. Sizes are the board's × 1.5.
    /// </summary>
    public class LobbySongPicker : MonoBehaviour
    {

        private enum Tab { Season, Featured, All }

        private RectTransform _content;
        private Action _closed;
        private Tab _tab = Tab.All;
        private List<SongEntry> _songs = new();
        private int _selected;
        private int _first; // the top visible line: kept while the selection stays in view, so hovering never scrolls
        private Lobby.LobbyState _shown;

        public static LobbySongPicker Open(RectTransform parent, Action closed)
        {
            var root = new GameObject("Song picker", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Ui.Stretch((RectTransform) root.transform);
            var picker = root.AddComponent<LobbySongPicker>();
            picker._closed = closed;
            picker._tab = OnlineSongs.Season?.IsRunning(OnlineSongs.ServerNow) == true ? Tab.Season : Tab.All;
            picker.Fill();
            // Start on the current pick, if it's on this tab
            string pick = Lobby.State?.Me?.Pick;
            picker._selected = Math.Max(0, picker._songs.FindIndex(s => OnlineSongs.ShortnameOf(s.ActualLocation) == pick));
            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Lobby.AddToSetlist", picker.Add),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", picker.Close),
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", () => picker.Move(-1)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", () => picker.Move(1)),
                new NavigationScheme.Entry(MenuAction.Orange, "Menu.Lobby.Tab", picker.NextTab),
            }, false));
            picker.Build();
            return picker;
        }

        private void Fill()
        {
            var online = SongContainer.Songs.Where(s => OnlineSongs.ShortnameOf(s.ActualLocation) != null);
            var now = OnlineSongs.ServerNow;
            _songs = (_tab switch
            {
                Tab.Season => online.Where(s => OnlineSongs.SeasonOf(s.ActualLocation, now) > 0),
                Tab.Featured => online.Where(s => OnlineSongs.IsFeatured(s.ActualLocation)),
                _ => online,
            }).OrderBy(s => s.Name.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
            _selected = Mathf.Clamp(_selected, 0, Math.Max(0, _songs.Count - 1));
        }

        private void Update()
        {
            // Players joining or changing parts change what the list shows
            if (Lobby.State != _shown)
            {
                if (Lobby.State is not { Phase: "lobby" } || Lobby.State.Me is not { Ready: false })
                {
                    Close();
                    return;
                }
                Build();
            }
        }

        private void Close()
        {
            Navigator.Instance.PopScheme();
            Destroy(gameObject);
            _closed?.Invoke();
        }

        private void Move(int by)
        {
            if (_songs.Count == 0) return;
            _selected = (_selected + by + _songs.Count) % _songs.Count;
            Build();
        }

        private void NextTab()
        {
            bool season = OnlineSongs.Season?.IsRunning(OnlineSongs.ServerNow) == true;
            do
            {
                _tab = (Tab) (((int) _tab + 1) % 3);
            } while (_tab == Tab.Season && !season);
            _selected = 0;
            Fill();
            Build();
        }

        private void Add()
        {
            if (_selected >= _songs.Count || Taken(_songs[_selected]) is { Length: > 0 }) return;
            _ = Lobby.Pick(OnlineSongs.ShortnameOf(_songs[_selected].ActualLocation));
            Close();
        }

        /// <summary>Why the song can't be added (someone else picked it), or null.</summary>
        private static string Taken(SongEntry song)
        {
            string shortname = OnlineSongs.ShortnameOf(song.ActualLocation);
            var state = Lobby.State;
            if (state == null) return null;
            if (state.Me?.Pick == shortname) return "";
            var other = state.Players.FirstOrDefault(p => p.Id != state.You && p.Pick == shortname);
            return other != null ? $"already in the setlist ({other.Name})" : null;
        }

        // ---------- view ----------

        private void Build()
        {
            _shown = Lobby.State;
            if (_content != null) Destroy(_content.gameObject);
            _content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(transform, false);
            Ui.Stretch(_content);
            var state = Lobby.State;
            int slot = state?.Players.FindIndex(p => p.Id == state.You) + 1 ?? 1;

            var back = Moonlight.Panel("Back", _content, 33, Moonlight.PanelFill);
            back.raycastTarget = true;
            back.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            Ui.PinTopLeft(back.rectTransform, new Vector2(42f, -39f), new Vector2(66f, 66f));
            var chevron = new GameObject("Chevron", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            chevron.transform.SetParent(back.transform, false);
            chevron.sprite = Moonlight.Chevron;
            chevron.color = Moonlight.Text;
            chevron.raycastTarget = false;
            Ui.Center(chevron.rectTransform, Vector2.zero, new Vector2(27f, 27f));
            var title = Ui.Text(_content, "Pick your song", 36f, Moonlight.Text, Moonlight.Heading);
            Ui.PinTopLeft(title.rectTransform, new Vector2(132f, -33f), new Vector2(420f, 78f));
            var sub = Ui.Text(_content, $"Lobby {state?.Code} · slot {slot}", 21f, Moonlight.Muted);
            Ui.PinTopLeft(sub.rectTransform, new Vector2(560f, -33f), new Vector2(600f, 78f));
            Tabs();

            List(state);
            Aside(state);
        }

        private void Tabs()
        {
            bool season = OnlineSongs.Season?.IsRunning(OnlineSongs.ServerNow) == true;
            var tabs = new List<(Tab Tab, string Name)>();
            if (season) tabs.Add((Tab.Season, "Season"));
            tabs.Add((Tab.Featured, "Featured"));
            tabs.Add((Tab.All, "All songs"));
            var bar = Moonlight.Panel("Tabs", _content, 36, Moonlight.PanelFill).rectTransform;
            float x = 6f;
            foreach (var (tab, name) in tabs)
            {
                bool on = tab == _tab;
                var pill = Moonlight.Panel(name, bar, 27, on ? Moonlight.Moon : Color.clear);
                pill.raycastTarget = true;
                var t = tab;
                pill.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _tab = t;
                    _selected = 0;
                    Fill();
                    Build();
                });
                var label = Ui.Text(pill.transform, name, 21f, on ? Moonlight.Night : Moonlight.Muted);
                label.fontWeight = on ? FontWeight.Bold : FontWeight.SemiBold;
                label.alignment = TextAlignmentOptions.Center;
                Ui.Stretch(label.rectTransform);
                float width = label.GetPreferredValues().x + 48f;
                Ui.PinTopLeft(pill.rectTransform, new Vector2(x, -6f), new Vector2(width, 54f));
                x += width + 6f;
            }
            bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.one;
            bar.anchoredPosition = new Vector2(-42f, -39f);
            bar.sizeDelta = new Vector2(x, 66f);
        }

        private const float LINE = 72f, HEAD = 57f, LIST_TOP = 15f, LIST_HEIGHT = 813f;

        private static string HeadOf(SongEntry song)
        {
            string name = song.Name.ToString();
            char c = name.Length > 0 ? char.ToUpperInvariant(name[0]) : '#';
            return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
        }

        /// <summary>The library's list (board FixSongPicker): title and artist on one line, the length at the right, and
        /// a letter heading before each new letter. Headings aren't selectable.</summary>
        private void List(Lobby.LobbyState state)
        {
            var panel = Moonlight.Panel("Songs", _content, 27, Moonlight.PanelFill).rectTransform;
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.offsetMin = new Vector2(42f, 102f);
            panel.offsetMax = new Vector2(42f + 1122f, -135f);

            if (_songs.Count == 0)
            {
                var none = Ui.Text(panel, _tab == Tab.All ? "No online songs yet. They show up after the next sync." : "No songs here right now.", 22.5f, Moonlight.Muted);
                Ui.Inset(none.rectTransform, 30f, 36f, 0f, 40f);
                return;
            }

            // Lines: a song's index, or -1 for a heading (its letter in heads)
            var lines = new List<int>();
            var heads = new List<string>();
            string last = null;
            for (int i = 0; i < _songs.Count; i++)
            {
                string head = HeadOf(_songs[i]);
                if (head != last)
                {
                    lines.Add(-1);
                    heads.Add(head);
                    last = head;
                }
                lines.Add(i);
                heads.Add(null);
            }
            float Height(int l) => lines[l] < 0 ? HEAD : LINE;

            // Scroll only as far as keeps the selection in view, so hovering a row never moves the list
            int sel = lines.IndexOf(_selected);
            _first = Mathf.Clamp(_first, 0, lines.Count - 1);
            if (sel < _first)
            {
                _first = sel > 0 && lines[sel - 1] < 0 ? sel - 1 : sel;
            }
            else
            {
                float used = 0f;
                for (int l = _first; l <= sel; l++) used += Height(l);
                if (used > LIST_HEIGHT)
                {
                    _first = sel;
                    used = Height(sel);
                    while (_first > 0 && used + Height(_first - 1) <= LIST_HEIGHT)
                    {
                        _first--;
                        used += Height(_first);
                    }
                }
            }

            float y = LIST_TOP;
            for (int l = _first; l < lines.Count && y + Height(l) <= LIST_TOP + LIST_HEIGHT; l++)
            {
                if (lines[l] < 0)
                {
                    var head = Ui.Text(panel, heads[l], 18f, Moonlight.Muted, Moonlight.Heading);
                    head.characterSpacing = 10f;
                    head.alignment = TextAlignmentOptions.BottomLeft;
                    Ui.Inset(head.rectTransform, y, 36f, 0f, HEAD - 15f);
                    Sides(head.rectTransform, 36f, 36f);
                    var rule = Moonlight.Panel("Rule", panel, 1, new Color(Moonlight.Muted.r, Moonlight.Muted.g, Moonlight.Muted.b, 0.35f));
                    Ui.Inset(rule.rectTransform, y + HEAD - 9f, 36f, 0f, 1.5f);
                    Sides(rule.rectTransform, 36f, 36f);
                    y += HEAD;
                    continue;
                }
                SongRow(panel, lines[l], y, state);
                y += LINE;
            }
        }

        private void SongRow(RectTransform panel, int index, float y, Lobby.LobbyState state)
        {
            var song = _songs[index];
            bool sel = index == _selected;
            string taken = Taken(song);
            var row = Moonlight.Panel("Song", panel, 15, sel ? Moonlight.MoonWash : Color.clear);
            row.raycastTarget = true;
            row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (_selected == index) Add();
                else
                {
                    _selected = index;
                    Build();
                }
            });
            Moonlight.OnHover(row.gameObject, () =>
            {
                if (_selected == index) return;
                _selected = index;
                Build();
            });
            Ui.Inset(row.rectTransform, y, 15f, 0f, LINE);
            Sides(row.rectTransform, 15f, 15f);

            if (sel)
            {
                var moon = new GameObject("Moon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                moon.transform.SetParent(row.transform, false);
                moon.sprite = Moonlight.Crescent;
                moon.preserveAspect = true;
                moon.color = Moonlight.Moon;
                Ui.PinLeft(moon.rectTransform, 21f, 21f);
            }
            var color = taken is { Length: > 0 } ? Moonlight.Faint : sel ? Moonlight.Moon : Moonlight.Text;
            var name = Ui.Text(row.transform, "", 25.5f, color);
            name.richText = true; // for the artist's size and colour; both names are escaped
            name.fontWeight = sel ? FontWeight.Bold : FontWeight.SemiBold;
            name.text = $"{Ui.Escape(song.Name)}<font-weight=400><size=21><color=#9AA1C7>   {Ui.Escape(song.Artist)}</color></size></font-weight>";
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.alignment = TextAlignmentOptions.MidlineLeft;
            Ui.Stretch(name.rectTransform);
            Sides(name.rectTransform, 57f, 360f);

            string note = taken == "" ? "Your current pick" : taken != null ? "In the setlist" : SitsOut(song, state);
            if (note != null)
            {
                var noteText = Ui.Text(row.transform, note.Substring(0, 1).ToUpperInvariant() + note.Substring(1), 19.5f, Moonlight.Muted);
                noteText.alignment = TextAlignmentOptions.MidlineRight;
                noteText.overflowMode = TextOverflowModes.Ellipsis;
                RightBlock(noteText.rectTransform, 96f, 254f);
            }
            var length = TimeSpan.FromMilliseconds(song.SongLengthMilliseconds);
            var time = Ui.Text(row.transform, $"{(int) length.TotalMinutes}:{length.Seconds:00}", 19.5f, sel ? Moonlight.Soft : Moonlight.Faint);
            time.alignment = TextAlignmentOptions.MidlineRight;
            RightBlock(time.rectTransform, 21f, 63f);
        }

        /// <summary>The library's song panel (board FixSongPicker): square cover and details, then Add to setlist and
        /// each lobby player's part, in place of the library's parts and leaderboard.</summary>
        private void Aside(Lobby.LobbyState state)
        {
            var aside = Moonlight.Panel("Aside", _content, 27, Moonlight.PanelFill).rectTransform;
            aside.anchorMin = Vector2.zero;
            aside.anchorMax = Vector2.one;
            aside.offsetMin = new Vector2(1194f, 102f);
            aside.offsetMax = new Vector2(-42f, -135f);
            if (_selected >= _songs.Count) return;
            var song = _songs[_selected];

            const float pad = 30f, cover = 192f, left = pad + cover + 24f;
            var art = Moonlight.Panel("Cover", aside, 18, Moonlight.Keycap);
            Ui.PinTopLeft(art.rectTransform, new Vector2(pad, -pad), new Vector2(cover, cover));
            Ui.LoadCover(art, song);

            // The details sit on the cover's bottom edge, as in the library: kicker, title, artist, then the meta line
            float bottom = pad + cover;
            var length = TimeSpan.FromMilliseconds(song.SongLengthMilliseconds);
            string meta = string.Join(" · ", new[]
            {
                song.ParsedYear, string.IsNullOrEmpty(song.Genre) ? null : song.Genre.ToString(),
                $"{(int) length.TotalMinutes}:{length.Seconds:00}",
                string.IsNullOrEmpty(song.Charter) ? null : "charted by " + song.Charter,
            }.Where(x => !string.IsNullOrEmpty(x)));
            Detail(aside, meta, 19.5f, Moonlight.Muted, bottom - 27f, 27f, left, pad);
            Detail(aside, song.Artist, 22.5f, Moonlight.Soft, bottom - 60f, 31.5f, left, pad);
            var title = Detail(aside, song.Name, 31.5f, Moonlight.Text, bottom - 105f, 45f, left, pad);
            title.font = Moonlight.Heading;
            title.fontWeight = FontWeight.Medium;
            if (YARG.Menu.MusicLibrary.LibrarySidebar.Kicker(song, out var kickerColor) is { } kicker)
            {
                var kick = Detail(aside, kicker, 18f, kickerColor, bottom - 135f, 27f, left, pad);
                kick.fontWeight = FontWeight.SemiBold;
                kick.characterSpacing = 14f;
            }

            float y = bottom + 24f;
            var add = Ui.Pill(aside, Localize.Key("Menu.Lobby.AddToSetlist"), Taken(song) is not { Length: > 0 }, Add);
            Ui.Inset(add, y, pad, 0f, 69f);
            Sides(add, pad, pad);
            y += 69f + 18f;

            foreach (var p in state?.Players ?? new List<Lobby.Player>())
            {
                var playable = Enum.TryParse(p.Instrument, out Instrument part) ? LobbyPlay.PlayableAs(song, part) : null;
                bool has = playable != null;
                string who = (p.Id == state!.You ? "You" : p.Name) + " · " + LobbyMenu.PartName(p).Split('·')[0].Trim();
                var line = Detail(aside, who, 22.5f, has ? Moonlight.Text : Moonlight.Faint, y, 60f, pad, 150f);
                line.fontWeight = FontWeight.SemiBold;
                var rule = Moonlight.Panel("Rule", aside, 1, new Color(0.627f, 0.686f, 1f, 0.12f));
                Ui.Inset(rule.rectTransform, y + 58.5f, pad, 0f, 1.5f);
                Sides(rule.rectTransform, pad, pad);
                if (has)
                {
                    int intensity = song[playable.Value].Intensity;
                    for (int m = 0; m < 6; m++)
                    {
                        var dot = Moonlight.Panel("Moon", aside, 8, m < intensity ? Moonlight.Moon : Moonlight.Keycap);
                        dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = dot.rectTransform.pivot = new Vector2(1f, 1f);
                        dot.rectTransform.anchoredPosition = new Vector2(-pad - (5 - m) * 22.5f, -y - 22.5f);
                        dot.rectTransform.sizeDelta = new Vector2(15f, 15f);
                    }
                }
                else
                {
                    var no = Detail(aside, "no part", 19.5f, Moonlight.Season, y, 60f, pad, pad);
                    no.alignment = TextAlignmentOptions.MidlineRight;
                }
                y += 60f;
            }
            string sits = SitsOut(song, state);
            var note = Detail(aside, Taken(song) is { Length: > 0 } t ? "Someone else picked this one: " + t.Replace("already in the setlist ", "")
                : sits != null ? sits.Substring(0, 1).ToUpperInvariant() + sits.Substring(1) + "." : "Everyone has a part in this one.",
                21f, Moonlight.Muted, y + 18f, 60f, pad, pad);
            note.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>A line of text from the top of the panel, between plain left and right insets.</summary>
        private static TextMeshProUGUI Detail(RectTransform parent, string text, float size, Color color, float top, float height,
            float left, float right)
        {
            var label = Ui.Text(parent, text, size, color);
            label.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Inset(label.rectTransform, top, left, 0f, height);
            Sides(label.rectTransform, left, right);
            return label;
        }

        /// <summary>Plain left and right insets (Ui.Inset's right one counts from its left one).</summary>
        private static void Sides(RectTransform rect, float left, float right)
        {
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-right, rect.offsetMax.y);
        }

        /// <summary>A full-height block at the row's right: right inset, then width.</summary>
        private static void RightBlock(RectTransform rect, float right, float width)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-right - width, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        /// <summary>"akira would sit out" when the song lacks someone's part, else null.</summary>
        private static string SitsOut(SongEntry song, Lobby.LobbyState state)
        {
            var names = (state?.Players ?? new List<Lobby.Player>())
                .Where(p => p.Instrument != null && !LobbyPlay.Has(song, p.Instrument))
                .Select(p => p.Id == state!.You ? "you" : p.Name).ToList();
            return names.Count == 0 ? null : string.Join(" and ", names) + " would sit out";
        }
    }
}
