using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    /// <summary>
    /// The flat pieces of the 2D highway (boards NextHighway*): notes, strike line marks, lane dividers. Each is a
    /// quad lying on the track, drawn with the beat line's material (a texture and a colour that the highway pipeline
    /// already places correctly), with a texture generated here.
    /// </summary>
    public static class FlatHighwayArt
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static Material _template;
        private static Mesh _quad;
        private static Texture2D _solid, _beam;

        public static bool Ready => _template != null;

        /// <summary>The player whose highway a piece is on (null outside a highway).</summary>
        public static YARG.Player.YargPlayer PlayerOf(Component piece) =>
            piece.GetComponentInParent<YARG.Gameplay.Player.TrackPlayer>(true)?.Player;

        /// <summary>Takes the material and quad from the beat line prefab (call before building any piece).</summary>
        public static void Use(GameObject beatlinePrefab)
        {
            if (_template != null || beatlinePrefab == null) return;
            var renderer = beatlinePrefab.GetComponentInChildren<MeshRenderer>(true);
            _template = renderer.sharedMaterial;
            _quad = renderer.GetComponent<MeshFilter>().sharedMesh;
        }

        /// <summary>A lift connector: full width and strongest at the top (the lift), narrowing and fading downward.</summary>
        public static Texture2D Beam => _beam != null ? _beam : _beam = MakeBeam();

        public static Texture2D Solid
        {
            get
            {
                if (_solid != null) return _solid;
                _solid = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                _solid.SetPixels32(pixels);
                _solid.Apply();
                return _solid;
            }
        }

        /// <summary>
        /// A flat piece on the track under <paramref name="parent"/>: <paramref name="width"/> across the lanes,
        /// <paramref name="depth"/> along them, lifted by <paramref name="lift"/> so it draws over what's beneath.
        /// </summary>
        public static MeshRenderer Piece(Transform parent, string name, Texture texture, Color color, float width, float depth,
            float lift = 0.01f, int queueOffset = 0)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = parent.gameObject.layer;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(0f, lift, 0f);
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(width, depth, 1f);
            go.GetComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.GetComponent<MeshRenderer>();
            var material = new Material(_template);
            material.SetTexture(BaseMap, texture);
            material.SetColor(ColorId, color);
            material.renderQueue = _template.renderQueue + queueOffset;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        public static void SetColor(Renderer piece, Color color)
        {
            if (piece != null) piece.sharedMaterial.SetColor(ColorId, color);
        }

        /// <summary>Hides a theme model's own meshes (its particle effects stay), for the flat piece to stand in.</summary>
        public static Bounds HideModel(Transform model)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                if (renderer.GetComponent<TMPro.TMP_Text>() != null) continue; // text stays (the multiplier)
                renderer.enabled = false;
                // The model's footprint, in the model root's space, to size the flat piece (from the mesh itself:
                // a renderer's bounds are empty while it's inactive, as pooled notes are)
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var toModel = model.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = toModel.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) bounds = new Bounds(corner, Vector3.zero);
                    else bounds.Encapsulate(corner);
                    first = false;
                }
            }
            return bounds;
        }

        // ---------- note shapes (board NextHighwayCosmetics) ----------

        /// <summary>One note shape's art: the note, the lift and their outlines, with sizes in the board's pixels
        /// (the pill note is 56 × 20; the game sizes every shape from the pill it replaces).</summary>
        public sealed class ShapeArt
        {
            public Texture2D Note, NoteRing, Lift, LiftRing;
            public Vector2 NoteSize, LiftSize;
            /// <summary>The strike line mark is the shape filled faintly (gem, star, crescent), not its outline.</summary>
            public bool FilledMark;
        }

        private static readonly System.Collections.Generic.Dictionary<string, ShapeArt> _shapes = new();

        /// <summary>The art for a note shape cosmetic by id; an unknown id (or none) is the free Pill.</summary>
        public static ShapeArt Shape(string id)
        {
            id = id switch { "orb-shape" or "gem-shape" or "star-shape" or "crescent-shape" or "bar-shape" => id, _ => "pill-shape" };
            if (_shapes.TryGetValue(id, out var art)) return art;
            art = id switch
            {
                "orb-shape" => Make(30, 30, p => Circle(p, new(15, 15), 15), 26, 14,
                    p => Mathf.Max(Circle(p, new(13, 14), 13), p.y - 14f), false),
                "gem-shape" => Make(34, 34, Poly(34, 34, 50, 0, 100, 50, 50, 100, 0, 50), 22, 30,
                    Poly(22, 30, 50, 0, 100, 62, 50, 100, 0, 62), true),
                "star-shape" => Make(36, 36, Poly(36, 36, 50, 0, 61, 35, 98, 35, 68, 57, 79, 91, 50, 70, 21, 91, 32, 57, 2, 35, 39, 35),
                    28, 28, Poly(28, 28, 50, 0, 60, 40, 100, 50, 60, 60, 50, 100, 40, 60, 0, 50, 40, 40), true),
                "crescent-shape" => Make(32, 32, p => Mathf.Max(Circle(p, new(16, 16), 16), -Circle(p, new(23f, 9.6f), 14.8f)),
                    30, 30, p => Mathf.Max(Circle(p, new(15, 15), 15), -Circle(p, new(15f, 5.4f), 15f)), true),
                "bar-shape" => Make(60, 10, p => Box(p, 60, 10, 3), 36, 14, Poly(36, 14, 50, 0, 100, 100, 0, 100), false),
                _ => Make(56, 20, p => Box(p, 56, 20, 10), 34, 20,
                    Poly(34, 20, 44, 6, 50, 0, 56, 6, 98, 88, 96, 98, 90, 100, 10, 100, 4, 98, 2, 88), false),
            };
            _shapes[id] = art;
            return art;
        }

        /// <summary>Lane light (board NextHighwayDepth, E): full at the bottom half (the strike line's side), fading out
        /// up the track.</summary>
        public static Texture2D LaneLight => _laneLight != null ? _laneLight : _laneLight = Fade(4, 128, (u, v) => 1f - Mathf.SmoothStep(0f, 1f, (v - 0.5f) / 0.5f));

        /// <summary>The shade the rails cast on the lanes: dark at both edges, clear by 9% in.</summary>
        public static Texture2D RailShade => _railShade != null ? _railShade : _railShade = Fade(128, 4, (u, v) =>
        {
            float t = Mathf.Clamp01(1f - Mathf.Min(u, 1f - u) / 0.09f);
            return t * t;
        });
        private static Texture2D _laneLight, _railShade;

        // A white texture with alpha from (u, v) in 0..1 (v = 0 is the strike line's side)
        private static Texture2D Fade(int w, int h, System.Func<float, float, float> alpha)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = new Color32(255, 255, 255, (byte) (Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h)) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Hit effect pieces: a soft round glow (Burst), a thin circle (Ripple), a solid dot (Stardust).</summary>
        public static Texture2D Glow => _glow != null ? _glow : _glow = MakeGlow();
        public static Texture2D CircleRing => _circleRing != null ? _circleRing : _circleRing = Draw(40, 40, p => Circle(p, new(20, 20), 19), true, 1.5f);
        public static Texture2D Dot => _dot != null ? _dot : _dot = Draw(16, 16, p => Circle(p, new(8, 8), 7.5f), false);
        private static Texture2D _glow, _circleRing, _dot;

        // Distances in the board's pixels, CSS-style (y down: 0 is the far end, up the track); negative inside
        private delegate float Distance(Vector2 p);

        private static ShapeArt Make(float w, float h, Distance note, float lw, float lh, Distance lift, bool filledMark) => new()
        {
            Note = Draw(w, h, note, false, shaded: true), NoteRing = Draw(w, h, note, true, shaded: true),
            Lift = Draw(lw, lh, lift, false, shaded: true), LiftRing = Draw(lw, lh, lift, true, shaded: true),
            NoteSize = new(w, h), LiftSize = new(lw, lh), FilledMark = filledMark,
        };

        // Shaded: a darker rim and a soft top-to-bottom fall-off, so a note keeps its edge and some roundness on any
        // highway colour (the game tints the white texture with the lane colour, so the shading carries over)
        private static Texture2D Draw(float w, float h, Distance shape, bool ring, float thickness = 3f, bool shaded = false)
        {
            const float scale = 4f; // texture pixels per board pixel
            int tw = Mathf.CeilToInt(w * scale), th = Mathf.CeilToInt(h * scale);
            var texture = new Texture2D(tw, th, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[tw * th];
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    // Texture rows count up from the bottom (the strike line side)
                    var p = new Vector2((x + 0.5f) / scale, h - (y + 0.5f) / scale);
                    float d = shape(p) * scale;
                    float alpha = Mathf.Clamp01(0.5f - d);
                    if (ring) alpha *= Mathf.Clamp01(d + thickness * scale + 0.5f);
                    float shade = 1f;
                    if (shaded && ring)
                    {
                        // An outline gets a fine dark edge on both sides, so it shows on a bright highway too
                        float edge = Mathf.Min(-d, d + thickness * scale) / scale;
                        shade = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((edge - 0.3f) / 0.9f));
                    }
                    else if (shaded)
                    {
                        float rim = Mathf.Clamp01((-d / scale - 0.4f) / 2f); // 0 at the edge, 1 two board pixels in
                        shade = Mathf.Lerp(0.32f, Mathf.Lerp(1f, 0.74f, p.y / h), rim);
                    }
                    byte c = (byte) (shade * 255f);
                    pixels[y * tw + x] = new Color32(c, c, c, (byte) (alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D MakeGlow()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    float a = Mathf.Clamp01(1f - r);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) (a * a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static float Circle(Vector2 p, Vector2 centre, float radius) => (p - centre).magnitude - radius;

        // A w × h box with rounded corners
        private static float Box(Vector2 p, float w, float h, float radius)
        {
            radius = Mathf.Min(radius, Mathf.Min(w, h) / 2f);
            var q = new Vector2(Mathf.Abs(p.x - w / 2f) - w / 2f + radius, Mathf.Abs(p.y - h / 2f) - h / 2f + radius);
            return Mathf.Min(Mathf.Max(q.x, q.y), 0f) + Vector2.Max(q, Vector2.zero).magnitude - radius;
        }

        // A polygon from the board's clip-path percentages (any simple polygon, convex or not)
        private static Distance Poly(float w, float h, params float[] percents)
        {
            var v = new Vector2[percents.Length / 2];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector2(percents[2 * i] / 100f * w, percents[2 * i + 1] / 100f * h);
            return p =>
            {
                float d = Vector2.Dot(p - v[0], p - v[0]);
                float sign = 1f;
                for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
                {
                    var e = v[j] - v[i];
                    var to = p - v[i];
                    var b = to - e * Mathf.Clamp01(Vector2.Dot(to, e) / Vector2.Dot(e, e));
                    d = Mathf.Min(d, Vector2.Dot(b, b));
                    bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * to.y > e.y * to.x;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
                }
                return sign * Mathf.Sqrt(d);
            };
        }

        // The beam (board NextHighway): 28% wide and faint at the bottom, full width at the top; half the board's
        // strength, since the game's glow brightens it
        private static Texture2D MakeBeam()
        {
            const int w = 64, h = 128;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float half = Mathf.Lerp(0.14f, 0.5f, v) * w;
                float strength = Mathf.Lerp(0.03f, 0.225f, v);
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs(x + 0.5f - w / 2f) - half;
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte) (strength * Mathf.Clamp01(0.5f - d) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
