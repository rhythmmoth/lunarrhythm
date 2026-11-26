using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Engine;
using YARG.Gameplay.Visuals;
using YARG.Helpers.Extensions;
using YARG.Player;

namespace YARG.Gameplay.HUD
{
    public class TrackView : MonoBehaviour
    {

        [SerializeField]
        private RectTransform _highwayEditContainer;
        [SerializeField]
        private RectTransform _topElementContainer;
        [SerializeField]
        private RectTransform _centerElementContainer;
        [SerializeField]
        private RectTransform _scaleContainer;

        [Space]
        [SerializeField]
        private SoloBox _soloBox;
        [SerializeField]
        private TextNotifications _textNotifications;
        [SerializeField]
        private CountdownDisplay _countdownDisplay;
        [SerializeField]
        private PlayerNameDisplay _playerNameDisplay;


        private HighwayCameraRendering _highwayRenderer;
        private Vector3 _lastTrackPlayerPosition;

        private const float CENTER_ELEMENT_DEPTH = 0.35f;

        private DraggableHudElement _topDraggable;
        private DraggableHudElement _highwayDraggable;
        private RectTransform _topElementParentRect;
        private Canvas _highwayEditCanvas;
        private RectTransform _highwayEditParentRect;
        private bool _defaultsInitialized;

        private readonly Vector3 _hiddenPosition = new(-10000f, -10000f, 0f);
        private float ExtraTopElementOffset => 8f * Screen.height / 1000f;

        // Perfect/Good popup, just left of the bottom of the highway
        private const float JUDGEMENT_DEPTH = 0.05f;
        private const float JUDGEMENT_HOLD = 0.15f;
        private const float JUDGEMENT_FADE = 0.25f;
        private static readonly Color PERFECT_COLOR = new(1f, 0.84f, 0.25f);
        private static readonly Color GOOD_COLOR = new(0.6f, 0.85f, 1f);
        private static readonly Color MISS_COLOR = new(1f, 0.54f, 0.6f);

        private TextMeshProUGUI _judgement;
        private float _judgementShownAt = float.NegativeInfinity;
        private int _highwayIndex;

        public void Initialize(HighwayCameraRendering highwayRenderer)
        {
            _highwayRenderer = highwayRenderer;
            _topDraggable = _topElementContainer.GetComponent<DraggableHudElement>();
            _highwayDraggable = _highwayEditContainer.GetComponent<DraggableHudElement>();
            _topElementParentRect = _topElementContainer.parent as RectTransform;
            _highwayEditCanvas = _highwayEditContainer.GetComponentInParent<Canvas>();
            _highwayEditParentRect = _highwayEditContainer.parent as RectTransform;
            _defaultsInitialized = false;
            _highwayDraggable.PositionChanged += OnHighwayDraggablePositionChanged;
            _highwayDraggable.ScaleChanged += OnHighwayDraggableScaleChanged;
            _highwayRenderer.SetScaleMultiplier(_highwayDraggable.CurrentScale);

            _centerElementContainer.position = _hiddenPosition;

            // Built from the notification text so it uses the HUD's font
            _judgement = Instantiate(_textNotifications.GetComponentInChildren<TextMeshProUGUI>(true),
                _centerElementContainer.parent);
            _judgement.name = "Hit Judgement";
            _judgement.gameObject.SetActive(true);
            _judgement.enableAutoSizing = false;
            _judgement.fontSize = 30f;
            _judgement.alignment = TextAlignmentOptions.Right;
            _judgement.enableWordWrapping = false;
            _judgement.raycastTarget = false;
            _judgement.rectTransform.pivot = new Vector2(1f, 0.5f);
            _judgement.text = string.Empty;
        }

        public void ShowJudgement(bool perfect)
        {
            if (_judgement == null)
            {
                return;
            }

            _judgement.text = perfect ? "PERFECT" : "GOOD";
            _judgement.color = perfect ? PERFECT_COLOR : GOOD_COLOR;
            _judgementShownAt = Time.unscaledTime;
        }

        /// <summary>A missed note, or a press with no note there (it breaks the streak the same way).</summary>
        public void ShowMiss()
        {
            if (_judgement == null)
            {
                return;
            }

            _judgement.text = "MISS";
            _judgement.color = MISS_COLOR;
            _judgementShownAt = Time.unscaledTime;
        }

