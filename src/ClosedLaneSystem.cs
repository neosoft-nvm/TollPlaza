using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace TollPlaza
{
    /// <summary>
    /// Makes route finding avoid closed booth lanes. A closed lane is handed straight to route finding (the way the
    /// Road Rules mod closes lanes) as a crawling, forbidden lane, so any other booth is far cheaper, and handed
    /// again every so often in case the game refreshed it meanwhile. When a lane is marked PathfindUpdated, the
    /// game's LaneDataSystem first resets its speed and flags and LanesModifiedSystem then hands it to route finding;
    /// this also runs between the two so that refresh keeps it closed. It isn't blocked outright: a vehicle with no
    /// other way through must still have a route, or the game removes it. The lane's real speed is set back to the
    /// booth speed by TollCollectionSystem a few frames later, so vehicles that do use it don't crawl. Nothing here is
    /// saved.
    /// </summary>
    public partial class ClosedLaneSystem : GameSystemBase
    {
        /// <summary>The lanes to keep closed (TollCollectionSystem keeps this up to date).</summary>
        public static readonly HashSet<Entity> Lanes = new HashSet<Entity>();

        /// <summary>
        /// Speed route finding sees on a closed lane (m/s): a booth lane of ~25 m then costs minutes, far more
        /// than any detour through the open booths.
        /// </summary>
        public const float ClosedSpeed = 0.1f;

        /// <summary>Extra route-finding cost of a closed lane, in each of time, behaviour, money and comfort.</summary>
        private static readonly Unity.Mathematics.float4 ClosedCost = new Unity.Mathematics.float4(1000f);

        // How often closed lanes are handed to route finding again (frames).
        private const int ResendFrames = 32;

        private static int s_Applied;
        private PathfindQueueSystem m_PathfindQueue;
        private readonly HashSet<Entity> m_Sent = new HashSet<Entity>();
        private int m_Frame;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PathfindQueue = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
        }

        protected override void OnUpdate()
        {
            if (Lanes.Count == 0)
            {
                m_Sent.Clear();
                return;
            }
            bool resend = ++m_Frame % ResendFrames == 0;
            m_Sent.RemoveWhere(lane => !Lanes.Contains(lane));
            foreach (Entity lane in Lanes)
            {
                if (!EntityManager.Exists(lane) || !EntityManager.HasComponent<Game.Net.CarLane>(lane) || EntityManager.HasComponent<Deleted>(lane))
                {
                    continue;
                }
                // The game is about to hand the lane to route finding itself: keep it closed.
                if (EntityManager.HasComponent<PathfindUpdated>(lane))
                {
                    Game.Net.CarLane before = EntityManager.GetComponentData<Game.Net.CarLane>(lane);
                    EntityManager.SetComponentData(lane, Close(before));
                    if (++s_Applied <= 20)
                    {
                        Mod.Log.Info($"[TollPlaza] Lane check: closed lane #{lane.Index} refreshed by the game, kept closed "
                            + $"(speed {before.m_SpeedLimit:F1} > {ClosedSpeed}, flags {before.m_Flags})");
                    }
                }
                // Newly closed, or due again: hand it to route finding directly.
                if (m_Sent.Add(lane) || resend)
                {
                    Send(lane);
                }
            }
        }

        /// <summary>Queues the closed lane's route-finding data (as the game builds it, but closed).</summary>
        private void Send(Entity lane)
        {
            if (!EntityManager.HasComponent<Lane>(lane) || !EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<PrefabRef>(lane))
            {
                return;
            }
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
            if (!EntityManager.HasComponent<NetLaneData>(prefab) || !EntityManager.HasComponent<CarLaneData>(prefab))
            {
                return;
            }
            NetLaneData netLaneData = EntityManager.GetComponentData<NetLaneData>(prefab);
            if (netLaneData.m_PathfindPrefab == Entity.Null || !EntityManager.HasComponent<PathfindCarData>(netLaneData.m_PathfindPrefab))
            {
                return;
            }
            Lane nodes = EntityManager.GetComponentData<Lane>(lane);
            Curve curve = EntityManager.GetComponentData<Curve>(lane);
            PathSpecification specification = PathUtils.GetCarDriveSpecification(curve, Close(EntityManager.GetComponentData<Game.Net.CarLane>(lane)),
                default(MasterLane), EntityManager.GetComponentData<CarLaneData>(prefab),
                EntityManager.GetComponentData<PathfindCarData>(netLaneData.m_PathfindPrefab), 0, 0.01f);
            // Costly whatever a driver weighs most (time, behaviour, money, comfort: drivers weigh them differently,
            // and some barely weigh time, which the crawling speed alone makes costly).
            specification.m_Costs.m_Value += ClosedCost;

            var action = new UpdateAction(1, Allocator.Persistent);
            action.m_UpdateData[0] = new UpdateActionData
            {
                m_Owner = lane,
                m_StartNode = nodes.m_StartNode,
                m_MiddleNode = nodes.m_MiddleNode,
                m_EndNode = nodes.m_EndNode,
                m_Specification = specification,
                m_Location = PathUtils.GetLocationSpecification(curve),
            };
            m_PathfindQueue.Enqueue(action, Dependency);
            if (++s_Applied <= 20)
            {
                Mod.Log.Info($"[TollPlaza] Lane check: closed lane #{lane.Index} queued for route finding (max speed {specification.m_MaxSpeed})");
            }
        }

        public static Game.Net.CarLane Close(Game.Net.CarLane carLane)
        {
            carLane.m_Flags |= Game.Net.CarLaneFlags.Forbidden;
            carLane.m_SpeedLimit = ClosedSpeed;
            return carLane;
        }

        public static Game.Net.CarLane Open(Game.Net.CarLane carLane)
        {
            carLane.m_Flags &= ~Game.Net.CarLaneFlags.Forbidden;
            // An earlier build blocked closed lanes outright: clear that too (the game recalculates any real
            // blockage, e.g. an accident, when the lane is refreshed).
            carLane.m_BlockageStart = byte.MaxValue;
            carLane.m_BlockageEnd = 0;
            return carLane;
        }
    }
}
