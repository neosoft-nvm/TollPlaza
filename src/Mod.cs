using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.UI;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace TollPlaza
{
    public class Mod : IMod
    {
        public static ILog Log { get; } = LogManager.GetLogger($"{nameof(TollPlaza)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        private const string UiHost = "tollplaza";
        private const string IconFile = "TollPlaza.svg";
        public static string IconUri => "coui://" + UiHost + "/" + IconFile;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("[TollPlaza] OnLoad " + typeof(Mod).Assembly.GetName().Version);

            try
            {
                RegisterIcon();
            }
            catch (Exception e)
            {
                Log.Error(e, "[TollPlaza] RegisterIcon failed");
            }

            try
            {
                var strings = new Dictionary<string, string>();
                foreach (var plaza in PlazaCatalog.Plazas)
                {
                    strings[$"Assets.NAME[{plaza.id}]"] = plaza.title + " (decorative)";
                    strings[$"Assets.DESCRIPTION[{plaza.id}]"] =
                        "Decorative toll plaza with no road of its own; place it anywhere.";
                    strings[$"Assets.NAME[{plaza.id}{PlazaAssetSystem.RoadSuffix}]"] = plaza.title + " with road";
                    strings[$"Assets.DESCRIPTION[{plaza.id}{PlazaAssetSystem.RoadSuffix}]"] =
                        "American-style toll plaza that brings its own road: place it, then connect your highway to both ends.";
                }
                // The plazas' own tab in the Roads menu (PlazaAssetSystem.TollPlazasTab).
                strings[$"SubServices.NAME[{PlazaAssetSystem.TabName}]"] = "Toll Plazas";
                strings[$"SubServices.DESCRIPTION[{PlazaAssetSystem.TabName}]"] =
                    "Toll plazas that collect tolls from passing vehicles, and decorative plazas without a road.";
                // The Budget screen line (TollBudgetSystem); its one source is IncomeSource.Count by name.
                strings[$"EconomyPanel.BUDGET_ITEM[{TollBudgetSystem.ItemId}]"] = "Toll Plazas";
                strings[$"EconomyPanel.BUDGET_ITEM_DESCRIPTION[{TollBudgetSystem.ItemId}]"] =
                    "Tolls paid by vehicles passing through your toll plazas: last month's total, or this month's so far until a month has passed. Paid into the city's money as vehicles pass.";
                strings["EconomyPanel.BUDGET_SUB_ITEM[Count]"] = "Toll Plazas";
                GameManager.instance.localizationManager.AddSource("en-US", new MemorySource(strings));
            }
            catch (Exception e)
            {
                Log.Error(e, "[TollPlaza] Localization failed");
            }

            updateSystem.UpdateAt<PlazaAssetSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAt<TollCollectionSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<TollUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<PlazaInfoSection>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<PlazaDiagnosticsSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<PlazaPersistence>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<TollBudgetSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<PlazaSignSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<PlazaConnectionGuardSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAfter<ClosedLaneSystem, Game.Pathfind.LaneDataSystem>(SystemUpdatePhase.ModificationEnd);
        }

        // The icon is written next to the DLL at load time and served to the UI through a coui:// host,
        // so the mod stays a single code-only package.
        /// <summary>The installed mod folder (holds the DLL, Icons and Textures).</summary>
        public static string ModDirectory { get; private set; }

        private void RegisterIcon()
        {
            ModDirectory = GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset)
                ? Path.GetDirectoryName(asset.path)
                : Path.GetDirectoryName(typeof(Mod).Assembly.Location);
            string iconDir = Path.Combine(ModDirectory, "Icons");
            Directory.CreateDirectory(iconDir);
            File.WriteAllText(Path.Combine(iconDir, IconFile), IconSvg);
            UIManager.defaultUISystem.AddHostLocation(UiHost, iconDir, false);
            Log.Info("[TollPlaza] Icon host registered at " + iconDir);
        }

        public void OnDispose()
        {
            Log.Info("[TollPlaza] OnDispose");
        }

        // Front view of a plaza: green TOLL sign over the canopy with navy stripe, booths on islands; two lanes
        // closed (red X, gate down) and the right-most open (green arrow, gate rising).
        private const string IconSvg =
@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 128 128"">
  <rect x=""34"" y=""2"" width=""60"" height=""22"" rx=""3"" fill=""#006b38""/>
  <rect x=""36.5"" y=""4.5"" width=""55"" height=""17"" rx=""2"" fill=""none"" stroke=""#fff"" stroke-width=""1.5""/>
  <g fill=""#fff"">
    <rect x=""41"" y=""7"" width=""10"" height=""3""/>
    <rect x=""44.5"" y=""7"" width=""3"" height=""12""/>
    <rect x=""54"" y=""7"" width=""9"" height=""12"" rx=""3""/>
    <rect x=""66"" y=""7"" width=""3"" height=""12""/>
    <rect x=""66"" y=""16"" width=""9"" height=""3""/>
    <rect x=""78"" y=""7"" width=""3"" height=""12""/>
    <rect x=""78"" y=""16"" width=""9"" height=""3""/>
  </g>
  <rect x=""57"" y=""10"" width=""3"" height=""6"" rx=""1"" fill=""#006b38""/>
  <rect x=""62.5"" y=""24"" width=""3"" height=""2"" fill=""#8a8f98""/>
  <rect x=""6"" y=""26"" width=""116"" height=""20"" rx=""2"" fill=""#dcdde0""/>
  <rect x=""6"" y=""26"" width=""116"" height=""6"" rx=""2"" fill=""#1a2e5c""/>
  <rect x=""20"" y=""34"" width=""22"" height=""10"" rx=""1"" fill=""#222""/>
  <rect x=""53"" y=""34"" width=""22"" height=""10"" rx=""1"" fill=""#222""/>
  <g stroke=""#e03a2f"" stroke-width=""2.5"" stroke-linecap=""round"">
    <path d=""M27 35.5 L35 42.5 M35 35.5 L27 42.5""/>
    <path d=""M60 35.5 L68 42.5 M68 35.5 L60 42.5""/>
  </g>
  <rect x=""86"" y=""34"" width=""22"" height=""10"" rx=""1"" fill=""#006b38""/>
  <path d=""M97 36 L97 41 M94 39 L97 42 L100 39"" stroke=""#fff"" stroke-width=""2"" fill=""none"" stroke-linecap=""round"" stroke-linejoin=""round""/>
  <rect x=""10"" y=""46"" width=""6"" height=""52"" fill=""#e6e6e0""/>
  <rect x=""112"" y=""46"" width=""6"" height=""52"" fill=""#e6e6e0""/>
  <g fill=""#eee3cc"">
    <rect x=""40"" y=""70"" width=""14"" height=""28""/>
    <rect x=""74"" y=""70"" width=""14"" height=""28""/>
  </g>
  <g fill=""#38526a"">
    <rect x=""42"" y=""74"" width=""10"" height=""10""/>
    <rect x=""76"" y=""74"" width=""10"" height=""10""/>
  </g>
  <g>
    <rect x=""16"" y=""84"" width=""22"" height=""4"" fill=""#fff""/>
    <rect x=""54"" y=""84"" width=""18"" height=""4"" fill=""#fff""/>
    <rect x=""88"" y=""84"" width=""22"" height=""4"" fill=""#fff"" transform=""rotate(-40 88 86)""/>
  </g>
  <g fill=""#e03a2f"">
    <rect x=""21"" y=""84"" width=""5"" height=""4""/>
    <rect x=""31"" y=""84"" width=""5"" height=""4""/>
    <rect x=""58"" y=""84"" width=""5"" height=""4""/>
    <rect x=""67"" y=""84"" width=""5"" height=""4""/>
    <g transform=""rotate(-40 88 86)"">
      <rect x=""93"" y=""84"" width=""5"" height=""4""/>
      <rect x=""103"" y=""84"" width=""5"" height=""4""/>
    </g>
  </g>
  <rect x=""4"" y=""98"" width=""120"" height=""6"" fill=""#b8b5ad""/>
  <g fill=""#ffc70d"">
    <rect x=""44"" y=""104"" width=""6"" height=""10""/>
    <rect x=""78"" y=""104"" width=""6"" height=""10""/>
  </g>
</svg>";
    }
}
