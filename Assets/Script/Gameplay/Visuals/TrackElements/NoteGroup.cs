using System.Linq;
using UnityEngine;
using YARG.Themes;

namespace YARG.Gameplay.Visuals
{
    public class NoteGroup : MonoBehaviour
    {
        private struct MaterialInfo
        {
            public Material MaterialCache;

            public float EmissionMultiplier;
            public float EmissionAddition;

            public static MaterialInfo From(MeshEmissionMaterialIndex a)
            {
                try
                {
                    return new MaterialInfo
                    {
                        MaterialCache = a.Mesh.materials[a.MaterialIndex],
                        EmissionMultiplier = a.EmissionMultiplier,
                        EmissionAddition = a.EmissionAddition,
                    };
                }
                catch (System.Exception x)
                {
                    throw x;
                }
            }
        }

        private static readonly int _emissionColor = Shader.PropertyToID("_EmissionColor");

        private static readonly int _randomFloat = Shader.PropertyToID("_RandomFloat");
        private static readonly int _randomVector = Shader.PropertyToID("_RandomVector");

        // If we want info to be copied over when we copy the prefab,
        // we must make them SerializeFields.
        [SerializeField]
        private ThemeNote _themeNote;

        private MaterialInfo[] _coloredMaterialCache;
        private MaterialInfo[] _coloredMaterialNoStarPowerCache;
        private MaterialInfo[] _allColoredCache;
        private MaterialInfo[] _coloredMetalMaterialCache;

        // The 2D highway's flat note, standing in for the theme model, with a darker edge under it and a faint shadow
        // (board NextHighwayDepth, E: notes with a little thickness)
        private MeshRenderer _flat, _flatEdge;
        private const float FLAT_DEPTH = 0.2f;

        /// <summary>Drawn as a lift (a triangle) on the 2D highway; set before the first Initialize.</summary>
        // Serialized so the pool's copies of the note prefab keep it
        [HideInInspector]
        public bool FlatLift;

        /// <summary>The flat piece's width across the lane (0 off the 2D highway).</summary>
        public float FlatWidth => _flat != null ? _flat.transform.localScale.x : 0f;
        public float FlatHeight => _flat != null ? _flat.transform.localScale.y : 0f;

