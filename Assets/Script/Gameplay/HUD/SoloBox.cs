using System;
using System.Collections;
using Cysharp.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Engine;
using YARG.Localization;

namespace YARG.Gameplay.HUD
{
    public class SoloBox : MonoBehaviour
    {
        [SerializeField]
        private Image           _soloBox;
        [SerializeField]
        private TextMeshProUGUI _soloTopText;
        [SerializeField]
        private TextMeshProUGUI _soloBottomText;
        [SerializeField]
        private TextMeshProUGUI _soloFullText;
        [SerializeField]
        private CanvasGroup     _soloBoxCanvasGroup;

        [Space]
        [SerializeField]
        private Sprite _soloSpriteNormal;
        [SerializeField]
        private Sprite _soloSpritePerfect;
        [SerializeField]
        private Sprite _soloSpriteMessy;

        [SerializeField]
        private TMP_ColorGradient _soloGradientNormal;
        [SerializeField]
        private TMP_ColorGradient _soloGradientPerfect;
        [SerializeField]
        private TMP_ColorGradient _soloGradientMessy;

        private bool _soloEnded;
        private SoloSection _solo;

        private bool _showingForPreview;
        private int _displayedHitPercent = int.MinValue;
        private int _displayedNotesHit = int.MinValue;
        private int _displayedNoteCount = int.MinValue;

        private int HitPercent => Mathf.FloorToInt((float) _solo.NotesHit / _solo.NoteCount * 100f);

        // ---------- the Moonlight pill (board NextSoloCountdown), drawn inside the old box so its fades still apply ----------

        private static readonly Color Gold = new(0.949f, 0.788f, 0.298f);
        private Image _chip, _fill;
        private TextMeshProUGUI _chipText, _big, _small;