        private void Update()
        {
            if (_judgement == null || _highwayRenderer == null)
            {
                return;
            }

            float age = Time.unscaledTime - _judgementShownAt;
            float alpha = age < JUDGEMENT_HOLD ? 1f : Mathf.Clamp01(1f - (age - JUDGEMENT_HOLD) / JUDGEMENT_FADE);
            _judgement.alpha = alpha;
            if (alpha <= 0f)
            {
                return;
            }

            // Left edge of the highway near the strikeline, nudged a little further left; in a group (a name tag shows)
            // inside the player's own column instead, just over the strike line, so it never leaves the screen
            if (_tag != null)
            {
                _judgement.alignment = TextAlignmentOptions.Center;
                _judgement.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _judgement.transform.position = _highwayRenderer.GetTrackPositionScreenSpace(_highwayIndex, 0.5f, JUDGEMENT_DEPTH * 3f)
                    is { } middle ? (Vector3) middle : _hiddenPosition;
                return;
            }
            var edge = _highwayRenderer.GetTrackPositionScreenSpace(_highwayIndex, 0f, JUDGEMENT_DEPTH);
            _judgement.transform.position = edge.HasValue
                ? (Vector3) (edge.Value + new Vector2(-12f * Screen.height / 1000f, 0f))
                : _hiddenPosition;
        }

        public void UpdateHUDPosition(int highwayIndex, int highwayCount)
        {
            // Scale ui according to number of highways,
            // 1 highway = 1.0 scale, 2 highways = 0.9 scale, 3 highways = 0.8 scale, etc, minimum of 0.5
            var newScale = Math.Max(0.5f, 1.1f - (0.1f * highwayCount));
            _scaleContainer.localScale = _scaleContainer.localScale.WithX(newScale).WithY(newScale);

            if (!_defaultsInitialized)
            {
                SetupDefaultHudPositions();
                _defaultsInitialized = true;
            }

            _highwayIndex = highwayIndex;
            UpdateHudElements(highwayIndex);
        }

        private void UpdateHudElements(int highwayIndex)
        {
            // Apply highway offset first so top/center positions are calculated from the current track position.
            UpdateTrackPosition(highwayIndex);
            UpdateTopHud(highwayIndex);
            UpdateCenterHud(highwayIndex);
        }

        private void SetupDefaultHudPositions()
        {
            // Compute highway default at center (offset 0)
            _highwayRenderer.SetHorizontalOffsetPx(0);
            _highwayDraggable.SetDefaultPosition(GetHighwayDefaultPosition());

            SetHighwayOffsetX(_highwayDraggable.CurrentPosition.x);
            UpdateTopDefaultPosition();
        }

        private void UpdateTopDefaultPosition()
        {
            _topDraggable.SetDefaultPosition(GetTopDefaultPosition());
        }

        private Vector2 GetTopDefaultPosition()
        {
            var topScreenPosition =
                _highwayRenderer.GetTrackPositionScreenSpaceRaised(0, 0.5f, 1.0f)?.AddY(ExtraTopElementOffset)
                ?? _hiddenPosition;
            return _topElementParentRect.ScreenPointToLocalPoint(topScreenPosition) ?? _hiddenPosition;
        }

        private Vector2 GetHighwayDefaultPosition()
        {
            var trackBounds = _highwayRenderer.GetTrackBoundsScreenSpaceRaised(0);
            return _highwayEditParentRect.ScreenPointToLocalPoint(trackBounds.center) ?? _hiddenPosition;
        }

        private void UpdateTopHud(int highwayIndex)
        {
            if (_topDraggable.HasCustomPosition)
            {
                return;
            }

            // Place top elements at 100% depth of the track, plus some extra amount above the track.
            var topPosition =
                _highwayRenderer.GetTrackPositionScreenSpaceUnflipped(highwayIndex, 0.5f, 1.0f)?.AddY(ExtraTopElementOffset)
                ?? _hiddenPosition;
            // The raised highway's far end is near the lyrics: popups stay low enough to clear them
            if (topPosition.x > -9000f) topPosition.y = Mathf.Min(topPosition.y, Screen.height * 0.82f);
            _topElementContainer.position = topPosition;
        }

        private void UpdateCenterHud(int highwayIndex)
        {
            var trackPositionScreenSpace =
                _highwayRenderer.GetTrackPositionScreenSpaceUnflipped(highwayIndex, 0.5f, CENTER_ELEMENT_DEPTH);
            var centerPosition = trackPositionScreenSpace ?? _hiddenPosition;
            _centerElementContainer.transform.position = centerPosition;
        }

