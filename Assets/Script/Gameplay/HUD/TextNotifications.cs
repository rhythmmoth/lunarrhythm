using DG.Tweening;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Helpers.Extensions;
using YARG.Settings;

namespace YARG.Gameplay.HUD
{
    public enum NoteStreakFrequencyMode
    {
        Frequent,
        Sparse,
        Disabled
    }

    public enum VocalStreakFrequencyMode
    {
        Frequent,
        Sparse,
        Disabled
    }

    public class TextNotifications : MonoBehaviour
    {
        [SerializeField]
        private float _animLength = 2f;
        [SerializeField]
        private float _animBaseToPeakInterval = 0.167f;
        [SerializeField]
        private float _animPeakToValleyInterval = 0.167f;
        [SerializeField]
        private float _animPeakScale = 1.1f;
        [SerializeField]
        private float _animValleyScale = 1f;

        private float _animHoldInterval => _animLength - 2f * (_animBaseToPeakInterval + _animPeakToValleyInterval);

        [SerializeField]
        private TextMeshProUGUI _text;
        [SerializeField]
        private RectTransform _containerRect;
        [SerializeField]
        private Image _notificationBackground;
        [SerializeField]
        private Color _defaultColor;
        [SerializeField]
        private Color _starpowerColor;
        [SerializeField]
        private Color _grooveColor;
        [SerializeField]
        private bool _isVocals;

        private int _streak;
        private int _nextStreakCount;

        private Coroutine _coroutine;

        private readonly TextNotificationQueue _notificationQueue = new();

        public bool ShouldShowNoteStreakNotification =>
                (!_isVocals && SettingsManager.Settings.NoteStreakFrequency.Value != NoteStreakFrequencyMode.Disabled)
            || (_isVocals && SettingsManager.Settings.VocalStreakFrequency.Value != VocalStreakFrequencyMode.Disabled);

        private readonly TextNotificationType[] _highPriorityNotifications = {
            TextNotificationType.FullCombo,
            TextNotificationType.StrongFinish
        };

        private Sequence _animationSequence => DOTween.Sequence()
                .Append(DOTween.Sequence()
                    .Append(_containerRect
                        .DOScale(_animPeakScale, _animBaseToPeakInterval)
                        .SetEase(Ease.OutCirc))
                    .Append(_containerRect

                        .DOScale(_animValleyScale, _animPeakToValleyInterval)
                        .SetEase(Ease.InOutSine))
                    .AppendInterval(_animHoldInterval))
                .Append(DOTween.Sequence()
                    .Append(_containerRect
                        .DOScale(_animPeakScale, _animPeakToValleyInterval)
                        .SetEase(Ease.InOutSine))
                    .Append(_containerRect
                        .DOScale(0f, _animBaseToPeakInterval)
                        .SetEase(Ease.InCirc)));


        private void OnEnable()
        {
            _text.text = string.Empty;
            _coroutine = null;
            _containerRect.localScale = Vector3.zero;
            _containerRect.gameObject.SetActive(true);
            _notificationBackground.gameObject.SetActive(true);

            // The Moonlight board's callouts: plain moon-cream Unbounded, no badge behind
            // (the hit judgement prefab shares this script and keeps its own look)
            if (name != "Text Notifications") return;
            _notificationBackground.enabled = false;
            if (YARG.Menu.Moonlight.Heading != null) _text.font = YARG.Menu.Moonlight.Heading;
            _text.fontWeight = FontWeight.Medium;
            _text.fontStyle = FontStyles.Normal;
            _text.fontSize = 33f;
            _text.color = YARG.Menu.Moonlight.Moon;

            // So the letters read over the visualizer's moon and lights: a dark edge on each letter, and a soft patch of
            // night behind the words (shade, not a badge)
            var material = _text.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, YARG.Menu.Moonlight.Night);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 1f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            if (_shade == null)
            {
                _shade = new GameObject("Shade", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                _shade.transform.SetParent(_text.transform.parent, false);
                _shade.transform.SetSiblingIndex(_text.transform.GetSiblingIndex());
                _shade.sprite = SoftSprite;
                _shade.color = YARG.Menu.Moonlight.Night.WithAlpha(0.8f);
                _shade.raycastTarget = false;
            }
            FitShade();
        }

