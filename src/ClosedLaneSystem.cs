using System.Collections.Generic;
using Game;
using Game.Common;
using Unity.Entities;

namespace TollPlaza
{
    /// <summary>
    /// Makes route finding avoid closed booth lanes. When a lane is marked PathfindUpdated, the game's
    /// LaneDataSystem first resets its speed, blockage and some flags, and LanesModifiedSystem then hands it to
    /// route finding; this runs between the two and gives each closed lane a crawling speed and the forbidden
    /// flag, so any other booth is far cheaper. It isn't blocked outright: a vehicle with no other way through
    /// (e.g. one that can only reach that booth) must still have a route, or the game removes it. The lane's
    /// real speed is set back to the booth speed by TollCollectionSystem a few frames later, so vehicles that do
    /// use it don't crawl. Nothing here is saved.
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

        protected override void OnUpdate()
        {
            if (Lanes.Count == 0)
            {
                return;
            }
            foreach (Entity lane in Lanes)
            {
                // Only while route finding is about to read the lane; otherwise the booth speed stands.
                if (EntityManager.Exists(lane) && EntityManager.HasComponent<Game.Net.CarLane>(lane)
                    && EntityManager.HasComponent<PathfindUpdated>(lane) && !EntityManager.HasComponent<Deleted>(lane))
                {
                    EntityManager.SetComponentData(lane, Close(EntityManager.GetComponentData<Game.Net.CarLane>(lane)));
                }
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
