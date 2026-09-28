using System.Collections.Generic;
using Game.Areas;
using Game.Net;
using Game.Prefabs;
using Game.UI;
using Unity.Entities;
using SubNet = Game.Net.SubNet;

namespace TollPlaza
{
    /// <summary>
    /// Plaza names for the panels, and which plazas still have an outdated road layout. A plaza is named
    /// after the district it stands in, else the city, else (a city with no name) the road it's built on
    /// ("Oak Hills Toll Plaza");
    /// plazas that end up with the same name are numbered ("... Toll Plaza 2"). A name the player typed
    /// (PlazaStats.Name, kept with the toll data outside the save) replaces the automatic one.
    /// </summary>
    public static class PlazaNames
    {
        private const string Suffix = " Toll Plaza";

        private static readonly Dictionary<Entity, string> s_Names = new Dictionary<Entity, string>();
        private static readonly HashSet<Entity> s_Outdated = new HashSet<Entity>();

        public static string Of(Entity plaza) => s_Names.TryGetValue(plaza, out string name) ? name : "Toll Plaza";

        /// <summary>Every placed plaza (with its own road) and its name, as of the last refresh.</summary>
        public static IEnumerable<Entity> Placed => s_Names.Keys;

        /// <summary>The name to show for a plaza: a rename shows at once, before the next refresh.</summary>
        public static string Shown(Entity plaza) => CustomName(plaza) ?? Of(plaza);

        /// <summary>True for a plaza placed before the current road layout; bulldoze and re-place it.</summary>
        public static bool IsOutdated(Entity plaza) => s_Outdated.Contains(plaza);

        /// <summary>The plaza's kept name (typed by the player, or picked automatically once), or null if none yet.</summary>
        public static string CustomName(Entity plaza) =>
            PlazaStats.Plazas.TryGetValue(plaza, out PlazaStats stats) && !string.IsNullOrWhiteSpace(stats.Name) ? stats.Name : null;

        /// <summary>
        /// Names every placed plaza (oldest first) and rechecks the layouts. A plaza keeps its name once it has
        /// one, so drawing a district later doesn't rename it; only a plaza without a name yet (new, or the
        /// player pressed "Auto name") gets one picked, numbered if another plaza already has it.
        /// </summary>
        public static void Refresh(EntityManager em, PrefabSystem prefabs, NameSystem names, string city,
            IList<(Entity entity, PrefabBase prefab, int lanes)> placed)
        {
            s_Names.Clear();
            s_Outdated.Clear();
            var unnamed = new List<Entity>();
            foreach (var p in placed)
            {
                if (IsOutdated(em, prefabs, p.entity, p.prefab))
                {
                    s_Outdated.Add(p.entity);
                }
                string kept = CustomName(p.entity);
                if (kept != null)
                {
                    s_Names[p.entity] = kept;
                }
                else
                {
                    unnamed.Add(p.entity);
                }
            }
            foreach (Entity plaza in unnamed)
            {
                Pick(em, prefabs, names, city, plaza);
            }
        }

        /// <summary>Picks and keeps a new automatic name for the plaza now (the "Auto name" button).</summary>
        public static void Repick(EntityManager em, PrefabSystem prefabs, NameSystem names, string city, Entity plaza)
        {
            s_Names.Remove(plaza);
            Pick(em, prefabs, names, city, plaza);
        }

        private static void Pick(EntityManager em, PrefabSystem prefabs, NameSystem names, string city, Entity plaza)
        {
            string name = AutoName(em, prefabs, names, plaza, city);
            var taken = new HashSet<string>(s_Names.Values);
            string unique = name;
            for (int n = 2; taken.Contains(unique); n++)
            {
                unique = $"{name} {n}";
            }
            s_Names[plaza] = unique;
            PlazaStats.Of(plaza).Name = unique;
        }

        private static string AutoName(EntityManager em, PrefabSystem prefabs, NameSystem names, Entity plaza, string city)
        {
            string name, source;
            Entity district = DistrictOf(em, plaza);
            Entity road = Entity.Null, edge = Entity.Null;
            if (district != Entity.Null)
            {
                name = Clean(names.GetRenderedLabelName(district));
                source = $"district {district}";
            }
            else if (!string.IsNullOrWhiteSpace(city))
            {
                name = city.Trim();
                source = "city";
            }
            else if ((road = RoadOf(em, plaza, out edge)) != Entity.Null)
            {
                name = Clean(names.GetRenderedLabelName(road));
                source = $"road {road} ({PrefabName(em, prefabs, road)}) via edge {edge} ({PrefabName(em, prefabs, edge)})";
            }
            else
            {
                name = "City";
                source = "nothing (no district, city name or road)";
            }
            // Logged when it changes, to trace names that don't match the road labels on the map.
            if (!s_Sources.TryGetValue(plaza, out string last) || last != name + source)
            {
                s_Sources[plaza] = name + source;
                Mod.Log.Info($"[TollPlaza] Plaza {plaza} named \"{name}\" from {source}");
            }
            return name + Suffix;
        }

