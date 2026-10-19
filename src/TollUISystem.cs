using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace TollPlaza
{
    /// <summary>
    /// Feeds the in-game Toll Plaza control panel (ui/ project) through the game's UI bindings
    /// (group "tollPlaza"): plaza and booth counts, vehicles and revenue, rates and lane speeds, and a
    /// trigger to change a rate. Values refresh about twice a second.
    /// </summary>
    public partial class TollUISystem : UISystemBase
    {
        private const string Group = "tollPlaza";

        private ValueBinding<int> m_Plazas;
        private ValueBinding<int> m_BoothLanes;
        private ValueBinding<string> m_Revenue;
        private ValueBinding<double> m_RevenueCents;
        private ValueBinding<int> m_Cars;
        private ValueBinding<int> m_Trucks;
        private ValueBinding<int> m_Buses;
        private ValueBinding<int> m_Exempt;
        private ValueBinding<int> m_CarRate;
        private ValueBinding<int> m_TruckRate;
        private ValueBinding<int> m_BusRate;
        private ValueBinding<int> m_CashMph;
        private ValueBinding<int> m_EtcMph;
        private RawValueBinding m_Months;
        private RawValueBinding m_PlazaList;
        private readonly List<(Entity entity, PrefabBase prefab, int lanes)> m_Placed = new List<(Entity, PrefabBase, int)>();

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ObjectQuery;
        private int m_Frame;

        /// <summary>Plaza prefabs placed with roads count their booth lanes; filled by PlazaAssetSystem.</summary>
        public static readonly Dictionary<PrefabBase, int> PlazaLaneCounts = new Dictionary<PrefabBase, int>();

        /// <summary>Placed plazas numbered 1, 2, ... in the order they were built.</summary>
        public static readonly Dictionary<Entity, int> PlazaNumbers = new Dictionary<Entity, int>();

        /// <summary>The plaza's name in the panels (see PlazaNames).</summary>
        public static string NameOf(Entity plaza) => PlazaNames.Of(plaza);

        private NameSystem m_NameSystem;
        private Game.City.CityConfigurationSystem m_CityConfiguration;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_CityConfiguration = World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>();
            m_ObjectQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Object>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            AddBinding(m_Plazas = new ValueBinding<int>(Group, "plazas", 0));
            AddBinding(m_BoothLanes = new ValueBinding<int>(Group, "boothLanes", 0));
            AddBinding(m_Revenue = new ValueBinding<string>(Group, "revenue", "$0.00"));
            AddBinding(m_RevenueCents = new ValueBinding<double>(Group, "revenueCents", 0));
            AddBinding(m_Cars = new ValueBinding<int>(Group, "cars", 0));
            AddBinding(m_Trucks = new ValueBinding<int>(Group, "trucks", 0));
            AddBinding(m_Buses = new ValueBinding<int>(Group, "buses", 0));
            AddBinding(m_Exempt = new ValueBinding<int>(Group, "exempt", 0));
            AddBinding(m_CarRate = new ValueBinding<int>(Group, "carRate", TollState.CarRateCents));
            AddBinding(m_TruckRate = new ValueBinding<int>(Group, "truckRate", TollState.TruckRateCents));
            AddBinding(m_BusRate = new ValueBinding<int>(Group, "busRate", TollState.BusRateCents));
            AddBinding(m_CashMph = new ValueBinding<int>(Group, "cashMph", TollState.CashMph));
            AddBinding(m_EtcMph = new ValueBinding<int>(Group, "etcMph", TollState.EtcMph));

            // City-wide traffic and revenue, this month and last.
            AddBinding(m_Months = new RawValueBinding(Group, "months", writer =>
            {
                writer.TypeBegin("TollPlaza.Stats");
                writer.PropertyName("thisMonth");
                PlazaInfoSection.WriteMonth(writer, PlazaStats.City.ThisMonth);
                writer.PropertyName("lastMonth");
                PlazaInfoSection.WriteMonth(writer, PlazaStats.City.LastMonth);
                writer.TypeEnd();
            }));

            // Every placed plaza with its monthly numbers; the panel links each one to the plaza in the city.
            AddBinding(m_PlazaList = new RawValueBinding(Group, "plazaList", writer =>
            {
                writer.ArrayBegin(m_Placed.Count);
                for (int i = 0; i < m_Placed.Count; i++)
                {
                    (Entity entity, PrefabBase prefab, int lanes) = m_Placed[i];
                    PlazaStats stats = PlazaStats.Of(entity);
                    stats.Roll(PlazaStats.City.Month);
                    writer.TypeBegin("TollPlaza.PlazaItem");
                    writer.PropertyName("entity");
                    writer.Write(entity);
                    writer.PropertyName("name");
                    writer.Write(NameOf(entity));
                    writer.PropertyName("lanes");
                    writer.Write(lanes);
                    writer.PropertyName("outdated");
                    writer.Write(PlazaNames.IsOutdated(entity));
                    writer.PropertyName("thisMonth");
                    PlazaInfoSection.WriteMonth(writer, stats.ThisMonth);
                    writer.PropertyName("lastMonth");
                    PlazaInfoSection.WriteMonth(writer, stats.LastMonth);
                    writer.TypeEnd();
                }
                writer.ArrayEnd();
            }));

            // setRate("car" | "truck" | "bus", deltaCents): the panel's +/- buttons.
            AddBinding(new TriggerBinding<string, int>(Group, "adjustRate", AdjustRate));
            // adjustSpeed("cash" | "etc", deltaMph): the lane speed +/- buttons.
            AddBinding(new TriggerBinding<string, int>(Group, "adjustSpeed", AdjustSpeed));
        }

        /// <summary>Carries out a city reset asked for in Options (Setting), then refreshes the panels.</summary>
        private void ApplyPendingReset()
        {
            Setting.CityReset reset = Setting.PendingReset;
            if (reset == Setting.CityReset.None)
            {
                return;
            }
            Setting.PendingReset = Setting.CityReset.None;
            if (!Game.SceneFlow.GameManager.instance.gameMode.IsGame())
            {
                return;
            }
            if (reset == Setting.CityReset.Everything)
            {
                PlazaStats.ResetEverything();
            }
            else
            {
                PlazaStats.ResetCounts();
            }
            Mod.Log.Info($"[TollPlaza] City toll data reset ({reset}) from Options");
            m_Frame = 29; // refresh the panel numbers on this update
        }

        private void AdjustSpeed(string lane, int deltaMph)
        {
            int Speed(int mph) => mph < 5 ? 5 : (mph > 70 ? 70 : mph);
            if (lane == "cash") TollState.CashMph = Speed(TollState.CashMph + deltaMph);
            if (lane == "etc") TollState.EtcMph = Speed(TollState.EtcMph + deltaMph);
            m_CashMph.Update(TollState.CashMph);
            m_EtcMph.Update(TollState.EtcMph);
        }

        private void AdjustRate(string vehicleClass, int deltaCents)
        {
            switch (vehicleClass)
            {
                case "car": TollState.CarRateCents = Clamp(TollState.CarRateCents + deltaCents); break;
                case "truck": TollState.TruckRateCents = Clamp(TollState.TruckRateCents + deltaCents); break;
                case "bus": TollState.BusRateCents = Clamp(TollState.BusRateCents + deltaCents); break;
            }
            m_CarRate.Update(TollState.CarRateCents);
            m_TruckRate.Update(TollState.TruckRateCents);
            m_BusRate.Update(TollState.BusRateCents);
        }

        private static int Clamp(int cents) => cents < 0 ? 0 : (cents > 10000 ? 10000 : cents);

        protected override void OnUpdate()
        {
            base.OnUpdate();
            ApplyPendingReset();
            if (++m_Frame % 30 != 0)
            {
                return;
            }

            CountPlazas(out int plazas, out int lanes);
            m_Plazas.Update(plazas);
            m_BoothLanes.Update(lanes);
            m_Revenue.Update("$" + (TollState.RevenueCents / 100.0).ToString("N2"));
            m_RevenueCents.Update(TollState.RevenueCents);
            m_Cars.Update(Count(VehicleClass.Car));
            m_Trucks.Update(Count(VehicleClass.Truck));
            m_Buses.Update(Count(VehicleClass.Bus));
            m_Exempt.Update(Count(VehicleClass.Exempt));
            // Rates and speeds change on city load too (restored by PlazaPersistence), not only from the panel.
            m_CarRate.Update(TollState.CarRateCents);
            m_TruckRate.Update(TollState.TruckRateCents);
            m_BusRate.Update(TollState.BusRateCents);
            m_CashMph.Update(TollState.CashMph);
            m_EtcMph.Update(TollState.EtcMph);
            PlazaStats.City.Roll(PlazaStats.MonthKey(World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>()));
            m_Months.Update();
            m_PlazaList.Update();
        }

        private static int Count(VehicleClass c) => TollState.Vehicles.TryGetValue(c, out int n) ? n : 0;

        private readonly Dictionary<Entity, int> m_LanesByPrefabEntity = new Dictionary<Entity, int>();

        private void CountPlazas(out int plazas, out int lanes)
        {
            plazas = 0;
            lanes = 0;
            if (m_LanesByPrefabEntity.Count != PlazaLaneCounts.Count)
            {
                m_LanesByPrefabEntity.Clear();
                foreach (var entry in PlazaLaneCounts)
                {
                    m_LanesByPrefabEntity[m_PrefabSystem.GetEntity(entry.Key)] = entry.Value;
                }
            }
            if (m_LanesByPrefabEntity.Count == 0)
            {
                return;
            }
            m_Placed.Clear();
            using (NativeArray<Entity> entities = m_ObjectQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_ObjectQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (m_LanesByPrefabEntity.TryGetValue(prefabs[i].m_Prefab, out int n))
                    {
                        plazas++;
                        lanes += n;
                        m_Placed.Add((entities[i], m_PrefabSystem.GetPrefab<PrefabBase>(prefabs[i]), n));
                    }
                }
            }
            m_Placed.Sort((a, b) => a.entity.Index.CompareTo(b.entity.Index)); // stable numbering: oldest first
            PlazaNumbers.Clear();
            for (int i = 0; i < m_Placed.Count; i++)
            {
                PlazaNumbers[m_Placed[i].entity] = i + 1;
            }
            PlazaNames.Refresh(EntityManager, m_PrefabSystem, m_NameSystem, m_CityConfiguration.cityName, m_Placed);
            foreach (var placed in m_Placed)
            {
                if (m_LayoutLogged.Add(placed.entity))
                {
                    LogRoadLayout(placed.entity, placed.prefab);
                }
            }
        }

        private readonly HashSet<Entity> m_LayoutLogged = new HashSet<Entity>();

        /// <summary>
        /// Logs a plaza's road pieces once, in plaza coordinates (x across, z along travel): which road, where
        /// it starts and ends, and how many roads meet at each end. For finding why the 6-booth plaza's road
        /// wraps around it instead of running under the booths.
        /// </summary>
        private void LogRoadLayout(Entity plaza, PrefabBase prefab)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubNet>(plaza) || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                Mod.Log.Info($"[TollPlaza] Layout of {NameOf(plaza)} ({prefab.name}): no road pieces.");
                return;
            }
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            Unity.Mathematics.quaternion inverse = Unity.Mathematics.math.inverse(t.m_Rotation);
            string Local(Unity.Mathematics.float3 p)
            {
                Unity.Mathematics.float3 l = Unity.Mathematics.math.mul(inverse, p - t.m_Position);
                return $"({l.x:F1}, {l.y:F1}, {l.z:F1})";
            }
            int Joined(Entity node) =>
                EntityManager.HasBuffer<Game.Net.ConnectedEdge>(node) ? EntityManager.GetBuffer<Game.Net.ConnectedEdge>(node, true).Length : -1;

            var lines = new List<string>();
            DynamicBuffer<Game.Net.SubNet> subNets = EntityManager.GetBuffer<Game.Net.SubNet>(plaza, isReadOnly: true);
            for (int i = 0; i < subNets.Length; i++)
            {
                Entity net = subNets[i].m_SubNet;
                if (!EntityManager.HasComponent<Game.Net.Edge>(net) || !EntityManager.HasComponent<Game.Net.Curve>(net))
                {
                    continue;
                }
                string road = EntityManager.HasComponent<PrefabRef>(net)
                    && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(net), out PrefabBase r) ? r.name : "?";
                Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(net);
                Game.Net.Edge edge = EntityManager.GetComponentData<Game.Net.Edge>(net);
                lines.Add($"{road} {Local(curve.m_Bezier.a)} > {Local(curve.m_Bezier.b)} > {Local(curve.m_Bezier.c)} > {Local(curve.m_Bezier.d)}, "
                    + $"{curve.m_Length:F1} m, roads at ends {Joined(edge.m_Start)}/{Joined(edge.m_End)}");
            }
            Mod.Log.Info($"[TollPlaza] Layout of {NameOf(plaza)} ({prefab.name}, {(PlazaNames.IsOutdated(plaza) ? "old" : "current")}): "
                + string.Join(" | ", lines));
        }
    }
}
