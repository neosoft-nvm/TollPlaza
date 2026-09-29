using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Toll rates in cents and running totals; shared with the control panel and PlazaPersistence.</summary>
    public static class TollState
    {
        public const int DefaultCarRateCents = 250;
        public const int DefaultTruckRateCents = 600;
        public const int DefaultBusRateCents = 400;

        public static int CarRateCents = DefaultCarRateCents;
        public static int TruckRateCents = DefaultTruckRateCents;
        public static int BusRateCents = DefaultBusRateCents;

        /// <summary>Booth lane speed limits in mph, set from the control panel and applied lane by lane.</summary>
        public static int CashMph = PlazaLayout.CashSpeedMph;
        public static int EtcMph = PlazaLayout.EtcSpeedMph;
        /// <summary>Speed at the end of the approach and on the exit: never below the faster booth lanes.</summary>
        public static int ZoneMph => System.Math.Max(PlazaLayout.ZoneSpeedMph, System.Math.Max(CashMph, EtcMph));

        public static long RevenueCents;
        public static readonly Dictionary<VehicleClass, int> Vehicles = new Dictionary<VehicleClass, int>();

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

        public static int Count(VehicleClass c) => Vehicles.TryGetValue(c, out int n) ? n : 0;

        /// <summary>Back to a new city's defaults (every city load starts here, then PlazaPersistence restores).</summary>
        public static void Reset()
        {
            CarRateCents = DefaultCarRateCents;
            TruckRateCents = DefaultTruckRateCents;
            BusRateCents = DefaultBusRateCents;
            CashMph = PlazaLayout.CashSpeedMph;
            EtcMph = PlazaLayout.EtcSpeedMph;
            RevenueCents = 0;
            Vehicles.Clear();
        }
    }

    /// <summary>
    /// Charges every vehicle that drives through a plaza's toll lanes and credits the city.
    /// Toll lanes are the car lanes of the plaza's own road (approach, booths and exit). Every few frames it
    /// checks each car's current lane against them. A vehicle pays once per plaza visit, the first time it's
    /// seen there; it's counted for a booth when it reaches the booth lanes. It's remembered while it stays on
    /// that plaza's lanes and forgotten a few seconds after leaving.
    /// Money is added on the main thread through EntityManager, the way the game pays milestone rewards,
    /// so no scheduled job can overwrite it.
    /// </summary>
    public partial class TollCollectionSystem : GameSystemBase
    {
        // Checked often enough that no vehicle crosses the plaza between two looks, even at E-Toll speed.
        private const int UpdateInterval = 4;           // simulation frames between updates
        private const int ForgetAfterUpdates = 160;     // ~10 s of game time off the plaza's road
        private const int RefreshEdgesEveryUpdates = 64;
        // Vehicles already inside a plaza when a city loads were never seen paying; the exit check skips them.
        private const int SettleAfterLoadUpdates = 60;

        /// <summary>
        /// Lane speed limit (m/s, as CarLane.m_SpeedLimit stores it) for a speed in mph as players read it.
        /// Lanes run at twice the speed the game shows (the base game's highways log as 66.7 m/s, 149 mph,
        /// for a road signed half that), so the booth and zone speeds are doubled to match the player's roads.
        /// </summary>
        public static float LaneSpeed(float mph) => mph * MphToMps * GameSpeedScale;
        public static float ShownMph(float laneSpeed) => laneSpeed / (MphToMps * GameSpeedScale);
        private const float MphToMps = 0.44704f;
        private const float GameSpeedScale = 2f;

        // Braking probe: the game moves cars in 16-frame steps (4/15 s), so speed is compared over at least that.
        private const float SimFramesPerSecond = 60f;
        private const int SpeedWindowUpdates = 4;
        /// <summary>Peak deceleration above this counts as hard braking (about 0.6 g; the cars' limit is ~1 g).</summary>
        private const float HardBrakingMps2 = 6f;

        private CitySystem m_CitySystem;
        private TimeSystem m_TimeSystem;
        private PrefabSystem m_PrefabSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_EdgeQuery;
        private EntityQuery m_CarQuery;
        private EntityQuery m_TrailerQuery;
        private EntityQuery m_RerouteQuery;

        private readonly List<(Entity edge, Entity plaza, bool booths)> m_TollEdges = new List<(Entity, Entity, bool)>();
        private readonly Dictionary<Entity, (Entity plaza, int booth, bool boothLane, bool pastBooths)> m_PlazaLanes =
            new Dictionary<Entity, (Entity, int, bool, bool)>();

        /// <summary>One vehicle's pass through one plaza.</summary>
        private struct Pass
        {
            public int LastSeen;
            public VehicleClass Class;
            public int Cents;
            public bool Charged;
            // Braking probe
            public float SpeedAt;
            public int SpeedTick;
            public float PeakDecel;
            public bool Measured;
            public bool StopLogged;
            // Sideways probe
            public bool SidewaysLogged;
            public bool SqueezeLogged;
        }

        private readonly Dictionary<(Entity vehicle, Entity plaza), Pass> m_Passes = new Dictionary<(Entity, Entity), Pass>();
        private int m_Tick;
        private int m_PendingCents;
        private (long, int, int) m_LastLogged = (-1, -1, -1);
        private readonly HashSet<Entity> m_TollRoadPrefabs = new HashSet<Entity>();

        // Braking probe totals since the last summary line.
        private int m_BrakePasses, m_HardBrakePasses;
        private float m_BrakePeakSum, m_BrakePeakMax;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => UpdateInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_EdgeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Net.SubLane>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            // Trailers ride on their own lane component, so the truck pulling one is the one charged.
            m_CarQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Vehicles.CarCurrentLane>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_RerouteQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Vehicles.CarCurrentLane>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadWrite<Game.Pathfind.PathOwner>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_TrailerQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Vehicles.CarTrailerLane>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        /// <summary>Forgets everything about the previous city (entities are renumbered on every load).</summary>
        public void ResetForNewCity()
        {
            m_TollEdges.Clear();
            m_PlazaLanes.Clear();
            m_Passes.Clear();
            m_ExitLanes.Clear();
            m_ExitSeen.Clear();
            m_EntrySpeeds.Clear();
            m_LoggedApproach.Clear();
            m_Layouts.Clear();
            m_Lengths.Clear();
            ClosedLaneSystem.Lanes.Clear();
            m_Rerouted.Clear();
            m_ClosedVersion = -1;
            m_ReroutedCount = 0;
            m_ChangedPlazas.Clear();
            m_Tick = 0;
            m_PendingCents = 0;
            m_ExitTotal = m_ExitMissed = 0;
            m_LastLogged = (-1, -1, -1);
            m_BrakePasses = m_HardBrakePasses = 0;
            m_BrakePeakSum = m_BrakePeakMax = 0f;
            m_StopPlaces.Clear();
            m_StopDetailsLogged = 0;
        }

        protected override void OnUpdate()
        {
            if (!ResolveLanePrefabs() || m_CitySystem.City == Entity.Null)
            {
                return;
            }

            m_Tick++;
            if (PlazaStats.MeasuringSince == null)
            {
                PlazaStats.MeasuringSince = m_SimulationSystem.frameIndex;
            }
            if (m_Tick % RefreshEdgesEveryUpdates == 1 || m_TollEdges.Count == 0)
            {
                RefreshTollEdges();
                FindPlazaEnds();
            }

            // The plaza's car lanes (which plaza, which booth), with the lane speeds re-applied in case the
            // game recalculates them.
            m_PlazaLanes.Clear();
            foreach ((Entity edge, Entity plaza, bool booths) in m_TollEdges)
            {
                if (!EntityManager.Exists(edge) || !EntityManager.HasBuffer<Game.Net.SubLane>(edge))
                {
                    continue;
                }
                AddPlazaLanes(edge, plaza, booths);
                // The joints between the plaza's pieces have lanes of their own (a car crossing a joint is on
                // the node, not an edge).
                Edge e = EntityManager.GetComponentData<Edge>(edge);
                AddPlazaLanes(e.m_Start, plaza, false);
                AddPlazaLanes(e.m_End, plaza, false);
            }

            // Every car in the city knows the lane it's on: whoever is on a plaza lane pays (once per pass).
            // The lanes' own vehicle lists missed fast cars, so they're not used.
            if (m_PlazaLanes.Count > 0)
            {
                System.DateTime date = m_TimeSystem.GetCurrentDateTime();
                int month = date.Year * 12 + date.Month;
                using (NativeArray<Entity> vehicles = m_CarQuery.ToEntityArray(Allocator.Temp))
                using (NativeArray<Game.Vehicles.CarCurrentLane> current = m_CarQuery.ToComponentDataArray<Game.Vehicles.CarCurrentLane>(Allocator.Temp))
                {
                    for (int j = 0; j < vehicles.Length; j++)
                    {
                        if (!m_PlazaLanes.TryGetValue(current[j].m_Lane, out var at)
                            && !m_PlazaLanes.TryGetValue(current[j].m_ChangeLane, out at))
                        {
                            continue;
                        }
                        var key = (vehicles[j], at.plaza);
                        m_Passes.TryGetValue(key, out Pass pass);
                        // Charged at the booth it drives through, like a real plaza, so the plaza's count always
                        // matches its booths' (cars still queuing on the approach aren't counted yet). A vehicle
                        // first seen past the booths (the plaza was placed around it) pays there instead.
                        if (!pass.Charged && (at.boothLane || at.pastBooths))
                        {
                            Charge(vehicles[j], at.plaza, month, ref pass);
                            if (at.boothLane && at.booth >= 0)
                            {
                                PlazaStats booth = PlazaStats.Of(at.plaza).Booth(at.booth);
                                booth.Roll(month);
                                booth.ThisMonth.Add(pass.Class, pass.Cents);
                                booth.Record(m_SimulationSystem.frameIndex);
                            }
                        }
                        if (at.boothLane || at.pastBooths)
                        {
                            HoldGate(vehicles[j], at.plaza);
                        }
                        MeasureBraking(vehicles[j], at.plaza, ref pass);
                        ProbeSideways(vehicles[j], at.plaza, current[j], ref pass);
                        pass.LastSeen = m_Tick;
                        m_Passes[key] = pass;
                    }

                    // Trailers keep their truck's gate up until they're through too.
                    using (NativeArray<Entity> trailers = m_TrailerQuery.ToEntityArray(Allocator.Temp))
                    using (NativeArray<Game.Vehicles.CarTrailerLane> trailerLanes = m_TrailerQuery.ToComponentDataArray<Game.Vehicles.CarTrailerLane>(Allocator.Temp))
                    {
                        for (int j = 0; j < trailers.Length; j++)
                        {
                            if (!m_PlazaLanes.TryGetValue(trailerLanes[j].m_Lane, out var at))
                            {
                                continue;
                            }
                            if (at.boothLane || at.pastBooths)
                            {
                                HoldGate(trailers[j], at.plaza);
                            }
                            AddSample(trailers[j], at.plaza);
                        }
                    }
                    CheckSqueezes();

                    // Check: every vehicle leaving on the player's road past the exit should have paid.
                    for (int j = 0; j < vehicles.Length; j++)
                    {
                        if (!m_ExitLanes.TryGetValue(current[j].m_Lane, out Entity plaza))
                        {
                            continue;
                        }
                        var key = (vehicles[j], plaza);
                        if (m_ExitSeen.ContainsKey(key))
                        {
                            m_ExitSeen[key] = m_Tick;
                            continue;
                        }
                        m_ExitSeen[key] = m_Tick;
                        if (m_Tick <= SettleAfterLoadUpdates)
                        {
                            continue;
                        }
                        m_ExitTotal++;
                        if (!m_Passes.TryGetValue(key, out Pass paid) || !paid.Charged)
                        {
                            m_ExitMissed++;
                            if (m_ExitMissed <= 8)
                            {
                                LogMissed(vehicles[j], plaza);
                            }
                        }
                    }
                }
            }

            if (m_Tick % RerouteEveryUpdates == 0)
            {
                RerouteAroundClosedLanes();
            }
            Forget();
            PayCity();

            // Logged whenever tolls or toll lanes change (at most every ~4 s), so a test shows money coming in.
            var summary = (TollState.RevenueCents, m_TollEdges.Count, m_ExitTotal);
            if (m_Tick % 64 == 0 && summary != m_LastLogged)
            {
                m_LastLogged = summary;
                Mod.Log.Info($"[TollPlaza] Tolls so far: ${TollState.RevenueCents / 100.0:F2} from "
                    + $"{TollState.Count(VehicleClass.Car)} cars, {TollState.Count(VehicleClass.Truck)} trucks, "
                    + $"{TollState.Count(VehicleClass.Bus)} buses, {TollState.Count(VehicleClass.Exempt)} exempt; "
                    + $"{m_TollEdges.Count} toll lane segments; city money ${EntityManager.GetComponentData<PlayerMoney>(m_CitySystem.City).money}; "
                    + $"check: {m_ExitTotal} vehicles left past the exits, {m_ExitMissed} of them unpaid");
                if (m_BrakePasses > 0)
                {
                    Mod.Log.Info($"[TollPlaza] Braking in plazas: {m_BrakePasses} passes, peak deceleration avg "
                        + $"{m_BrakePeakSum / m_BrakePasses:F1} m/s2, max {m_BrakePeakMax:F1} m/s2, "
                        + $"{100f * m_HardBrakePasses / m_BrakePasses:F0}% braked harder than {HardBrakingMps2:F0} m/s2");
                    m_BrakePasses = m_HardBrakePasses = 0;
                    m_BrakePeakSum = m_BrakePeakMax = 0f;
                }
                if (m_StopPlaces.Count > 0)
                {
                    Mod.Log.Info("[TollPlaza] Hard stops and reversing by place: "
                        + string.Join(", ", m_StopPlaces.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
                    m_StopPlaces.Clear();
                    m_StopDetailsLogged = 0;
                    m_SidewaysLogged = m_SqueezesLogged = 0;
                }
            }
        }

        // Stop probe: where in the plaza cars brake hard or reverse, and what they were waiting for.
        private const int StopDetailsPerSummary = 6;
        private readonly Dictionary<string, int> m_StopPlaces = new Dictionary<string, int>();
        private int m_StopDetailsLogged;

        /// <summary>
        /// Tracks a vehicle's hardest braking while it's in the plaza (speed drop per game second), and logs
        /// where hard stops and reversing happen. Speed is signed along the car's heading, so backing up counts.
        /// </summary>
        private void MeasureBraking(Entity vehicle, Entity plaza, ref Pass pass)
        {
            if (!EntityManager.HasComponent<Game.Objects.Moving>(vehicle) || !EntityManager.HasComponent<Game.Objects.Transform>(vehicle))
            {
                return;
            }
            Game.Objects.Transform transform = EntityManager.GetComponentData<Game.Objects.Transform>(vehicle);
            float3 velocity = EntityManager.GetComponentData<Game.Objects.Moving>(vehicle).m_Velocity;
            float speed = math.dot(velocity, math.forward(transform.m_Rotation));
            bool reversing = speed < -0.5f;
            if (pass.SpeedTick == 0)
            {
                pass.SpeedAt = speed;
                pass.SpeedTick = m_Tick;
                return;
            }
            int updates = m_Tick - pass.SpeedTick;
            if (updates < SpeedWindowUpdates && !reversing)
            {
                return;
            }
            float decel = 0f;
            if (updates >= SpeedWindowUpdates && updates <= SpeedWindowUpdates * 2)
            {
                float seconds = updates * UpdateInterval / SimFramesPerSecond;
                decel = (pass.SpeedAt - speed) / seconds;
                pass.PeakDecel = math.max(pass.PeakDecel, decel);
                pass.Measured = true;
            }
            if ((decel > HardBrakingMps2 || reversing) && !pass.StopLogged)
            {
                pass.StopLogged = true;
                LogStop(vehicle, plaza, transform, pass.SpeedAt, speed, reversing);
            }
            if (updates >= SpeedWindowUpdates)
            {
                pass.SpeedAt = speed;
                pass.SpeedTick = m_Tick;
            }
        }

        /// <summary>
        /// Raises the lane's exit gate (signal green) just in time for the vehicle's front to reach it, and keeps
        /// it up until its back is through (trucks and buses too; a truck's trailer is checked on its own). The
        /// raise starts when the front is the arm's raise time away at the vehicle's speed. The lane is the one
        /// the vehicle is actually in (by its position), not the lane piece it's on.
        /// </summary>
        private void HoldGate(Entity vehicle, Entity plaza)
        {
            PlazaLayout layout = LayoutOf(plaza);
            if (layout == null || !EntityManager.HasComponent<Game.Objects.Transform>(vehicle)
                || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                return;
            }
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            quaternion toLocal = math.inverse(t.m_Rotation);
            float3 local = math.mul(toLocal, EntityManager.GetComponentData<Game.Objects.Transform>(vehicle).m_Position - t.m_Position);
            if (local.z < layout.IslandStartZ - GateClearMeters)
            {
                return; // still on the approach, may yet change lanes
            }
            float2 length = LengthOf(vehicle);
            float front = local.z + length.y;
            float back = local.z + length.x;
            float speed = EntityManager.HasComponent<Game.Objects.Moving>(vehicle)
                ? math.max(0f, math.mul(toLocal, EntityManager.GetComponentData<Game.Objects.Moving>(vehicle).m_Velocity).z)
                : 0f;
            // The lead covers the arm's raise and the gap between two looks, plus a little room.
            float lead = math.min(speed * (LaneGate.RaiseSeconds + UpdateInterval / SimFramesPerSecond) + GateClearMeters, GateOpenMaxMeters);
            if (front < layout.GateZ - lead || back > layout.GateZ + GateClearMeters)
            {
                return;
            }
            int lane = 0;
            for (int i = 1; i < layout.LaneCount; i++)
            {
                if (math.abs(layout.LaneCenterX(i) - local.x) < math.abs(layout.LaneCenterX(lane) - local.x))
                {
                    lane = i;
                }
            }
            PlazaStats.Of(plaza).Booth(lane).Gate.Open(m_SimulationSystem.frameIndex);
        }

        // A gate starts rising at most this far ahead of a vehicle (a fast one); it stays up until the vehicle's
        // back is this far past it, and starts rising when the front is at least this far off.
        private const float GateOpenMaxMeters = 40f;
        private const float GateClearMeters = 1.5f;

        private readonly Dictionary<Entity, float2> m_Lengths = new Dictionary<Entity, float2>();

        /// <summary>Back and front of a vehicle along its heading, relative to its position (from its model).</summary>
        private float2 LengthOf(Entity vehicle)
        {
            if (!EntityManager.HasComponent<PrefabRef>(vehicle))
            {
                return new float2(-2.5f, 2.5f);
            }
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab;
            if (!m_Lengths.TryGetValue(prefab, out float2 length))
            {
                length = EntityManager.HasComponent<ObjectGeometryData>(prefab)
                    ? new float2(EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds.min.z, EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds.max.z)
                    : new float2(-2.5f, 2.5f);
                m_Lengths[prefab] = length;
            }
            return length;
        }

        private void LogStop(Entity vehicle, Entity plaza, Game.Objects.Transform transform, float before, float after, bool reversing)
        {
            PlazaLayout layout = LayoutOf(plaza);
            string place = "unknown";
            float3 local = default;
            if (layout != null && EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
                local = math.mul(math.inverse(t.m_Rotation), transform.m_Position - t.m_Position);
                place = PlaceOf(layout, local.z);
            }
            string key = place + (reversing ? " (reversing)" : string.Empty);
            m_StopPlaces.TryGetValue(key, out int n);
            m_StopPlaces[key] = n + 1;
            if (m_StopDetailsLogged >= StopDetailsPerSummary)
            {
                return;
            }
            m_StopDetailsLogged++;

            string blocker = "nothing";
            if (EntityManager.HasComponent<Game.Vehicles.Blocker>(vehicle))
            {
                Game.Vehicles.Blocker b = EntityManager.GetComponentData<Game.Vehicles.Blocker>(vehicle);
                if (b.m_Type != Game.Vehicles.BlockerType.None || b.m_Blocker != Entity.Null)
                {
                    blocker = $"{b.m_Type} by {NameOf(b.m_Blocker)} (max speed {b.m_MaxSpeed})";
                }
            }
            string lanes = "?";
            if (EntityManager.HasComponent<Game.Vehicles.CarCurrentLane>(vehicle))
            {
                Game.Vehicles.CarCurrentLane lane = EntityManager.GetComponentData<Game.Vehicles.CarCurrentLane>(vehicle);
                lanes = $"lane {OwnerName(lane.m_Lane)}"
                    + (lane.m_ChangeLane != Entity.Null ? $", changing to {OwnerName(lane.m_ChangeLane)} ({lane.m_ChangeProgress:F2})" : string.Empty)
                    + $", flags {lane.m_LaneFlags}";
            }
            Mod.Log.Info($"[TollPlaza] Hard stop: {NameOf(vehicle)} at {place}, plaza-local x {local.x:F1} z {local.z:F1}, "
                + $"speed {ShownMph(before):F0} > {ShownMph(after):F0} mph{(reversing ? ", REVERSING" : string.Empty)}; "
                + $"blocked: {blocker}; {lanes}");
        }

        // ---- Sideways probe (temporary, to-do 12): cars turned across the lanes, and vehicles squeezed side by side ----

        /// <summary>A car this far off the plaza's direction of travel is logged as sideways.</summary>
        private const float SidewaysDegrees = 35f;
        private const int SidewaysDetailsPerSummary = 8;
        private int m_SidewaysLogged;
        private int m_SqueezesLogged;
        private int m_SidewaysCount, m_SqueezeCount;

        // This update's vehicles on plaza lanes: plaza-local position, heading and size, for the squeeze check.
        private readonly List<(Entity vehicle, Entity plaza, float3 local, float2 forward, float2 length, float halfWidth)> m_Samples =
            new List<(Entity, Entity, float3, float2, float2, float)>();

        private void ProbeSideways(Entity vehicle, Entity plaza, Game.Vehicles.CarCurrentLane lane, ref Pass pass)
        {
            PlazaLayout layout = LayoutOf(plaza);
            if (layout == null || !EntityManager.HasComponent<Game.Objects.Transform>(vehicle) || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                return;
            }
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            quaternion toLocal = math.inverse(t.m_Rotation);
            Game.Objects.Transform v = EntityManager.GetComponentData<Game.Objects.Transform>(vehicle);
            float3 local = math.mul(toLocal, v.m_Position - t.m_Position);
            float3 heading = math.mul(toLocal, math.forward(v.m_Rotation));
            float2 forward = math.normalizesafe(heading.xz);
            float angle = math.degrees(math.acos(math.clamp(forward.y, -1f, 1f)));
            float2 length = LengthOf(vehicle);
            AddSample(vehicle, plaza);

            if (angle < SidewaysDegrees || pass.SidewaysLogged)
            {
                return;
            }
            pass.SidewaysLogged = true;
            m_SidewaysCount++;
            string place = PlaceOf(layout, local.z);
            m_StopPlaces.TryGetValue(place + " (sideways)", out int n);
            m_StopPlaces[place + " (sideways)"] = n + 1;
            if (m_SidewaysLogged >= SidewaysDetailsPerSummary)
            {
                return;
            }
            m_SidewaysLogged++;
            Mod.Log.Info($"[TollPlaza] Sideways: {NameOf(vehicle)} ({length.y - length.x:F1} m) at {place}, plaza-local x {local.x:F1} z {local.z:F1}, "
                + $"{angle:F0} deg off, {(local.x < 0 ? "left" : "right")} of center; speed {ShownMph(SpeedOf(vehicle)):F0} mph; "
                + DescribeLanes(vehicle, plaza, layout, lane));
        }

        private void AddSample(Entity vehicle, Entity plaza)
        {
            if (!EntityManager.HasComponent<Game.Objects.Transform>(vehicle) || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                return;
            }
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            quaternion toLocal = math.inverse(t.m_Rotation);
            Game.Objects.Transform v = EntityManager.GetComponentData<Game.Objects.Transform>(vehicle);
            float3 local = math.mul(toLocal, v.m_Position - t.m_Position);
            float2 forward = math.normalizesafe(math.mul(toLocal, math.forward(v.m_Rotation)).xz);
            m_Samples.Add((vehicle, plaza, local, forward, LengthOf(vehicle), HalfWidthOf(vehicle)));
        }

        /// <summary>The vehicle driving this one (a trailer's truck), or itself.</summary>
        private Entity DriverOf(Entity vehicle) =>
            EntityManager.HasComponent<Game.Vehicles.Controller>(vehicle) && EntityManager.GetComponentData<Game.Vehicles.Controller>(vehicle).m_Controller != Entity.Null
                ? EntityManager.GetComponentData<Game.Vehicles.Controller>(vehicle).m_Controller
                : vehicle;

        /// <summary>
        /// Vehicles overlapping side by side (closer across than their half widths allow while level along the
        /// lane): what the player sees as a truck shoving a car into an occupied lane.
        /// </summary>
        private void CheckSqueezes()
        {
            for (int a = 0; a < m_Samples.Count; a++)
            {
                for (int b = a + 1; b < m_Samples.Count; b++)
                {
                    var s1 = m_Samples[a];
                    var s2 = m_Samples[b];
                    if (s1.plaza != s2.plaza || DriverOf(s1.vehicle) == DriverOf(s2.vehicle))
                    {
                        continue;
                    }
                    if (!m_Passes.ContainsKey((s1.vehicle, s1.plaza)))
                    {
                        (s1, s2) = (s2, s1); // log it against the driven vehicle, not a trailer
                    }
                    // Along the lane, the two bodies overlap; across, they're closer than their widths allow.
                    bool level = s1.local.z + s1.length.x < s2.local.z + s2.length.y && s2.local.z + s2.length.x < s1.local.z + s1.length.y;
                    float gap = math.abs(s1.local.x - s2.local.x) - s1.halfWidth - s2.halfWidth;
                    if (!level || gap > -0.3f)
                    {
                        continue;
                    }
                    var key1 = (s1.vehicle, s1.plaza);
                    if (!m_Passes.TryGetValue(key1, out Pass p1) || p1.SqueezeLogged)
                    {
                        continue;
                    }
                    p1.SqueezeLogged = true;
                    m_Passes[key1] = p1;
                    m_SqueezeCount++;
                    PlazaLayout layout = LayoutOf(s1.plaza);
                    string place = layout != null ? PlaceOf(layout, s1.local.z) : "?";
                    m_StopPlaces.TryGetValue(place + " (squeezed)", out int n);
                    m_StopPlaces[place + " (squeezed)"] = n + 1;
                    if (m_SqueezesLogged >= SidewaysDetailsPerSummary || layout == null)
                    {
                        continue;
                    }
                    m_SqueezesLogged++;
                    string Describe(Entity vehicle, float3 local, float2 forward, float2 length)
                    {
                        string lanes = EntityManager.HasComponent<Game.Vehicles.CarCurrentLane>(vehicle)
                            ? DescribeLanes(vehicle, s1.plaza, layout, EntityManager.GetComponentData<Game.Vehicles.CarCurrentLane>(vehicle))
                            : "?";
                        return $"{NameOf(vehicle)} ({length.y - length.x:F1} m) x {local.x:F1} z {local.z:F1}, "
                            + $"{math.degrees(math.acos(math.clamp(forward.y, -1f, 1f))):F0} deg off, speed {ShownMph(SpeedOf(vehicle)):F0} mph; {lanes}";
                    }
                    Mod.Log.Info($"[TollPlaza] Squeezed at {place}, overlapping {-gap:F1} m across: "
                        + Describe(s1.vehicle, s1.local, s1.forward, s1.length) + " || " + Describe(s2.vehicle, s2.local, s2.forward, s2.length));
                }
            }
            m_Samples.Clear();
        }

        /// <summary>Lane column the vehicle is on, the one it's changing to, the booth its route heads for, and the queue ahead.</summary>
        private string DescribeLanes(Entity vehicle, Entity plaza, PlazaLayout layout, Game.Vehicles.CarCurrentLane lane)
        {
            string Column(Entity l) => m_PlazaLanes.TryGetValue(l, out var at) && at.plaza == plaza && at.booth >= 0 ? (at.booth + 1).ToString() : "-";

            int targetBooth = -1;
            if (EntityManager.HasBuffer<Game.Vehicles.CarNavigationLane>(vehicle))
            {
                foreach (Game.Vehicles.CarNavigationLane nav in EntityManager.GetBuffer<Game.Vehicles.CarNavigationLane>(vehicle, true))
                {
                    if (m_PlazaLanes.TryGetValue(nav.m_Lane, out var at) && at.plaza == plaza && at.boothLane && at.booth >= 0)
                    {
                        targetBooth = at.booth;
                        break;
                    }
                }
            }
            if (targetBooth < 0 && EntityManager.HasBuffer<Game.Pathfind.PathElement>(vehicle))
            {
                DynamicBuffer<Game.Pathfind.PathElement> path = EntityManager.GetBuffer<Game.Pathfind.PathElement>(vehicle, true);
                int start = EntityManager.GetComponentData<Game.Pathfind.PathOwner>(vehicle).m_ElementIndex;
                for (int i = math.max(0, start); i < math.min(path.Length, start + RerouteLookAhead); i++)
                {
                    if (m_PlazaLanes.TryGetValue(path[i].m_Target, out var at) && at.plaza == plaza && at.boothLane && at.booth >= 0)
                    {
                        targetBooth = at.booth;
                        break;
                    }
                }
            }

            // Vehicles ahead in the lane it's on (and how many are stopped).
            int ahead = 0, stopped = 0;
            if (EntityManager.HasBuffer<Game.Net.LaneObject>(lane.m_Lane))
            {
                foreach (Game.Net.LaneObject o in EntityManager.GetBuffer<Game.Net.LaneObject>(lane.m_Lane, true))
                {
                    if (o.m_LaneObject != vehicle && o.m_CurvePosition.y > lane.m_CurvePosition.x)
                    {
                        ahead++;
                        if (!EntityManager.HasComponent<Game.Objects.Moving>(o.m_LaneObject) || math.length(EntityManager.GetComponentData<Game.Objects.Moving>(o.m_LaneObject).m_Velocity) < 0.5f)
                        {
                            stopped++;
                        }
                    }
                }
            }
            return $"column {Column(lane.m_Lane)}"
                + (lane.m_ChangeLane != Entity.Null ? $" changing to {Column(lane.m_ChangeLane)} ({lane.m_ChangeProgress:F2})" : string.Empty)
                + $", route booth {(targetBooth >= 0 ? (targetBooth + 1).ToString() : "?")}, {ahead} ahead in lane ({stopped} stopped), "
                + $"piece {OwnerName(lane.m_Lane)}, flags {lane.m_LaneFlags}";
        }

        private float SpeedOf(Entity vehicle) =>
            EntityManager.HasComponent<Game.Objects.Moving>(vehicle) ? math.length(EntityManager.GetComponentData<Game.Objects.Moving>(vehicle).m_Velocity) : 0f;

        private readonly Dictionary<Entity, float> m_HalfWidths = new Dictionary<Entity, float>();

        private float HalfWidthOf(Entity vehicle)
        {
            if (!EntityManager.HasComponent<PrefabRef>(vehicle))
            {
                return 0.9f;
            }
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab;
            if (!m_HalfWidths.TryGetValue(prefab, out float half))
            {
                half = EntityManager.HasComponent<ObjectGeometryData>(prefab)
                    ? (EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds.max.x - EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds.min.x) * 0.5f
                    : 0.9f;
                m_HalfWidths[prefab] = half;
            }
            return half;
        }

        /// <summary>Which part of the plaza a plaza-local Z falls in.</summary>
        private static string PlaceOf(PlazaLayout layout, float z)
        {
            if (z < layout.ApproachStartZ) return "before the plaza";
            if (z < layout.ChannelStartZ)
            {
                int piece = (int)((z - layout.ApproachStartZ) / PlazaLayout.StepLength);
                return $"approach step {piece + 1}";
            }
            if (z < layout.IslandStartZ) return "channel";
            if (z < layout.IslandEndZ) return "booths";
            if (z < layout.IslandEndZ + PlazaLayout.ExitLength) return "exit";
            return "after the plaza";
        }

        private string NameOf(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                return "none";
            }
            return EntityManager.HasComponent<PrefabRef>(entity)
                && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(entity), out PrefabBase p)
                ? p.name : entity.ToString();
        }

        /// <summary>A lane's road piece (edge or node prefab name), for the stop probe.</summary>
        private string OwnerName(Entity lane)
        {
            if (lane == Entity.Null || !EntityManager.Exists(lane))
            {
                return "none";
            }
            Entity owner = EntityManager.HasComponent<Owner>(lane) ? EntityManager.GetComponentData<Owner>(lane).m_Owner : Entity.Null;
            string kind = owner != Entity.Null && EntityManager.HasComponent<Node>(owner) ? "node of " : string.Empty;
            if (owner != Entity.Null && EntityManager.HasComponent<Node>(owner) && EntityManager.HasBuffer<ConnectedEdge>(owner))
            {
                DynamicBuffer<ConnectedEdge> edges = EntityManager.GetBuffer<ConnectedEdge>(owner, isReadOnly: true);
                return kind + string.Join(" + ", Enumerable.Range(0, edges.Length).Select(i => NameOf(edges[i].m_Edge)));
            }
            return NameOf(owner);
        }

        /// <param name="booths">The island piece (booth speeds); otherwise the approach or zone speed applies.</param>
        private void AddPlazaLanes(Entity owner, Entity plaza, bool booths)
        {
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(owner))
            {
                return;
            }
            DynamicBuffer<Game.Net.SubLane> subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(owner, isReadOnly: true);
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity lane = subLanes[i].m_SubLane;
                if (!m_PlazaLanes.ContainsKey(lane) && EntityManager.HasComponent<Game.Net.CarLane>(lane))
                {
                    int booth = BoothOf(lane, plaza, booths, out bool pastBooths);
                    m_PlazaLanes[lane] = (plaza, booth, booths, pastBooths);
                }
            }
        }

        // Diagnostics: the player's road just past each plaza exit, and who drove out on it.
        private readonly Dictionary<Entity, Entity> m_ExitLanes = new Dictionary<Entity, Entity>();
        private readonly Dictionary<(Entity, Entity), int> m_ExitSeen = new Dictionary<(Entity, Entity), int>();
        private int m_ExitTotal, m_ExitMissed;

        /// <summary>Speed limit (m/s) of the player's road coming into each plaza; the approach steps down from it.</summary>
        private readonly Dictionary<Entity, float> m_EntrySpeeds = new Dictionary<Entity, float>();
        private readonly Dictionary<Entity, float> m_LoggedApproach = new Dictionary<Entity, float>();

        private void LogMissed(Entity vehicle, Entity plaza)
        {
            var types = new List<string>();
            using (NativeArray<ComponentType> components = EntityManager.GetComponentTypes(vehicle, Allocator.Temp))
            {
                foreach (ComponentType c in components)
                {
                    string n = c.GetManagedType()?.Name;
                    if (n != null && !n.StartsWith("Interpolated") && !n.StartsWith("Culling"))
                    {
                        types.Add(n);
                    }
                }
            }
            string prefab = EntityManager.HasComponent<PrefabRef>(vehicle)
                && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(vehicle), out PrefabBase p) ? p.name : "?";
            Mod.Log.Info($"[TollPlaza] Unpaid vehicle {vehicle} ({prefab}) leaving plaza {plaza}: {string.Join(", ", types)}");
        }

        /// <summary>
        /// The player's roads joined to each plaza: car lanes past the exit end (plaza-local +Z) for the paid
        /// check, and the fastest car lane into the approach end for the approach speed steps.
        /// </summary>
        private void FindPlazaEnds()
        {
            m_ExitLanes.Clear();
            m_EntrySpeeds.Clear();
            foreach ((Entity edge, Entity plaza, bool booths) in m_TollEdges)
            {
                if (!EntityManager.HasComponent<Edge>(edge) || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
                {
                    continue;
                }
                Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
                Edge e = EntityManager.GetComponentData<Edge>(edge);
                foreach (Entity node in new[] { e.m_Start, e.m_End })
                {
                    if (!EntityManager.HasBuffer<ConnectedEdge>(node) || !EntityManager.HasComponent<Node>(node))
                    {
                        continue;
                    }
                    float3 local = math.mul(math.inverse(t.m_Rotation), EntityManager.GetComponentData<Node>(node).m_Position - t.m_Position);
                    bool exit = local.z > 0f;
                    DynamicBuffer<ConnectedEdge> connected = EntityManager.GetBuffer<ConnectedEdge>(node, isReadOnly: true);
                    for (int i = 0; i < connected.Length; i++)
                    {
                        Entity other = connected[i].m_Edge;
                        if (OwnerOf(other) == plaza || !EntityManager.HasBuffer<Game.Net.SubLane>(other))
                        {
                            continue;
                        }
                        DynamicBuffer<Game.Net.SubLane> lanes = EntityManager.GetBuffer<Game.Net.SubLane>(other, isReadOnly: true);
                        for (int l = 0; l < lanes.Length; l++)
                        {
                            Entity lane = lanes[l].m_SubLane;
                            if (!EntityManager.HasComponent<Game.Net.CarLane>(lane))
                            {
                                continue;
                            }
                            if (exit)
                            {
                                m_ExitLanes[lane] = plaza;
                            }
                            else
                            {
                                float limit = EntityManager.GetComponentData<Game.Net.CarLane>(lane).m_SpeedLimit;
                                m_EntrySpeeds.TryGetValue(plaza, out float fastest);
                                m_EntrySpeeds[plaza] = math.max(fastest, limit);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Speed limit (m/s) of an approach lane at plaza-local <paramref name="z"/>. The approach is
        /// StepPieces pieces before the channel; their limits fall in even steps of speed squared, a steady
        /// deceleration, from just under the entry road's limit to the zone speed on the last piece before the
        /// channel (the channel and islands run at the booth speeds). The game's cars brake as late and as hard
        /// as they can for each step, but each step is small.
        /// </summary>
        private float ApproachSpeed(Entity plaza, PlazaLayout layout, float z)
        {
            float zone = LaneSpeed(TollState.ZoneMph);
            if (z >= layout.ChannelStartZ || !m_EntrySpeeds.TryGetValue(plaza, out float entry) || entry <= zone)
            {
                return zone;
            }
            int n = PlazaLayout.StepPieces;
            float along = (z - layout.ApproachStartZ) / (layout.ChannelStartZ - layout.ApproachStartZ);
            int piece = math.clamp((int)math.floor(along * n), 0, n - 1);
            float speed = StepSpeed(entry, zone, piece, n);

            if (!m_LoggedApproach.TryGetValue(plaza, out float logged) || math.abs(logged - entry) > 0.1f)
            {
                m_LoggedApproach[plaza] = entry;
                var steps = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    steps.Add(ShownMph(StepSpeed(entry, zone, i, n)).ToString("F0"));
                }
                Mod.Log.Info($"[TollPlaza] Plaza {plaza} approach from {ShownMph(entry):F0} mph: "
                    + string.Join(" > ", steps) + $" mph, then booths {TollState.EtcMph}/{TollState.CashMph} mph");
            }
            return speed;
        }

        private static float StepSpeed(float entry, float zone, int piece, int pieces)
        {
            float remaining = (pieces - 1 - piece) / (float)pieces;
            return math.sqrt(zone * zone + (entry * entry - zone * zone) * remaining);
        }

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
        /// Sets a plaza lane's speed limit (booth speed on the island piece, the approach steps before it, the
        /// zone speed after) and finds its booth. A lane belongs to the booth whose lane center, in the plaza's
        /// own coordinates, is nearest. Re-applied every update in case the game recalculates lane speeds.
        /// </summary>
        /// <returns>The booth lane index (0 = leftmost), or -1 if unknown.</returns>
        /// <param name="pastBooths">Set for a lane after the booths (the exit and its joints).</param>
        private int BoothOf(Entity lane, Entity plaza, bool boothSpeed, out bool pastBooths)
        {
            pastBooths = false;
            PlazaLayout layout = LayoutOf(plaza);
            if (layout == null || !EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<Game.Objects.Transform>(plaza))
            {
                return -1;
            }
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            float3 mid = MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier, 0.5f);
            float3 local = math.mul(math.inverse(t.m_Rotation), mid - t.m_Position);
            pastBooths = !boothSpeed && local.z > layout.IslandStartZ;

            int nearest = 0;
            for (int i = 1; i < layout.LaneCount; i++)
            {
                if (math.abs(layout.LaneCenterX(i) - local.x) < math.abs(layout.LaneCenterX(nearest) - local.x))
                {
                    nearest = i;
                }
            }
            // Booth lanes get their booth's speed; the approach steps down to the zone speed (see
            // ApproachSpeed), and the exit and its joints stay at the zone speed.
            float metersPerSecond = boothSpeed
                ? LaneSpeed(PlazaStats.IsCashLane(plaza, layout, nearest) ? TollState.CashMph : TollState.EtcMph)
                : ApproachSpeed(plaza, layout, local.z);

            Game.Net.CarLane carLane = EntityManager.GetComponentData<Game.Net.CarLane>(lane);
            if (math.abs(carLane.m_SpeedLimit - metersPerSecond) > 0.01f)
            {
                carLane.m_SpeedLimit = metersPerSecond;
                EntityManager.SetComponentData(lane, carLane);
            }

            // A closed booth's own lane on the booth piece is made very costly for route finding (ClosedLaneSystem),
            // so vehicles plan through the open booths. Lanes shared between booths (a lane group, e.g. where the
            // booths merge back at the exit) are never closed: that would close them for every booth.
            bool atBooth = boothSpeed && !EntityManager.HasComponent<MasterLane>(lane) && !EntityManager.HasComponent<SlaveLane>(lane);
            bool close = atBooth && PlazaStats.IsClosedLane(plaza, nearest);
            if (close != ClosedLaneSystem.Lanes.Contains(lane))
            {
                carLane = EntityManager.GetComponentData<Game.Net.CarLane>(lane);
                if (close)
                {
                    ClosedLaneSystem.Lanes.Add(lane);
                    m_ClosedChanges++;
                    m_ChangedPlazas.Add(plaza);
                    carLane = ClosedLaneSystem.Close(carLane);
                }
                else
                {
                    ClosedLaneSystem.Lanes.Remove(lane);
                    m_ClosedChanges++;
                    m_ChangedPlazas.Add(plaza);
                    carLane = ClosedLaneSystem.Open(carLane);
                }
                EntityManager.SetComponentData(lane, carLane);
                // Re-read by route finding.
                if (!EntityManager.HasComponent<PathfindUpdated>(lane))
                {
                    EntityManager.AddComponent<PathfindUpdated>(lane);
                }
                Mod.Log.Info($"[TollPlaza] {PlazaNames.Shown(plaza)} booth {nearest + 1} lane {(close ? "closed" : "reopened")}");
            }
            else if (!close && boothSpeed && (carLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != 0)
            {
                // Still marked closed in the save (the game keeps lane data) but open in the toll data (closed
                // booths are kept there, not in the save; see PlazaPersistence): open it again. The game never
                // forbids a booth lane itself.
                EntityManager.SetComponentData(lane, ClosedLaneSystem.Open(carLane));
                if (!EntityManager.HasComponent<PathfindUpdated>(lane))
                {
                    EntityManager.AddComponent<PathfindUpdated>(lane);
                }
                Mod.Log.Info($"[TollPlaza] {PlazaNames.Shown(plaza)} booth {nearest + 1} lane was still closed from an earlier session: reopened");
            }
            return nearest;
        }

        // Rerouting around closed lanes: how often (updates), and how near a plaza a vehicle must be to be checked.
        private const int RerouteEveryUpdates = 8;
        private const float RerouteRadiusMeters = 400f;
        // How many upcoming route steps are checked per vehicle (the plaza is near, so it's early in the route).
        private const int RerouteLookAhead = 64;
        // Rerouted vehicles: when last, and how many times (a new route can still use the closed lane if it was
        // planned before route finding had the closure; those are rerouted again, a few times at most).
        private readonly Dictionary<Entity, (uint frame, int tries)> m_Rerouted = new Dictionary<Entity, (uint, int)>();
        private const int RerouteTries = 3;
        private const uint RerouteRetryFrames = 180;
        // Rerouting waits this long after a lane is closed, so route finding has the closure first.
        private const uint RerouteDelayFrames = 60;
        private uint m_ClosedChangedFrame;
        private int m_ClosedVersion = -1;
        private int m_ClosedChanges;
        private int m_ReroutedCount;

        /// <summary>
        /// Vehicles only look for a new route when something blocks them, so one that planned its route through a
        /// booth before it closed would still drive into it. Every vehicle near a plaza whose upcoming lanes or
        /// route use a closed lane gets its route marked out of date (the game's own flag), and plans a new one
        /// from where it is, around the closed booth. Only vehicles not yet on the plaza's road: the rest drive
        /// through the closed booth rather than cross lanes in a hurry.
        /// </summary>
        private void RerouteAroundClosedLanes()
        {
            if (m_ClosedVersion != m_ClosedChanges)
            {
                m_ClosedVersion = m_ClosedChanges;
                m_Rerouted.Clear();
                m_ClosedChangedFrame = m_SimulationSystem.frameIndex;
                RefreshChangedPlazas();
            }
            if (ClosedLaneSystem.Lanes.Count == 0)
            {
                return;
            }

            // Plazas with a closed lane.
            var centers = new List<float3>();
            foreach ((Entity edge, Entity plaza, bool booths) in m_TollEdges)
            {
                if (booths && EntityManager.Exists(plaza) && EntityManager.HasComponent<Game.Objects.Transform>(plaza))
                {
                    PlazaLayout layout = LayoutOf(plaza);
                    for (int lane = 0; layout != null && lane < layout.LaneCount; lane++)
                    {
                        if (PlazaStats.IsClosedLane(plaza, lane))
                        {
                            centers.Add(EntityManager.GetComponentData<Game.Objects.Transform>(plaza).m_Position);
                            break;
                        }
                    }
                }
            }
            if (centers.Count == 0)
            {
                return;
            }

            int rerouted = 0;
            uint now = m_SimulationSystem.frameIndex;
            float radiusSq = RerouteRadiusMeters * RerouteRadiusMeters;
            using (NativeArray<Entity> vehicles = m_RerouteQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Game.Objects.Transform> transforms = m_RerouteQuery.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
            using (NativeArray<Game.Vehicles.CarCurrentLane> current = m_RerouteQuery.ToComponentDataArray<Game.Vehicles.CarCurrentLane>(Allocator.Temp))
            {
                for (int j = 0; j < vehicles.Length; j++)
                {
                    Entity vehicle = vehicles[j];
                    bool retry = m_Rerouted.TryGetValue(vehicle, out var before);
                    if (now - m_ClosedChangedFrame < RerouteDelayFrames
                        || (retry && (before.tries >= RerouteTries || now - before.frame < RerouteRetryFrames))
                        || m_PlazaLanes.ContainsKey(current[j].m_Lane))
                    {
                        // Too soon, done trying, or already on the plaza's road: let it through the closed booth. A
                        // new route there leaves too little room to cross lanes, and a car told to switch lanes
                        // close to where it must turn swings sideways, stops and reverses (seen on the approach).
                        continue;
                    }
                    bool near = false;
                    foreach (float3 c in centers)
                    {
                        near |= math.distancesq(c, transforms[j].m_Position) < radiusSq;
                    }
                    if (!near || !UsesClosedLane(vehicle))
                    {
                        continue;
                    }
                    Game.Pathfind.PathOwner owner = EntityManager.GetComponentData<Game.Pathfind.PathOwner>(vehicle);
                    owner.m_State |= Game.Pathfind.PathFlags.Obsolete;
                    EntityManager.SetComponentData(vehicle, owner);
                    m_Rerouted[vehicle] = (now, retry ? before.tries + 1 : 1);
                    rerouted++;
                }
            }
            if (rerouted > 0 && (m_ReroutedCount += rerouted) % 50 < rerouted)
            {
                Mod.Log.Info($"[TollPlaza] Rerouted {m_ReroutedCount} vehicles around closed booth lanes so far");
            }
        }

        // Plazas whose closed lanes changed since the last reroute check.
        private readonly HashSet<Entity> m_ChangedPlazas = new HashSet<Entity>();

        /// <summary>
        /// Has route finding re-read every lane of each plaza whose closed lanes changed. What route finding holds
        /// for a plaza's lanes can be out of date (it's loaded with the city), and then it may see only some booths
        /// as usable, so a closed booth was still the only way through.
        /// </summary>
        private void RefreshChangedPlazas()
        {
            foreach (var entry in m_PlazaLanes)
            {
                if (m_ChangedPlazas.Contains(entry.Value.plaza) && EntityManager.Exists(entry.Key)
                    && !EntityManager.HasComponent<PathfindUpdated>(entry.Key))
                {
                    EntityManager.AddComponent<PathfindUpdated>(entry.Key);
                }
            }
            m_ChangedPlazas.Clear();
        }

        private bool UsesClosedLane(Entity vehicle)
        {
            if (EntityManager.HasBuffer<Game.Vehicles.CarNavigationLane>(vehicle))
            {
                foreach (Game.Vehicles.CarNavigationLane lane in EntityManager.GetBuffer<Game.Vehicles.CarNavigationLane>(vehicle, true))
                {
                    if (ClosedLaneSystem.Lanes.Contains(lane.m_Lane))
                    {
                        return true;
                    }
                }
            }
            if (EntityManager.HasBuffer<Game.Pathfind.PathElement>(vehicle))
            {
                DynamicBuffer<Game.Pathfind.PathElement> path = EntityManager.GetBuffer<Game.Pathfind.PathElement>(vehicle, true);
                int start = EntityManager.GetComponentData<Game.Pathfind.PathOwner>(vehicle).m_ElementIndex;
                int end = math.min(path.Length, start + RerouteLookAhead);
                for (int i = math.max(0, start); i < end; i++)
                {
                    if (ClosedLaneSystem.Lanes.Contains(path[i].m_Target))
                    {
                        return true;
                    }
                }
            }
            return false;
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
                // The booth pieces first (they identify the plazas), then every other piece of those plazas'
                // own road (approach and exit), so a vehicle is seen somewhere along the whole plaza. It still
                // pays once per pass.
                var plazas = new HashSet<Entity>();
                for (int i = 0; i < edges.Length; i++)
                {
                    if (m_TollRoadPrefabs.Contains(prefabs[i].m_Prefab))
                    {
                        Entity plaza = OwnerOf(edges[i]);
                        plazas.Add(plaza);
                        m_TollEdges.Add((edges[i], plaza, true));
                    }
                }
                for (int i = 0; i < edges.Length; i++)
                {
                    if (!m_TollRoadPrefabs.Contains(prefabs[i].m_Prefab) && plazas.Contains(OwnerOf(edges[i])))
                    {
                        m_TollEdges.Add((edges[i], OwnerOf(edges[i]), false));
                    }
                }
            }
        }

        private Entity OwnerOf(Entity edge) =>
            EntityManager.HasComponent<Owner>(edge) ? EntityManager.GetComponentData<Owner>(edge).m_Owner : edge;

        /// <summary>Charges a vehicle at a plaza's booth and counts it for the plaza and the city.</summary>
        private void Charge(Entity vehicle, Entity plaza, int month, ref Pass pass)
        {
            VehicleClass cls = Classify(vehicle);
            int cents = TollState.RateCents(cls);
            TollState.Vehicles[cls] = TollState.Count(cls) + 1;

            foreach (PlazaStats monthly in new[] { PlazaStats.Of(plaza), PlazaStats.City })
            {
                monthly.Roll(month);
                monthly.ThisMonth.Add(cls, cents);
                monthly.Record(m_SimulationSystem.frameIndex);
            }
            if (cents > 0)
            {
                TollState.RevenueCents += cents;
                m_PendingCents += cents;
            }
            pass.Class = cls;
            pass.Cents = cents;
            pass.Charged = true;
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
            foreach (var entry in m_Passes)
            {
                if (m_Tick - entry.Value.LastSeen > ForgetAfterUpdates)
                {
                    m_ToForget.Add(entry.Key);
                }
            }
            foreach (var key in m_ToForget)
            {
                Pass pass = m_Passes[key];
                if (pass.Measured)
                {
                    m_BrakePasses++;
                    m_BrakePeakSum += pass.PeakDecel;
                    m_BrakePeakMax = math.max(m_BrakePeakMax, pass.PeakDecel);
                    if (pass.PeakDecel > HardBrakingMps2)
                    {
                        m_HardBrakePasses++;
                    }
                }
                m_Passes.Remove(key);
            }
            m_ToForget.Clear();
            foreach (var entry in m_ExitSeen)
            {
                if (m_Tick - entry.Value > ForgetAfterUpdates)
                {
                    m_ToForget.Add(entry.Key);
                }
            }
            foreach (var key in m_ToForget)
            {
                m_ExitSeen.Remove(key);
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
