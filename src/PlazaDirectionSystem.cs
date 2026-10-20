using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using RoadFlags = Game.Net.RoadFlags;
using SubLane = Game.Net.SubLane;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TollPlaza
{
    /// <summary>
    /// Keeps a one-way road joined to a plaza end flowing the same way as the plaza. When a plaza snaps onto the end
    /// of a road, the game lines it up with the road but ignores which way the road's traffic goes, so a highway drawn
    /// away from that point meets the plaza head-on and both dead-end. Every half second this checks each working
    /// plaza's two ends; a wrong-way one-way road there is reversed (as the road tool's replace mode does), together
    /// with the road pieces continuing it, back to where it ends. If that stretch reaches a junction or another
    /// building, it is left alone: reversing it could break the player's network, and PlazaConnectionGuardSystem
    /// already turns such a placement red. Reversing changes the road itself, so it is saved with the city like any
    /// road edit; the mod itself saves nothing.
    /// </summary>
    public partial class PlazaDirectionSystem : GameSystemBase
    {
        private const int CheckFrames = 30;
        private const int MaxChain = 256;

        public enum Result
        {
            Fine,
            Reversible,
            Blocked,
        }

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_Plazas;
        private readonly HashSet<Entity> m_LoggedBlocked = new HashSet<Entity>();
        private readonly Dictionary<(Entity plaza, Entity node), string> m_LastState = new Dictionary<(Entity, Entity), string>();
        private readonly Dictionary<(Entity node, Entity road), int> m_Joined = new Dictionary<(Entity, Entity), int>();
        private const int ClearChecks = 4;
        private IconCommandSystem m_IconCommandSystem;
        private EntityQuery m_TrafficConfig;
        private bool m_Primed;
        private int m_Frame;

        /// <summary>Diagnostic text for the log: what is joined at a plaza end and which way each piece flows.</summary>
        private static string Describe(EntityManager em, Entity node, bool plazaFlowsOut)
        {
            var text = new System.Text.StringBuilder(plazaFlowsOut ? "entry" : "exit");
            if (node == Entity.Null || !em.HasBuffer<ConnectedEdge>(node))
            {
                return text.Append(", no node").ToString();
            }
            foreach (ConnectedEdge connected in em.GetBuffer<ConnectedEdge>(node, true))
            {
                Entity edge = connected.m_Edge;
                bool live = IsLive(em, edge);
                text.Append($"; {edge}");
                text.Append(em.HasComponent<Owner>(edge) ? " owned" : " player");
                text.Append(live ? "" : " not-live");
                if (live)
                {
                    int flow = Flow(em, edge);
                    text.Append(flow == 0 ? " flow 0" : $" flow {flow} into-end {FlowsInto(em, edge, flow, node)}");
                    text.Append($" lanes {(em.HasBuffer<SubLane>(edge) ? em.GetBuffer<SubLane>(edge, true).Length : -1)}");
                    if (flow == 0)
                    {
                        text.Append(" [" + LaneStats(em, edge) + "]");
                    }
                }
            }
            return text.ToString();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_IconCommandSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            m_TrafficConfig = GetEntityQuery(ComponentType.ReadOnly<TrafficConfigurationData>());
            m_Plazas = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Object>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Net.SubNet>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            if (++m_Frame % CheckFrames != 0 || PlazaAssetSystem.PlazaLayouts.Count == 0)
            {
                return;
            }
            using (NativeArray<Entity> plazas = m_Plazas.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity plaza in plazas)
                {
                    if (!m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(plaza), out PrefabBase prefab)
                        || !PlazaAssetSystem.PlazaLayouts.ContainsKey(prefab))
                    {
                        continue;
                    }
                    foreach ((Entity node, bool plazaFlowsOut) in Ends(EntityManager, plaza))
                    {
                        Result result = Check(EntityManager, node, plazaFlowsOut, out List<Entity> chain);
                        string state = Describe(EntityManager, node, plazaFlowsOut) + " -> " + result;
                        if (!m_LastState.TryGetValue((plaza, node), out string last) || last != state)
                        {
                            m_LastState[(plaza, node)] = state;
                            Mod.Log.Info($"[TollPlaza] Direction check, plaza {plaza} end {node}: {state}");
                        }
                        if (result == Result.Fine)
                        {
                            ClearDeadEndIcons(node);
                        }
                        if (result == Result.Reversible)
                        {
                            foreach (Entity edge in chain)
                            {
                                Reverse(edge);
                            }
                            Mod.Log.Info($"[TollPlaza] Reversed {chain.Count} wrong-way road piece(s) joined to plaza {plaza} so traffic flows with it");
                        }
                        else if (result == Result.Blocked && m_LoggedBlocked.Add(node))
                        {
                            Mod.Log.Info($"[TollPlaza] Plaza {plaza}: the road at one end runs the other way and reaches a junction; left as it is");
                        }
                    }
                }
            }
            m_Primed = true;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Primed = false;
            m_Joined.Clear();
            m_LastState.Clear();
            m_LoggedBlocked.Clear();
        }

        /// <summary>
        /// A road newly joined to a plaza end is cleared of the game's "Dead end" notification. That icon sits on the
        /// road's end lane from when the road stopped loose; the game only removes it when it re-checks the road, and
        /// the joint never triggers that, so it stayed until the player reversed the road and back by hand (which
        /// rebuilds the lanes and drops their icons). The clearing is repeated for a few checks in case the game
        /// re-adds it. Roads already joined when the city loaded are only noted, not touched.
        /// </summary>
        private void ClearDeadEndIcons(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<ConnectedEdge>(node))
            {
                return;
            }
            foreach (ConnectedEdge connected in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                Entity edge = connected.m_Edge;
                if (!IsLive(EntityManager, edge) || EntityManager.HasComponent<Owner>(edge) || !EntityManager.HasBuffer<SubLane>(edge))
                {
                    continue;
                }
                if (!m_Joined.TryGetValue((node, edge), out int left))
                {
                    left = m_Primed ? ClearChecks : 0;
                }
                if (left <= 0)
                {
                    m_Joined[(node, edge)] = 0;
                    continue;
                }
                m_Joined[(node, edge)] = left - 1;
                if (m_TrafficConfig.IsEmptyIgnoreFilter)
                {
                    continue;
                }
                Entity deadEnd = m_TrafficConfig.GetSingleton<TrafficConfigurationData>().m_DeadEndNotification;
                IconCommandBuffer icons = m_IconCommandSystem.CreateCommandBuffer();
                foreach (SubLane sub in EntityManager.GetBuffer<SubLane>(edge, true))
                {
                    icons.Remove(sub.m_SubLane, deadEnd, Entity.Null);
                    icons.Remove(sub.m_SubLane, deadEnd, Entity.Null, Game.Notifications.IconFlags.SecondaryLocation);
                }
                if (left == ClearChecks)
                {
                    Mod.Log.Info($"[TollPlaza] Cleared dead-end icons from road piece {edge} newly joined at plaza end {node}");
                }
            }
        }

        /// <summary>
        /// A plaza's two outer nodes (each with one of the plaza's own road pieces) and whether the plaza's traffic
        /// flows out of that node (its entry) or into it (its exit). Works for a preview plaza too (temporary nodes).
        /// </summary>
        public static List<(Entity node, bool plazaFlowsOut)> Ends(EntityManager em, Entity plaza)
        {
            var ends = new List<(Entity, bool)>();
            if (!em.HasBuffer<Game.Net.SubNet>(plaza))
            {
                return ends;
            }
            var uses = new Dictionary<Entity, int>();
            var flowsOut = new Dictionary<Entity, bool>();
            foreach (Game.Net.SubNet sub in em.GetBuffer<Game.Net.SubNet>(plaza, true))
            {
                if (!em.HasComponent<Edge>(sub.m_SubNet))
                {
                    continue;
                }
                Edge edge = em.GetComponentData<Edge>(sub.m_SubNet);
                // The plaza's pieces run in the direction of travel (PlazaRoad), so traffic leaves the start node.
                uses[edge.m_Start] = (uses.TryGetValue(edge.m_Start, out int s) ? s : 0) + 1;
                uses[edge.m_End] = (uses.TryGetValue(edge.m_End, out int e) ? e : 0) + 1;
                flowsOut[edge.m_Start] = true;
                flowsOut[edge.m_End] = false;
            }
            foreach (var use in uses)
            {
                if (use.Value == 1)
                {
                    ends.Add((use.Key, flowsOut[use.Key]));
                }
            }
            return ends;
        }

        /// <summary>
        /// Checks the road joined to a plaza end at <paramref name="node"/> (an existing node). Fine: nothing there,
        /// a junction, a two-way road, or a one-way road already flowing the right way. Reversible: a one-way road
        /// flowing the wrong way whose stretch ends loose; <paramref name="chain"/> lists its pieces. Blocked: it
        /// flows the wrong way but its stretch reaches a junction or a building's road.
        /// </summary>
        public static Result Check(EntityManager em, Entity node, bool plazaFlowsOut, out List<Entity> chain)
        {
            chain = null;
            if (node == Entity.Null || !em.HasBuffer<ConnectedEdge>(node))
            {
                return Result.Fine;
            }
            Entity road = Entity.Null;
            int roads = 0;
            foreach (ConnectedEdge connected in em.GetBuffer<ConnectedEdge>(node, true))
            {
                if (IsLive(em, connected.m_Edge) && !em.HasComponent<Owner>(connected.m_Edge))
                {
                    road = connected.m_Edge;
                    roads++;
                }
            }
            if (roads != 1)
            {
                return Result.Fine; // nothing joined, or the player built a junction right at the plaza end
            }
            int flow = Flow(em, road);
            if (flow == 0)
            {
                return Result.Fine; // two-way (or no car lanes): no direction to match
            }
            if (FlowsInto(em, road, flow, node) == plazaFlowsOut)
            {
                return Result.Fine; // road traffic arrives at the entry, or leaves the exit
            }

            // Walk the stretch away from the plaza: piece after piece through plain joints (one road in, one out).
            chain = new List<Entity> { road };
            Entity current = road, at = node;
            while (chain.Count < MaxChain)
            {
                Edge edge = em.GetComponentData<Edge>(current);
                Entity far = edge.m_Start == at ? edge.m_End : edge.m_Start;
                Entity next = Entity.Null;
                int others = 0;
                foreach (ConnectedEdge connected in em.GetBuffer<ConnectedEdge>(far, true))
                {
                    if (connected.m_Edge != current && IsLive(em, connected.m_Edge))
                    {
                        next = connected.m_Edge;
                        others++;
                    }
                }
                if (others == 0)
                {
                    return Result.Reversible; // the stretch ends loose
                }
                if (others > 1 || em.HasComponent<Owner>(next) || chain.Contains(next))
                {
                    return Result.Blocked; // a junction, a building's or plaza's road, or a loop
                }
                int nextFlow = Flow(em, next);
                // Same stretch: the next piece is one-way and carries the traffic on through the joint.
                if (nextFlow == 0 || FlowsInto(em, next, nextFlow, far) == FlowsInto(em, current, Flow(em, current), far))
                {
                    return Result.Blocked;
                }
                chain.Add(next);
                current = next;
                at = far;
            }
            return Result.Blocked;
        }

        /// <summary>
        /// Whether a lane runs the same way as its road piece. Compares the lane's overall direction with the piece's,
        /// not where the lane starts: a lane split into shorter pieces can start nearer the piece's far end, which
        /// made a one-way road read as half forward and half backward (no direction) and the wrong-way fix skip it.
        /// </summary>
        private static bool RunsForward(Bezier4x3 lane, Bezier4x3 road) =>
            math.dot(lane.d - lane.a, road.d - road.a) >= 0f;

        /// <summary>Diagnostic: why Flow gave 0. Car lanes split by direction, two-way ones, and the lane flags seen.</summary>
        private static string LaneStats(EntityManager em, Entity edge)
        {
            if (!em.HasBuffer<SubLane>(edge) || !em.HasComponent<Curve>(edge))
            {
                return "no lanes or curve";
            }
            Bezier4x3 road = em.GetComponentData<Curve>(edge).m_Bezier;
            int forward = 0, backward = 0, twoway = 0, car = 0, noCurve = 0;
            Game.Net.CarLaneFlags seen = 0;
            foreach (SubLane sub in em.GetBuffer<SubLane>(edge, true))
            {
                Entity lane = sub.m_SubLane;
                if (!em.HasComponent<Game.Net.CarLane>(lane))
                {
                    continue;
                }
                car++;
                Game.Net.CarLaneFlags flags = em.GetComponentData<Game.Net.CarLane>(lane).m_Flags;
                seen |= flags;
                if (!em.HasComponent<Curve>(lane))
                {
                    noCurve++;
                    continue;
                }
                if ((flags & Game.Net.CarLaneFlags.Twoway) != 0)
                {
                    twoway++;
                    continue;
                }
                if (RunsForward(em.GetComponentData<Curve>(lane).m_Bezier, road)) forward++; else backward++;
            }
            return $"car {car} fwd {forward} bwd {backward} twoway {twoway} nocurve {noCurve} flags {seen}";
        }

        private static bool IsLive(EntityManager em, Entity edge) =>
            em.HasComponent<Edge>(edge) && !em.HasComponent<Deleted>(edge) && !em.HasComponent<Game.Tools.Temp>(edge);

        private static bool FlowsInto(EntityManager em, Entity edge, int flow, Entity node)
        {
            Edge e = em.GetComponentData<Edge>(edge);
            return flow > 0 ? e.m_End == node : e.m_Start == node;
        }

        /// <summary>+1: one-way from start to end; -1: one-way from end to start; 0: two-way or no car lanes.</summary>
        private static int Flow(EntityManager em, Entity edge)
        {
            if (!em.HasBuffer<SubLane>(edge) || !em.HasComponent<Curve>(edge))
            {
                return 0;
            }
            Bezier4x3 road = em.GetComponentData<Curve>(edge).m_Bezier;
            int forward = 0, backward = 0;
            foreach (SubLane sub in em.GetBuffer<SubLane>(edge, true))
            {
                Entity lane = sub.m_SubLane;
                if (!em.HasComponent<Game.Net.CarLane>(lane) || !em.HasComponent<Curve>(lane))
                {
                    continue;
                }
                if ((em.GetComponentData<Game.Net.CarLane>(lane).m_Flags & Game.Net.CarLaneFlags.Twoway) != 0)
                {
                    return 0;
                }
                if (RunsForward(em.GetComponentData<Curve>(lane).m_Bezier, road))
                {
                    forward++;
                }
                else
                {
                    backward++;
                }
            }
            return forward > 0 && backward == 0 ? 1 : (backward > 0 && forward == 0 ? -1 : 0);
        }

        /// <summary>
        /// Turns a road piece round, as the road tool does when a one-way road is replaced the other way: its ends
        /// swap, its curve runs backwards, and its per-end and left/right settings swap with it. The game then
        /// rebuilds its lanes and shape.
        /// </summary>
        private void Reverse(Entity edge)
        {
            Edge e = EntityManager.GetComponentData<Edge>(edge);
            EntityManager.SetComponentData(edge, new Edge { m_Start = e.m_End, m_End = e.m_Start });

            Curve curve = EntityManager.GetComponentData<Curve>(edge);
            Bezier4x3 b = curve.m_Bezier;
            curve.m_Bezier = new Bezier4x3(b.d, b.c, b.b, b.a);
            EntityManager.SetComponentData(edge, curve);

            if (EntityManager.HasComponent<Elevation>(edge))
            {
                Elevation elevation = EntityManager.GetComponentData<Elevation>(edge);
                elevation.m_Elevation = elevation.m_Elevation.yx;
                EntityManager.SetComponentData(edge, elevation);
            }
            if (EntityManager.HasComponent<Upgraded>(edge))
            {
                Upgraded upgraded = EntityManager.GetComponentData<Upgraded>(edge);
                upgraded.m_Flags = NetCompositionHelpers.InvertCompositionFlags(upgraded.m_Flags);
                EntityManager.SetComponentData(edge, upgraded);
            }
            if (EntityManager.HasComponent<BuildOrder>(edge))
            {
                BuildOrder order = EntityManager.GetComponentData<BuildOrder>(edge);
                EntityManager.SetComponentData(edge, new BuildOrder { m_Start = order.m_End, m_End = order.m_Start });
            }
            if (EntityManager.HasComponent<Composition>(edge))
            {
                Composition composition = EntityManager.GetComponentData<Composition>(edge);
                (composition.m_StartNode, composition.m_EndNode) = (composition.m_EndNode, composition.m_StartNode);
                EntityManager.SetComponentData(edge, composition);
            }
            if (EntityManager.HasComponent<Road>(edge))
            {
                Road road = EntityManager.GetComponentData<Road>(edge);
                RoadFlags flags = road.m_Flags & ~(RoadFlags.StartHalfAligned | RoadFlags.EndHalfAligned);
                if ((road.m_Flags & RoadFlags.StartHalfAligned) != 0) flags |= RoadFlags.EndHalfAligned;
                if ((road.m_Flags & RoadFlags.EndHalfAligned) != 0) flags |= RoadFlags.StartHalfAligned;
                road.m_Flags = flags;
                EntityManager.SetComponentData(edge, road);
            }

            foreach (Entity entity in new[] { edge, e.m_Start, e.m_End })
            {
                if (!EntityManager.HasComponent<Updated>(entity))
                {
                    EntityManager.AddComponent<Updated>(entity);
                }
            }
        }
    }
}
