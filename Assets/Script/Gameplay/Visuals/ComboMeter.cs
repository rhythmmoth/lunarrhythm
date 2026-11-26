using TMPro;
using UnityEngine;
using YARG.Core.Game;
using YARG.Helpers.UI;

namespace YARG.Gameplay.Visuals
{
    public class ComboMeter : MonoBehaviour
    {
        private static readonly int _spriteIndexProperty = Shader.PropertyToID("_SpriteIndex");
        private static readonly int _multiplierColorProperty = Shader.PropertyToID("_MultiplierColor");

        [SerializeField]
        private TextMeshPro _multiplierText;
        [SerializeField]
        private MeshRenderer _comboMesh;

        [Header("FC Ring")]
        [SerializeField]
        private MeshRenderer _ringMesh;

        [SerializeField]
        private Material _fcRingMaterial;
        [SerializeField]
        private Material _noFcRingMaterial;

        [Header("Preset Colors")]
        [SerializeField]
        private Color _defaultPresetColor;
        [SerializeField]
        private Color _customPresetColor;
        [SerializeField]
        private Color _soloTapsPresetColor;

        private TextMeshPro[] _textCache;

        public void Initialize(EnginePreset preset, int maxMultiplier, bool isMultiplayer, bool upscroll = false, bool compact = false)
        {
            _multiplierText.enabled = false;
            _multiplierText.text = string.Empty;
            _textCache = MultiplierTextHelper.CreateMultiplierTextCache(maxMultiplier, _multiplierText, isMultiplayer);

            Color color;

            // Right now Solo Taps is the only default preset that has a different color. Add more here if needed.
            if (preset == EnginePreset.SoloTaps)
            {
                color = _soloTapsPresetColor;
            }
            else if (EnginePreset.Defaults.Contains(preset))
            {
                color = _defaultPresetColor;
            }
            else
            {
                color = _customPresetColor;
            }

            _comboMesh.material.SetColor(_multiplierColorProperty, color);

            if (FlatHighway.Enabled) MakeFlat(upscroll, compact);
        }

        // The 2D highway (board NextHighway): just the multiplier, lying flat to the left of the strike line, and the
        // Overdrive meter's label on the right (the meter itself is StarpowerBar's)
        private void MakeFlat(bool upscroll, bool compact)
        {
            FlatHighwayArt.HideModel(transform);
            // Beside the strike line; in a group, small and inside the column, just under the strike line
            transform.localPosition = compact ? new Vector3(-0.9f, 0f, -0.3f) : new Vector3(-1.3f, 0f, 0.06f);
            TextMeshPro sample = null;
            foreach (var text in _textCache)
            {
                if (text == null) continue; // the cache leaves slots empty for multipliers it never shows
                sample ??= text;
                LayFlat(text, TextAlignmentOptions.Right);
                text.transform.localScale *= compact ? 1.1f : 2.2f; // seen from above it reads small: the board's ×4 is big
                text.color = new Color(0.953f, 0.91f, 0.8f, 1f);
                if (upscroll) Unmirror(text.transform);
                if (compact) text.alignment = TextAlignmentOptions.Left;
            }
            if (compact) return; // no "OVERDRIVE" label in a group: the meter's place in the column says it

            var label = Instantiate(_multiplierText, transform.parent);
            LayFlat(label, TextAlignmentOptions.Left);
            label.name = "Overdrive Label";
            label.text = "OVERDRIVE";
            // The multiplier's on-screen scale (unstretched), whatever the parents' scales; a third of its size
            if (sample != null)
            {
                // (the sample may already be flipped for upscroll: its sizes, not its signs)
                var world = new Vector3(Mathf.Abs(sample.transform.lossyScale.x), Mathf.Abs(sample.transform.lossyScale.y),
                    Mathf.Abs(sample.transform.lossyScale.z));
                var parent = label.transform.parent.lossyScale;
                // Even proportions: measured in game, halving its width (as LayFlat does) squashed it
                label.transform.localScale = new Vector3(world.y / parent.x, world.y / parent.y, world.z / parent.z);
                label.fontSize = sample.fontSize * 0.22f;
                // The base text's font is a condensed one; the multipliers' reads right
                label.font = sample.font;
                label.fontSharedMaterial = sample.fontSharedMaterial;
            }
            label.characterSpacing = 3f;
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            label.color = new Color(0.953f, 0.91f, 0.8f, 1f);
            // Just right of the meter's foot, level with the strike line
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.transform.localPosition = new Vector3(1.22f, 0.02f, -0.12f);
            label.enabled = true;
            if (upscroll) Unmirror(label.transform);
        }

        // Upscroll flips the highway top to bottom; flipping the text back keeps it readable
        private static void Unmirror(Transform text) =>
            text.localScale = new Vector3(text.localScale.x, -text.localScale.y, text.localScale.z);

        private static void LayFlat(TextMeshPro text, TextAlignmentOptions alignment)
        {
            var t = text.transform;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localPosition = new Vector3(0f, 0.02f, 0f);
            // The flat highway stretches everything across the lanes: undo it, so text keeps its shape
            t.localScale = new Vector3(t.localScale.x / FlatHighway.LaneStretch, t.localScale.y, t.localScale.z);
            text.alignment = alignment;
        }

        public void SetCombo(int multiplier, int displayMultiplier, int maxMultiplier, int combo, bool codaStarted)
        {
            _multiplierText.enabled = false;

            // No multiplier text or updates while coda is active
            if (codaStarted)
            {
                _comboMesh.material.SetFloat(_spriteIndexProperty, 0);
                return;
            }

            if (displayMultiplier > 1)
            {
                _multiplierText = _textCache[displayMultiplier - 2];
                _multiplierText.enabled = true;
            }

            int index = combo % 10;
            if (combo != 0 && index == 0)
            {
                index = 10;
            }
            else if (multiplier == maxMultiplier)
            {
                index = 10;
            }

            _comboMesh.material.SetFloat(_spriteIndexProperty, index);
        }

        public void SetFullCombo(bool isFc)
        {
            _ringMesh.sharedMaterial = isFc ? _fcRingMaterial : _noFcRingMaterial;
        }
    }
}