        private void BuildPill()
        {
            if (_chip != null) return;
            // The old look goes; the box keeps its place, its canvas group and its show/hide
            _soloBox.enabled = false;
            _soloTopText.enabled = _soloBottomText.enabled = _soloFullText.enabled = false;

            var pill = Menu.Moonlight.Panel("Moonlight Solo", _soloBox.transform, 27, new Color(0.024f, 0.04f, 0.11f, 0.88f)).rectTransform;
            pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0.5f, 0.5f);
            pill.anchoredPosition = Vector2.zero;
            pill.sizeDelta = new Vector2(300f, 64f);
            _chip = Menu.Moonlight.Panel("Chip", pill, 17, Menu.Moonlight.Moon);
            var chip = _chip.rectTransform;
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 0.5f);
            chip.anchoredPosition = new Vector2(12f, 0f);
            chip.sizeDelta = new Vector2(112f, 36f);
            _chipText = Menu.Moonlight.Label("Chip Text", chip, Menu.Moonlight.Heading, 15f, new Color(0.043f, 0.063f, 0.149f));
            _chipText.alignment = TextAlignmentOptions.Center;
            _chipText.characterSpacing = 8f;
            Stretch(_chipText.rectTransform);
            _big = Menu.Moonlight.Label("Percent", pill, Menu.Moonlight.Heading, 24f, Menu.Moonlight.Text);
            Place(_big.rectTransform, new Vector2(138f, -8f), new Vector2(90f, 32f));
            _small = Menu.Moonlight.Label("Notes", pill, Menu.Moonlight.Body, 16f, Menu.Moonlight.Muted);
            _small.alignment = TextAlignmentOptions.MidlineRight;
            Place(_small.rectTransform, new Vector2(214f, -10f), new Vector2(72f, 28f));
            var track = Menu.Moonlight.Panel("Bar", pill, 2, Menu.Moonlight.Keycap);
            Place(track.rectTransform, new Vector2(138f, -46f), new Vector2(148f, 4f));
            _fill = Menu.Moonlight.Panel("Fill", track.rectTransform, 2, Menu.Moonlight.Moon);
            _fill.rectTransform.anchorMin = Vector2.zero;
            _fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            _fill.rectTransform.offsetMin = _fill.rectTransform.offsetMax = Vector2.zero;
        }

        private void SetPill(string chip, Color chipColor, string big, string small, float fill)
        {
            BuildPill();
            _chipText.text = chip;
            _chip.color = chipColor;
            _fill.color = chipColor;
            _big.text = big;
            _small.text = small;
            _fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        private Coroutine _currentCoroutine;

        public void StartSolo(SoloSection solo)
        {
            // Don't even bother if the solo has no points
            if (solo.NoteCount == 0) return;

            _solo = solo;
            _soloEnded = false;
            _displayedHitPercent = int.MinValue;
            _displayedNotesHit = int.MinValue;
            _displayedNoteCount = int.MinValue;
            gameObject.SetActive(true);

            StopCurrentCoroutine();

            _currentCoroutine = StartCoroutine(ShowCoroutine());
        }

        private IEnumerator ShowCoroutine()
        {
            _soloFullText.text = string.Empty;
            _soloBox.sprite = _soloSpriteNormal;

            // Set some dummy text
            _soloTopText.text = "0%";
            _soloBottomText.SetTextFormat("0/{0}", _solo.NoteCount);
            SetPill("SOLO", Menu.Moonlight.Moon, "0%", $"0 / {_solo.NoteCount}", 0f);

            // Fade in the box
            yield return _soloBoxCanvasGroup
                .DOFade(1f, 0.25f)
                .WaitForCompletion();
        }

        private void Update()
        {
            if (_soloEnded || _showingForPreview) return;

            var hitPercent = HitPercent;
            if (_displayedHitPercent != hitPercent)
            {
                _displayedHitPercent = hitPercent;
                _soloTopText.SetTextFormat("{0}%", hitPercent);
            }

            if (_displayedNotesHit != _solo.NotesHit || _displayedNoteCount != _solo.NoteCount)
            {
                _displayedNotesHit = _solo.NotesHit;
                _displayedNoteCount = _solo.NoteCount;
                _soloBottomText.SetTextFormat("{0}/{1}", _displayedNotesHit, _displayedNoteCount);
                SetPill("SOLO", Menu.Moonlight.Moon, $"{hitPercent}%", $"{_displayedNotesHit} / {_displayedNoteCount}",
                    (float) _displayedNotesHit / Mathf.Max(1, _displayedNoteCount));
            }
        }

        public void EndSolo(int soloBonus, Action endCallback)
        {
            StopCurrentCoroutine();

            _currentCoroutine = StartCoroutine(HideCoroutine(soloBonus, endCallback));
        }

        public void ForceReset()
        {
            StopCurrentCoroutine();
            _soloEnded = true;

            _soloBox.gameObject.SetActive(false);
            _currentCoroutine = null;
            _solo = null;
        }

        private IEnumerator HideCoroutine(int soloBonus, Action endCallback)
        {
            _soloEnded = true;

            // Hide the top and bottom text
            _soloTopText.text = string.Empty;
            _soloBottomText.text = string.Empty;

            // Get the correct gradient and color
            var (sprite, gradient) = HitPercent switch
            {
                >= 100 => (_soloSpritePerfect, _soloGradientPerfect),
                >= 60  => (_soloSpriteNormal, _soloGradientNormal),
                _      => (_soloSpriteMessy, _soloGradientMessy),
            };
            _soloBox.sprite = sprite;
            _soloFullText.colorGradientPreset = gradient;

            // Display final hit percentage
            _soloFullText.SetTextFormat("{0}%", HitPercent);
            bool perfect = HitPercent >= 100;
            SetPill(perfect ? "PERFECT" : "SOLO", perfect ? Gold : Menu.Moonlight.Moon, $"{HitPercent}%",
                $"{_solo.NotesHit} / {_solo.NoteCount}", 1f);

            yield return new WaitForSeconds(1f);

            // Show performance text
            string performanceKey = HitPercent switch
            {
                > 100 => "How",
                  100 => "Perfect",
                >= 95 => "Awesome",
                >= 90 => "Great",
                >= 80 => "Good",
                >= 70 => "Solid",
                   69 => "Nice",
                >= 60 => "Okay",
                >= 0  => "Messy",
                <  0  => "How",
            };

            _soloFullText.text = Localize.Key("Gameplay.Solo.Performance", performanceKey);
            if (!perfect) _small.text = _soloFullText.text; // "Great", "Messy"...

            yield return new WaitForSeconds(1f);

            // Show point bonus; Festival scoring has none, so there's nothing to show
            if (soloBonus > 0)
            {
                _soloFullText.text = Localize.KeyFormat("Gameplay.Solo.PointsResult", soloBonus);

                yield return new WaitForSeconds(1f);
            }

            // Fade out the box
            yield return _soloBoxCanvasGroup
                .DOFade(0f, 0.25f)
                .WaitForCompletion();

            _soloBox.gameObject.SetActive(false);
            _currentCoroutine = null;
            _solo = null;

            endCallback?.Invoke();
        }

        private void StopCurrentCoroutine()
        {
            if (_currentCoroutine != null)
            {
                StopCoroutine(_currentCoroutine);
                _currentCoroutine = null;
            }
        }

        public void PreviewForEditMode(bool on)
        {
            if (on && !_soloBox.gameObject.activeSelf)
            {
                _soloBox.gameObject.SetActive(true);

                // Set preview solo box properties
                _soloFullText.text = string.Empty;
                _soloBox.sprite = _soloSpriteNormal;
                _soloTopText.text = "50%";
                _soloBottomText.text = "50/100";
                _soloBoxCanvasGroup.alpha = 1f;

                _showingForPreview = true;
            }
            else if (!on && _showingForPreview)
            {
                _soloBox.gameObject.SetActive(false);
                _showingForPreview = false;
            }
        }
    }
}
