using Colossal.UI.Binding;
using Game.Prefabs;
using Game.Simulation;
using Game.UI.InGame;
using Unity.Entities;

namespace TollPlaza
{
    /// <summary>
    /// "Toll Plaza" section of the game's info panel when a plaza is clicked: this month's and last month's
    /// vehicles and revenue for the whole plaza and for each booth, and a button per booth to switch it
    /// between cash and E-Toll. Drawn by ui/src/mods/plaza-section.tsx.
    /// </summary>
    public partial class PlazaInfoSection : InfoSectionBase
    {
        private PrefabSystem m_PrefabSystem;
        private TimeSystem m_TimeSystem;
        private PlazaLayout m_Layout;

        protected override string group => nameof(PlazaInfoSection);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_InfoUISystem.AddMiddleSection(this);
            AddBinding(new TriggerBinding<int>("tollPlaza", "toggleBooth", ToggleBooth));
            AddBinding(new TriggerBinding<string>("tollPlaza", "renamePlaza", RenamePlaza));
        }

        /// <summary>Names the selected plaza; an empty name goes back to the automatic one.</summary>
        private void RenamePlaza(string name)
        {
            if (m_Layout == null || selectedEntity == Entity.Null)
            {
                return;
            }
            name = name?.Trim();
            if (name != null && name.Length > 40)
            {
                name = name.Substring(0, 40);
            }
            PlazaStats.Of(selectedEntity).Name = string.IsNullOrEmpty(name) ? null : name;
            m_InfoUISystem.RequestUpdate();
        }

        private void ToggleBooth(int lane)
        {
            if (m_Layout != null && selectedEntity != Entity.Null)
            {
                PlazaStats.ToggleCashLane(selectedEntity, m_Layout, lane);
                m_InfoUISystem.RequestUpdate();
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            m_Layout = null;
            if (selectedEntity != Entity.Null && EntityManager.HasComponent<PrefabRef>(selectedEntity)
                && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(selectedEntity), out PrefabBase prefab)
                && TollUISystem.PlazaLaneCounts.ContainsKey(prefab))
            {
                PlazaAssetSystem.PlazaLayouts.TryGetValue(prefab, out m_Layout);
            }
            visible = m_Layout != null;
            // Keep the numbers live while the panel is open.
            if (visible && ++m_Frame % 60 == 0)
            {
                m_InfoUISystem.RequestUpdate();
            }
        }

        private int m_Frame;

        protected override void Reset()
        {
        }

        protected override void OnProcess()
        {
        }

        private int CurrentMonth()
        {
            System.DateTime date = m_TimeSystem.GetCurrentDateTime();
            return date.Year * 12 + date.Month;
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            int month = CurrentMonth();
            PlazaStats plaza = PlazaStats.Of(selectedEntity);
            plaza.Roll(month);

            writer.PropertyName("name");
            // Right after a rename the automatic names haven't been recomputed yet; the typed name shows at once.
            writer.Write(PlazaNames.CustomName(selectedEntity) ?? TollUISystem.NameOf(selectedEntity));
            writer.PropertyName("custom");
            writer.Write(PlazaNames.CustomName(selectedEntity) != null);
            writer.PropertyName("outdated");
            writer.Write(PlazaNames.IsOutdated(selectedEntity));
            writer.PropertyName("plaza");
            WriteStats(writer, plaza);

            writer.PropertyName("booths");
            int lanes = m_Layout?.LaneCount ?? 0;
            writer.ArrayBegin(lanes);
            for (int lane = 0; lane < lanes; lane++)
            {
                PlazaStats booth = plaza.Booth(lane);
                booth.Roll(month);
                writer.TypeBegin("TollPlaza.Booth");
                writer.PropertyName("cash");
                writer.Write(PlazaStats.IsCashLane(selectedEntity, m_Layout, lane));
                writer.PropertyName("stats");
                WriteStats(writer, booth);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static void WriteStats(IJsonWriter writer, PlazaStats stats)
        {
            writer.TypeBegin("TollPlaza.Stats");
            writer.PropertyName("thisMonth");
            WriteMonth(writer, stats.ThisMonth);
            writer.PropertyName("lastMonth");
            WriteMonth(writer, stats.LastMonth);
            writer.TypeEnd();
        }

        /// <summary>Also used by the control panel for the city-wide totals.</summary>
        public static void WriteMonth(IJsonWriter writer, MonthStats month)
        {
            writer.TypeBegin("TollPlaza.Month");
            writer.PropertyName("cars");
            writer.Write(month.Cars);
            writer.PropertyName("trucks");
            writer.Write(month.Trucks);
            writer.PropertyName("buses");
            writer.Write(month.Buses);
            writer.PropertyName("exempt");
            writer.Write(month.Exempt);
            writer.PropertyName("cents");
            writer.Write((double)month.Cents);
            writer.TypeEnd();
        }
    }
}