        // Keep the edit box sized to the track bounds and vertically centered to the track.
        private void UpdateTrackPosition(int highwayIndex)
        {
            bool hasCustomPosition = _highwayDraggable.HasCustomPosition;
            SetHighwayOffsetX(hasCustomPosition ? _highwayDraggable.CurrentPosition.x : 0f);

            var trackBounds = _highwayRenderer.GetTrackBoundsScreenSpace(highwayIndex);
            if (trackBounds == null)
            {
                _highwayEditContainer.position = _hiddenPosition;
                return;
            }

            //Set highway edit box size in canvas units
            float width = trackBounds.Value.width / _highwayEditCanvas.scaleFactor;
            float height = trackBounds.Value.height / _highwayEditCanvas.scaleFactor;
            _highwayEditContainer.sizeDelta = new Vector2(width, height);

            //Center the highway edit box on the highway
            var trackCenterScreenSpace = trackBounds.Value.center;
            var localCenter = _highwayEditParentRect.ScreenPointToLocalPoint(trackCenterScreenSpace);
            if (localCenter == null)
            {
                _highwayEditContainer.position = _hiddenPosition;
                return;
            }

            float targetX = hasCustomPosition
                ? _highwayDraggable.CurrentPosition.x
                : localCenter.Value.x;
            _highwayEditContainer.anchoredPosition = new Vector2(targetX, localCenter.Value.y);
        }

        private void OnHighwayDraggablePositionChanged(Vector2 position)
        {
            UpdateHudElements(0);
            UpdateTopDefaultPosition();
        }

        private void OnHighwayDraggableScaleChanged(float scale)
        {
            _highwayRenderer.SetScaleMultiplier(scale);
            UpdateTopHud(0);
            UpdateCenterHud(0);
            UpdateTrackPosition(0);
            UpdateTopDefaultPosition();
        }

        private void SetHighwayOffsetX(float xOffsetLocal)
        {
            float offsetPx = xOffsetLocal * _highwayEditCanvas.scaleFactor;
            _highwayRenderer.SetHorizontalOffsetPx(offsetPx);
        }

        public void UpdateCountdown(double countdownLength, double endTime)
        {
            _countdownDisplay.UpdateCountdown(countdownLength, endTime);
        }

        public void StartSolo(SoloSection solo)
        {
            _soloBox.StartSolo(solo);

            // No text notifications during the solo
            _textNotifications.SetActive(false);
        }

        public void EndSolo(int soloBonus)
        {
            _soloBox.EndSolo(soloBonus, () =>
            {
                // Show text notifications again
                _textNotifications.SetActive(!_quietPopups);
            });
        }

        public void UpdateNoteStreak(int streak)
        {
            _textNotifications.UpdateNoteStreak(streak);
        }

        public void ShowNewHighScore()
        {
            _textNotifications.ShowNewHighScore();
        }

        public void ShowFullCombo()
        {
            _textNotifications.ShowFullCombo();
        }

        public void ShowHotStart()
        {
            _textNotifications.ShowHotStart();
        }

        public void ShowBassGroove()
        {
            _textNotifications.ShowBassGroove();
        }

        public void ShowStarPowerReady()
        {
            _textNotifications.ShowStarPowerReady();
        }

        public void ShowStrongFinish()
        {
            _textNotifications.ShowStrongFinish();
        }

        // ---------- a group's name tag (boards MHighways, NextOnlineHud) ----------

        private RectTransform _tag;
        private CanvasGroup _tagGroup;
        private Image _tagEdge;
        private TextMeshProUGUI _tagName, _tagScore, _tagSub;
        private string _tagPart, _tagNote;
        private int _tagShownScore = -1, _tagShownCombo = -1;
        private bool _tagShownFc;
        private bool _quietPopups;
        private static readonly Color TagGold = new(0.949f, 0.788f, 0.298f);

        /// <summary>In a group (online or local), the column's name tag above the highway: name and score, part and
        /// streak. Yours is outlined. It replaces the name that shows in the middle of the highway.</summary>
        public void ShowTag(YargPlayer player, bool mine)
        {
            if (_tag == null) BuildTag();
            _tagName.text = player.Profile.Name;
            _tagName.color = mine ? Menu.Moonlight.Moon : Menu.Moonlight.Text;
            _tagEdge.enabled = mine;
            // Popups are for the player's own highway: others' would cover their name tags
            _quietPopups = !mine;
            _textNotifications.SetActive(!_quietPopups);
            _tagPart = player.Profile.CurrentInstrument switch
            {
                Instrument.FiveFretGuitar => "Lead",
                Instrument.FiveFretBass => "Bass",
                Instrument.FiveFretRhythm => "Drums",
                Instrument.Keys => "Vocals",
                var other => other.ToString(),
            };
            _tagShownScore = -1;
            UpdateTag(0, 0, true);
        }

