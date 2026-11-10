using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using UnityEngine;
using YARG.Assets.Script.Gameplay.Player;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Keys;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Themes;

namespace YARG.Gameplay.Visuals
{
    public sealed class FiveLaneKeysNoteElement : NoteElement<GuitarNote, FiveLaneKeysPlayer>
    {
        private const float OPEN_LANE_SCALE_FACTOR = 5f / 6f;
        private Vector3 OPEN_LANE_SCALE = new(OPEN_LANE_SCALE_FACTOR, OPEN_LANE_SCALE_FACTOR, 1);

        private enum NoteType
        {
            Normal = 0,
            Open = 1,
            Wildcard = 2,
            Lift = 3,
            Count
        }

        [Space]
        [SerializeField]
        private SustainLine _normalSustainLine;
        [SerializeField]
        private SustainLine _openSustainLine;
        [SerializeField]
        private SustainLine _wildcardSustainLine;

        private SustainLine _sustainLine;

        // Make sure the remove it later if it has a sustain
        protected override float RemovePointOffset => (float) NoteRef.TimeLength * Player.NoteSpeed;

        public override void SetThemeModels(
            Dictionary<ThemeNoteType, GameObject> models,
            Dictionary<ThemeNoteType, GameObject> starPowerModels)
        {
            CreateNoteGroupArrays((int) NoteType.Count);

            AssignNoteGroup(models, starPowerModels, (int) NoteType.Normal, ThemeNoteType.Normal);
            AssignNoteGroup(models, starPowerModels, (int) NoteType.Open,     ThemeNoteType.Open);
            AssignNoteGroup(models, starPowerModels, (int) NoteType.Wildcard, ThemeNoteType.Wildcard);

            if (models.ContainsKey(ThemeNoteType.Lift))
            {
                AssignNoteGroup(models, starPowerModels, (int) NoteType.Lift, ThemeNoteType.Lift);
            }
            else
            {
                // No theme ships a lift model, so reshape the normal note into a triangle
                AssignNoteGroup(models, starPowerModels, (int) NoteType.Lift, ThemeNoteType.Normal);
                MakeTriangle(NoteGroups[(int) NoteType.Lift]);
                if (StarPowerNoteGroups[(int) NoteType.Lift] != NoteGroups[(int) NoteType.Lift])
                {
                    MakeTriangle(StarPowerNoteGroups[(int) NoteType.Lift]);
                }
            }
        }

        private static void MakeTriangle(NoteGroup group)
        {
            // Keep only the note's main colored mesh, so the triangle takes the lane color
            var colored = group.GetComponentInChildren<ThemeNote>().ColoredMaterials.First();
            foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.enabled = renderer == colored.Mesh;
            }

            // Work in the note group's space so rotated model imports still give a flat triangle
            var filter = colored.Mesh.GetComponent<MeshFilter>();
            var toGroup = group.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var meshBounds = filter.sharedMesh.bounds;
            var bounds = new Bounds(toGroup.MultiplyPoint3x4(meshBounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                bounds.Encapsulate(toGroup.MultiplyPoint3x4(corner));
            }

            filter.sharedMesh = CreateTriangleMesh(bounds, toGroup.inverse, filter.sharedMesh.subMeshCount,
                colored.MaterialIndex);
        }

        // A flat triangular prism in the note's footprint, pointing down the track
        private static Mesh CreateTriangleMesh(Bounds bounds, Matrix4x4 toMesh, int subMeshCount, int subMesh)
        {
            // Smaller and flatter than the note it replaces, so it reads as a marker rather than a block
            var c = bounds.center;
            float halfWidth = bounds.extents.x * 0.6f;
            float halfDepth = halfWidth * 0.5f;
            float halfHeight = bounds.extents.y * 0.35f;
            Vector3[] top =
            {
                new(c.x - halfWidth, c.y + halfHeight, c.z - halfDepth),
                new(c.x,             c.y + halfHeight, c.z + halfDepth),
                new(c.x + halfWidth, c.y + halfHeight, c.z - halfDepth),
            };
            var bottom = top.Select(v => new Vector3(v.x, c.y - halfHeight, v.z)).ToArray();

            var vertices = new List<Vector3>();
            void Tri(Vector3 a, Vector3 b, Vector3 d)
            {
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(d);
            }

            // Clockwise winding is front-facing in Unity
            Tri(top[0], top[1], top[2]);
            Tri(bottom[0], bottom[2], bottom[1]);
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                Tri(top[i], bottom[i], bottom[j]);
                Tri(top[i], bottom[j], top[j]);
            }

