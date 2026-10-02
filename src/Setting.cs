using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace TollPlaza
{
    /// <summary>
    /// The mod's page in Options: plaza purchase prices, and the toll rates and lane speeds a city starts
    /// with. Kept in the game's settings folder (ModsSettings/TollPlaza), never in a save. Rates and speeds
    /// here only seed cities without toll data of their own; a city's panel settings still win.
    /// </summary>
    [FileLocation(nameof(TollPlaza))]
    [SettingsUIGroupOrder(PricesGroup, DefaultsGroup, CityGroup, ResetGroup)]
    [SettingsUIShowGroupName(PricesGroup, DefaultsGroup, CityGroup, ResetGroup)]
    public class Setting : ModSetting
    {
        public const string Section = "Main";
        public const string PricesGroup = "Prices";
        public const string DefaultsGroup = "NewCities";
        public const string CityGroup = "CityData";
        public const string ResetGroup = "Reset";

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        private const float PriceMin = 10000, PriceMax = 500000, PriceStep = 5000;

        [SettingsUISection(Section, PricesGroup)]
        [SettingsUISlider(min = PriceMin, max = PriceMax, step = PriceStep, unit = Unit.kMoney)]
        public int Price2 { get; set; }

        [SettingsUISection(Section, PricesGroup)]
        [SettingsUISlider(min = PriceMin, max = PriceMax, step = PriceStep, unit = Unit.kMoney)]
        public int Price4 { get; set; }

        [SettingsUISection(Section, PricesGroup)]
        [SettingsUISlider(min = PriceMin, max = PriceMax, step = PriceStep, unit = Unit.kMoney)]
        public int Price6 { get; set; }

        [SettingsUISection(Section, DefaultsGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 0.25f, unit = Unit.kFloatTwoFractions)]
        public float CarRate { get; set; }

        [SettingsUISection(Section, DefaultsGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 0.25f, unit = Unit.kFloatTwoFractions)]
        public float TruckRate { get; set; }

        [SettingsUISection(Section, DefaultsGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 0.25f, unit = Unit.kFloatTwoFractions)]
        public float BusRate { get; set; }

        [SettingsUISection(Section, DefaultsGroup)]
        [SettingsUISlider(min = 5, max = 70, step = 5, unit = Unit.kInteger)]
        public int CashMph { get; set; }

        [SettingsUISection(Section, DefaultsGroup)]
        [SettingsUISlider(min = 5, max = 70, step = 5, unit = Unit.kInteger)]
        public int EtcMph { get; set; }

        /// <summary>Set by the city reset buttons; carried out on the next update in a loaded city (TollUISystem).</summary>
        public static CityReset PendingReset;

        private static bool InCity() => Game.SceneFlow.GameManager.instance?.gameMode.IsGame() == true;

        public enum CityReset
        {
            None,
            Counts,
            Everything,
        }

        [SettingsUISection(Section, CityGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation(null, null)]
        public bool ResetCityCounts
        {
            set { PendingReset = InCity() ? CityReset.Counts : CityReset.None; }
        }

        [SettingsUISection(Section, CityGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation(null, null)]
        public bool ResetCityEverything
        {
            set { PendingReset = InCity() ? CityReset.Everything : CityReset.None; }
        }

        [SettingsUISection(Section, ResetGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation(null, null)]
        public bool ResetToDefaults
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            Price2 = 75000;
            Price4 = 100000;
            Price6 = 125000;
            CarRate = TollState.DefaultCarRateCents / 100f;
            TruckRate = TollState.DefaultTruckRateCents / 100f;
            BusRate = TollState.DefaultBusRateCents / 100f;
            CashMph = PlazaLayout.CashSpeedMph;
            EtcMph = PlazaLayout.EtcSpeedMph;
        }

        public static int Cents(float dollars) => (int)System.Math.Round(System.Math.Max(0f, dollars) * 100f);
    }

    /// <summary>English text for the Options page.</summary>
    public class SettingLocale : IDictionarySource
    {
        private readonly Setting m_Setting;

        public SettingLocale(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var s = m_Setting;
            return new Dictionary<string, string>
            {
                { s.GetSettingsLocaleID(), "Toll Plaza" },
                { s.GetOptionTabLocaleID(Setting.Section), "Settings" },
                { s.GetOptionGroupLocaleID(Setting.PricesGroup), "Working plaza prices (odd sizes cost halfway between)" },
                { s.GetOptionGroupLocaleID(Setting.DefaultsGroup), "New cities: starting toll rates ($) and lane speeds (mph)" },
                { s.GetOptionGroupLocaleID(Setting.CityGroup), "This city's toll data (works while a city is loaded)" },
                { s.GetOptionGroupLocaleID(Setting.ResetGroup), "Settings on this page" },

                { s.GetOptionLabelLocaleID(nameof(Setting.ResetCityCounts)), "Reset counts and revenue" },
                { s.GetOptionDescLocaleID(nameof(Setting.ResetCityCounts)), "Starts this city's toll numbers over: vehicles charged (this month, last month and all time), revenue collected, and every plaza's and booth's numbers. Plaza names, lanes and rates stay. Money already collected stays in the city's treasury. Save the city afterwards to keep the reset." },
                { s.GetOptionWarningLocaleID(nameof(Setting.ResetCityCounts)), "Reset this city's toll counts and revenue to zero?" },
                { s.GetOptionLabelLocaleID(nameof(Setting.ResetCityEverything)), "Reset all toll data" },
                { s.GetOptionDescLocaleID(nameof(Setting.ResetCityEverything)), "Starts this city's tolls over completely: the counts and revenue, plus every plaza's own rates, names, cash/E-Toll lanes and closed booths, and the city's toll rates and lane speeds (back to the defaults above). Placed plazas stay. Save the city afterwards to keep the reset." },
                { s.GetOptionWarningLocaleID(nameof(Setting.ResetCityEverything)), "Reset ALL of this city's toll data, including plaza names, rates and lanes?" },

                { s.GetOptionLabelLocaleID(nameof(Setting.Price2)), "2-booth plaza" },
                { s.GetOptionDescLocaleID(nameof(Setting.Price2)), "Purchase price of the 2-booth plaza with road. The 3-booth plaza costs halfway between this and the 4-booth price." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Price4)), "4-booth plaza" },
                { s.GetOptionDescLocaleID(nameof(Setting.Price4)), "Purchase price of the 4-booth plaza with road. The 5-booth plaza costs halfway between this and the 6-booth price." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Price6)), "6-booth plaza" },
                { s.GetOptionDescLocaleID(nameof(Setting.Price6)), "Purchase price of the 6-booth plaza with road." },

                { s.GetOptionLabelLocaleID(nameof(Setting.CarRate)), "Car toll" },
                { s.GetOptionDescLocaleID(nameof(Setting.CarRate)), "Toll per car, in dollars, for a city that has no toll settings of its own yet. Change a city's rates in its Toll Plaza panel." },
                { s.GetOptionLabelLocaleID(nameof(Setting.TruckRate)), "Truck toll" },
                { s.GetOptionDescLocaleID(nameof(Setting.TruckRate)), "Toll per truck, in dollars, for a city that has no toll settings of its own yet." },
                { s.GetOptionLabelLocaleID(nameof(Setting.BusRate)), "Bus toll" },
                { s.GetOptionDescLocaleID(nameof(Setting.BusRate)), "Toll per bus, in dollars, for a city that has no toll settings of its own yet." },
                { s.GetOptionLabelLocaleID(nameof(Setting.CashMph)), "Cash lane speed" },
                { s.GetOptionDescLocaleID(nameof(Setting.CashMph)), "Speed through cash booths, in mph, for a city that has no toll settings of its own yet." },
                { s.GetOptionLabelLocaleID(nameof(Setting.EtcMph)), "E-Toll lane speed" },
                { s.GetOptionDescLocaleID(nameof(Setting.EtcMph)), "Speed through E-Toll booths, in mph, for a city that has no toll settings of its own yet." },

                { s.GetOptionLabelLocaleID(nameof(Setting.ResetToDefaults)), "Reset to defaults" },
                { s.GetOptionDescLocaleID(nameof(Setting.ResetToDefaults)), "Puts every setting on this page back to the mod's defaults." },
                { s.GetOptionWarningLocaleID(nameof(Setting.ResetToDefaults)), "Reset all Toll Plaza settings to their defaults?" },
            };
        }

        public void Unload()
        {
        }
    }
}
