using System;
using TMPro;
using UnityEngine;
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

            // Left edge of the highway near the strikeline, nudged a little further left
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
                _highwayRenderer.GetTrackPositionScreenSpace(highwayIndex, 0.5f, 1.0f)?.AddY(ExtraTopElementOffset)
                ?? _hiddenPosition;
            _topElementContainer.position = topPosition;
        }

        private void UpdateCenterHud(int highwayIndex)
        {
            var trackPositionScreenSpace =
                _highwayRenderer.GetTrackPositionScreenSpace(highwayIndex, 0.5f, CENTER_ELEMENT_DEPTH);
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
                _textNotifications.SetActive(true);
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

        public void ShowPlayerName(YargPlayer player)
        {
            _playerNameDisplay.ShowPlayer(player);
        }

        public void ForceReset()
        {
            _textNotifications.SetActive(true);

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
