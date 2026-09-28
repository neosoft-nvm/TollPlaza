using System.Collections.Generic;
using System.Text;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TollPlaza
{
    /// <summary>
    /// Writes each placed plaza's road pieces to the mod log, in the plaza's own coordinates: which road
    /// prefab, where each piece starts and ends, how long it is, and what every node connects to. Logged
    /// again whenever that changes (e.g. after the player connects a highway), so the road can be checked
    /// against the layout without screenshots.
    /// </summary>
    public partial class PlazaDiagnosticsSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_PlazaQuery;
        private readonly Dictionary<Entity, string> m_Logged = new Dictionary<Entity, string>();

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 128;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PlazaQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Object>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Net.SubNet>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnUpdate()
        {
            using (NativeArray<Entity> plazas = m_PlazaQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity plaza in plazas)
                {
                    if (!m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(plaza), out PrefabBase prefab)
                        || !PlazaAssetSystem.PlazaLayouts.ContainsKey(prefab))
                    {
                        continue;
                    }
                    string report = Describe(plaza);
                    if (!m_Logged.TryGetValue(plaza, out string last) || last != report)
                    {
                        m_Logged[plaza] = report;
                        Mod.Log.Info($"[TollPlaza] Plaza {plaza} ({prefab.name}) roads:\n{report}");
                    }
                }
            }
        }

        private string Describe(Entity plaza)
        {
            Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(plaza);
            quaternion inv = math.inverse(t.m_Rotation);
            string Local(float3 p)
            {
                float3 l = math.mul(inv, p - t.m_Position);
                return $"({l.x:F1}, {l.y:F1}, {l.z:F1})";
            }

            var sb = new StringBuilder();
            DynamicBuffer<Game.Net.SubNet> subNets = EntityManager.GetBuffer<Game.Net.SubNet>(plaza, isReadOnly: true);
            for (int i = 0; i < subNets.Length; i++)
            {
                Entity net = subNets[i].m_SubNet;
                string name = PrefabName(net);
                if (EntityManager.HasComponent<Edge>(net) && EntityManager.HasComponent<Curve>(net))
                {
                    Curve curve = EntityManager.GetComponentData<Curve>(net);
                    Edge edge = EntityManager.GetComponentData<Edge>(net);
                    Bezier4x3 b = curve.m_Bezier;
                    sb.Append($"  edge {net} {name}: {Local(b.a)} -> {Local(b.d)} ctrl {Local(b.b)} {Local(b.c)} length {curve.m_Length:F1}");
                    if (EntityManager.HasComponent<Composition>(net))
                    {
                        Composition c = EntityManager.GetComponentData<Composition>(net);
                        if (EntityManager.HasComponent<NetCompositionData>(c.m_Edge))
                        {
                            sb.Append($" width {EntityManager.GetComponentData<NetCompositionData>(c.m_Edge).m_Width:F1}");
                        }
                    }
                    sb.Append($" nodes {edge.m_Start}/{edge.m_End}\n");
                }
                else if (EntityManager.HasComponent<Node>(net))
                {
                    Node node = EntityManager.GetComponentData<Node>(net);
                    sb.Append($"  node {net} {name} at {Local(node.m_Position)} connects:");
                    if (EntityManager.HasBuffer<ConnectedEdge>(net))
                    {
                        DynamicBuffer<ConnectedEdge> connected = EntityManager.GetBuffer<ConnectedEdge>(net, isReadOnly: true);
                        for (int j = 0; j < connected.Length; j++)
                        {
                            Entity e = connected[j].m_Edge;
                            bool own = EntityManager.HasComponent<Owner>(e) && EntityManager.GetComponentData<Owner>(e).m_Owner == plaza;
                            sb.Append($" {e} {PrefabName(e)}{(own ? string.Empty : " [player road]")};");
                        }
                    }
                    sb.Append('\n');
                }
                else
                {
                    sb.Append($"  other {net} {name}\n");
                }
            }
            return sb.ToString();
        }

        private string PrefabName(Entity entity)
        {
            return EntityManager.HasComponent<PrefabRef>(entity)
                && m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(entity), out PrefabBase prefab)
                ? prefab.name
                : "?";
        }
    }
}
