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
    /// after the district it stands in, else the road it's built on, else the city ("Oak Hills Toll Plaza");
    /// plazas that end up with the same name are numbered ("... Toll Plaza 2"). A name the player typed
    /// (PlazaStats.Name, kept with the toll data outside the save) replaces the automatic one.
    /// </summary>
    public static class PlazaNames
    {
        private const string Suffix = " Toll Plaza";

        private static readonly Dictionary<Entity, string> s_Names = new Dictionary<Entity, string>();
        private static readonly HashSet<Entity> s_Outdated = new HashSet<Entity>();

        public static string Of(Entity plaza) => s_Names.TryGetValue(plaza, out string name) ? name : "Toll Plaza";

        /// <summary>True for a plaza placed before the current road layout; bulldoze and re-place it.</summary>
        public static bool IsOutdated(Entity plaza) => s_Outdated.Contains(plaza);

        /// <summary>The player's name for the plaza, or null for the automatic one.</summary>
        public static string CustomName(Entity plaza) =>
            PlazaStats.Plazas.TryGetValue(plaza, out PlazaStats stats) && !string.IsNullOrWhiteSpace(stats.Name) ? stats.Name : null;

        /// <summary>Recomputes every placed plaza's name and layout check (oldest plaza first).</summary>
        public static void Refresh(EntityManager em, PrefabSystem prefabs, NameSystem names, string city,
            IList<(Entity entity, PrefabBase prefab, int lanes)> placed)
        {
            s_Names.Clear();
            s_Outdated.Clear();
            var auto = new List<(Entity entity, string name)>();
            var uses = new Dictionary<string, int>();
            foreach (var p in placed)
            {
                if (IsOutdated(em, prefabs, p.entity, p.prefab))
                {
                    s_Outdated.Add(p.entity);
                }
                string custom = CustomName(p.entity);
                if (custom != null)
                {
                    s_Names[p.entity] = custom;
                    continue;
                }
                string name = AutoName(em, names, p.entity, city);
                auto.Add((p.entity, name));
                uses[name] = uses.TryGetValue(name, out int n) ? n + 1 : 1;
            }
            var numbered = new Dictionary<string, int>();
            foreach ((Entity entity, string name) in auto)
            {
                if (uses[name] == 1)
                {
                    s_Names[entity] = name;
                    continue;
                }
                int number = numbered.TryGetValue(name, out int k) ? k + 1 : 1;
                numbered[name] = number;
                s_Names[entity] = number == 1 ? name : $"{name} {number}";
            }
        }

        private static string AutoName(EntityManager em, NameSystem names, Entity plaza, string city)
        {
            Entity district = DistrictOf(em, plaza);
            if (district != Entity.Null)
            {
                return Clean(names.GetRenderedLabelName(district)) + Suffix;
            }
            Entity road = RoadOf(em, plaza);
            if (road != Entity.Null)
            {
                return Clean(names.GetRenderedLabelName(road)) + Suffix;
            }
            return (string.IsNullOrWhiteSpace(city) ? "City" : city.Trim()) + Suffix;
        }

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
        private static Entity RoadOf(EntityManager em, Entity plaza)
        {
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
                        if (!own.Contains(other) && em.HasComponent<Aggregated>(other))
                        {
                            Entity aggregate = em.GetComponentData<Aggregated>(other).m_Aggregate;
                            if (aggregate != Entity.Null)
                            {
                                return aggregate;
                            }
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
