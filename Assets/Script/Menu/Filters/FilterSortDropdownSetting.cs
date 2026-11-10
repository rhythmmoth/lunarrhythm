using YARG.Core.Extensions;
using YARG.Core;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Player;
using YARG.Settings.Types;
using YARG.Song;

namespace YARG.Menu.Filters
{
    public class FilterSortDropdownSetting : DropdownSetting<SortAttribute>
    {
        public FilterSortDropdownSetting(System.Action<SortAttribute> onChange)
            : base(SortAttribute.Name, onChange, localizable: false)
        {
        }

        public override void UpdateValues()
        {
            _possibleValues.Clear();

            foreach (var sort in EnumExtensions<SortAttribute>.Values)
            {
                // Source is always the same for the pad game's songs
                if (sort is SortAttribute.Unspecified or SortAttribute.Source)
                    continue;

                if (sort == SortAttribute.Playcount && PlayerContainer.OnlyHasBotsActive())
                    continue;

                if (sort >= SortAttribute.Instrument)
                    break;

                _possibleValues.Add(sort);
            }

            foreach (var instrument in EnumExtensions<Instrument>.Values)
            {
                if (SongContainer.HasInstrument(instrument))
                    _possibleValues.Add(instrument.ToSortAttribute());
            }
        }

        public override string ValueToString(SortAttribute value)
        {
            return value.ToLocalizedName();
        }
    }
}