        public void Initialize()
        {
            if (FlatHighway.Enabled && _flat == null && FlatHighwayArt.Ready) MakeFlat();
            _coloredMaterialCache ??= _themeNote.ColoredMaterials.Select(MaterialInfo.From).ToArray();
            _coloredMaterialNoStarPowerCache ??= _themeNote.ColoredMaterialsNoStarPower.Select(MaterialInfo.From).ToArray();
            _allColoredCache ??= _coloredMaterialCache.Concat(_coloredMaterialNoStarPowerCache).ToArray();
            _coloredMetalMaterialCache ??= _themeNote.ColoredMetalMaterials.Select(MaterialInfo.From).ToArray();

            // Set random values
            var randomFloat = Random.Range(-1f, 1f);
            var randomVector = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f));
            foreach (var info in _allColoredCache)
            {
                var material = info.MaterialCache;

                if (material.HasFloat(_randomFloat))
                {
                    material.SetFloat(_randomFloat, randomFloat);
                }

                if (material.HasVector(_randomVector))
                {
                    material.SetVector(_randomVector, randomVector);
                }
            }
        }

        /// <summary>Builds the 2D highway's flat note ahead of its first use (see TrackPlayer.PrepareFlatNotes).</summary>
        public void PrepareFlat()
        {
            if (FlatHighway.Enabled && _flat == null && FlatHighwayArt.Ready) MakeFlat();
        }

        private void MakeFlat()
        {
            // Measured and placed in the note group's own space (a theme's model may be rotated or scaled)
            var footprint = FlatHighwayArt.HideModel(transform);
            // The pill this note would be (as wide as the model, a fixed depth; a lift's model is 60% of a note), then
            // the worn shape sized from it. Notes spanning the lanes (opens) stay pills.
            float pill = Mathf.Max(footprint.size.x * 0.85f, 0.1f) / (FlatLift ? 0.6f : 1f);
            var art = FlatHighwayArt.Shape(pill > 0.6f ? null : FlatHighwayArt.PlayerOf(this)?.NoteShape);
            var size = FlatLift ? art.LiftSize : art.NoteSize;
            float scale = pill > 0.6f ? 1f : FlatHighway.NoteScale; // opens already span the lanes
            float width = pill * size.x / 56f * scale, depth = FLAT_DEPTH * size.y / 20f * scale;
            if (scale > 1f) width = Mathf.Min(width, FlatHighway.MaxNoteWidth);
            var model = transform;
            var texture = FlatLift ? art.Lift : art.Note;
            _flat = FlatHighwayArt.Piece(model, "Flat Note", texture, Color.white, width, depth, 0.01f, 0);
            // The edge shows below the note, toward the strike line (the track's -z)
            _flatEdge = FlatHighwayArt.Piece(model, "Flat Note Edge", texture, Color.white, width, depth, 0.009f, -1);
            _flatEdge.transform.localPosition += new Vector3(0f, 0f, -depth * 0.14f);
            var shadow = FlatHighwayArt.Piece(model, "Flat Note Shadow", FlatHighwayArt.Glow, new Color(0f, 0f, 0f, 0.28f),
                width * 1.05f, depth * 1.7f, 0.008f, -2);
            shadow.transform.localPosition += new Vector3(0f, 0f, -depth * 0.1f);
        }

        public void SetColorWithEmission(Color color, Color colorNoStarPower)
        {
            if (_flat != null)
            {
                // The colour it's given: an overdrive note is white (the colour profile's overdrive colour), as before
                // the 2D highway, rather than its lane colour with an edge
                FlatHighwayArt.SetColor(_flat, color);
                FlatHighwayArt.SetColor(_flatEdge, new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, color.a));
                return;
            }
            // Deal with color (with star power)

            foreach (var info in _coloredMaterialCache)
            {
                float a = info.EmissionAddition;
                var realColor = color + new Color(a, a, a);

                info.MaterialCache.color = realColor;
                info.MaterialCache.SetColor(_emissionColor, realColor * info.EmissionMultiplier);
            }

            // Deal with color (no star power)
            if (_coloredMaterialNoStarPowerCache.Length == 0) return;

            foreach (var info in _coloredMaterialNoStarPowerCache)
            {
                float a = info.EmissionAddition;
                var realColor = colorNoStarPower + new Color(a, a, a);

                info.MaterialCache.color = realColor;
                info.MaterialCache.SetColor(_emissionColor, realColor * info.EmissionMultiplier);
            }
        }

        public void SetMetalColor(Color metalColor)
        {
            if (_flat != null || _coloredMetalMaterialCache.Length == 0) return;

            foreach (var info in _coloredMetalMaterialCache)
            {
                info.MaterialCache.color = metalColor;
                info.MaterialCache.SetColor(_emissionColor, metalColor);
            }
        }

        public void SetActive(bool a) => gameObject.SetActive(a);

        /// <summary>
        /// This method is only called <b>once</b> when the note prefab is being created
        /// for this theme.
        /// </summary>
        public static NoteGroup CreateNoteGroupFromTheme(Transform parent, GameObject themeModel)
        {
            var noteObj = new GameObject("Note Group");
            var noteTransform = noteObj.transform;

            noteTransform.parent = parent;
            noteTransform.localPosition = Vector3.zero;

            var noteGroup = noteObj.AddComponent<NoteGroup>();
            noteGroup.SetModelFromTheme(themeModel);

            return noteGroup;
        }

        private void SetModelFromTheme(GameObject model)
        {
            // Copy the model
            var copy = Instantiate(model, transform);
            copy.transform.localPosition = Vector3.zero;

            // Set new information
            var themeNote = copy.GetComponent<ThemeNote>();
            _themeNote = themeNote;
        }
    }
}