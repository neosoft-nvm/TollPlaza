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

        /// <summary>The one-way highway with the given lane count, preferring the base game's over asset-pack copies.</summary>
        public static RoadPrefab FindHighway(PrefabSystem prefabSystem, EntityManager entityManager, int lanes)
        {
            // One-way only: a two-way highway ("Highway 6 Lane") would send half the plaza's lanes backwards.
            // The lane count must match exactly: a 1-lane highway at the ends of a 6-booth plaza left half the
            // booths standing on grass. The base game's one-way highways stop at 5 lanes; past that an installed
            // asset pack's (e.g. "NAO Highway Oneway - 6 lanes") is used, or the plaza composes its own.
            return AllRoads(prefabSystem, entityManager)
                .Where(r => r.m_HighwayRules && IsOneWay(r.name) && HasLaneCount(r.name, lanes)
                    && !r.name.StartsWith("TollPlaza") && !IsStructure(r.name) && CarSections(r).Count == lanes)
                .OrderByDescending(r => Score(r.name, lanes))
                .FirstOrDefault();
        }

        /// <summary>Bridges, tunnels and elevated roads carry towers and pillars (a suspension bridge's towers
        /// stood over the 6-booth plaza), so only ground roads are used.</summary>
        private static bool IsStructure(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("bridge") || n.Contains("elevated") || n.Contains("tunnel") || n.Contains("suspension")
                || n.Contains("cable") || n.Contains("viaduct") || n.Contains("pillar") || n.Contains("transport");
        }

        private static bool HasLaneCount(string name, int lanes)
        {
            string n = name.ToLowerInvariant();
            return n.EndsWith(" " + lanes + " lanes") || n.EndsWith(" " + lanes + " lane");
        }

        private static bool IsOneWay(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("oneway") || n.Contains("one-way") || n.Contains("one way");
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

        private static NetSectionPrefab FindSection(PrefabSystem prefabSystem, string name) =>
            prefabSystem.TryGetPrefab(new PrefabID(nameof(NetSectionPrefab), name), out PrefabBase section) ? section as NetSectionPrefab : null;

        /// <summary>
        /// The plaza highway: the game's one-lane one-way highway with its driving section repeated once per
        /// booth lane (how the Road Builder mod composes roads). The island piece (<paramref name="islands"/>) has a
        /// raised median between each pair of lanes; the open pieces before and after are an ordinary highway,
        /// which the game widens into the island piece at the joining node. One
        /// real highway, so no junctions, signs or street names inside the plaza, and cars can't change lanes
        /// across the medians. Runs at the E-Toll speed (cash lanes are slowed per lane at runtime by
        /// TollCollectionSystem) and is hidden from the road menus.
        /// </summary>
        public static NetPrefab CreatePlazaHighway(PrefabSystem prefabSystem, EntityManager entityManager, int lanes, bool islands)
        {
            string name = "TollPlaza Highway " + lanes + " lanes" + (islands ? string.Empty : " Open");
            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(RoadPrefab), name), out PrefabBase existing) && existing is RoadPrefab existingRoad)
            {
                if (islands)
                {
                    TollRoads.Add(existingRoad);
                    Measure(existingRoad, lanes);
                }
                return existingRoad;
            }
            // The booth piece is made from the very highway at the plaza ends (same shoulders, same lane
            // positions), each 4 m lane narrowed to 3 m with a 1 m raised island beside it, so every lane keeps
            // its exact center and runs straight through.
            // Past the game's widest one-way highway (5 lanes) the widest one is extended with more lanes. A copy
            // of the 1-lane highway widened to 6 lanes drew wedges of road beside its pieces, even with the very
            // lane list of the 5-lane highway, so the base road itself matters.
            RoadPrefab road = null;
            for (int n = lanes; n >= 1 && road == null; n--)
            {
                road = FindHighway(prefabSystem, entityManager, n);
            }
            if (road == null)
            {
                Mod.Log.Warn("[TollPlaza] No base-game one-lane one-way highway found.");
                return null;
            }
            NetSectionPrefab median = FindSection(prefabSystem, "Road Median 1");
            NetSectionPrefab narrowLane = FindSection(prefabSystem, "Car Drive Section 3");

            var copy = (RoadPrefab)road.Clone(name);
            // Keeps the highway's own speed limit: a different one makes the game put up its own speed signs at
            // the booths. The booth speeds are set lane by lane at runtime (TollCollectionSystem). No street of
            // its own either, so no road name is written across the plaza (the player's roads keep theirs).
            copy.m_AggregateType = null;
            copy.Remove<UIObject>(); // only part of the plaza, not a road players draw themselves

            NetSectionInfo[] sections = copy.m_Sections ?? new NetSectionInfo[0];
            List<int> cars = CarSections(road);
            if (cars.Count == 0 || median == null)
            {
                Mod.Log.Warn($"[TollPlaza] Can't compose {name} (car lanes {cars.Count}, 'Road Median 1' {(median != null ? "found" : "missing")}); "
                    + $"sections of {road.name}: " + string.Join(", ", sections.Select(s => s.m_Section?.name)));
                return null;
            }

            var composed = new List<NetSectionInfo>();
            if (cars.Count == lanes)
            {
                // Same lanes as the highway: narrow each and put an island between neighbours.
                for (int i = 0; i < sections.Length; i++)
                {
                    int car = cars.IndexOf(i);
                    if (car < 0 || !islands)
                    {
                        composed.Add(sections[i]);
                        continue;
                    }
                    if (car > 0)
                    {
                        composed.Add(Plain(median));
                    }
                    composed.Add(Narrowed(sections[i], narrowLane, name));
                }
            }
            else
            {
                // A narrower highway extended: its inner lane repeated, its outer ("Transport Option") lane
                // last, as the game's own highways are laid out. Whatever lay between its lanes is left out.
                int first = cars[0], last = cars[cars.Count - 1];
                composed.AddRange(sections.Take(first));
                for (int lane = 0; lane < lanes; lane++)
                {
                    NetSectionInfo source = sections[lane < lanes - 1 ? first : last];
                    if (islands)
                    {
                        if (lane > 0)
                        {
                            composed.Add(Plain(median));
                        }
                        composed.Add(Narrowed(source, narrowLane, name));
                    }
                    else
                    {
                        composed.Add(source);
                    }
                }
                composed.AddRange(sections.Skip(last + 1));
            }
            copy.m_Sections = composed.ToArray();
            Mod.Log.Info($"[TollPlaza] {name} from {road.name}: "
                + string.Join(" | ", copy.m_Sections.Select(s => s.m_Section?.name + (s.m_Invert ? " (inv)" : ""))));

            if (!prefabSystem.AddPrefab(copy))
            {
                Mod.Log.Warn("[TollPlaza] Couldn't add " + name + ".");
                return null;
            }
            if (!islands)
            {
                Measure(copy, lanes, record: false);
                return copy;
            }
            TollRoads.Add(copy);

            float laneWidth = RoadLaneProbe.SectionWidth(narrowLane ?? median);
            float medianWidth = RoadLaneProbe.SectionWidth(median);
            if (laneWidth > 2f) PlazaLayout.MeasuredLaneWidth = laneWidth;
            if (medianWidth > 0.5f) PlazaLayout.MeasuredMedianWidth = medianWidth;
            Mod.Log.Info($"[TollPlaza] Lane width {PlazaLayout.MeasuredLaneWidth:F2} m, median width {PlazaLayout.MeasuredMedianWidth:F2} m");
            Measure(copy, lanes);
            return copy;
        }

        private static List<int> CarSections(RoadPrefab road)
        {
            var result = new List<int>();
            NetSectionInfo[] sections = road.m_Sections ?? new NetSectionInfo[0];
            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i].m_Section != null && RoadLaneProbe.HasCarLane(sections[i].m_Section))
                {
                    result.Add(i);
                }
            }
            return result;
        }

        private static NetSectionInfo Plain(NetSectionPrefab section) => new NetSectionInfo
        {
            m_Section = section,
            m_RequireAll = new NetPieceRequirements[0],
            m_RequireAny = new NetPieceRequirements[0],
            m_RequireNone = new NetPieceRequirements[0],
        };

        /// <summary>The lane with another section, same flags and conditions.</summary>
        private static NetSectionInfo Replaced(NetSectionInfo lane, NetSectionPrefab section) => new NetSectionInfo
        {
            m_Section = section,
            m_Invert = lane.m_Invert,
            m_Flip = lane.m_Flip,
            m_Median = lane.m_Median,
            m_Offset = lane.m_Offset,
            m_RequireAll = lane.m_RequireAll ?? new NetPieceRequirements[0],
            m_RequireAny = lane.m_RequireAny ?? new NetPieceRequirements[0],
            m_RequireNone = lane.m_RequireNone ?? new NetPieceRequirements[0],
        };

        /// <summary>The lane section 1 m narrower (3 m for the highway's 4 m), same flags and conditions.</summary>
        private static NetSectionInfo Narrowed(NetSectionInfo lane, NetSectionPrefab narrow, string road)
        {
            if (narrow == null || RoadLaneProbe.SectionWidth(lane.m_Section) < 3.9f)
            {
                Mod.Log.Warn($"[TollPlaza] {road}: can't narrow {lane.m_Section.name}; lanes will shift at the islands.");
                return lane;
            }
            return Replaced(lane, narrow);
        }

        /// <summary>Records the plaza highway's lane positions, so the islands land exactly on its medians.</summary>
        private static void Measure(RoadPrefab road, int lanes, bool record = true)
        {
            if (RoadLaneProbe.TryGetCarLanes(road, out List<float> centers, out float width) && centers.Count == lanes)
            {
                if (record)
                {
                    PlazaLayout.MeasuredRoads[lanes] = (centers.ToArray(), width);
                }
                Mod.Log.Info($"[TollPlaza] {road.name}: width {width:F2} m, car lanes at "
                    + string.Join(", ", centers.Select(c => c.ToString("F2"))));
            }
            else
            {
                Mod.Log.Warn($"[TollPlaza] Couldn't measure {road.name} (found {centers.Count} car lanes, width {width:F2}); using computed lane spacing.");
            }
        }

        /// <summary>Turns PlazaLayout.RoadSegments (shared with the preview) into the prefab's sub-nets.</summary>
        public static ObjectSubNetInfo[] Build(PlazaLayout layout, System.Func<int, RoadPiece, NetPrefab> roadFor)
        {
            var subNets = new List<ObjectSubNetInfo>();
            foreach (var s in layout.RoadSegments())
            {
                NetPrefab road = roadFor(s.lanes, s.kind);
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
