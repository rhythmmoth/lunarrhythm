using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Engine.Keys;
using YARG.Helpers.Extensions;

namespace YARG.Menu.ScoreScreen
{
    public class ProKeysScoreCard : ScoreCard<KeysStats>
    {
        [Space]
        [SerializeField]
        private TextMeshProUGUI _overhits;

        public override void SetCardContents()
        {
            base.SetCardContents();

            // Similarly to the guitar card, we would want to differentiate when
            // keys are used to play a guitar/bass part, but the pro keys icon
            // looks exactly the same as 5L keys when zoomed in here anyway
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>($"InstrumentIcons[{Player.Profile.CurrentInstrument.ToResourceName()}]")
                .WaitForCompletion();

            _overhits.text = ColorizePrimary(Stats.Overhits);

            // Perfect/Good rows, copied from the Overhits row and placed above it
            var overhitsRow = _overhits.GetComponentInParent<StatInfo>(true);
            AddJudgementRow(overhitsRow, "Perfect", Stats.PerfectHits);
            AddJudgementRow(overhitsRow, "Good", Stats.GoodHits);
        }

        private void AddJudgementRow(StatInfo template, string label, int count)
        {
            var row = Instantiate(template, template.transform.parent);
            row.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
            row.Label.text = label;

            // Share of all notes, so Perfect + Good + missed adds up to 100%
            float percent = Stats.TotalNotes > 0 ? 100f * count / Stats.TotalNotes : 0f;
            row.Value.text = $"{ColorizePrimary(count)} {ColorizeSecondary($"({percent:0}%)")}";
        }
    }
}