            var mesh = new Mesh { name = "Lift Note" };
            mesh.SetVertices(vertices.Select(v => toMesh.MultiplyPoint3x4(v)).ToList());
            mesh.SetUVs(0, new Vector2[vertices.Count]);
            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(i == subMesh ? Enumerable.Range(0, vertices.Count).ToArray() : new int[0], i);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        protected override void InitializeElement()
        {
            base.InitializeElement();

            var noteGroups = NoteRef.IsStarPower ? StarPowerNoteGroups : NoteGroups;

            if (Player.IsNormalNote(NoteRef))
            {
                // Deal with non-open notes
                var lane = Player.GetLanePosition((FiveFretGuitarFret) NoteRef.Fret);

                // Set the position
                transform.localPosition = new Vector3(GetElementX(lane, Player.LaneCount), 0f, 0f);

                // Set the scale; in more complex scenarios this can be computed as 5f/Player.LaneCount, but since we only have two
                // possible lane counts, it's more efficient to just precompute the one nondefault scale
                if (Player.UsingOpenLane) {
                    gameObject.transform.localScale = OPEN_LANE_SCALE;
                }
                else if (Player.LaneCount == 4)
                {
                    // Wider lanes on Festival's four-lane difficulties: widen only, so a note's look matches its timing
                    gameObject.transform.localScale = Vector3.Scale(gameObject.transform.localScale, new Vector3(5f / 4f, 1f, 1f));
                }

                // Get which note model to use
                NoteGroup = NoteRef.IsLift ? noteGroups[(int) NoteType.Lift] : NoteRef.Type switch
                {
                    GuitarNoteType.Strum or
                    GuitarNoteType.Hopo  or
                    GuitarNoteType.Tap   => noteGroups[(int) NoteType.Normal],
                    _ => throw new ArgumentOutOfRangeException(nameof(NoteRef.Type))
                };

                _sustainLine = _normalSustainLine;
            }
            else if (NoteRef.FiveLaneKeysAction is FiveLaneKeysEngine.FiveLaneKeysAction.OpenNote)
            {
                // Deal with open notes

                // Set the position
                transform.localPosition = Vector3.zero;

                // Get which note model to use
                NoteGroup = NoteRef.Type switch
                {
                    GuitarNoteType.Strum or
                    GuitarNoteType.Hopo or
                    GuitarNoteType.Tap   => noteGroups[(int) NoteType.Open],
                    _ => throw new ArgumentOutOfRangeException(nameof(NoteRef.Type))
                };

                _sustainLine = _openSustainLine;
            }
            else
            {
                // Deal with wildcard notes
                transform.localPosition = Vector3.zero;

                NoteGroup = noteGroups[(int) NoteType.Wildcard];

                _sustainLine = _wildcardSustainLine;
            }

            // Show and set material properties
            NoteGroup.SetActive(true);
            NoteGroup.Initialize();

            // Set line length
            if (NoteRef.IsSustain)
            {
                _sustainLine.gameObject.SetActive(true);

                float len = (float) NoteRef.TimeLength * Player.NoteSpeed;
                _sustainLine.Initialize(len);
            }

            // Set note and sustain color
            UpdateColor();
        }

        public override void HitNote()
        {
            base.HitNote();

            if (NoteRef.IsSustain)
            {
                HideNotes();
            }
            else
            {
                ParentPool.Return(this);
            }
        }


        public override void MissNote()
        {
            base.MissNote();

            UpdateColor();
        }

        protected override void UpdateElement()
        {
            base.UpdateElement();

            UpdateSustain();
        }

        protected override void OnNoteStateChanged()
        {
            base.OnNoteStateChanged();

            UpdateColor();
        }

        public override void OnStarPowerUpdated()
        {
            base.OnStarPowerUpdated();

            UpdateColor();
        }

        protected override bool CalcStarPowerVisible()
        {
            if (!NoteRef.IsStarPower)
            {
                return false;
            }
            return !(((KeysEngineParameters) Player.BaseParameters).NoStarPowerOverlap && Player.BaseStats.IsStarPowerActive);
        }

        private void UpdateSustain()
        {
            _sustainLine.UpdateSustainLine();
        }

        private void UpdateColor()
        {
            var colors = Player.Player.ColorProfile.FiveFretGuitar;

            // Get which note color to use
            var colorNoStarPower = colors.GetNoteColor(NoteRef.Fret);
            var color = NoteRef.IsStarPower
                ? colors.GetNoteStarPowerColor(NoteRef.Fret)
                : colorNoStarPower;

            if (NoteRef.WasMissed)
            {
                color = colors.Miss;
            }

            // Set the note color if not hidden
            if (!NoteRef.WasHit)
            {
                NoteGroup.SetColorWithEmission(color.ToUnityColor(), colorNoStarPower.ToUnityColor());

                // Set the metal color
                NoteGroup.SetMetalColor(colors.GetMetalColor(NoteRef.IsStarPower).ToUnityColor());
            }

            // The rest of this method is for sustain only
            if (!NoteRef.IsSustain) return;

            _sustainLine.SetState(SustainState, color.ToUnityColor());
        }

        protected override void HideElement()
        {
            HideNotes();

            _normalSustainLine.gameObject.SetActive(false);
            _openSustainLine.gameObject.SetActive(false);
        }
    }
}