using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using YARG.Core.Game;
using YARG.Helpers.Extensions;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG.Gameplay.Visuals
{
    public class TrackMaterial : MonoBehaviour
    {
        // TODO: MOST OF THIS CLASS IS TEMPORARY UNTIL THE TRACK TEXTURE SETTINGS ARE IN

        private static readonly int _scrollProperty         = Shader.PropertyToID("_Scroll");
        private static readonly int _starpowerStateProperty = Shader.PropertyToID("_Starpower_State");
        private static readonly int _starpowerTimeProperty  = Shader.PropertyToID("_Starpower_Start_Time");
        private static readonly int _wavinessProperty       = Shader.PropertyToID("_Waviness");

        private static readonly int _layer1ColorProperty = Shader.PropertyToID("_Layer_1_Color");
        private static readonly int _layer2ColorProperty = Shader.PropertyToID("_Layer_2_Color");
        private static readonly int _layer3ColorProperty = Shader.PropertyToID("_Layer_3_Color");
        private static readonly int _layer4ColorProperty = Shader.PropertyToID("_Layer_4_Color");

        private static readonly int _starPowerColorProperty = Shader.PropertyToID("_Starpower_Color");
        private static readonly int _starpowerTextureProperty = Shader.PropertyToID("_Starpower_Texture");

        private static readonly int _baseTextureProperty = Shader.PropertyToID("_Layer_2_Texture");
        private static readonly int _baseParallaxProperty = Shader.PropertyToID("_Layer_2_Parallax");
        private static readonly int _baseWavinessProperty = Shader.PropertyToID("_Layer_2_Wavy_Amount");
        private static readonly int _sidePatternProperty = Shader.PropertyToID("_Layer_4_Texture");
        private static readonly int _sideParallaxProperty = Shader.PropertyToID("_Layer_4_Parallax");
        private static readonly int _sideWavinessProperty = Shader.PropertyToID("_Layer_4_Wavy_Amount");

        private Texture _originalBaseTexture;
        private Texture _originalSidePattern;
        private float   _originalBaseParallax;
        private float   _originalSideParallax;
        private float   _originalBaseWaviness;
        private float   _originalSideWaviness;

        public struct Preset
        {
            public Color    Layer1;
            public Color    Layer2;
            public Color    Layer3;
            public Color    Layer4;
            public FileInfo BaseTexture;
            public FileInfo SidePattern;
            public float    BaseWaviness;
            public float    SideWaviness;

            public static Preset FromHighwayPreset(HighwayPreset preset, bool groove)
            {
                if (groove)
                {
                    return new Preset
                    {
                        Layer1 = preset.BackgroundGrooveBaseColor1.ToUnityColor(),
                        Layer2 = preset.BackgroundGrooveBaseColor2.ToUnityColor(),
                        Layer3 = preset.BackgroundGrooveBaseColor3.ToUnityColor(),
                        Layer4 = preset.BackgroundGroovePatternColor.ToUnityColor(),
                        BaseTexture = preset.BackgroundImage,
                        SidePattern = preset.SideImage,
                        BaseWaviness = preset.BaseWaviness,
                        SideWaviness = preset.SideWaviness
                    };
                }

                return new Preset
                {
                    Layer1 = preset.BackgroundBaseColor1.ToUnityColor(),
                    Layer2 = preset.BackgroundBaseColor2.ToUnityColor(),
                    Layer3 = preset.BackgroundBaseColor3.ToUnityColor(),
                    Layer4 = preset.BackgroundPatternColor.ToUnityColor(),
                    BaseTexture = preset.BackgroundImage,
                    SidePattern = preset.SideImage,
                    BaseWaviness = preset.BaseWaviness,
                    SideWaviness = preset.SideWaviness
                };
            }
        }

        private Preset _normalPreset;
        private Preset _groovePreset;

        private float _grooveState;
        private float GrooveState
        {
            get => _grooveState;
            set
            {
                _grooveState = value;

                _material.SetColor(_layer1ColorProperty,
                    Color.Lerp(_normalPreset.Layer1, _groovePreset.Layer1, value));
                _material.SetColor(_layer2ColorProperty,
                    Color.Lerp(_normalPreset.Layer2, _groovePreset.Layer2, value));
                _material.SetColor(_layer3ColorProperty,
                    Color.Lerp(_normalPreset.Layer3, _groovePreset.Layer3, value));
                _material.SetColor(_layer4ColorProperty,
                    Color.Lerp(_normalPreset.Layer4, _groovePreset.Layer4, value));

                _material.SetFloat(_wavinessProperty, value);
            }
        }

        [HideInInspector]
        public bool GrooveMode;

        private bool _starpowerMode;
        [HideInInspector]
        public bool StarpowerMode
        {
            get => _starpowerMode;
            // When going from false to true, also set the starpower time
            set
            {
                if (value && !_starpowerMode)
                {
                    _material.SetFloat(_starpowerTimeProperty, Time.time);
                }
                _starpowerMode = value;
            }
        }
        private GameManager _gameManager;

        public float StarpowerState
        {
            get => _material.GetFloat(_starpowerStateProperty);
            set => _material.SetFloat(_starpowerStateProperty, value);
        }

        [SerializeField]
        private MeshRenderer _trackMesh;

        [SerializeField]
        private MeshRenderer[] _trackTrims;

        private Material _material;
        private readonly List<Material> _trimMaterials = new();

        private Texture2D _baseTexture;
        private Texture2D _sidePattern;

        private const string BASE_TEXTURE_NAME = "baseTexture.png";
        private const string SIDE_PATTERN_NAME = "sidePattern.png";

        private void Awake()
        {
            // Get materials
            _material = _trackMesh.material;
            foreach (var trim in _trackTrims)
            {
                _trimMaterials.Add(trim.material);
            }

            _normalPreset = new()
            {
                Layer1 = FromHex("0F0F0F", 1f),
                Layer2 = FromHex("4B4B4B", 0.15f),
                Layer3 = FromHex("FFFFFF", 0f),
                Layer4 = FromHex("575757", 1f)
            };

            _groovePreset = new()
            {
                Layer1 = FromHex("000933", 1f),
                Layer2 = FromHex("23349C", 0.15f),
                Layer3 = FromHex("FFFFFF", 0f),
                Layer4 = FromHex("2C499E", 1f)
            };

            // If the original textures haven't been saved yet, save them now
            if (_originalBaseTexture == null)
            {
                _originalBaseTexture = _material.GetTexture(_baseTextureProperty);
                _originalBaseParallax = _material.GetFloat(_baseParallaxProperty);
                _originalBaseWaviness = _material.GetFloat(_baseWavinessProperty);
                _originalSidePattern = _material.GetTexture(_sidePatternProperty);
                _originalSideParallax = _material.GetFloat(_sideParallaxProperty);
                _originalSideWaviness = _material.GetFloat(_sideWavinessProperty);
            }
        }

        public void Initialize(HighwayPreset highwayPreset)
        {
            _material.SetColor(_starPowerColorProperty, highwayPreset.StarPowerColor.ToUnityColor() );
            _normalPreset = Preset.FromHighwayPreset(highwayPreset, false);
            _groovePreset = Preset.FromHighwayPreset(highwayPreset, true);

            // Waviness applies whether or not custom textures are used
            _material.SetFloat(_baseWavinessProperty, _normalPreset.BaseWaviness);
            _material.SetFloat(_sideWavinessProperty, _normalPreset.SideWaviness);

            SetTextures();

            if (FlatHighway.Enabled)
            {
                // The 2D highway: plain lanes, no textured or scrolling layers (the lane dividers are drawn
                // separately). Night unless a highway look is worn: then its base colour, and a streak shifts it
                // toward the look's pattern colour (not to the preset's own groove colours)
                var lanes = new Color(0.016f, 0.031f, 0.094f, 1f);
                var streak = Color.Lerp(lanes, new Color(0.106f, 0.165f, 0.42f, 1f), 0.5f);
                if (FlatHighway.IsThemed(highwayPreset))
                {
                    lanes = _normalPreset.Layer1.WithAlpha(1f);
                    streak = Color.Lerp(lanes, _normalPreset.Layer4.WithAlpha(1f), 0.35f);
                }
                _normalPreset.Layer1 = lanes;
                _normalPreset.Layer2 = _normalPreset.Layer3 = _normalPreset.Layer4 = Color.clear;
                _groovePreset.Layer1 = streak;
                _groovePreset.Layer2 = _groovePreset.Layer3 = _groovePreset.Layer4 = Color.clear;
                _material.SetFloat(_baseWavinessProperty, 0f);
                _material.SetFloat(_sideWavinessProperty, 0f);
                GrooveState = _grooveState;
                // Overdrive lights the lanes with a soft cream glow, not the 3D theme's swirls: the swirl patterns
                // themselves go (overdrive brightens them even with their colour layers cleared)
                _material.SetTexture(_baseTextureProperty, Texture2D.blackTexture);
                _material.SetTexture(_sidePatternProperty, Texture2D.blackTexture);
                _material.SetTexture(_starpowerTextureProperty, Texture2D.whiteTexture);
                _material.SetColor(_starPowerColorProperty, new Color(0.953f, 0.91f, 0.8f, 0.35f));
                MakeFlatLanes(lanes, streak);
            }
        }

        // The 2D highway's lanes: one flat piece in the look's colour over the track (whose shader shades its base
        // colour, so a look wouldn't show as itself), from below the screen's bottom edge to where notes fade in.
        // A streak shifts it to the streak colour and overdrive warms it toward cream.
        private MeshRenderer _flatLanes;
        private Color _flatLanesColor, _flatStreakColor;
        private static readonly Color FlatOverdrive = new(0.953f, 0.91f, 0.8f, 1f);

        private void MakeFlatLanes(Color lanes, Color streak)
        {
            _flatLanesColor = lanes;
            _flatStreakColor = streak;
            var player = GetComponentInParent<Player.TrackPlayer>();
            if (_flatLanes != null || player == null || !FlatHighwayArt.Ready) return;
            float near = Player.TrackPlayer.STRIKE_LINE_POS - 3f, far = player.ZeroFadePosition;
            _flatLanes = FlatHighwayArt.Piece(player.transform, "Flat Lanes", FlatHighwayArt.Solid, lanes,
                Player.TrackPlayer.TRACK_WIDTH, far - near, 0.001f, -10);
            _flatLanes.transform.localPosition = new Vector3(0f, 0.001f, (near + far) / 2f);
            // Depth (board NextHighwayDepth, E): light pooling at the strike line in the look's second colour (moonlight
            // without one), and the rails shading the lane edges
            var preset = player.Player?.HighwayPreset;
            var tint = FlatHighway.IsThemed(preset) ? preset.BackgroundBaseColor2.ToUnityColor() : new Color(0.627f, 0.686f, 1f);
            float top = Player.TrackPlayer.STRIKE_LINE_POS + 4f;
            var light = FlatHighwayArt.Piece(player.transform, "Flat Lane Light", FlatHighwayArt.LaneLight,
                Color.Lerp(tint, Color.white, 0.5f).WithAlpha(0.16f), Player.TrackPlayer.TRACK_WIDTH, top - near, 0.0013f, -9);
            light.transform.localPosition = new Vector3(0f, 0.0013f, (near + top) / 2f);
            var shade = FlatHighwayArt.Piece(player.transform, "Flat Rail Shade", FlatHighwayArt.RailShade,
                new Color(0f, 0f, 0f, 0.4f), Player.TrackPlayer.TRACK_WIDTH, far - near, 0.0016f, -8);
            shade.transform.localPosition = new Vector3(0f, 0.0016f, (near + far) / 2f);
            // The side trims end a little under the strike line; stretch them down with the lanes to the screen's edge
            foreach (var trim in _trackTrims)
            {
                var t = trim.transform;
                float min = player.transform.InverseTransformPoint(trim.bounds.min).z;
                float max = player.transform.InverseTransformPoint(trim.bounds.max).z;
                if (min <= near || max - min < 0.01f) continue;
                t.localScale = t.localScale.WithZ(t.localScale.z * (max - near) / (max - min));
                float shift = near - player.transform.InverseTransformPoint(trim.bounds.min).z;
                t.position += player.transform.forward * shift;
            }
            // The lanes stand in for the track's own surface, which would stay solid under them at a lower opacity
            _trackMesh.enabled = false;
        }

        private void Update()
        {
            if (_flatLanes != null)
            {
                var colour = Color.Lerp(_flatLanesColor, _flatStreakColor, GrooveState);
                colour = Color.Lerp(colour, FlatOverdrive, 0.22f * StarpowerState);
                FlatHighwayArt.SetColor(_flatLanes, colour.WithAlpha(SettingsManager.Settings.TrackOpacity.Value));
            }

            if (GrooveMode)
            {
                GrooveState = Mathf.Lerp(GrooveState, 1f, Time.deltaTime * 5f);
            }
            else
            {
                GrooveState = Mathf.Lerp(GrooveState, 0f, Time.deltaTime * 3f);
            }

            if (StarpowerMode && SettingsManager.Settings.StarPowerHighwayFx.Value is StarPowerHighwayFxMode.On)
            {
                StarpowerState = 1.0f;
            }
            else
            {
                StarpowerState = Mathf.Lerp(StarpowerState, 0f, Time.deltaTime * 4f);
            }
        }

        private static Color FromHex(string hex, float alpha)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out var color))
            {
                color.a = alpha;
                return color;
            }

            throw new InvalidOperationException();
        }

        public void SetTrackScroll(double time, float noteSpeed)
        {
            float position = (float) time * noteSpeed / 4f;
            _material.SetFloat(_scrollProperty, position);
        }

        // TODO: Integrate this with CustomContentManager so it can be managed in settings
        private void SetTextures()
        {
            if (_normalPreset.BaseTexture == null || _normalPreset.SidePattern == null)
            {
                SetDefaultTextures();
                return;
            }

            var baseTexturePath = _normalPreset.BaseTexture.FullName;
            var sidePatternPath = _normalPreset.SidePattern.FullName;

            if (!File.Exists(baseTexturePath) || !File.Exists(sidePatternPath))
            {
                // Use default textures
                SetDefaultTextures();
                return;
            }

            var bytes = File.ReadAllBytes(baseTexturePath);
            _baseTexture = new Texture2D(2, 2);
            var success = _baseTexture.LoadImage(bytes);

            bytes = File.ReadAllBytes(sidePatternPath);
            _sidePattern = new Texture2D(2, 2);
            success = success && _sidePattern.LoadImage(bytes);

            // If either didn't load, use defaults
            if (!success)
            {
                SetDefaultTextures();
                return;
            }

            _material.SetTexture(_baseTextureProperty, _baseTexture);
            _material.SetFloat(_baseParallaxProperty, 1f);
            _material.SetTexture(_sidePatternProperty, _sidePattern);
            _material.SetFloat(_sideParallaxProperty, 1f);
        }

        private void SetDefaultTextures()
        {
            _material.SetTexture(_baseTextureProperty, _originalBaseTexture);
            _material.SetFloat(_baseParallaxProperty, _originalBaseParallax);
            _material.SetTexture(_sidePatternProperty, _originalSidePattern);
            _material.SetFloat(_sideParallaxProperty, _originalSideParallax);
        }
    }
}