        private Image _shade;
        private static Sprite _softSprite;

        /// <summary>A soft oval, dark in the middle and fading to nothing at the edge.</summary>
        private static Sprite SoftSprite
        {
            get
            {
                if (_softSprite != null) return _softSprite;
                const int w = 128, h = 64;
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                        float a = 1f - Mathf.SmoothStep(0.25f, 1f, Mathf.Sqrt(dx * dx + dy * dy));
                        pixels[y * w + x] = new Color32(255, 255, 255, (byte) (a * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                return _softSprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            }
        }

        // The shade sits behind the words, sized to them
        private void FitShade()
        {
            if (_shade == null) return;
            bool show = !string.IsNullOrEmpty(_text.text);
            _shade.enabled = show;
            if (!show) return;
            var text = _text.rectTransform;
            var shade = _shade.rectTransform;
            shade.anchorMin = text.anchorMin;
            shade.anchorMax = text.anchorMax;
            shade.pivot = text.pivot;
            shade.anchoredPosition = text.anchoredPosition;
            shade.localScale = text.localScale;
            shade.sizeDelta = text.sizeDelta;
            // Grow it from the text's own box to the words' width plus a soft margin
            float extraX = _text.preferredWidth + 140f - text.rect.width, extraY = 120f - text.rect.height;
            shade.sizeDelta += new Vector2(extraX, extraY);
        }

        private void OnDisable()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
            }

            _animationSequence.Kill();
            _notificationBackground.gameObject.SetActive(false);
            _containerRect.gameObject.SetActive(false);
        }

        public void ShowNewHighScore()
        {
            ShowNotification(TextNotificationType.NewHighScore);
        }

        public void ShowFullCombo()
        {
            ShowNotification(TextNotificationType.FullCombo);
        }

        public void ShowStrongFinish()
        {
            ShowNotification(TextNotificationType.StrongFinish);
        }

        public void ShowHotStart()
        {
            ShowNotification(TextNotificationType.HotStart);
        }

        public void ShowBassGroove()
        {
            ShowNotification(TextNotificationType.BassGroove);
        }

        public void ShowStarPowerReady()
        {
            ShowNotification(TextNotificationType.StarPowerReady);
        }

        // All of these notifications imply that the player got an AWESOME rating. Avoid displaying that if one of these is already queued.
        private TextNotificationType[] incompatibleNotifications = new[]
        {
                TextNotificationType.FullCombo,
                TextNotificationType.StrongFinish,
                TextNotificationType.HotStart,
                TextNotificationType.PhraseStreak
         };

        public void ShowVocalPhraseResult(string result, int streak)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (Array.IndexOf(incompatibleNotifications, _notificationQueue.Current ?? TextNotificationType.VocalPhraseResult) != -1)
            {
                return;
            }

            _notificationQueue.Enqueue(new TextNotification(TextNotificationType.VocalPhraseResult, result));
        }

        public void ShowNotification(TextNotificationType notificationType)
        {
            var allowSuppress = Array.IndexOf(_highPriorityNotifications, notificationType) == -1;

            if (allowSuppress && !isActiveAndEnabled)
            {
                return;
            }

            _notificationQueue.Enqueue(new TextNotification(notificationType));
        }

