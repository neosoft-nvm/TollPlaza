using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TollPlaza
{
    /// <summary>
    /// Keeps other roads from joining a working plaza anywhere but its two ends. The plaza's road is several pieces
    /// (approach steps, channel and booths, exit), and the road tool lets a road connect wherever two pieces meet,
    /// as it does for any building's own roads, so a street could join right under the canopy. While a road is
    /// being drawn, any new piece that would join the plaza between its ends gets the game's Error mark (drawn red,
    /// and the tool won't build while anything carries it) and an overlap warning icon. The game checks the preview
    /// and clears marks it didn't set during the Modification phases, so this runs afterwards (UIUpdate), before the
    /// next click is handled. Nothing is saved: it only touches the tool's preview.
    /// </summary>
    public partial class PlazaConnectionGuardSystem : GameSystemBase
    {
        private EntityQuery m_TempEdges;
        private EntityQuery m_ToolErrors;
        private IconCommandSystem m_IconCommandSystem;
        private PrefabSystem m_PrefabSystem;
        private readonly HashSet<Entity> m_Flagged = new HashSet<Entity>();
        private Entity m_OverlapIcon;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_TempEdges = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Owner>());
            m_ToolErrors = GetEntityQuery(ComponentType.ReadOnly<ToolErrorData>());
            m_IconCommandSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            RequireForUpdate(m_TempEdges);
        }

        protected override void OnUpdate()
        {
            using (NativeArray<Entity> edges = m_TempEdges.ToEntityArray(Allocator.Temp))
            {
                var still = new HashSet<Entity>();
                foreach (Entity edge in edges)
                {
                    Temp temp = EntityManager.GetComponentData<Temp>(edge);
                    if ((temp.m_Flags & TempFlags.Delete) != 0)
                    {
                        continue;
                    }
                    Edge nodes = EntityManager.GetComponentData<Edge>(edge);
                    Entity plazaNode = InnerPlazaNode(nodes.m_Start);
                    if (plazaNode == Entity.Null)
                    {
                        plazaNode = InnerPlazaNode(nodes.m_End);
                    }
                    if (plazaNode == Entity.Null)
                    {
                        continue;
                    }
                    still.Add(edge);
                    if (!EntityManager.HasComponent<Error>(edge))
                    {
                        EntityManager.AddComponent<Error>(edge);
                        EntityManager.AddComponent<BatchesUpdated>(edge);
                    }
                    if (m_Flagged.Add(edge))
                    {
                        AddIcon(edge, plazaNode);
                    }
                }
                m_Flagged.IntersectWith(still);
            }
        }

        /// <summary>
        /// The existing node behind <paramref name="node"/> (a node of the preview) if it is a working plaza's node
        /// between two of the plaza's own pieces, else Entity.Null. The two end nodes have one plaza piece and are fine.
        /// </summary>
        private Entity InnerPlazaNode(Entity node)
        {
            if (EntityManager.HasComponent<Temp>(node))
            {
                node = EntityManager.GetComponentData<Temp>(node).m_Original;
            }
            if (node == Entity.Null || !EntityManager.HasComponent<Owner>(node) || !EntityManager.HasBuffer<ConnectedEdge>(node))
            {
                return Entity.Null;
            }
            Entity plaza = EntityManager.GetComponentData<Owner>(node).m_Owner;
            if (!IsWorkingPlaza(plaza))
            {
                return Entity.Null;
            }
            int plazaPieces = 0;
            foreach (ConnectedEdge connected in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                if (EntityManager.HasComponent<Owner>(connected.m_Edge) && EntityManager.GetComponentData<Owner>(connected.m_Edge).m_Owner == plaza)
                {
                    plazaPieces++;
                }
            }
            return plazaPieces >= 2 ? node : Entity.Null;
        }

        private bool IsWorkingPlaza(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.HasComponent<PrefabRef>(entity))
            {
                return false;
            }
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            return m_PrefabSystem.TryGetPrefab(prefab, out PrefabBase prefabBase) && prefabBase != null
                && PlazaAssetSystem.PlazaLayouts.ContainsKey(prefabBase);
        }

        /// <summary>The game's "overlaps existing" warning icon over the plaza node, as the tool shows for its own errors.</summary>
        private void AddIcon(Entity edge, Entity plazaNode)
        {
            if (m_OverlapIcon == Entity.Null)
            {
                using (NativeArray<Entity> prefabs = m_ToolErrors.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity prefab in prefabs)
                    {
                        if (EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error == ErrorType.OverlapExisting)
                        {
                            m_OverlapIcon = prefab;
                            break;
                        }
                    }
                }
            }
            if (m_OverlapIcon == Entity.Null || !EntityManager.HasComponent<Node>(plazaNode))
            {
                return;
            }
            float3 location = EntityManager.GetComponentData<Node>(plazaNode).m_Position;
            m_IconCommandSystem.CreateCommandBuffer().Add(edge, m_OverlapIcon, location, IconPriority.Error, IconClusterLayer.Default,
                IconFlags.IgnoreTarget, plazaNode, isTemp: true);
        }
    }
}
