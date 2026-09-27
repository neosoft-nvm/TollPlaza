using System.Collections.Generic;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace TollPlaza
{
    public enum VehicleClass
    {
        Car,
        Truck,
        Bus,
        Exempt, // emergency and city service vehicles ride free, as on real toll roads
    }

    /// <summary>Toll rates in cents and running totals; shared with the (upcoming) control panel.</summary>
    public static class TollState
    {
        public static int CarRateCents = 250;
        public static int TruckRateCents = 600;
        public static int BusRateCents = 400;

        public static long RevenueCents;
        public static readonly Dictionary<VehicleClass, int> Vehicles = new Dictionary<VehicleClass, int>();
        public static readonly Dictionary<Entity, (int vehicles, long cents)> PerPlaza = new Dictionary<Entity, (int, long)>();

        public static int RateCents(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Truck: return TruckRateCents;
                case VehicleClass.Bus: return BusRateCents;
                case VehicleClass.Exempt: return 0;
                default: return CarRateCents;
            }
        }
    }

    /// <summary>
    /// Charges every vehicle that drives through a plaza's toll lanes and credits the city.
    /// Toll lanes are the edges built from the plaza's own lane roads ("TollPlaza Cash Lane" /
    /// "TollPlaza E-Toll Lane"). Rather than scanning every car in the city, it reads the vehicle list
    /// each of those lanes keeps (LaneObject buffer). A vehicle pays once per plaza visit: it's
    /// remembered while it stays on that plaza's toll lanes and forgotten a few seconds after leaving.
    /// Money is added on the main thread through EntityManager, the way the game pays milestone rewards,
    /// so no scheduled job can overwrite it.
    /// </summary>
    public partial class TollCollectionSystem : GameSystemBase
    {
        private const int UpdateInterval = 16;          // simulation frames between updates
        private const int ForgetAfterUpdates = 40;      // ~10 s of game time off the toll lanes
        private const int RefreshEdgesEveryUpdates = 16;

        private CitySystem m_CitySystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_EdgeQuery;

        private readonly List<(Entity edge, Entity plaza)> m_TollEdges = new List<(Entity, Entity)>();
        private readonly Dictionary<(Entity vehicle, Entity plaza), int> m_LastSeen = new Dictionary<(Entity, Entity), int>();
        private int m_Tick;
        private int m_PendingCents;
        private readonly HashSet<Entity> m_TollRoadPrefabs = new HashSet<Entity>();

        public override int GetUpdateInterval(SystemUpdatePhase phase) => UpdateInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_EdgeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Net.SubLane>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnUpdate()
        {
            if (!ResolveLanePrefabs() || m_CitySystem.City == Entity.Null)
            {
                return;
            }

            m_Tick++;
            if (m_Tick % RefreshEdgesEveryUpdates == 1 || m_TollEdges.Count == 0)
            {
                RefreshTollEdges();
            }

            foreach ((Entity edge, Entity plaza) in m_TollEdges)
            {
                if (!EntityManager.Exists(edge) || !EntityManager.HasBuffer<Game.Net.SubLane>(edge))
                {
                    continue;
                }
                DynamicBuffer<Game.Net.SubLane> subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(edge, isReadOnly: true);
                for (int i = 0; i < subLanes.Length; i++)
                {
                    Entity lane = subLanes[i].m_SubLane;
                    if (!EntityManager.HasComponent<Game.Net.CarLane>(lane) || !EntityManager.HasBuffer<LaneObject>(lane))
                    {
                        continue;
                    }
                    ApplyLaneSpeed(lane, plaza);
                    DynamicBuffer<LaneObject> objects = EntityManager.GetBuffer<LaneObject>(lane, isReadOnly: true);
                    for (int j = 0; j < objects.Length; j++)
                    {
                        Entity vehicle = objects[j].m_LaneObject;
                        if (EntityManager.HasComponent<Game.Vehicles.CarTrailer>(vehicle))
                        {
                            continue; // the truck pulling it pays
                        }
                        var key = (vehicle, plaza);
                        if (!m_LastSeen.ContainsKey(key))
                        {
                            Charge(vehicle, plaza);
                        }
                        m_LastSeen[key] = m_Tick;
                    }
                }
            }

            Forget();
            PayCity();

            if (m_Tick % 240 == 0)
            {
                Mod.Log.Info($"[TollPlaza] Tolls so far: ${TollState.RevenueCents / 100.0:F2} from "
                    + $"{Count(VehicleClass.Car)} cars, {Count(VehicleClass.Truck)} trucks, "
                    + $"{Count(VehicleClass.Bus)} buses, {Count(VehicleClass.Exempt)} exempt; "
                    + $"{m_TollEdges.Count} toll lane segments");
            }
        }

        private static int Count(VehicleClass c) => TollState.Vehicles.TryGetValue(c, out int n) ? n : 0;

        private bool ResolveLanePrefabs()
        {
            if (m_TollRoadPrefabs.Count == PlazaRoad.TollRoads.Count && m_TollRoadPrefabs.Count > 0)
            {
                return true;
            }
            m_TollRoadPrefabs.Clear();
            foreach (NetPrefab road in PlazaRoad.TollRoads)
            {
                m_TollRoadPrefabs.Add(m_PrefabSystem.GetEntity(road));
            }
            return m_TollRoadPrefabs.Count > 0;
        }

        /// <summary>
        /// The plaza highway runs at the E-Toll speed; cash lanes are slowed here, lane by lane. A lane is a
        /// cash lane when its center, in the plaza's own coordinates, is nearest a cash lane of the layout.
        /// Re-applied every update in case the game recalculates lane speeds.
        /// </summary>
        private void ApplyLaneSpeed(Entity lane, Entity plaza)
        {
            PlazaLayout layout = LayoutOf(plaza);
            if (layout == null || !EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                return;
            }
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            float3 mid = MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier, 0.5f);
            float3 local = math.mul(math.inverse(t.m_Rotation), mid - t.m_Position);

            int nearest = 0;
            for (int i = 1; i < layout.LaneCount; i++)
            {
                if (math.abs(layout.LaneCenterX(i) - local.x) < math.abs(layout.LaneCenterX(nearest) - local.x))
                {
                    nearest = i;
                }
            }
            float mph = layout.IsCashLane(nearest) ? PlazaLayout.CashSpeedMph : PlazaLayout.EtcSpeedMph;
            float metersPerSecond = mph * 0.44704f;

            Game.Net.CarLane carLane = EntityManager.GetComponentData<Game.Net.CarLane>(lane);
            if (math.abs(carLane.m_SpeedLimit - metersPerSecond) > 0.01f)
            {
                carLane.m_SpeedLimit = metersPerSecond;
                EntityManager.SetComponentData(lane, carLane);
            }
        }

        private readonly Dictionary<Entity, PlazaLayout> m_Layouts = new Dictionary<Entity, PlazaLayout>();

        private PlazaLayout LayoutOf(Entity plaza)
        {
            if (m_Layouts.TryGetValue(plaza, out PlazaLayout layout))
            {
                return layout;
            }
            layout = null;
            if (EntityManager.HasComponent<PrefabRef>(plaza)
                && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(plaza), out PrefabBase prefab))
            {
                PlazaAssetSystem.PlazaLayouts.TryGetValue(prefab, out layout);
            }
            m_Layouts[plaza] = layout;
            return layout;
        }

        private void RefreshTollEdges()
        {
            m_TollEdges.Clear();
            using (NativeArray<Entity> edges = m_EdgeQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = m_EdgeQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity prefab = prefabs[i].m_Prefab;
                    if (!m_TollRoadPrefabs.Contains(prefab))
                    {
                        continue;
                    }
                    // The plaza object owns its sub-net edges; group stats by plaza.
                    Entity plaza = EntityManager.HasComponent<Owner>(edges[i])
                        ? EntityManager.GetComponentData<Owner>(edges[i]).m_Owner
                        : edges[i];
                    m_TollEdges.Add((edges[i], plaza));
                }
            }
        }

        private void Charge(Entity vehicle, Entity plaza)
        {
            VehicleClass cls = Classify(vehicle);
            int cents = TollState.RateCents(cls);
            TollState.Vehicles.TryGetValue(cls, out int count);
            TollState.Vehicles[cls] = count + 1;
            if (cents <= 0)
            {
                return;
            }
            TollState.RevenueCents += cents;
            m_PendingCents += cents;
            TollState.PerPlaza.TryGetValue(plaza, out var stats);
            TollState.PerPlaza[plaza] = (stats.vehicles + 1, stats.cents + cents);
        }

        private VehicleClass Classify(Entity vehicle)
        {
            if (EntityManager.HasComponent<Game.Vehicles.Ambulance>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.FireEngine>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.PoliceCar>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.Hearse>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.GarbageTruck>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.MaintenanceVehicle>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.PostVan>(vehicle))
            {
                return VehicleClass.Exempt;
            }
            if (EntityManager.HasComponent<Game.Vehicles.PublicTransport>(vehicle))
            {
                return VehicleClass.Bus;
            }
            if (EntityManager.HasComponent<Game.Vehicles.DeliveryTruck>(vehicle)
                || EntityManager.HasComponent<Game.Vehicles.CargoTransport>(vehicle))
            {
                return VehicleClass.Truck;
            }
            return VehicleClass.Car;
        }

        private readonly List<(Entity, Entity)> m_ToForget = new List<(Entity, Entity)>();

        private void Forget()
        {
            m_ToForget.Clear();
            foreach (var entry in m_LastSeen)
            {
                if (m_Tick - entry.Value > ForgetAfterUpdates)
                {
                    m_ToForget.Add(entry.Key);
                }
            }
            foreach (var key in m_ToForget)
            {
                m_LastSeen.Remove(key);
            }
        }

        /// <summary>Credits whole dollars to the city; cents carry over to the next update.</summary>
        private void PayCity()
        {
            int dollars = m_PendingCents / 100;
            if (dollars <= 0)
            {
                return;
            }
            m_PendingCents -= dollars * 100;
            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(m_CitySystem.City);
            money.Add(dollars);
            EntityManager.SetComponentData(m_CitySystem.City, money);
        }
    }
}
