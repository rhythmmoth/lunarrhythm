using System.Collections.Generic;
using UnityEngine;
using YARG.Helpers.Extensions;
using YARG.Themes;
using Color = System.Drawing.Color;

namespace YARG.Gameplay.Visuals
{
    public class Fret : MonoBehaviour, IThemeBindable<ThemeFret>
    {
        private static readonly int _fade          = Shader.PropertyToID("Fade");
        private static readonly int _emissionColor = Shader.PropertyToID("_EmissionColor");

        private static readonly int _hit       = Animator.StringToHash("Hit");
        private static readonly int _cymbalHit = Animator.StringToHash("CymbalHit");
        private static readonly int _miss      = Animator.StringToHash("Miss");
        private static readonly int _openMiss  = Animator.StringToHash("OpenMiss");
        private static readonly int _pressed   = Animator.StringToHash("Pressed");
        private static readonly int _sustain   = Animator.StringToHash("Sustain");

        // If we want info to be copied over when we copy the prefab,
        // we must make them SerializeFields.
        [field: SerializeField]
        [field: HideInInspector]
        public ThemeFret ThemeBind { get; set; }

        private readonly List<Material> _topMaterials   = new();
        private readonly List<Material> _innerMaterials = new();

        private bool _hasPressedParam;
        private bool _hasSustainParam;
        private bool _hasOpenMissTrigger;

        // These need to be saved since the colors can now change during play
        // They are saved as Unity colors to avoid having to repeatedly convert
        // when transitioning between active and inactive states
        private UnityEngine.Color _originalUnityTopColor;
        private UnityEngine.Color _originalUnityInnerColor;
        private UnityEngine.Color _originalEmissionColor;

        // TODO: Consider making this customizable or perhaps just a desaturated and dimmed version of the base color
        private UnityEngine.Color _inactiveColor = new(0.321f, 0.321f, 0.321f, 1.0f);

        private bool _active             = true;
        private bool _colorChangeEnabled = false;
        private bool _fadeDirection      = true;
        // True is pulsing, false is fading
        private bool  _pulseOrFade  = true;
        private float _fadeDuration = 0.25f;
        private float _fadeStartTime;
        private float _fadeAmount = 0.0f;

        public enum AnimType
        {
            CorrectNormal,
            CorrectHard,
            CorrectSoft,
            TooHard,
            TooSoft,
        }

        public void Initialize(Color top, Color inner, Color particles, Color openParticles)
        {
            _originalUnityTopColor = top.ToUnityColor();
            _originalUnityInnerColor = inner.ToUnityColor();
            _originalEmissionColor = top.ToUnityColor() * 11.5f;

            // Set the top material color
            foreach (var material in ThemeBind.GetColoredMaterials())
            {
                material.color = _originalUnityTopColor;
                material.SetColor(_emissionColor, _originalEmissionColor);
                _topMaterials.Add(material);
            }

            // Set the inner material color
            foreach (var material in ThemeBind.GetInnerColoredMaterials())
            {
                material.color = _originalUnityInnerColor;
                _innerMaterials.Add(material);
            }

            // Set the particle colors
            ThemeBind.HitEffect.SetColor(particles.ToUnityColor());
            ThemeBind.OpenHitEffect.SetColor(openParticles.ToUnityColor());
            ThemeBind.SustainEffect.SetColor(particles.ToUnityColor());
            ThemeBind.PressedEffect.SetColor(particles.ToUnityColor());

            // See if certain parameters exist
            _hasPressedParam = ThemeBind.Animator.HasParameter(_pressed);
            _hasSustainParam = ThemeBind.Animator.HasParameter(_sustain);
            _hasOpenMissTrigger = ThemeBind.Animator.HasParameter(_openMiss);

            if (FlatHighway.Enabled && _flatMark == null && FlatHighwayArt.Ready) MakeFlat();
        }

