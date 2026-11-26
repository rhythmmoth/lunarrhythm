using System;
using UnityEngine;
using YARG.Core.Chart;
using YARG.Gameplay.Player;

namespace YARG.Gameplay.Visuals
{
    public class BeatlineElement : TrackElement<TrackPlayer>
    {
        private const float WEAK_BEAT_SCALE   = 0.03f;
        private const float STRONG_BEAT_SCALE = 0.05f;
        private const float MEASURE_SCALE     = 0.07f;

        private const float WEAK_BEAT_ALPHA   = 0.3f;
        private const float STRONG_BEAT_ALPHA = 0.4f;
        private const float MEASURE_ALPHA     = 0.6f;

        [SerializeField]
        private MeshRenderer _meshRenderer;

        public Beatline BeatlineRef;

        public override double ElementTime => BeatlineRef.Time;

        protected override void InitializeElement()
        {
            transform.localPosition = Vector3.zero;

            float yScale;
            float alpha;

            switch (BeatlineRef.Type)
            {
                case BeatlineType.Measure:
                    yScale = MEASURE_SCALE;
                    alpha = MEASURE_ALPHA;
                    break;
                case BeatlineType.Strong:
                    yScale = STRONG_BEAT_SCALE;
                    alpha = STRONG_BEAT_ALPHA;
                    break;
                case BeatlineType.Weak:
                    yScale = WEAK_BEAT_SCALE;
                    alpha = WEAK_BEAT_ALPHA;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(BeatlineRef.Type));
            }

            var cachedTransform = _meshRenderer.transform;
            cachedTransform.localScale = cachedTransform.localScale.WithY(yScale);

            var material = _meshRenderer.material;
            // Upscroll's flipped highway hides beat lines drawn before the transparent queue's middle (found by
            // trying queues in game: hidden at 2990, shown at 3010; downscroll shows them at the material's 2975)
            // shortcut: the cause isn't pinned down; if upscroll hides other flat pieces, look at what draws at 3000
            if (FlatHighway.IsUpscroll(Player.Player)) material.renderQueue = 3001;
            var color = material.color;
            // On a themed 2D highway, in the look's second colour: white lines vanish on a pale look
            var preset = Player.Player?.HighwayPreset;
            if (FlatHighway.Enabled && FlatHighway.IsThemed(preset))
            {
                color = YARG.Helpers.Extensions.ColorExtensions.ToUnityColor(preset.BackgroundBaseColor2);
            }
            color.a = alpha;
            material.color = color;
        }

        protected override void UpdateElement()
        {
        }

        protected override void HideElement()
        {
        }
    }
}