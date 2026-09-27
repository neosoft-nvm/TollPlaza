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
        private ValueBinding<int> m_Cars;
        private ValueBinding<int> m_Trucks;
        private ValueBinding<int> m_Buses;
        private ValueBinding<int> m_Exempt;
        private ValueBinding<int> m_CarRate;
        private ValueBinding<int> m_TruckRate;
        private ValueBinding<int> m_BusRate;
        private ValueBinding<int> m_CashMph;
        private ValueBinding<int> m_EtcMph;

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ObjectQuery;
        private int m_Frame;

        /// <summary>Plaza prefabs placed with roads count their booth lanes; filled by PlazaAssetSystem.</summary>
        public static readonly Dictionary<PrefabBase, int> PlazaLaneCounts = new Dictionary<PrefabBase, int>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ObjectQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Object>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            AddBinding(m_Plazas = new ValueBinding<int>(Group, "plazas", 0));
            AddBinding(m_BoothLanes = new ValueBinding<int>(Group, "boothLanes", 0));
            AddBinding(m_Revenue = new ValueBinding<string>(Group, "revenue", "$0.00"));
            AddBinding(m_Cars = new ValueBinding<int>(Group, "cars", 0));
            AddBinding(m_Trucks = new ValueBinding<int>(Group, "trucks", 0));
            AddBinding(m_Buses = new ValueBinding<int>(Group, "buses", 0));
            AddBinding(m_Exempt = new ValueBinding<int>(Group, "exempt", 0));
            AddBinding(m_CarRate = new ValueBinding<int>(Group, "carRate", TollState.CarRateCents));
            AddBinding(m_TruckRate = new ValueBinding<int>(Group, "truckRate", TollState.TruckRateCents));
            AddBinding(m_BusRate = new ValueBinding<int>(Group, "busRate", TollState.BusRateCents));
            AddBinding(m_CashMph = new ValueBinding<int>(Group, "cashMph", PlazaLayout.CashSpeedMph));
            AddBinding(m_EtcMph = new ValueBinding<int>(Group, "etcMph", PlazaLayout.EtcSpeedMph));

            // setRate("car" | "truck" | "bus", deltaCents): the panel's +/- buttons.
            AddBinding(new TriggerBinding<string, int>(Group, "adjustRate", AdjustRate));
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
            if (++m_Frame % 30 != 0)
            {
                return;
            }

            CountPlazas(out int plazas, out int lanes);
            m_Plazas.Update(plazas);
            m_BoothLanes.Update(lanes);
            m_Revenue.Update("$" + (TollState.RevenueCents / 100.0).ToString("N2"));
            m_Cars.Update(Count(VehicleClass.Car));
            m_Trucks.Update(Count(VehicleClass.Truck));
            m_Buses.Update(Count(VehicleClass.Bus));
            m_Exempt.Update(Count(VehicleClass.Exempt));
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
            using (NativeArray<PrefabRef> prefabs = m_ObjectQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (m_LanesByPrefabEntity.TryGetValue(prefabs[i].m_Prefab, out int n))
                    {
                        plazas++;
                        lanes += n;
                    }
                }
            }
        }
    }
}
