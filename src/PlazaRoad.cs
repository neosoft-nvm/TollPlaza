using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TollPlaza
{
    /// <summary>
    /// The plaza's own road ("sub-nets", the feature service buildings use for their internal roads): one
    /// straight one-way highway per road group runs through the plaza, one lane per booth, with the islands
    /// on its lane lines. No junctions inside the plaza, so the game adds no extra signs or lane-change
    /// markings and cars stay in their lanes. Players connect their highway to the two ends.
    /// Coordinates match PlazaLayout: X across, Y up, Z along travel, traffic from -Z.
    /// </summary>
    internal static class PlazaRoad
    {
        /// <summary>The plaza highways registered this launch (toll lanes are the lanes of these roads).</summary>
        public static readonly HashSet<NetPrefab> TollRoads = new HashSet<NetPrefab>();

        private static List<RoadPrefab> s_Roads;

        private static List<RoadPrefab> AllRoads(PrefabSystem prefabSystem, EntityManager entityManager)
        {
            if (s_Roads != null)
            {
                return s_Roads;
            }
            s_Roads = new List<RoadPrefab>();
            using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RoadData>(), ComponentType.ReadOnly<PrefabData>()))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (prefabSystem.TryGetPrefab(entity, out RoadPrefab road) && road != null)
                    {
                        s_Roads.Add(road);
                    }
                }
            }
            return s_Roads;
        }

        /// <summary>The base game's one-way highway with the given lane count (not asset-pack copies).</summary>
        public static RoadPrefab FindHighway(PrefabSystem prefabSystem, EntityManager entityManager, int lanes)
        {
            RoadPrefab best = AllRoads(prefabSystem, entityManager)
                .Where(r => r.m_HighwayRules)
                .OrderByDescending(r => Score(r.name, lanes))
                .FirstOrDefault();
            return best != null && Score(best.name, lanes) >= 9 ? best : null;
        }

        private static int Score(string name, int lanes)
        {
            string n = name.ToLowerInvariant();
            int score = 0;
            if (n.Contains("oneway") || n.Contains("one-way") || n.Contains("one way")) score += 2;
            if (n.EndsWith(" " + lanes + " lanes") || n.EndsWith(" " + lanes + " lane")) score += 3;
            // Prefer the base game's road over asset-pack copies (prefixed like "NAO " or "BXP "), so the plaza
            // doesn't depend on another mod being installed.
            if (n.StartsWith("highway")) score += 4;
            if (n.Contains("elevated") || n.Contains("tunnel") || n.Contains("bridge") || n.Contains("transport")) score -= 10;
            return score;
        }

        /// <summary>
        /// Measures lane positions of the base one-way highways (1-5 lanes) into PlazaLayout.MeasuredRoads, so
        /// the islands land exactly on the lane lines.
        /// </summary>
        public static void MeasureHighways(PrefabSystem prefabSystem, EntityManager entityManager)
        {
            for (int lanes = 1; lanes <= 5; lanes++)
            {
                RoadPrefab road = FindHighway(prefabSystem, entityManager, lanes);
                if (road == null)
                {
                    continue;
                }
                if (RoadLaneProbe.TryGetCarLanes(road, out List<float> centers, out float width) && centers.Count == lanes)
                {
                    PlazaLayout.MeasuredRoads[lanes] = (centers.ToArray(), width);
                    Mod.Log.Info($"[TollPlaza] {road.name}: width {width:F2} m, car lanes at "
                        + string.Join(", ", centers.Select(c => c.ToString("F2"))));
                }
                else
                {
                    Mod.Log.Warn($"[TollPlaza] Couldn't measure {road.name} (found {centers.Count} car lanes, width {width:F2}); using default lane spacing.");
                }
            }
        }

        /// <summary>
        /// A copy of the one-way highway with the plaza's lane speed, used only inside plazas. Hidden from the
        /// road menus. Cash lanes are slowed further per lane at runtime by TollCollectionSystem.
        /// </summary>
        public static NetPrefab CreatePlazaHighway(PrefabSystem prefabSystem, EntityManager entityManager, int lanes)
        {
            string name = "TollPlaza Highway " + lanes + " lanes";
            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(RoadPrefab), name), out PrefabBase existing) && existing is NetPrefab existingRoad)
            {
                TollRoads.Add(existingRoad);
                return existingRoad;
            }
            RoadPrefab road = FindHighway(prefabSystem, entityManager, lanes);
            if (road == null)
            {
                Mod.Log.Warn("[TollPlaza] No base-game " + lanes + "-lane one-way highway found.");
                return null;
            }

            var copy = (RoadPrefab)road.Clone(name);
            copy.m_SpeedLimit = PlazaLayout.EtcSpeedMph * 1.609344f; // RoadPrefab speed limits are km/h
            copy.Remove<UIObject>();                                  // not a road players draw themselves
            if (!prefabSystem.AddPrefab(copy))
            {
                Mod.Log.Warn("[TollPlaza] Couldn't add " + name + "; using " + road.name);
                return road;
            }
            TollRoads.Add(copy);
            Mod.Log.Info("[TollPlaza] " + name + " (from " + road.name + ") at " + PlazaLayout.EtcSpeedMph + " mph");
            return copy;
        }

        /// <summary>Turns PlazaLayout.RoadSegments (shared with the preview) into the prefab's sub-nets.</summary>
        public static ObjectSubNetInfo[] Build(PlazaLayout layout, System.Func<int, NetPrefab> roadForLanes)
        {
            var subNets = new List<ObjectSubNetInfo>();
            foreach (var s in layout.RoadSegments())
            {
                NetPrefab road = roadForLanes(s.lanes);
                if (road == null)
                {
                    return null;
                }
                subNets.Add(new ObjectSubNetInfo
                {
                    m_NetPrefab = road,
                    m_BezierCurve = new Bezier4x3(s.a, s.b, s.c, s.d),
                    m_NodeIndex = new int2(s.startNode, s.endNode),
                });
            }
            return subNets.ToArray();
        }
    }
}