        // The 2D highway's strike line mark: the worn note shape in the lane colour (its outline, or a faint fill for
        // gem, star and crescent), filled while held; and the worn hit effect (board NextHighwayCosmetics)
        private MeshRenderer _flatMark, _flatFill;
        private float _flatPressed, _flatFlash;
        private MeshRenderer[] _hitPieces;
        private string _hitEffect;
        private float _pillWidth, _hitAge = -1f;
        private static readonly UnityEngine.Color Cream = new(0.953f, 0.91f, 0.8f, 1f);
        private static readonly float[] MoteOffsets = { -0.25f, 0.05f, 0.3f };

        private void MakeFlat()
        {
            var footprint = FlatHighwayArt.HideModel(transform);
            var player = FlatHighwayArt.PlayerOf(this);
            var art = FlatHighwayArt.Shape(player?.NoteShape);
            _pillWidth = Mathf.Max(footprint.size.x * 0.75f, 0.1f) * FlatHighway.NoteScale;
            float width = Mathf.Min(_pillWidth * art.NoteSize.x / 56f, FlatHighway.MaxNoteWidth), depth = 0.2f * art.NoteSize.y / 20f * FlatHighway.NoteScale;
            // A dark socket under the mark, so it reads as pressed into the track (board NextHighwayDepth, E)
            FlatHighwayArt.Piece(transform, "Flat Socket", art.Note, new UnityEngine.Color(0f, 0f, 0f, 0.45f), width, depth, 0.006f, 1);
            _flatFill = FlatHighwayArt.Piece(transform, "Flat Fill", art.Note, UnityEngine.Color.clear, width, depth, 0.008f, 2);
            _flatMark = FlatHighwayArt.Piece(transform, "Flat Mark", art.FilledMark ? art.Note : art.NoteRing,
                _originalUnityTopColor.WithAlpha(art.FilledMark ? 0.45f : 1f), width, depth, 0.01f, 3);

            _hitEffect = player?.HitEffect is "ripple-hit" or "stardust-hit" ? player.HitEffect : "burst-hit";
            var texture = _hitEffect switch
            {
                "ripple-hit" => FlatHighwayArt.CircleRing,
                "stardust-hit" => FlatHighwayArt.Dot,
                _ => FlatHighwayArt.Glow,
            };
            _hitPieces = new MeshRenderer[_hitEffect == "stardust-hit" ? 3 : 1];
            for (int i = 0; i < _hitPieces.Length; i++)
            {
                _hitPieces[i] = FlatHighwayArt.Piece(transform, "Flat Hit Effect", texture, UnityEngine.Color.clear, 0.01f, 0.01f, 0.014f, 4);
                _hitPieces[i].enabled = false;
            }
        }

        private void UpdateFlat()
        {
            if (_flatFill == null) return;
            // Held: mostly filled; a hit flashes it full and fades back
            _flatFlash = Mathf.MoveTowards(_flatFlash, 0f, Time.deltaTime * 5f);
            float fill = Mathf.Max(_flatPressed * 0.55f, _flatFlash);
            FlatHighwayArt.SetColor(_flatFill, _originalUnityTopColor.WithAlpha(fill));
            UpdateHitEffect();
        }

        // A size given in screen pixels at the strike line (the pill mark is about 80 × 28 at 1080p), as a flat piece's scale
        private Vector3 Pixels(float w, float h) => new(_pillWidth * w / 80f, 0.2f * h / 28f, 1f);

