using System.Collections;
using TMPro;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using System;

namespace YARG.Gameplay.HUD
{
    public enum CountdownDisplayMode
    {
        Disabled,
        Measures,
        Seconds
    }

    public class CountdownDisplay : GameplayBehaviour
    {
        private const float FADE_ANIM_LENGTH = 0.5f;
        private const double HIDE_DELAY = 1;

        public static CountdownDisplayMode DisplayStyle;

        [SerializeField]
        private Image _backgroundCircle;
        [SerializeField]
        private TextMeshProUGUI _countdownText;
        [SerializeField]
        private Image _progressBar;

        [Space]
        [SerializeField]
        private CanvasGroup _canvasGroup;

        private Coroutine _currentCoroutine;

        private bool _displayActive;
        private int _displayedCountdownValue = int.MinValue;
        private TextMeshProUGUI _caption;

        // Moonlight (board NextSoloCountdown): a dark disc, a cream ring emptying as the gap passes, the count in
        // Unbounded, and what it counts underneath ("Get ready" in the last one)
        protected override void GameplayAwake()
        {
            _backgroundCircle.color = new Color(0.024f, 0.04f, 0.11f, 0.88f);
            _progressBar.color = Menu.Moonlight.Moon;
            if (Menu.Moonlight.Heading != null) _countdownText.font = Menu.Moonlight.Heading;
            _countdownText.color = Menu.Moonlight.Moon;
            _caption = Menu.Moonlight.Label("Caption", _backgroundCircle.transform.parent, Menu.Moonlight.Body, 20f, Menu.Moonlight.Text);
            _caption.fontWeight = FontWeight.Bold;
            _caption.alignment = TextAlignmentOptions.Top;
            var circle = _backgroundCircle.rectTransform;
            var rect = _caption.rectTransform;
            rect.anchorMin = rect.anchorMax = circle.anchorMin;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = circle.anchoredPosition + new Vector2(0f, -circle.rect.height * (1f - circle.pivot.y) - 8f);
            rect.sizeDelta = new Vector2(360f, 30f);
        }

        public void UpdateCountdown(double countdownLength, double endTime)
        {
            if (DisplayStyle == CountdownDisplayMode.Disabled)
            {
                return;
            }

            double currentTime = GameManager.SongTime;
            double timeRemaining = endTime - currentTime;
            if (timeRemaining < 0)
            {
                return;
            }
            bool shouldDisplay = timeRemaining > HIDE_DELAY + FADE_ANIM_LENGTH;

            if (GameManager.IsPractice)
            {
                double sectionStartTime = GameManager.PracticeManager.TimeStart;
                if (currentTime <= sectionStartTime)
                {
                    // Do not show a countdown before the start of a practice section
                    // where all of the notes before that section are removed for practice stats
                    shouldDisplay = false;
                }
            }

            ToggleDisplay(shouldDisplay);

            if (!gameObject.activeSelf)
            {
                return;
            }

            switch (DisplayStyle)
            {
                case CountdownDisplayMode.Seconds:
                {
                    SetCountdownValue((int) Math.Ceiling(timeRemaining));
                    break;
                }
                case CountdownDisplayMode.Measures:
                {
                    var syncTrack = GameManager.Chart.SyncTrack;
                    // This is floored to snap the end time to the start of the measure
                    double endMeasure = Math.Floor(syncTrack.GetMeasurePosition(endTime));
                    double currentMeasure = syncTrack.GetMeasurePosition(currentTime);
                    int remainingMeasures = (int) Math.Ceiling(endMeasure - currentMeasure);
                    SetCountdownValue(remainingMeasures);
                    break;
                }
            }

            _progressBar.fillAmount = (float) (timeRemaining / countdownLength);
        }

        public void ForceReset()
        {
            StopCurrentCoroutine();

            _canvasGroup.alpha = 0f;
            gameObject.SetActive(true);
            _displayActive = false;
            _displayedCountdownValue = int.MinValue;
        }

        private void SetCountdownValue(int value)
        {
            if (_displayedCountdownValue == value)
            {
                return;
            }

            _displayedCountdownValue = value;
            _countdownText.SetText(value.ToString());
            if (_caption != null)
            {
                _caption.text = value <= 1 ? "Get ready"
                    : DisplayStyle == CountdownDisplayMode.Measures ? "Measures until you're back in" : "Seconds until you're back in";
            }
        }

        private void ToggleDisplay(bool isActive)
        {
            if (isActive == _displayActive)
            {
                return;
            }

            _displayActive = isActive;

            StopCurrentCoroutine();

            if (isActive)
            {
                _canvasGroup.alpha = 0f;
                gameObject.SetActive(true);

                _currentCoroutine = StartCoroutine(ShowCoroutine());
            }
            else
            {
                if (_canvasGroup.alpha == 0f)
                {
                    // Do not animate a fade out if this is already invisible
                    gameObject.SetActive(false);
                    return;
                }

                _currentCoroutine = StartCoroutine(HideCoroutine());
            }
        }

        private IEnumerator ShowCoroutine()
        {
            // Fade in
            yield return _canvasGroup
                .DOFade(1f, FADE_ANIM_LENGTH)
                .WaitForCompletion();
        }

        private IEnumerator HideCoroutine()
        {
            // Fade out
            yield return _canvasGroup
                .DOFade(0f, FADE_ANIM_LENGTH)
                .WaitForCompletion();

            gameObject.SetActive(false);
            _currentCoroutine = null;
        }

        private void StopCurrentCoroutine()
        {
            if (_currentCoroutine != null)
            {
                StopCoroutine(_currentCoroutine);
                _currentCoroutine = null;
            }
        }
    }
}