        /// <summary>The tag's live numbers; only redrawn when they change.</summary>
        public void UpdateTag(int score, int combo, bool fc)
        {
            if (_tag == null || (score == _tagShownScore && combo == _tagShownCombo && fc == _tagShownFc)) return;
            _tagShownScore = score;
            _tagShownCombo = combo;
            _tagShownFc = fc;
            _tagScore.text = score.ToString("N0");
            _tagSub.text = _tagNote ?? (_tagPart + " · " + (fc && combo > 0 ? $"FC {combo}" : $"{combo} streak"));
            _tagSub.color = _tagNote != null ? Menu.Moonlight.Danger : fc && combo > 0 ? TagGold : Menu.Moonlight.Muted;
        }

        /// <summary>A note in place of the streak ("Reconnecting…", "Left"), with the column dimmed; null clears it.</summary>
        public void SetTagNote(string note)
        {
            if (_tag == null || note == _tagNote) return;
            _tagNote = note;
            _tagGroup.alpha = note != null ? 0.55f : 1f;
            _tagShownScore = -1;
            UpdateTag(_tagShownScore, _tagShownCombo, _tagShownFc);
        }

        private void BuildTag()
        {
            var panel = Menu.Moonlight.Panel("Name Tag", _topElementContainer, 15, new Color(0.024f, 0.04f, 0.11f, 0.82f));
            _tag = panel.rectTransform;
            _tag.anchorMin = _tag.anchorMax = new Vector2(0.5f, 0.5f);
            _tag.pivot = new Vector2(0.5f, 0f);
            _tag.sizeDelta = new Vector2(240f, 62f);
            _tag.anchoredPosition = new Vector2(0f, 34f); // clear of the popups just above the highway
            _tagGroup = panel.gameObject.AddComponent<CanvasGroup>();
            _tagGroup.blocksRaycasts = false;
            _tagEdge = Menu.Moonlight.Panel("Edge", _tag, 15, new Color(0.953f, 0.91f, 0.8f, 0.6f));
            _tagEdge.sprite = Menu.Moonlight.OutlineSprite(15);
            _tagEdge.rectTransform.anchorMin = Vector2.zero;
            _tagEdge.rectTransform.anchorMax = Vector2.one;
            _tagEdge.rectTransform.offsetMin = _tagEdge.rectTransform.offsetMax = Vector2.zero;

            _tagName = Menu.Moonlight.Label("Name", _tag, Menu.Moonlight.Body, 20f, Menu.Moonlight.Text);
            _tagName.fontWeight = FontWeight.Bold;
            _tagName.richText = false;
            PlaceTagText(_tagName.rectTransform, 9f, 26f, 0f, 92f);
            _tagScore = Menu.Moonlight.Label("Score", _tag, Menu.Moonlight.Body, 20f, Menu.Moonlight.Text);
            _tagScore.fontWeight = FontWeight.Bold;
            _tagScore.alignment = TextAlignmentOptions.MidlineRight;
            PlaceTagText(_tagScore.rectTransform, 9f, 26f, 100f, 0f);
            _tagSub = Menu.Moonlight.Label("Part", _tag, Menu.Moonlight.Body, 16f, Menu.Moonlight.Muted);
            PlaceTagText(_tagSub.rectTransform, 35f, 20f, 0f, 0f);
        }

        // A line of the tag: its top, height, and how far in from the left and right edges (14 px padding added)
        private void PlaceTagText(RectTransform rect, float top, float height, float fromLeft, float fromRight)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(14f + fromLeft, -top - height);
            rect.offsetMax = new Vector2(-14f - fromRight, -top);
        }

        public void ShowPlayerName(YargPlayer player)
        {
            _playerNameDisplay.ShowPlayer(player);
        }

        public void ForceReset()
        {
            _textNotifications.SetActive(!_quietPopups);

            _soloBox.ForceReset();
            _textNotifications.ForceReset();
            _countdownDisplay.ForceReset();
        }

        private void OnDestroy()
        {
            _highwayDraggable.PositionChanged -= OnHighwayDraggablePositionChanged;
            _highwayDraggable.ScaleChanged -= OnHighwayDraggableScaleChanged;
        }
    }
}
