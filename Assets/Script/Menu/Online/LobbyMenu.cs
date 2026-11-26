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
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Dialogs;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Menu.Online
{
    /// <summary>
    /// The online lobby over the main menu (boards MLobby, MSync, MSummary). Everything shown comes from
    /// <see cref="Lobby.State"/>; the view is rebuilt whenever the server sends a new one. Sizes are the boards'
    /// (1280×720) × 1.5.
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        private enum View { Connecting, Offline, Join, Lobby, Sync, Between, Summary }

        private static LobbyMenu _open;

        private RectTransform _root;
        private RectTransform _content;
        private List<(CanvasGroup Group, float Alpha, bool Blocks)> _hidden;
        private Lobby.LobbyState _shown;
        private View _view = (View) (-1);
        private bool _pushed;
        private LobbySongPicker _picker;

        public static bool IsOpen => _open != null;

        // A lobby code from Discord's Ask to Join, joined once the lobby screen is connected
        private static string _pendingJoin;

        /// <summary>A friend accepted us from Discord: join their lobby, opening the lobby screen first if need be.</summary>
        public static void JoinFromDiscord(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            if (_open != null && Lobby.Connected)
            {
                _ = Lobby.Join(code);
                return;
            }
            _pendingJoin = code;
            if (_open == null && UnityEngine.Object.FindAnyObjectByType<Main.MainMenu>() is { } menu)
            {
                menu.PlayOnline();
            }
            else if (_open == null)
            {
                ToastManager.ToastMessage("You'll join your friend's lobby when you're back on the menu.");
            }
        }

        /// <summary>Opens the lobby screen (connecting first). Back on the menu after a lobby song, it reopens itself.</summary>
        public static void Open(Transform parent)
        {
            if (_open != null) return;
            LobbyPlay.Returned();
            LobbyPlay.BackToList = false;
            var hidden = new List<(CanvasGroup Group, float Alpha, bool Blocks)>();
            foreach (Transform child in parent)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                hidden.Add((group, Moonrise.Settle(group), group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            var root = new GameObject("Lobby", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            var background = root.GetComponent<Image>();
            background.color = Color.clear;
            Ui.Stretch(background.rectTransform);
            Moonrise.Rise(root);
            var menu = root.AddComponent<LobbyMenu>();
            menu._root = background.rectTransform;
            menu._hidden = hidden;
            _open = menu;
            menu.Connect().Forget();
        }

        private async UniTaskVoid Connect()
        {
            Show(View.Connecting);
            bool ok = await Lobby.Connect();
            if (this == null) return;
            Show(ok ? View.Join : View.Offline);
            if (ok && _pendingJoin != null && Lobby.State == null)
            {
                _ = Lobby.Join(_pendingJoin);
            }
            _pendingJoin = null;
        }

        // Leaving a lobby (or its setlist) goes back to the open lobbies, still connected; Back there closes the screen
        private void LeaveLobby()
        {
            if (Lobby.State == null || !Lobby.Connected)
            {
                Close();
                return;
            }
            _lastCode = null;
            LobbyPlay.LeaveToList();
        }

        private void Close()
        {
            LobbyPlay.Leave();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
            if (_pushed) Navigator.Instance?.PopScheme();
            foreach (var (group, alpha, blocks) in _hidden)
            {
                if (group != null)
                {
                    Moonrise.Return(group, alpha);
                    group.blocksRaycasts = blocks;
                }
            }
        }

        private void Update()
        {
            // "unknown": an older server without the open lobby list; nothing the player can do about it
            if (Lobby.TakeError() is { } error && error != "unknown")
            {
                ToastManager.ToastWarning(ErrorText(error));
            }
            if (_view is View.Connecting or View.Offline)
            {
                return;
            }
            var state = Lobby.State;
            if (state != null)
            {
                _lastCode = state.Code;
            }
            if (state == null && !Lobby.Connected && !Lobby.Reconnecting)
            {
                if (Lobby.Closed is "replaced" or "update_required")
                {
                    ToastManager.ToastWarning(Lobby.Closed == "replaced"
                        ? "You opened the lobby somewhere else, so it closed here."
                        : "Lobbies need the newest version. Update the game to play online.");
                    Close();
                    return;
                }
                // Lost: back to the open lobbies, with a way back into the one it was in
                _lostCode = _lastCode;
                _lastCode = null;
                LobbyPlay.Returned();
                Connect().Forget();
                return;
            }
            if (_view == View.Join && Lobby.Connected && Time.unscaledTime >= _nextList)
            {
                _nextList = Time.unscaledTime + 5f;
                _ = Lobby.List();
            }
            if (_view == View.Join && state == null && !ReferenceEquals(Lobby.Open, _listed))
            {
                Show(View.Join);
                return;
            }

            // A new song to sync: download and load it (once per song)
            if (state is { Phase: "sync" } && state.Song != LobbyPlay.Handled && state.Me is { Loaded: false, Dropped: false })
            {
                LobbyPlay.Sync(state.Song, state.Setlist[state.Song].Song).Forget();
            }

            var view = state == null ? View.Join
                : state.Phase == "lobby" ? (LobbyPlay.SummaryPending && state.Last != null ? View.Summary : View.Lobby)
                : state.Phase == "between" ? View.Between
                : View.Sync;
            if (_view == View.Between && _betweenTimer != null && state?.Deadline is { } deadline)
            {
                _betweenTimer.text = Math.Max(0, (int) Math.Ceiling((deadline - Lobby.ServerNowMs) / 1000.0)).ToString();
            }
            if (state != _shown || view != _view)
            {
                _shown = state;
                if (view == View.Lobby && state!.Me is { Instrument: null })
                {
                    LobbyPlay.SendPart();
                }
                Show(view);
            }
        }

        private static string ErrorText(string reason) => reason switch
        {
            "no_lobby" => "No lobby has that code.",
            "lobby_full" => "That lobby is full.",
            "playing" => "That lobby is playing a setlist. Try again once it's done.",
            "full" => "Too many lobbies are open right now. Try again soon.",
            "unknown_song" => "That song isn't on the server.",
            "pick_first" => "Pick a song before you ready.",
            "locked" => "Unready to change your pick or part.",
            _ => "The lobby said no (" + reason + ").",
        };

        // ---------- views ----------

        private void Show(View view)
        {
            _view = view;
            if (_content != null) Destroy(_content.gameObject);
            _content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_root, false);
            Ui.Stretch(_content);
            _content.gameObject.SetActive(_picker == null);

            var entries = new List<NavigationScheme.Entry>();
            switch (view)
            {
                case View.Connecting:
                    Ui.Centered(_content, "Connecting to the lobby…");
                    entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close));
                    break;
                case View.Offline:
                    Ui.Centered(_content, "Couldn't reach the lobby server. Check your connection and try again.");
                    entries.Add(new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Continue", () => Connect().Forget()));
                    entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close));
                    break;
                case View.Join:
                    BuildJoin(entries);
                    break;
                case View.Lobby:
                    BuildLobby(_shown, entries);
                    break;
                case View.Sync:
                    BuildSync(_shown, entries);
                    break;
                case View.Between:
                    BuildBetween(_shown, entries);
                    break;
                case View.Summary:
                    BuildSummary(_shown, entries);
                    break;
            }
            if (_picker != null)
            {
                return; // the picker has the controls until it closes
            }
            if (_pushed) Navigator.Instance.PopScheme();
            Navigator.Instance.PushScheme(new NavigationScheme(entries, true));
            _pushed = true;
        }

        // The open lobbies screen: what was listed (to redraw when a new list comes), the selection, the private
        // toggle, and the lobby a lost connection came from
        private List<Lobby.OpenLobby> _listed;
        private int _listIndex;
        private bool _private;
        private float _nextList;
        private string _lastCode;
        private static string _lostCode;

        private void BuildJoin(List<NavigationScheme.Entry> entries)
        {
            _listed = Lobby.Open;
            var open = _listed ?? new List<Lobby.OpenLobby>();
            _listIndex = Mathf.Clamp(_listIndex, 0, Mathf.Max(0, open.Count - 1));
            Header(_content, "Play online", null, open.Count == 1 ? "1 open lobby" : $"{open.Count} open lobbies");
            float top = 135f;

            // After a lost connection
            if (_lostCode != null)
            {
                var banner = Moonlight.Panel("Lost", _content, 21, new Color(1f, 0.54f, 0.6f, 0.1f)).rectTransform;
                Ui.PinTopLeft(banner, new Vector2(42f, -top), new Vector2(1920f - 84f, 78f));
                var lost = Ui.Text(banner, $"You lost the connection to {_lostCode}. Rejoin to carry on if it's still picking songs.", 22.5f, Moonlight.Text);
                lost.fontWeight = FontWeight.SemiBold;
                Ui.Inset(lost.rectTransform, 0f, 30f, 260f, 78f);
                string code = _lostCode;
                var rejoin = Ui.Pill(banner, "Rejoin", true, () => { _lostCode = null; _ = Lobby.Join(code); });
                rejoin.anchorMin = rejoin.anchorMax = rejoin.pivot = new Vector2(1f, 0.5f);
                rejoin.anchoredPosition = new Vector2(-21f, 0f);
                rejoin.sizeDelta = new Vector2(180f, 54f);
                top += 99f;
            }

            // The list
            var panel = Moonlight.Panel("Open lobbies", _content, 27, Moonlight.PanelFill).rectTransform;
            Ui.PinTopLeft(panel, new Vector2(42f, -top), new Vector2(1200f, 1080f - top - 135f));
            var head = Ui.Text(panel, "HOST", 18f, Moonlight.Muted, Moonlight.Heading);
            head.characterSpacing = 12f;
            Ui.Inset(head.rectTransform, 21f, 36f, 0f, 30f);
            var playersHead = Ui.Text(panel, "PLAYERS", 18f, Moonlight.Muted, Moonlight.Heading);
            playersHead.characterSpacing = 12f;
            playersHead.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Inset(playersHead.rectTransform, 21f, 36f, 0f, 30f);
            if (open.Count == 0)
            {
                var none = Ui.Text(panel, Lobby.Open == null ? "Looking for lobbies…" : "No open lobbies right now. Create one, or join a friend's with their code.", 22.5f, Moonlight.Muted);
                none.alignment = TextAlignmentOptions.Center;
                Ui.Inset(none.rectTransform, 160f, 36f, 0f, 60f);
            }
            for (int i = 0; i < open.Count && i < 9; i++)
            {
                var lobby = open[i];
                bool selected = i == _listIndex;
                var row = Moonlight.Panel(lobby.Code, panel, 18, selected ? Moonlight.MoonWash : Color.clear);
                row.raycastTarget = true;
                string code = lobby.Code;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => _ = Lobby.Join(code));
                Ui.Inset(row.rectTransform, 63f + i * 87f, 18f, 0f, 81f);
                var name = Ui.Text(row.rectTransform, lobby.Host.Name, 24f, Moonlight.Text);
                name.richText = false;
                name.fontWeight = FontWeight.Bold;
                Ui.Inset(name.rectTransform, 9f, 21f, 0f, 36f);
                var others = lobby.Players.Skip(1).ToList();
                var with = Ui.Text(row.rectTransform, $"LV {lobby.Host.Level} · " + (others.Count == 0 ? "on their own" : "with " + string.Join(", ", others)), 19.5f, Moonlight.Muted);
                with.richText = false;
                Ui.Inset(with.rectTransform, 45f, 21f, 200f, 30f);
                for (int slot = 0; slot < Lobby.MAX_PLAYERS; slot++)
                {
                    bool taken = slot < lobby.Players.Count;
                    var dot = Moonlight.Panel("Slot", row.rectTransform, 12, taken ? Moonlight.Moon : Moonlight.Keycap);
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = dot.rectTransform.pivot = new Vector2(1f, 0.5f);
                    dot.rectTransform.anchoredPosition = new Vector2(-21f - (Lobby.MAX_PLAYERS - 1 - slot) * 33f, 0f);
                    dot.rectTransform.sizeDelta = new Vector2(24f, 24f);
                }
            }

            // Create and join by code
            var side = Moonlight.Panel("Create", _content, 27, Moonlight.PanelFill).rectTransform;
            Ui.PinTopLeft(side, new Vector2(1263f, -top), new Vector2(1920f - 1263f - 42f, 255f));
            var createHead = Ui.Text(side, "CREATE", 18f, Moonlight.Muted, Moonlight.Heading);
            createHead.characterSpacing = 12f;
            Ui.Inset(createHead.rectTransform, 24f, 30f, 0f, 30f);
            var create = Ui.Pill(side, Localize.Key("Menu.Lobby.Create"), true, () => _ = Lobby.Create(_private));
            Ui.Inset(create, 72f, 30f, 0f, 66f);
            var toggle = Moonlight.Panel("Private", side, 18, Moonlight.Keycap);
            toggle.raycastTarget = true;
            toggle.gameObject.AddComponent<Button>().onClick.AddListener(TogglePrivate);
            Ui.Inset(toggle.rectTransform, 162f, 30f, 0f, 66f);
            var privateText = Ui.Text(toggle.rectTransform, _private ? "Private: code only" : "Public: listed here", 21f, Moonlight.Text);
            Ui.Inset(privateText.rectTransform, 0f, 21f, 90f, 66f);
            var track = Moonlight.Panel("Switch", toggle.rectTransform, 15, _private ? Moonlight.Moon : Moonlight.Faint);
            track.rectTransform.anchorMin = track.rectTransform.anchorMax = track.rectTransform.pivot = new Vector2(1f, 0.5f);
            track.rectTransform.anchoredPosition = new Vector2(-21f, 0f);
            track.rectTransform.sizeDelta = new Vector2(60f, 30f);
            var knob = Moonlight.Panel("Knob", track.rectTransform, 12, _private ? new Color(0.043f, 0.063f, 0.149f) : Moonlight.Text);
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(_private ? 1f : 0f, 0.5f);
            knob.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            knob.rectTransform.anchoredPosition = new Vector2(_private ? -15f : 15f, 0f);
            knob.rectTransform.sizeDelta = new Vector2(24f, 24f);

            var joinPanel = Moonlight.Panel("Join by code", _content, 27, Moonlight.PanelFill).rectTransform;
            Ui.PinTopLeft(joinPanel, new Vector2(1263f, -top - 276f), new Vector2(1920f - 1263f - 42f, 186f));
            var joinHead = Ui.Text(joinPanel, "JOIN BY CODE", 18f, Moonlight.Muted, Moonlight.Heading);
            joinHead.characterSpacing = 12f;
            Ui.Inset(joinHead.rectTransform, 24f, 30f, 0f, 30f);
            var join = Ui.Pill(joinPanel, "Join with a code", false, AskCode);
            Ui.Inset(join, 72f, 30f, 0f, 66f);

            entries.Add(new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", () => MoveList(-1)));
            entries.Add(new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", () => MoveList(1)));
            if (open.Count > 0)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Green, "Menu.Lobby.Join", () => _ = Lobby.Join(open[_listIndex].Code)));
            }
            entries.Add(new NavigationScheme.Entry(MenuAction.Blue, "Menu.Lobby.Create", () => _ = Lobby.Create(_private)));
            entries.Add(new NavigationScheme.Entry(MenuAction.Orange, "Menu.Lobby.Private", TogglePrivate));
            entries.Add(new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Lobby.JoinCode", AskCode));
            entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close));
        }

        private void MoveList(int direction)
        {
            int count = _listed?.Count ?? 0;
            if (count == 0) return;
            _listIndex = Mathf.Clamp(_listIndex + direction, 0, count - 1);
            Show(View.Join);
        }

        private void TogglePrivate()
        {
            _private = !_private;
            Show(View.Join);
        }

        private static void AskCode()
        {
            DialogMoonlight.NextFieldLabel = "Code";
            DialogManager.Instance.ShowRenameDialog("Join a lobby", code => _ = Lobby.Join(code));
        }

        private void BuildLobby(Lobby.LobbyState s, List<NavigationScheme.Entry> entries)
        {
            var me = s.Me;
            int ready = s.Players.Count(p => p.Ready);
            string status = ready == s.Players.Count ? "Everyone's ready"
                : ready == 0 ? $"0 of {s.Players.Count} ready"
                : $"{ready} of {s.Players.Count} ready · waiting for {string.Join(", ", s.Players.Where(p => !p.Ready).Select(p => p.Name))}";
            Header(_content, "Lobby", s.Code, $"{s.Players.Count} of {Lobby.MAX_PLAYERS} · share the code", status);

            // Player cards
            for (int i = 0; i < Lobby.MAX_PLAYERS; i++)
            {
                var card = PlayerCard(i < s.Players.Count ? s.Players[i] : null, s);
                Ui.PinTopLeft(card, new Vector2(42f + i * (Ui.Column4 + 21f), -135f), new Vector2(Ui.Column4, 258f));
            }

            // Setlist
            var panel = Moonlight.Panel("Setlist", _content, 27, Moonlight.PanelFill).rectTransform;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(42f, 198f);
            panel.offsetMax = new Vector2(-42f, -420f);
            var kicker = Ui.Text(panel, "SETLIST", 18f, Moonlight.Muted, Moonlight.Heading);
            kicker.characterSpacing = 12f;
            Ui.Inset(kicker.rectTransform, 18f, 36f, 0f, 30f);
            var rule = Ui.Text(panel, "One pick each · plays in slot order · ready locks your pick", 19.5f, Moonlight.Muted);
            rule.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Inset(rule.rectTransform, 18f, 36f, 0f, 30f);
            for (int i = 0; i < Lobby.MAX_PLAYERS; i++)
            {
                var row = SetlistRow(panel, i, i < s.Players.Count ? s.Players[i] : null, s);
                Ui.Inset(row, 60f + i * 81f, 18f, 0f, 78f);
            }

            // Ready
            bool hasPick = me?.Pick != null;
            var button = Ui.Pill(_content, Localize.Key(me?.Ready == true ? "Menu.Lobby.Unready" : "Menu.Lobby.Ready"),
                me?.Ready != true, ToggleReady);
            button.anchorMin = button.anchorMax = button.pivot = Vector2.zero;
            button.anchoredPosition = new Vector2(42f, 102f);
            button.sizeDelta = new Vector2(210f, 69f);
            var hint = Ui.Text(_content, !hasPick ? "Pick a song before you ready."
                : me.Ready ? "Your pick and part are locked. Unready to change them."
                : "Once everyone is ready, every song downloads and syncs before the first one starts.", 21f, Moonlight.Soft);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = hint.rectTransform.pivot = Vector2.zero;
            hint.rectTransform.anchoredPosition = new Vector2(273f, 102f);
            hint.rectTransform.sizeDelta = new Vector2(1500f, 69f);

            entries.Add(new NavigationScheme.Entry(MenuAction.Green, me?.Ready == true ? "Menu.Lobby.Unready" : "Menu.Lobby.Ready", ToggleReady));
            entries.Add(new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Lobby.Pick", OpenPicker));
            entries.Add(new NavigationScheme.Entry(MenuAction.Blue, "Menu.Lobby.Instrument", () => CyclePart(true)));
            entries.Add(new NavigationScheme.Entry(MenuAction.Orange, "Menu.Lobby.Difficulty", () => CyclePart(false)));
            entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Lobby.Leave", LeaveLobby));
        }

        private RectTransform PlayerCard(Lobby.Player p, Lobby.LobbyState s)
        {
            if (p == null)
            {
                var empty = Moonlight.Panel("Open", _content, 27, Moonlight.PanelFill * new Color(1f, 1f, 1f, 0.5f)).rectTransform;
                var outline = new GameObject("Dashes", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                outline.transform.SetParent(empty, false);
                outline.sprite = Moonlight.OutlineSprite(27);
                outline.type = Image.Type.Sliced;
                outline.color = Moonlight.Keycap;
                outline.raycastTarget = false;
                Ui.Stretch(outline.rectTransform);
                var plus = new GameObject("Plus", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                plus.transform.SetParent(empty, false);
                plus.sprite = Moonlight.Plus;
                plus.color = Moonlight.Faint;
                plus.raycastTarget = false;
                Ui.Center(plus.rectTransform, new Vector2(0f, 18f), new Vector2(39f, 39f));
                var open = Ui.Text(empty, "Open slot", 22.5f, Moonlight.Faint);
                open.fontWeight = FontWeight.SemiBold;
                open.alignment = TextAlignmentOptions.Center;
                Ui.Center(open.rectTransform, new Vector2(0f, -30f), new Vector2(300f, 36f));
                return empty;
            }

            bool me = p.Id == s.You;
            var card = Moonlight.Panel(p.Name, _content, 27, Moonlight.PanelFill).rectTransform;
            if (me)
            {
                var ring = new GameObject("You", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                ring.transform.SetParent(card, false);
                ring.sprite = Moonlight.OutlineSprite(27);
                ring.type = Image.Type.Sliced;
                ring.color = new Color(Moonlight.Moon.r, Moonlight.Moon.g, Moonlight.Moon.b, 0.5f);
                ring.raycastTarget = false;
                Ui.Stretch(ring.rectTransform);
            }

            // Avatar: the Discord picture in the player's frame colour, the initial until it loads (or without one)
            var frame = Moonlight.Panel("Frame", card, 35, Ui.Hex(p.Frame) ?? Color.clear);
            Ui.PinTopLeft(frame.rectTransform, new Vector2(24f, -24f), new Vector2(69f, 69f));
            var face = Moonlight.Panel("Face", frame.rectTransform, 30, Moonlight.Keycap);
            Ui.Center(face.rectTransform, Vector2.zero, new Vector2(60f, 60f));
            var initial = Ui.Text(face.rectTransform, p.Name.Length > 0 ? p.Name.Substring(0, 1).ToUpperInvariant() : "?", 27f, Moonlight.Text, Moonlight.Heading);
            initial.alignment = TextAlignmentOptions.Center;
            Ui.Stretch(initial.rectTransform);
            if (p.Avatar != null)
            {
                face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                picture.transform.SetParent(face.rectTransform, false);
                picture.raycastTarget = false;
                picture.enabled = false;
                Ui.Stretch(picture.rectTransform);
                MusicLibrary.LeaderboardCard.LoadAvatar(p.Avatar, picture).Forget();
            }

            var name = Ui.Text(card, "", 25.5f, Ui.Hex(p.Color) ?? Moonlight.Text);
            name.fontWeight = FontWeight.Bold;
            name.richText = true; // for the level; the name itself is escaped
            name.text = $"{Ui.Escape(p.Name)}<size=18><color=#5F6790>  LV {p.Level}</color></size>";
            Ui.Inset(name.rectTransform, 27f, 108f, 0f, 33f);
            string who = string.Join(" · ", new[] { p.Id == s.Host ? "Host" : null, me ? "You" : null, p.Alias }.Where(x => x != null));
            var alias = Ui.Text(card, who, 19.5f, Moonlight.Muted);
            Ui.Inset(alias.rectTransform, 60f, 108f, 0f, 27f);

            var part = Moonlight.Panel("Part", card, 18, Moonlight.Raised).rectTransform;
            Ui.Inset(part, 111f, 24f, 0f, 69f);
            var partText = Ui.Text(part, PartName(p), 22.5f, Moonlight.Text);
            partText.fontWeight = FontWeight.SemiBold;
            Ui.Stretch(partText.rectTransform);
            partText.rectTransform.offsetMin = new Vector2(18f, 0f);

            string readyText = p.Ready ? "Ready" : p.Pick == null ? "Choosing a song" : "Not ready";
            var readyPill = Moonlight.Panel("Ready", card, 22, p.Ready ? Moonlight.OverDark(Moonlight.Good, 0.12f) : Moonlight.Raised).rectTransform;
            Ui.Inset(readyPill, 195f, 24f, 0f, 45f);
            var readyLabel = Ui.Text(readyPill, readyText, 21f, p.Ready ? Moonlight.Good : Moonlight.Muted);
            readyLabel.fontWeight = FontWeight.Bold;
            readyLabel.alignment = TextAlignmentOptions.Center;
            Ui.Stretch(readyLabel.rectTransform);
            return card;
        }

        private RectTransform SetlistRow(RectTransform panel, int slot, Lobby.Player p, Lobby.LobbyState s)
        {
            bool mine = p != null && p.Id == s.You;
            var row = Moonlight.Panel("Slot", panel, 15, mine ? Moonlight.MoonWash : Color.clear).rectTransform;

            var n = Ui.Text(row, (slot + 1).ToString(), 19.5f, Moonlight.Faint, Moonlight.Heading);
            Ui.PinLeft(n.rectTransform, 18f, 33f);
            var song = p?.Pick != null ? LobbyPlay.Find(p.Pick) : null;
            var cover = Moonlight.Panel("Cover", row, 12, p?.Pick != null ? Moonlight.Keycap : p == null ? Color.clear : Moonlight.Raised);
            Ui.PinLeft(cover.rectTransform, 51f, 54f);
            if (song != null) Ui.LoadCover(cover, song);

            string title, sub;
            Color titleColor = Moonlight.Text;
            if (p == null)
            {
                (title, sub, titleColor) = ("Open slot", "Fills when another player joins", Moonlight.Faint);
            }
            else if (p.Pick == null)
            {
                (title, sub, titleColor) = mine ? ("No song picked", "Your pick goes here", Moonlight.Muted)
                    : ("Choosing…", $"{p.Name} is picking", Moonlight.Muted);
            }
            else
            {
                title = song?.Name ?? p.Pick;
                sub = (song?.Artist ?? "Not in your library yet") + " · " + (mine ? "your pick" : p.Name);
            }
            var titleText = Ui.Text(row, title, 24f, titleColor);
            titleText.fontWeight = FontWeight.Bold;
            Ui.Inset(titleText.rectTransform, 9f, 126f, 420f, 33f);
            var subText = Ui.Text(row, sub, 19.5f, Moonlight.Muted);
            Ui.Inset(subText.rectTransform, 42f, 126f, 420f, 27f);

            if (song != null)
            {
                var out_ = s.Players.Where(x => x.Instrument != null && !LobbyPlay.Has(song, x.Instrument))
                    .Select(x => $"{x.Name} sits out: no {PartOnly(x)}").ToList();
                if (out_.Count > 0)
                {
                    var sits = Ui.Text(row, string.Join(" · ", out_), 19.5f, Moonlight.Season);
                    sits.alignment = TextAlignmentOptions.MidlineRight;
                    Ui.Inset(sits.rectTransform, 0f, 0f, mine ? 255f : 18f, 78f);
                }
            }

            if (mine)
            {
                RectTransform action = null;
                if (p.Ready)
                {
                    var locked = Ui.Text(row, "Locked while ready", 19.5f, Moonlight.Muted);
                    locked.alignment = TextAlignmentOptions.MidlineRight;
                    action = locked.rectTransform;
                }
                else if (p.Pick != null)
                {
                    action = Ui.Pill(row, "Remove", false, () => _ = Lobby.Pick(null));
                }
                else
                {
                    action = Ui.Pill(row, "Pick a song", true, OpenPicker);
                }
                action.anchorMin = action.anchorMax = action.pivot = new Vector2(1f, 0.5f);
                action.anchoredPosition = new Vector2(-18f, 0f);
                action.sizeDelta = new Vector2(p.Ready ? 240f : 180f, 51f);
            }
            return row;
        }

        private void BuildSync(Lobby.LobbyState s, List<NavigationScheme.Entry> entries)
        {
            var current = s.Current;
            var song = current != null ? LobbyPlay.Find(current.Song) : null;
            var by = current != null ? s.ById(current.By)?.Name ?? "someone who left" : "";

            var cover = Moonlight.Panel("Cover", _content, 27, Moonlight.Keycap);
            Ui.Center(cover.rectTransform, new Vector2(0f, 540f - 105f - 135f), new Vector2(270f, 270f));
            cover.rectTransform.anchorMin = cover.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            if (song != null) Ui.LoadCover(cover, song);
            var kicker = Ui.Text(_content, $"SONG {s.Song + 1} OF {s.Setlist.Count} · PICKED BY {by.ToUpperInvariant()}", 19.5f, Moonlight.Muted);
            kicker.fontWeight = FontWeight.SemiBold;
            kicker.characterSpacing = 14f;
            kicker.alignment = TextAlignmentOptions.Center;
            Ui.Center(kicker.rectTransform, new Vector2(0f, 540f - 405f), new Vector2(1600f, 30f));
            var title = Ui.Text(_content, song?.Name ?? current?.Song ?? "", 51f, Moonlight.Text, Moonlight.Heading);
            title.alignment = TextAlignmentOptions.Center;
            Ui.Center(title.rectTransform, new Vector2(0f, 540f - 462f), new Vector2(1600f, 66f));
            var artist = Ui.Text(_content, song?.Artist ?? "", 25.5f, Moonlight.Soft);
            artist.alignment = TextAlignmentOptions.Center;
            Ui.Center(artist.rectTransform, new Vector2(0f, 540f - 516f), new Vector2(1600f, 36f));

            float width = (1920f - 360f - 3 * 21f) / 4f;
            for (int i = 0; i < s.Players.Count; i++)
            {
                var tile = SyncTile(s.Players[i], s, current);
                Ui.PinTopLeft(tile, new Vector2(180f + i * (width + 21f), -630f), new Vector2(width, 210f));
            }

            string note;
            var me = s.Me;
            if (s.Phase == "playing")
            {
                note = me is { SitsOut: true } ? "You're sitting this one out. You're back for the next song."
                    : me is { Dropped: true } ? "You didn't load in time, so this one went on without you."
                    : "Waiting for everyone to finish this song.";
            }
            else
            {
                note = $"Starts for everyone together once all {s.Players.Count} have loaded. A download that stops for 30 seconds sits that player out for this song.";
            }
            var text = Ui.Text(_content, note, 22.5f, Moonlight.Muted);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 0f);
            text.rectTransform.pivot = new Vector2(0.5f, 0f);
            text.rectTransform.anchoredPosition = new Vector2(0f, 105f);
            text.rectTransform.sizeDelta = new Vector2(0f, 33f);

            entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Lobby.LeaveSetlist", LeaveLobby));
        }

        private TextMeshProUGUI _betweenTimer;

        /// <summary>After a song of the setlist (board NextBetween): everyone's result, your part for the next song
        /// (changeable until you ready), and the next song, which starts once all are ready or the timer runs out.</summary>
        private void BuildBetween(Lobby.LobbyState s, List<NavigationScheme.Entry> entries)
        {
            var done = s.Song > 0 && s.Song - 1 < s.Setlist.Count ? s.Setlist[s.Song - 1] : null;
            var next = s.Song < s.Setlist.Count ? s.Setlist[s.Song] : null;
            var doneSong = done != null ? LobbyPlay.Find(done.Song) : null;
            var me = s.Me;

            var kicker = Ui.Text(_content, $"SONG {s.Song} OF {s.Setlist.Count} DONE", 19.5f, Moonlight.Muted);
            kicker.fontWeight = FontWeight.SemiBold;
            kicker.characterSpacing = 14f;
            Ui.PinTopLeft(kicker.rectTransform, new Vector2(42f, -42f), new Vector2(1200f, 30f));
            var title = Ui.Text(_content, doneSong?.Name ?? done?.Song ?? "", 42f, Moonlight.Text, Moonlight.Heading);
            Ui.PinTopLeft(title.rectTransform, new Vector2(42f, -75f), new Vector2(1500f, 57f));
            var artist = Ui.Text(_content, doneSong?.Artist ?? "", 22.5f, Moonlight.Soft);
            Ui.PinTopLeft(artist.rectTransform, new Vector2(42f, -135f), new Vector2(1500f, 33f));

            // Everyone's result, and whether they're ready (with the part they'll play next)
            for (int i = 0; i < s.Players.Count; i++)
            {
                var p = s.Players[i];
                var result = done?.Results.FirstOrDefault(r => r.Id == p.Id);
                var tile = Moonlight.Panel(p.Name, _content, 27, Moonlight.PanelFill).rectTransform;
                Ui.PinTopLeft(tile, new Vector2(42f + i * (Ui.Column4 + 21f), -195f), new Vector2(Ui.Column4, 222f));
                if (p.Id == s.You)
                {
                    var ring = new GameObject("You", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    ring.transform.SetParent(tile, false);
                    ring.sprite = Moonlight.OutlineSprite(27);
                    ring.type = Image.Type.Sliced;
                    ring.color = new Color(Moonlight.Moon.r, Moonlight.Moon.g, Moonlight.Moon.b, 0.5f);
                    Ui.Stretch(ring.rectTransform);
                }
                var name = Ui.Text(tile, p.Name, 24f, p.Id == s.You ? Moonlight.Moon : Moonlight.Text);
                name.fontWeight = FontWeight.Bold;
                Ui.Inset(name.rectTransform, 24f, 27f, 27f, 33f);
                var score = Ui.Text(tile, result != null ? result.Score.ToString("N0") : p.Away ? "Reconnecting…" : "—", 36f, Moonlight.Text, Moonlight.Heading);
                Ui.Inset(score.rectTransform, 63f, 27f, 27f, 48f);
                var tag = Ui.Text(tile, result == null ? "Didn't play this one" : result.Fc ? "Full combo" : $"{result.Stars} stars", 19.5f,
                    result is { Fc: true } ? Ui.Gold : Moonlight.Muted);
                Ui.Inset(tag.rectTransform, 117f, 27f, 27f, 30f);
                var ready = Moonlight.Panel("Next", tile, 15, p.Ready ? new Color(0.5f, 0.88f, 0.69f, 0.12f) : Moonlight.Keycap);
                Ui.Inset(ready.rectTransform, 162f, 21f, 21f, 39f);
                var readyText = Ui.Text(ready.rectTransform, (p.Ready ? "Ready · " : "Choosing · ") + PartName(p), 18f, p.Ready ? Moonlight.Good : Moonlight.Muted);
                readyText.fontWeight = FontWeight.Bold;
                readyText.alignment = TextAlignmentOptions.Center;
                Ui.Stretch(readyText.rectTransform);
            }

            // Your part for the next song
            var part = Moonlight.Panel("Your part", _content, 27, Moonlight.PanelFill).rectTransform;
            Ui.PinTopLeft(part, new Vector2(42f, -447f), new Vector2(1920f - 84f, 240f));
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            edge.transform.SetParent(part, false);
            edge.sprite = Moonlight.OutlineSprite(27);
            edge.type = Image.Type.Sliced;
            edge.color = new Color(Moonlight.Moon.r, Moonlight.Moon.g, Moonlight.Moon.b, 0.35f);
            Ui.Stretch(edge.rectTransform);
            var partKicker = Ui.Text(part, "YOUR PART FOR THE NEXT SONG", 18f, Moonlight.Moon, Moonlight.Heading);
            partKicker.characterSpacing = 12f;
            Ui.Inset(partKicker.rectTransform, 24f, 30f, 0f, 30f);
            var partRule = Ui.Text(part, me?.Ready == true ? "Locked: unready to change it" : "Change it until you press Ready", 19.5f, Moonlight.Muted);
            partRule.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Inset(partRule.rectTransform, 24f, 30f, 30f, 30f);
            string[] labels = { "Instrument", "Difficulty" };
            string[] values =
            {
                me?.Instrument != null && Enum.TryParse(me.Instrument, out Instrument inst) ? inst.ToLocalizedName() : "—",
                me?.Difficulty != null && Enum.TryParse(me.Difficulty, out Difficulty diff) ? diff.ToLocalizedName() : "—",
            };
            float half = (1920f - 84f - 60f - 21f) / 2f;
            for (int i = 0; i < 2; i++)
            {
                var picker = Moonlight.Panel(labels[i], part, 21, Moonlight.Keycap).rectTransform;
                picker.anchorMin = picker.anchorMax = picker.pivot = new Vector2(0f, 1f);
                picker.anchoredPosition = new Vector2(30f + i * (half + 21f), -78f);
                picker.sizeDelta = new Vector2(half, 75f);
                var key = Ui.Text(picker, labels[i], 19.5f, Moonlight.Muted);
                Ui.Inset(key.rectTransform, 0f, 24f, 0f, 75f);
                var value = Ui.Text(picker, "‹  " + values[i] + "  ›", 27f, Moonlight.Text);
                value.fontWeight = FontWeight.Bold;
                value.alignment = TextAlignmentOptions.Center;
                Ui.Stretch(value.rectTransform);
            }
            var nextSong = next != null ? LobbyPlay.Find(next.Song) : null;
            var parts = nextSong == null ? "" : string.Join(", ", LobbyPlay.Instruments(LobbyPlay.Me?.Profile ?? new Core.Game.YargProfile())
                .Where(i => LobbyPlay.PlayableAs(nextSong, i) != null).Select(i => i.ToLocalizedName()));
            var partHint = Ui.Text(part, parts.Length > 0 ? $"{nextSong.Name} has {parts}" : "", 19.5f, Moonlight.Soft);
            Ui.Inset(partHint.rectTransform, 177f, 30f, 30f, 36f);

            // Up next, with the timer
            var bar = Moonlight.Panel("Up next", _content, 27, Moonlight.PanelFill).rectTransform;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.offsetMin = new Vector2(42f, 114f);
            bar.offsetMax = new Vector2(-42f, 222f);
            var upKicker = Ui.Text(bar, "UP NEXT", 18f, Moonlight.Muted, Moonlight.Heading);
            upKicker.characterSpacing = 12f;
            Ui.Inset(upKicker.rectTransform, 39f, 30f, 0f, 30f);
            var cover = Moonlight.Panel("Cover", bar, 15, Moonlight.Keycap);
            cover.rectTransform.anchorMin = cover.rectTransform.anchorMax = cover.rectTransform.pivot = new Vector2(0f, 0.5f);
            cover.rectTransform.anchoredPosition = new Vector2(210f, 0f);
            cover.rectTransform.sizeDelta = new Vector2(66f, 66f);
            if (nextSong != null) Ui.LoadCover(cover, nextSong);
            var by = next != null ? s.ById(next.By)?.Name ?? "someone who left" : "";
            var nextTitle = Ui.Text(bar, nextSong?.Name ?? next?.Song ?? "", 25.5f, Moonlight.Text);
            nextTitle.fontWeight = FontWeight.Bold;
            Ui.Inset(nextTitle.rectTransform, 21f, 297f, 300f, 36f);
            var nextSub = Ui.Text(bar, (nextSong?.Artist ?? "") + " · picked by " + by, 19.5f, Moonlight.Muted);
            Ui.Inset(nextSub.rectTransform, 57f, 297f, 300f, 30f);
            int readyCount = s.Players.Count(p => p.Ready);
            var status = Ui.Text(bar, $"{readyCount} of {s.Players.Count} ready · starts when all are, or when the timer runs out", 21f, Moonlight.Soft);
            status.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Inset(status.rectTransform, 0f, 140f, 0f, 108f);
            status.rectTransform.anchorMin = new Vector2(0f, 0f);
            status.rectTransform.anchorMax = new Vector2(1f, 1f);
            status.rectTransform.offsetMin = new Vector2(900f, 0f);
            status.rectTransform.offsetMax = new Vector2(-150f, 0f);
            _betweenTimer = Ui.Text(bar, "", 36f, Moonlight.Moon, Moonlight.Heading);
            _betweenTimer.alignment = TextAlignmentOptions.Center;
            _betweenTimer.rectTransform.anchorMin = _betweenTimer.rectTransform.anchorMax = _betweenTimer.rectTransform.pivot = new Vector2(1f, 0.5f);
            _betweenTimer.rectTransform.anchoredPosition = new Vector2(-39f, 0f);
            _betweenTimer.rectTransform.sizeDelta = new Vector2(96f, 66f);

            entries.Add(new NavigationScheme.Entry(MenuAction.Green, me?.Ready == true ? "Menu.Lobby.Unready" : "Menu.Lobby.Ready", ToggleReady));
            entries.Add(new NavigationScheme.Entry(MenuAction.Blue, "Menu.Lobby.Instrument", () => CyclePart(true)));
            entries.Add(new NavigationScheme.Entry(MenuAction.Orange, "Menu.Lobby.Difficulty", () => CyclePart(false)));
            entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Lobby.LeaveSetlist", LeaveLobby));
        }

        private RectTransform SyncTile(Lobby.Player p, Lobby.LobbyState s, Lobby.SetlistSong current)
        {
            var tile = Moonlight.Panel(p.Name, _content, 27, Moonlight.PanelFill).rectTransform;
            if (p.Id == s.You)
            {
                var ring = new GameObject("You", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                ring.transform.SetParent(tile, false);
                ring.sprite = Moonlight.OutlineSprite(27);
                ring.type = Image.Type.Sliced;
                ring.color = new Color(Moonlight.Moon.r, Moonlight.Moon.g, Moonlight.Moon.b, 0.5f);
                Ui.Stretch(ring.rectTransform);
            }
            bool finished = current != null && current.Results.Any(r => r.Id == p.Id);
            (string state, Color color, float fill) = p.Dropped ? ("Dropped for this song", Moonlight.Danger, 1f)
                : p.SitsOut ? ("Sitting out", Moonlight.Season, 1f)
                : s.Phase == "playing" ? (finished ? "Finished" : "Playing", finished ? Moonlight.Good : Moonlight.Text, 1f)
                : p.Loaded ? ("Loaded", Moonlight.Good, 1f)
                : p.Progress >= 60 ? ("Loading the chart", Moonlight.Text, p.Progress / 100f)
                : ($"Downloading · {p.Progress}%", Moonlight.Text, p.Progress / 100f);

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            track.transform.SetParent(tile, false);
            track.sprite = Moonlight.Ring;
            track.color = Moonlight.Keycap;
            Ui.Inset(track.rectTransform, 24f, 0f, 0f, 81f);
            track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            track.rectTransform.sizeDelta = new Vector2(81f, 81f);
            var bar = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bar.transform.SetParent(track.transform, false);
            bar.sprite = Moonlight.Ring;
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Radial360;
            bar.fillOrigin = (int) Image.Origin360.Top;
            bar.fillClockwise = true;
            bar.fillAmount = Mathf.Clamp01(fill);
            bar.color = color;
            Ui.Stretch(bar.rectTransform);

            var name = Ui.Text(tile, p.Name, 24f, Moonlight.Text);
            name.fontWeight = FontWeight.Bold;
            name.alignment = TextAlignmentOptions.Center;
            Ui.Inset(name.rectTransform, 117f, 12f, 0f, 33f);
            var label = Ui.Text(tile, state, 19.5f, color);
            label.alignment = TextAlignmentOptions.Center;
            Ui.Inset(label.rectTransform, 153f, 12f, 0f, 30f);
            return tile;
        }

        private void BuildSummary(Lobby.LobbyState s, List<NavigationScheme.Entry> entries)
        {
            var list = s.Last!;
            var ids = list.SelectMany(x => x.Results.Select(r => r.Id)).Concat(list.Select(x => x.By)).Distinct()
                .OrderBy(id => s.Players.FindIndex(p => p.Id == id) is var i && i >= 0 ? i : 99).Take(Lobby.MAX_PLAYERS).ToList();
            string Name(long id) => s.ById(id)?.Name ?? "Left";
            double minutes = list.Sum(x => LobbyPlay.Find(x.Song)?.SongLengthSeconds ?? 0) / 60.0;
            Header(_content, "Setlist complete", null, $"{list.Count} songs · {minutes:0} minutes · lobby {s.Code}");

            var table = Moonlight.Panel("Table", _content, 27, Moonlight.PanelFill).rectTransform;
            table.anchorMin = new Vector2(0f, 0f);
            table.anchorMax = new Vector2(0f, 1f);
            table.pivot = new Vector2(0f, 0.5f);
            table.offsetMin = new Vector2(42f, 102f);
            table.offsetMax = new Vector2(42f + 1230f, -135f);

            const float COL = 165f;
            void Cells(float top, float height, string first, string firstSub, IList<(string Text, Color Color)> cells, float size, bool bold)
            {
                var a = Ui.Text(table, first, firstSub == null ? 18f : 24f, firstSub == null ? Moonlight.Muted : Moonlight.Text,
                    firstSub == null && bold ? Moonlight.Heading : null);
                a.fontWeight = firstSub != null ? FontWeight.Bold : FontWeight.Regular;
                Ui.Inset(a.rectTransform, top + (firstSub != null ? 9f : 0f), 24f, COL * 4 + 24f, firstSub != null ? 33f : height);
                if (firstSub != null)
                {
                    var b = Ui.Text(table, firstSub, 19.5f, Moonlight.Muted);
                    Ui.Inset(b.rectTransform, top + 42f, 24f, COL * 4 + 24f, 27f);
                }
                for (int i = 0; i < cells.Count; i++)
                {
                    var c = Ui.Text(table, cells[i].Text, size, cells[i].Color);
                    c.fontWeight = bold ? FontWeight.Bold : FontWeight.SemiBold;
                    c.alignment = TextAlignmentOptions.MidlineRight;
                    c.rectTransform.anchorMin = c.rectTransform.anchorMax = c.rectTransform.pivot = new Vector2(1f, 1f);
                    c.rectTransform.anchoredPosition = new Vector2(-24f - (cells.Count - 1 - i) * COL, -top);
                    c.rectTransform.sizeDelta = new Vector2(COL, height);
                }
            }

            Color Col(long id) => id == s.You ? Moonlight.Moon : Moonlight.Text;
            Cells(15f, 66f, "Song", null, ids.Select(id => (Name(id), id == s.You ? Moonlight.Moon : Moonlight.Muted)).ToList(), 19.5f, false);
            float y = 81f;
            foreach (var x in list)
            {
                var song = LobbyPlay.Find(x.Song);
                Cells(y, 84f, song?.Name ?? x.Song, "Picked by " + Name(x.By),
                    ids.Select(id => x.Results.FirstOrDefault(r => r.Id == id) is { } r ? (r.Score.ToString("N0"), Col(id)) : ("—", Moonlight.Faint)).ToList(),
                    22.5f, false);
                y += 84f;
            }
            Cells(y + 6f, 87f, "TOTAL", null, ids.Select(id => (list.Sum(x => (long) (x.Results.FirstOrDefault(r => r.Id == id)?.Score ?? 0)).ToString("N0"), Col(id))).ToList(), 25.5f, true);

            // Aside
            var aside = new GameObject("Aside", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            aside.SetParent(_content, false);
            aside.anchorMin = new Vector2(0f, 0f);
            aside.anchorMax = new Vector2(1f, 1f);
            aside.offsetMin = new Vector2(1302f, 102f);
            aside.offsetMax = new Vector2(-42f, -135f);
            var stack = aside.GetComponent<VerticalLayoutGroup>();
            stack.spacing = 21f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            long band = list.Sum(x => x.Results.Sum(r => (long) r.Score));
            Card(aside, "BAND TOTAL", band.ToString("N0"), null, Moonlight.Muted, false);
            var best = list.SelectMany(x => x.Results.Select(r => (Song: x, Run: r))).OrderByDescending(b => b.Run.Score).FirstOrDefault();
            if (best.Run != null)
            {
                Card(aside, "BEST PERFORMANCE", $"{Name(best.Run.Id)} · {LobbyPlay.Find(best.Song.Song)?.Name ?? best.Song.Song}",
                    $"{(best.Run.Fc ? "Full combo · " : "")}{best.Run.Stars} stars · {best.Run.Score:N0}", Ui.Gold, true);
            }
            var mine = list.Select(x => x.Results.FirstOrDefault(r => r.Id == s.You)).Where(r => r != null).ToList();
            Card(aside, "YOUR SETLIST", $"{mine.Count} runs · {mine.Count(r => r.Fc)} full combos",
                "Every run also counts for the song leaderboards and challenges.", Moonlight.Muted, false);
            var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(aside, false);
            gap.GetComponent<LayoutElement>().flexibleHeight = 1f;
            var back = Ui.Pill(aside, Localize.Key("Menu.Lobby.BackToLobby"), true, CloseSummary);
            back.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
            var leave = Ui.Pill(aside, Localize.Key("Menu.Lobby.Leave"), false, LeaveLobby);
            leave.gameObject.AddComponent<LayoutElement>().preferredHeight = 66f;

            entries.Add(new NavigationScheme.Entry(MenuAction.Green, "Menu.Lobby.BackToLobby", CloseSummary));
            entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Lobby.Leave", LeaveLobby));
        }

        private void CloseSummary()
        {
            LobbyPlay.SummaryPending = false;
            _shown = null; // rebuild as the lobby
        }

        private static void Card(RectTransform parent, string kicker, string big, string small, Color color, bool ringed)
        {
            var card = Moonlight.Panel(kicker, parent, 27, Moonlight.PanelFill).rectTransform;
            var stack = card.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(30, 30, 27, 27);
            stack.spacing = 6f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            if (ringed)
            {
                var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
                ring.transform.SetParent(card, false);
                ring.GetComponent<LayoutElement>().ignoreLayout = true;
                ring.sprite = Moonlight.OutlineSprite(27);
                ring.type = Image.Type.Sliced;
                ring.color = new Color(color.r, color.g, color.b, 0.5f);
                Ui.Stretch(ring.rectTransform);
            }
            var k = Ui.Text(card, kicker, 18f, color);
            k.fontWeight = FontWeight.SemiBold;
            k.characterSpacing = 14f;
            k.gameObject.AddComponent<LayoutElement>().preferredHeight = 27f;
            var b = Ui.Text(card, big, small == null ? 45f : 27f, Moonlight.Text, Moonlight.Heading);
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = small == null ? 60f : 39f;
            if (small != null)
            {
                var sm = Ui.Text(card, small, 21f, Moonlight.Soft);
                sm.textWrappingMode = TextWrappingModes.Normal;
                sm.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
            }
        }

        private static void Header(RectTransform parent, string title, string code, string hint, string status = null)
        {
            var heading = Ui.Text(parent, title, 36f, Moonlight.Text, Moonlight.Heading);
            heading.fontWeight = FontWeight.Medium;
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            heading.overflowMode = TextOverflowModes.Overflow;
            Ui.PinTopLeft(heading.rectTransform, new Vector2(42f, -33f), new Vector2(heading.GetPreferredValues().x, 78f));
            float x = 42f + heading.GetPreferredValues().x + 24f;
            if (code != null)
            {
                var pill = Moonlight.Panel("Code", parent, 27, Moonlight.PanelFill).rectTransform;
                var label = Ui.Text(pill, code, 21f, Moonlight.Moon, Moonlight.Heading);
                label.characterSpacing = 8f;
                label.alignment = TextAlignmentOptions.Center;
                Ui.Stretch(label.rectTransform);
                float width = label.GetPreferredValues().x + 42f;
                Ui.PinTopLeft(pill, new Vector2(x, -45f), new Vector2(width, 54f));
                x += width + 24f;
            }
            var text = Ui.Text(parent, hint, 21f, Moonlight.Muted);
            Ui.PinTopLeft(text.rectTransform, new Vector2(x, -33f), new Vector2(1100f, 78f));
            if (status != null)
            {
                var right = Ui.Text(parent, status, 21f, Moonlight.Soft);
                right.alignment = TextAlignmentOptions.MidlineRight;
                right.rectTransform.anchorMin = right.rectTransform.anchorMax = right.rectTransform.pivot = Vector2.one;
                right.rectTransform.anchoredPosition = new Vector2(-42f, -33f);
                right.rectTransform.sizeDelta = new Vector2(700f, 78f);
            }
        }

        // ---------- actions ----------

        private static void ToggleReady()
        {
            if (Lobby.State?.Me is { } me)
            {
                _ = Lobby.Ready(!me.Ready);
            }
        }

        /// <summary>X: the next instrument of the player's profile; Orange: the next difficulty.</summary>
        private static void CyclePart(bool instrument)
        {
            if (LobbyPlay.Me is not { } player || Lobby.State?.Me is not { Ready: false }) return;
            var profile = player.Profile;
            if (instrument)
            {
                var all = LobbyPlay.Instruments(profile);
                profile.CurrentInstrument = all[(Array.IndexOf(all, profile.CurrentInstrument) + 1) % all.Length];
            }
            else
            {
                var all = LobbyPlay.Difficulties(profile.CurrentInstrument);
                profile.CurrentDifficulty = all[(Array.IndexOf(all, profile.CurrentDifficulty) + 1) % all.Length];
            }
            LobbyPlay.SendPart();
        }

        private void OpenPicker()
        {
            if (Lobby.State?.Me is not { Ready: false } || _picker != null) return;
            if (_pushed)
            {
                Navigator.Instance.PopScheme();
                _pushed = false;
            }
            _content.gameObject.SetActive(false);
            _picker = LobbySongPicker.Open(_root, () =>
            {
                _picker = null;
                _shown = null; // rebuild, which also brings the lobby's controls back
            });
        }

        internal static string PartName(Lobby.Player p)
        {
            if (p.Instrument == null) return "Choosing a part";
            string inst = Enum.TryParse(p.Instrument, out Instrument i) ? i.ToLocalizedName() : p.Instrument;
            string diff = Enum.TryParse(p.Difficulty, out Difficulty d) ? d.ToLocalizedName() : p.Difficulty;
            return $"{inst} · {diff}";
        }

        private static string PartOnly(Lobby.Player p) =>
            Enum.TryParse(p.Instrument, out Instrument i) ? i.ToLocalizedName().ToLowerInvariant() : "part";
    }

    /// <summary>Layout helpers shared by the lobby screens.</summary>
    internal static class Ui
    {
        public const float Column4 = (1920f - 84f - 3 * 21f) / 4f;
        public static readonly Color Gold = new(0.949f, 0.788f, 0.298f);

        public static Color? Hex(string hex) =>
            hex != null && ColorUtility.TryParseHtmlString(hex, out var c) ? c : null;

        public static string Escape(string text) => text.Replace("<", "<​");

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, TMP_FontAsset font = null)
        {
            var label = Moonlight.Label("Text", parent, font ? font : Moonlight.Body, size, color);
            label.richText = false;
            label.text = text;
            return label;
        }

        public static void Centered(RectTransform parent, string text)
        {
            var label = Text(parent, text, 24f, Moonlight.Muted);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
        }

        /// <summary>A rounded button: moon cream (primary) or outlined.</summary>
        public static RectTransform Pill(Transform parent, string text, bool primary, Action click)
        {
            var pill = Moonlight.Panel(text, parent, 34, primary ? Moonlight.Moon : Color.clear);
            pill.raycastTarget = true;
            if (!primary)
            {
                var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                ring.transform.SetParent(pill.transform, false);
                ring.sprite = Moonlight.OutlineSprite(34);
                ring.type = Image.Type.Sliced;
                ring.color = new Color(0.627f, 0.686f, 1f, 0.35f);
                ring.raycastTarget = false;
                Stretch(ring.rectTransform);
            }
            pill.gameObject.AddComponent<Button>().onClick.AddListener(() => click());
            Moonlight.HoverWash(pill, 34);
            var label = Text(pill.transform, text, 22.5f, primary ? Moonlight.Night : Moonlight.Text);
            label.fontWeight = FontWeight.Bold;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return pill.rectTransform;
        }

        // The lobby rebuilds often (every loading step), so each cover loads once
        // shortcut: never emptied; fine for a library of a few hundred online songs, add a size cap if it grows past that
        private static readonly Dictionary<string, Texture> Covers = new();

        /// <summary>Shows the song's album art over the panel once it's loaded.</summary>
        public static async void LoadCover(Image holder, SongEntry song)
        {
            var art = new GameObject("Art", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            art.transform.SetParent(holder.transform, false);
            art.raycastTarget = false;
            art.enabled = false;
            art.uvRect = new Rect(0f, 0f, 1f, -1f); // covers load upside down, as in the library
            Stretch(art.rectTransform);
            if (!Covers.TryGetValue(song.ActualLocation, out var texture))
            {
                using var image = await UniTask.RunOnThreadPool(song.LoadAlbumData);
                texture = image?.LoadTexture(false);
                Covers[song.ActualLocation] = texture;
            }
            if (art == null || texture == null) return;
            art.texture = texture;
            art.enabled = true;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static void PinTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static void PinLeft(RectTransform rect, float left, float size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, 0f);
            rect.sizeDelta = new Vector2(size, size);
        }

        public static void Center(RectTransform rect, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>From the top of the parent, inset left and right.</summary>
        public static void Inset(RectTransform rect, float top, float left, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-left - right, -top);
        }
    }
}
