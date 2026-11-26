using UnityEngine;

namespace YARG.Gameplay.Visuals
{
    public class StarpowerBar : MonoBehaviour
    {
        private static readonly int _fill = Shader.PropertyToID("Fill");
        private static readonly int _pulse = Shader.PropertyToID("Pulse");

        [SerializeField]
        private MeshRenderer _starpowerBar;

        private double _starpowerAmount;
        private bool _starpowerActive;

        public void SetStarpower(double starpowerAmount, bool starpowerActive, bool codaStarted)
        {
            // Starpower bar should be empty during coda mode
            if (codaStarted)
            {
                starpowerAmount = 0;
                starpowerActive = false;
            }

            _starpowerAmount = starpowerAmount;
            _starpowerActive = starpowerActive;

            _starpowerBar.material.SetFloat(_fill, (float) starpowerAmount);
            UpdateFlat();
        }

        // The 2D highway (board NextHighway): a flat vertical meter to the right of the strike line, filling upward;
        // in a group (board NextOnlineHud) a thin one up the column's own right edge
        private float FLAT_X => Compact ? 0.95f : 1.16f;
        private float FLAT_NEAR => Compact ? -0.05f : -0.2f;
        private float FLAT_LENGTH => Compact ? 1.2f : 1.5f;
        private float FLAT_WIDTH => Compact ? 0.03f : 0.06f;

        /// <summary>Set before the first update: the group look.</summary>
        [System.NonSerialized]
        public bool Compact;
        private static readonly Color Cream = new(0.953f, 0.91f, 0.8f, 1f);
        private MeshRenderer _flatFill;

        private void UpdateFlat()
        {
            if (!FlatHighway.Enabled || !FlatHighwayArt.Ready) return;
            if (_flatFill == null)
            {
                FlatHighwayArt.HideModel(transform);
                var track = FlatHighwayArt.Piece(transform, "Flat Overdrive Track", FlatHighwayArt.Solid,
                    new Color(0.137f, 0.173f, 0.333f, 1f), FLAT_WIDTH, FLAT_LENGTH, 0.004f, 1);
                track.transform.localPosition = new Vector3(FLAT_X, 0.004f, FLAT_NEAR + FLAT_LENGTH / 2f);
                _flatFill = FlatHighwayArt.Piece(transform, "Flat Overdrive Fill", FlatHighwayArt.Solid, Cream,
                    FLAT_WIDTH, FLAT_LENGTH, 0.006f, 2);
            }
            float amount = Mathf.Clamp01((float) _starpowerAmount);
            _flatFill.enabled = amount > 0f;
            var t = _flatFill.transform;
            t.localScale = new Vector3(FLAT_WIDTH, FLAT_LENGTH * amount, 1f);
            t.localPosition = new Vector3(FLAT_X, 0.006f, FLAT_NEAR + FLAT_LENGTH * amount / 2f);
            FlatHighwayArt.SetColor(_flatFill, _starpowerActive ? Color.white : Cream);
        }

        public void UpdateFlash(double beatPercentage)
        {
            if (_starpowerAmount >= 0.5 || _starpowerActive)
            {
                float pulse = Mathf.Pow(1 - (float) beatPercentage, 2);
                _starpowerBar.material.SetFloat(_pulse, pulse);
            }
            else
            {
                _starpowerBar.material.SetFloat(_pulse, 0f);
            }
        }
    }
}