        private static readonly Dictionary<Entity, string> s_Sources = new Dictionary<Entity, string>();

        private static string PrefabName(EntityManager em, PrefabSystem prefabs, Entity entity) =>
            em.HasComponent<PrefabRef>(entity) && prefabs.TryGetPrefab(em.GetComponentData<PrefabRef>(entity), out PrefabBase prefab)
                ? prefab.name : "?";

        private static string Clean(string name) => string.IsNullOrWhiteSpace(name) ? "City" : name.Trim();

        /// <summary>The district the plaza stands in: its own, else the one beside its road pieces.</summary>
        private static Entity DistrictOf(EntityManager em, Entity plaza)
        {
            if (em.HasComponent<CurrentDistrict>(plaza))
            {
                Entity own = em.GetComponentData<CurrentDistrict>(plaza).m_District;
                if (own != Entity.Null)
                {
                    return own;
                }
            }
            if (!em.HasBuffer<SubNet>(plaza))
            {
                return Entity.Null;
            }
            DynamicBuffer<SubNet> subNets = em.GetBuffer<SubNet>(plaza, isReadOnly: true);
            for (int i = 0; i < subNets.Length; i++)
            {
                if (em.HasComponent<BorderDistrict>(subNets[i].m_SubNet))
                {
                    BorderDistrict border = em.GetComponentData<BorderDistrict>(subNets[i].m_SubNet);
                    if (border.m_Left != Entity.Null) return border.m_Left;
                    if (border.m_Right != Entity.Null) return border.m_Right;
                }
            }
            return Entity.Null;
        }

        /// <summary>The named road (street aggregate) joined to either end of the plaza, if any.</summary>
        private static Entity RoadOf(EntityManager em, Entity plaza, out Entity via)
        {
            via = Entity.Null;
            if (!em.HasBuffer<SubNet>(plaza))
            {
                return Entity.Null;
            }
            DynamicBuffer<SubNet> subNets = em.GetBuffer<SubNet>(plaza, isReadOnly: true);
            var own = new HashSet<Entity>();
            for (int i = 0; i < subNets.Length; i++)
            {
                own.Add(subNets[i].m_SubNet);
            }
            for (int i = 0; i < subNets.Length; i++)
            {
                if (!em.HasComponent<Edge>(subNets[i].m_SubNet))
                {
                    continue;
                }
                Edge edge = em.GetComponentData<Edge>(subNets[i].m_SubNet);
                foreach (Entity node in new[] { edge.m_Start, edge.m_End })
                {
                    if (!em.HasBuffer<ConnectedEdge>(node))
                    {
                        continue;
                    }
                    DynamicBuffer<ConnectedEdge> connected = em.GetBuffer<ConnectedEdge>(node, isReadOnly: true);
                    for (int j = 0; j < connected.Length; j++)
                    {
                        Entity other = connected[j].m_Edge;
                        // Only built roads: not previews still being drawn (Temp) or pieces being removed.
                        if (own.Contains(other) || !em.HasComponent<Aggregated>(other)
                            || em.HasComponent<Game.Tools.Temp>(other) || em.HasComponent<Game.Common.Deleted>(other)
                            || !em.HasComponent<PrefabRef>(other)
                            || !em.HasComponent<RoadData>(em.GetComponentData<PrefabRef>(other).m_Prefab))
                        {
                            continue;
                        }
                        Entity aggregate = em.GetComponentData<Aggregated>(other).m_Aggregate;
                        if (aggregate != Entity.Null && !em.HasComponent<Game.Tools.Temp>(aggregate))
                        {
                            via = other;
                            return aggregate;
                        }
                    }
                }
            }
            return Entity.Null;
        }

        /// <summary>
        /// The current layout has one median-road piece running from the channel start to the island tails;
        /// older plazas have a shorter one (islands only) or split ones.
        /// </summary>
        private static bool IsOutdated(EntityManager em, PrefabSystem prefabs, Entity plaza, PrefabBase prefab)
        {
            if (!PlazaAssetSystem.PlazaLayouts.TryGetValue(prefab, out PlazaLayout layout) || !em.HasBuffer<SubNet>(plaza))
            {
                return false;
            }
            float expected = layout.IslandEndZ - layout.ChannelStartZ;
            DynamicBuffer<SubNet> subNets = em.GetBuffer<SubNet>(plaza, isReadOnly: true);
            for (int i = 0; i < subNets.Length; i++)
            {
                Entity net = subNets[i].m_SubNet;
                if (em.HasComponent<Curve>(net) && em.HasComponent<PrefabRef>(net)
                    && prefabs.TryGetPrefab(em.GetComponentData<PrefabRef>(net), out PrefabBase road)
                    && road is NetPrefab netPrefab && PlazaRoad.TollRoads.Contains(netPrefab)
                    && em.GetComponentData<Curve>(net).m_Length > expected - 1f)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