        public void UpdateNoteStreak(int streak)
        {
            // Don't build up notifications during a solo
            if (!isActiveAndEnabled)
            {
                return;
            }

            // Only push to the queue if there is a change to the streak
            if (streak == _streak)
            {
                return;
            }

            // If the streak is less than before, then reset
            if (streak < _streak || _streak == 0)
            {
                _nextStreakCount = 0;
                NextStreakNotification();
            }

            // Update the streak
            _streak = streak;

            // Queue the note streak notification
            if (_streak >= _nextStreakCount)
            {
                if (!ShouldShowNoteStreakNotification)
                {
                    return;
                }

                var type = _isVocals ? TextNotificationType.PhraseStreak : TextNotificationType.NoteStreak;
                _notificationQueue.Enqueue(new TextNotification(type, _nextStreakCount));

                NextStreakNotification();
            }
        }

        private void Update()
        {
            // Never update this if text notifications are disabled
            if (SettingsManager.Settings.DisableTextNotifications.Value && !_isVocals)
            {
                return;
            }

            if (_coroutine == null && _notificationQueue.Count > 0)
            {
                var textNotification = _notificationQueue.Dequeue();

                // Set the color of the text background image based on the notifcation type
                _notificationBackground.color = GetBackgroundColor(textNotification.Type);

                _coroutine = StartCoroutine(ShowNextNotification(textNotification.Text));
            }
        }

        private IEnumerator ShowNextNotification(string notificationText)
        {
            _text.text = notificationText;
            FitShade();
            yield return _animationSequence.WaitForCompletion();

            _text.text = string.Empty;
            FitShade();
            _coroutine = null;
        }

        private void NextStreakNotification()
        {
            if (_isVocals)
            {
                NextPhraseStreakNotification();
            }
            else
            {
                NextNoteStreakNotification();
            }
        }

        private void NextNoteStreakNotification()
        {
            if (SettingsManager.Settings.NoteStreakFrequency.Value == NoteStreakFrequencyMode.Disabled)
            {
                _nextStreakCount = int.MaxValue;
                return;
            }

            switch (_nextStreakCount)
            {
                case 0:
                    _nextStreakCount = 50;
                    break;
                case 50:
                    _nextStreakCount = 100;
                    break;
                case >= 100 when SettingsManager.Settings.NoteStreakFrequency.Value == NoteStreakFrequencyMode.Frequent:
                    _nextStreakCount += 100;
                    break;
                case 100 when SettingsManager.Settings.NoteStreakFrequency.Value == NoteStreakFrequencyMode.Sparse:
                    _nextStreakCount = 250;
                    break;
                case >= 250 when SettingsManager.Settings.NoteStreakFrequency.Value == NoteStreakFrequencyMode.Sparse:
                    _nextStreakCount += 250;
                    break;
            }
        }

        private void NextPhraseStreakNotification()
        {
            if (SettingsManager.Settings.VocalStreakFrequency.Value == VocalStreakFrequencyMode.Disabled)
            {
                _nextStreakCount = int.MaxValue;
                return;
            }

            switch (_nextStreakCount)
            {
                case 0:
                    _nextStreakCount = 5;
                    break;
                case 5:
                    _nextStreakCount = 10;
                    break;
                case >= 10 when SettingsManager.Settings.VocalStreakFrequency.Value == VocalStreakFrequencyMode.Frequent:
                    _nextStreakCount += 10;
                    break;
                case 10 when SettingsManager.Settings.VocalStreakFrequency.Value == VocalStreakFrequencyMode.Sparse:
                    _nextStreakCount = 25;
                    break;
                case >= 25 when SettingsManager.Settings.VocalStreakFrequency.Value == VocalStreakFrequencyMode.Sparse:
                    _nextStreakCount += 25;
                    break;
            }
        }

        public void ForceReset()
        {
            _notificationQueue.Clear();
            _nextStreakCount = 0;
            _streak = 0;
        }

        public void SetActive(bool active)
        {
            _containerRect.gameObject.SetActive(active);
        }

        private Color GetBackgroundColor(TextNotificationType type)
        {
            return type switch
            {
                TextNotificationType.FullCombo => _starpowerColor,
                TextNotificationType.BassGroove => _grooveColor,
                TextNotificationType.StarPowerReady => _starpowerColor,
                _ => _defaultColor,
            };
        }
    }
}