        // Burst: a glow swelling and fading. Ripple: a thin ring spreading out. Stardust: three motes rising up the track.
        private void UpdateHitEffect()
        {
            if (_hitAge < 0f) return;
            _hitAge += Time.deltaTime;
            bool done = true;
            for (int i = 0; i < _hitPieces.Length; i++)
            {
                var piece = _hitPieces[i];
                float t;
                var colour = _originalUnityTopColor;
                switch (_hitEffect)
                {
                    case "ripple-hit":
                        t = _hitAge / 0.45f;
                        piece.transform.localScale = Pixels(Mathf.Lerp(24f, 90f, t), Mathf.Lerp(24f, 90f, t));
                        break;
                    case "stardust-hit":
                        t = (_hitAge - i * 0.06f) / 0.5f;
                        float size = 14f - 2f * i;
                        piece.transform.localScale = Pixels(size, size);
                        piece.transform.localPosition = new Vector3(_pillWidth * MoteOffsets[i], 0.014f,
                            Mathf.Lerp(0f, 0.4f, Mathf.Clamp01(t)));
                        if (i != 1) colour = Cream;
                        break;
                    default:
                        t = _hitAge / 0.35f;
                        float glow = Mathf.Lerp(40f, 110f, 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t)));
                        piece.transform.localScale = Pixels(glow, glow);
                        break;
                }
                piece.enabled = t >= 0f && t < 1f;
                if (t < 1f) done = false;
                if (piece.enabled) FlatHighwayArt.SetColor(piece, colour.WithAlpha(1f - t));
            }
            if (done) _hitAge = -1f;
        }

        public void Update()
        {
            UpdateColor();
            UpdateFlat();
        }

        public void SetPressed(bool pressed)
        {
            SetPressed(pressed, pressed ? 1f : 0f);
        }

        public void SetPressedDrum(bool pressed, AnimType animType)
        {
            // Set the inner portion of the fret
            SetPressed(pressed, pressed ? GetFretInnerBrightnessMultiplier(animType) : 0f);
        }

        public void WhitenFretColor()
        {
            foreach (var material in _topMaterials)
            {
                material.color = UnityEngine.Color.white;
                material.SetColor(_emissionColor, UnityEngine.Color.white);
            }

            foreach (var material in _innerMaterials)
            {
                material.color = UnityEngine.Color.white;
                material.SetColor(_emissionColor, UnityEngine.Color.white);
            }

            foreach (var light in ThemeBind.HitEffect.EffectLights)
            {
                light.IsBrightened = true;
            }

            foreach (var particle in ThemeBind.HitEffect.EffectParticles)
            {
                particle.BrightenColor();
            }
        }

        public void RestoreFretColor()
        {
            foreach (var material in _topMaterials)
            {
                material.color = _originalUnityTopColor;
                material.SetColor(_emissionColor, _originalEmissionColor);
            }

            foreach (var material in _innerMaterials)
            {
                material.color = _originalUnityInnerColor;
                material.SetColor(_emissionColor, _originalEmissionColor);
            }

            foreach (var light in ThemeBind.HitEffect.EffectLights)
            {
                light.IsBrightened = false;
            }

            foreach (var particle in ThemeBind.HitEffect.EffectParticles)
            {
                particle.RestoreColor();
            }
        }

        private float GetFretInnerBrightnessMultiplier(AnimType animType)
        {
            // Normal and hard should both be 1f
            return AnimType.CorrectSoft == animType ? 0f : 1f;
        }

        public void SetPressed(bool pressed, float value)
        {
            _flatPressed = pressed ? value : 0f;
            foreach (var material in _innerMaterials)
            {
                material.SetFloat(_fade, value);
            }

            if (_hasPressedParam)
            {
                ThemeBind.Animator.SetBool(_pressed, pressed);
            }

            if (pressed)
            {
                ThemeBind.PressedEffect.Play();
            }
            else
            {
                ThemeBind.PressedEffect.Stop();
            }
        }

        public void PlayHitAnimation()
        {
            _flatFlash = 1f;
            if (_hitPieces != null) _hitAge = 0f;
            ThemeBind.Animator.SetTrigger(_hit);
        }

        public void PlayCymbalHitAnimation()
        {
            ThemeBind.Animator.SetTrigger(_cymbalHit);
        }

        public void PlayHitParticles()
        {
            if (_hitPieces != null) return; // the 2D highway's hit effect cosmetic stands in for the theme's
            ThemeBind.HitEffect.Play();
        }

        public void PlayOpenHitParticles()
        {
            if (_hitPieces != null) return;
            ThemeBind.OpenHitEffect.Play();
        }

        public void PlayMissAnimation()
        {
            ThemeBind.Animator.SetTrigger(_miss);
        }

        public void PlayMissParticles()
        {
            ThemeBind.MissEffect.Play();
        }

        public void PlayOpenMissAnimation()
        {
            if (_hasOpenMissTrigger)
            {
                ThemeBind.Animator.SetTrigger(_openMiss);
            }
            else
            {
                PlayMissAnimation();
            }
        }

        public void PlayOpenMissParticles()
        {
            ThemeBind.OpenMissEffect.Play();
        }

        public void SetSustained(bool sustained)
        {
            if (sustained)
            {
                ThemeBind.SustainEffect.Play();
            }
            else
            {
                ThemeBind.SustainEffect.Stop();
            }

            if (_hasSustainParam)
            {
                ThemeBind.Animator.SetBool(_sustain, sustained);
            }
        }

        public void DimColor(bool fade = true)
        {
            _active = false;
            _fadeDirection = false;
            _colorChangeEnabled = true;
            _fadeAmount = 0.0f;

            if (fade)
            {
                FadeColor(_fadeDuration, false, false);
            }
            else
            {
                foreach (var material in _topMaterials)
                {
                    material.color = _inactiveColor;
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = _inactiveColor;
                }
            }
        }

        public void ResetColor(bool fade = false)
        {
            _active = true;
            _fadeDirection = true;
            _colorChangeEnabled = false;
            _fadeAmount = 0.0f;

            if (fade)
            {
                FadeColor(_fadeDuration, false, true);
            }
            else
            {
                foreach (var material in _topMaterials)
                {
                    material.color = _originalUnityTopColor;
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = _originalUnityInnerColor;
                }
            }
        }

        public void SetBreMode(bool breMode)
        {
            ThemeBind.SetBreMode(breMode);
        }

        /// <summary>
        /// Fades or pulses the fret color between the normal color and inactive color
        ///
        /// Note that the pulse happens only once per call, it does not continue indefinitely.
        /// </summary>
        /// <param name="duration">Length of time transition will take</param>
        /// <param name="pulse">Whether to pulse or to fade once</param>
        /// <param name="fadeDirection">True fades in, false fades out (default true)</param>
        public void FadeColor(float duration, bool pulse, bool fadeDirection = true)
        {
            if (_active && pulse)
            {
                // Can't pulse if the fret is already active
                return;
            }

            _pulseOrFade = pulse;
            _fadeDuration = duration;
            _fadeDirection = fadeDirection;
            _fadeStartTime = Time.time;
            _colorChangeEnabled = true;
            _fadeAmount = 0.0f;
        }

        // TODO: Investigate whether we should be using a MaterialPropertyBlock to set the color
        //  instead of setting the color directly so that draw call batching is possible
        //  (I think it doesn't actually matter much since there's only 10 of these materials active at a time,
        //   but every bit helps, I guess?)
        public void UpdateColor()
        {
            if (!_colorChangeEnabled)
            {
                return;
            }

            var rateAdjustment = 1; //_pulseOrFade ? 2 : 1;

            _fadeAmount += Time.deltaTime / (_fadeDuration / rateAdjustment);
            var fadeIntensity = _pulseOrFade ? _fadeAmount : ((Mathf.Cos(Mathf.PI * _fadeAmount) / 2) * -1) + 1;

            if (_fadeDirection)
            {
                // Fading in
                foreach (var material in _topMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_inactiveColor, _originalUnityTopColor, fadeIntensity);
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_inactiveColor, _originalUnityTopColor, fadeIntensity);
                }
            }
            else
            {
                // Fading out
                foreach (var material in _topMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_originalUnityTopColor, _inactiveColor, fadeIntensity);
                }

                foreach (var material in _innerMaterials)
                {
                    material.color = UnityEngine.Color.Lerp(_originalUnityTopColor, _inactiveColor, fadeIntensity);
                }
            }

            if (Time.time - _fadeStartTime >= _fadeDuration)
            {
                _colorChangeEnabled = false;
                _fadeAmount = 0.0f;

                if (_fadeDirection)
                {
                    // Fading in
                    foreach (var material in _topMaterials)
                    {
                        material.color = _originalUnityTopColor;
                    }

                    foreach (var material in _innerMaterials)
                    {
                        // Why was this _originalUnityTopColor?
                        material.color = _originalUnityInnerColor;
                    }
                }
                else
                {
                    // Fading out
                    foreach (var material in _topMaterials)
                    {
                        material.color = _inactiveColor;
                    }

                    foreach (var material in _innerMaterials)
                    {
                        material.color = _inactiveColor;
                    }
                }
            }
        }
    }
}