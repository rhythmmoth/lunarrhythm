using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;

namespace YARG.Menu.Main
{
    /// <summary>
    /// Title screen pill in the top left (MTitle): the signed-in player's picture inside their XP ring, name, level and
    /// alias. Clicking it (or Y) opens the profile popup.
    /// </summary>
    public class ProfileCard : MonoBehaviour
    {
        private TextMeshProUGUI _initial;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _level;
        private TextMeshProUGUI _detail;
        private Image _xp;
        private string _shown;
        private RawImage _picture;
        private static string _boardAvatar;
        private TMP_FontAsset _font;

        /// <summary>The last profile from the server (null until it answers, signed out or offline).</summary>
        public static Leaderboards.Profile Profile { get; private set; }
        private static bool _loading;

        /// <summary>Asks the server for the profile again (after equipping, say); the card follows.</summary>
        public static async UniTask Reload()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                var profile = await Leaderboards.GetProfile();
                if (profile != null) Profile = profile;
            }
            finally
            {
                _loading = false;
            }
        }

        public void OpenPopup()
        {
            if (Leaderboards.SignedIn && !AccountGate.PlayingOffline)
            {
                ProfilePopup.Open(transform.parent, _font);
            }
        }

        /// <summary>The signed-in player's picture path, from /lb/me or (older servers) the global board.</summary>
        public static string Avatar => Leaderboards.PlayerAvatar ?? _boardAvatar;

        /// <summary>
        /// Servers from before /lb/me sent the picture: the global board's own entry has it. Asked once per launch.
        /// </summary>
        private static async UniTaskVoid AvatarFromBoard()
        {
            var board = await Leaderboards.GetGlobal(1);
            _boardAvatar = board?.Me?.Avatar;
        }
        private string _pictureShown;

        public static ProfileCard Create(Transform parent, TMP_FontAsset font)
        {
            var rect = Moonlight.Panel("Profile", parent, 51, Moonlight.PanelFill).rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(42f, -36f);
            rect.sizeDelta = new Vector2(360f, 102f);

            // The XP ring (this level's progress) around the avatar disc
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            ring.transform.SetParent(rect, false);
            ring.sprite = Moonlight.ThinRing;
            ring.color = new Color(0.627f, 0.686f, 1f, 0.18f);
            ring.raycastTarget = false;
            Place(ring.rectTransform, 15f, 75f);
            var xp = new GameObject("XP", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            xp.transform.SetParent(ring.rectTransform, false);
            xp.sprite = Moonlight.ThinRing;
            xp.color = Moonlight.Moon;
            xp.type = Image.Type.Filled;
            xp.fillMethod = Image.FillMethod.Radial360;
            xp.fillOrigin = (int) Image.Origin360.Top;
            xp.fillAmount = 0f;
            xp.raycastTarget = false;
            Stretch(xp.rectTransform);
            var avatar = Moonlight.Panel("Avatar", ring.rectTransform, 29, new Color(0.227f, 0.145f, 0.439f));
            Center(avatar.rectTransform, 57f);

            var card = rect.gameObject.AddComponent<ProfileCard>();
            card._font = font;
            card._xp = xp;
            rect.GetComponent<Image>().raycastTarget = true;
            rect.gameObject.AddComponent<Button>().onClick.AddListener(card.OpenPopup);
            card._initial = Moonlight.Label("Initial", avatar.rectTransform, Moonlight.Heading ? Moonlight.Heading : font, 24f,
                Moonlight.Text);
            card._initial.alignment = TextAlignmentOptions.Center;
            Stretch(card._initial.rectTransform);
            // The Discord picture over the initial, once it loads
            avatar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            card._picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            card._picture.transform.SetParent(avatar.rectTransform, false);
            card._picture.raycastTarget = false;
            card._picture.enabled = false;
            Stretch(card._picture.rectTransform);

            card._name = Moonlight.Label("Name", rect, font, 27f, Moonlight.Text);
            card._name.fontStyle = FontStyles.Bold;
            card._name.richText = false; // a Discord name is anyone's text
            Line(card._name.rectTransform, 22f, 34f);
            card._level = Moonlight.Label("Level", rect, Moonlight.Heading ? Moonlight.Heading : font, 18f, Moonlight.Moon);
            Line(card._level.rectTransform, 22f, 34f);
            card._detail = Moonlight.Label("Detail", rect, font, 19f, Moonlight.Muted);
            card._detail.richText = false; // an alias is the team's text, but keep it plain like names
            Line(card._detail.rectTransform, 56f, 26f);
            card.Refresh();
            return card;
        }

        // Sign-in can change while the menu is open (from Settings), so keep the card in step
        private void Update() => Refresh();

        private bool _askedBoard;
        private bool _askedProfile;

        private void Refresh()
        {
            string avatar = Leaderboards.PlayerAvatar ?? _boardAvatar;
            if (_picture != null && avatar != _pictureShown)
            {
                _pictureShown = avatar;
                _picture.enabled = false;
                if (_pictureShown != null)
                {
                    YARG.Menu.MusicLibrary.LeaderboardCard.LoadAvatar(_pictureShown, _picture).Forget();
                }
            }
            if (avatar == null && !_askedBoard && Leaderboards.SignedIn && !AccountGate.PlayingOffline)
            {
                _askedBoard = true;
                AvatarFromBoard().Forget();
            }
            string name = Leaderboards.PlayerName;
            bool signedIn = !string.IsNullOrEmpty(name);
            // Asked once the player is signed in and online (at startup that's after the account check)
            if (signedIn && Profile == null && !_askedProfile && !AccountGate.PlayingOffline)
            {
                _askedProfile = true;
                Reload().Forget();
            }
            var profile = signedIn ? Profile : null;
            string detail = !signedIn ? "Not signed in"
                : AccountGate.PlayingOffline ? $"Playing offline · {Mathf.CeilToInt((float) Leaderboards.OfflineTimeLeft.TotalHours)} h left"
                : profile?.Look.Alias ?? "Signed in with Discord";
            string level = profile != null ? profile.Level.ToString() : "";
            if (_xp != null) _xp.fillAmount = profile != null && profile.Need > 0 ? Mathf.Clamp01(profile.Into / (float) profile.Need) : 0f;
            if (_name == null || (name == _shown && detail == _detail.text && level == _level.text))
            {
                return;
            }
            _shown = name;
            _name.text = signedIn ? name : "Not signed in";
            _initial.text = signedIn ? name[..1].ToUpperInvariant() : "?";
            _detail.text = detail;
            _detail.color = AccountGate.PlayingOffline ? Moonlight.Moon : Moonlight.Muted;
            // The level just after the name, as on the board
            _level.text = level;
            float nameWidth = Mathf.Min(_name.GetPreferredValues(_name.text).x, 200f);
            _level.rectTransform.offsetMin = new Vector2(104f + nameWidth + 9f, _level.rectTransform.offsetMin.y);
        }

        private static void Place(RectTransform rect, float left, float size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, 0f);
            rect.sizeDelta = new Vector2(size, size);
        }

        private static void Center(RectTransform rect, float size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
        }

        private static void Line(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(104f, -top - height);
            rect.offsetMax = new Vector2(-36f, -top);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
