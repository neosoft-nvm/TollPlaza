using System.Collections.Generic;
using Game;
using Game.Common;
using Unity.Entities;

namespace TollPlaza
{
    /// <summary>
    /// Keeps closed booth lanes closed where route finding reads them. When a lane is marked PathfindUpdated,
    /// the game's LaneDataSystem first resets its speed, blockage and some flags, and LanesModifiedSystem then
    /// hands it to route finding; this runs between the two and marks the closed lanes blocked end to end (the
    /// game's own "lane blocked" data, as for an accident) and forbidden, so routes can't go through them.
    /// TollCollectionSystem decides which lanes are closed; nothing here is saved.
    /// </summary>
    public partial class ClosedLaneSystem : GameSystemBase
    {
        /// <summary>The lanes to keep closed (TollCollectionSystem keeps this up to date).</summary>
        public static readonly HashSet<Entity> Lanes = new HashSet<Entity>();

        protected override void OnUpdate()
        {
            if (Lanes.Count == 0)
            {
                return;
            }
            foreach (Entity lane in Lanes)
            {
                if (EntityManager.Exists(lane) && EntityManager.HasComponent<Game.Net.CarLane>(lane) && !EntityManager.HasComponent<Deleted>(lane))
                {
                    Game.Net.CarLane carLane = EntityManager.GetComponentData<Game.Net.CarLane>(lane);
                    if (!IsClosed(carLane))
                    {
                        EntityManager.SetComponentData(lane, Close(carLane));
                    }
                }
            }
        }

        public static bool IsClosed(Game.Net.CarLane carLane) =>
            (carLane.m_Flags & Game.Net.CarLaneFlags.Forbidden) != 0 && carLane.m_BlockageStart == 0 && carLane.m_BlockageEnd == byte.MaxValue;

        public static Game.Net.CarLane Close(Game.Net.CarLane carLane)
        {
            carLane.m_Flags |= Game.Net.CarLaneFlags.Forbidden;
            carLane.m_BlockageStart = 0;
            carLane.m_BlockageEnd = byte.MaxValue;
            return carLane;
        }

        public static Game.Net.CarLane Open(Game.Net.CarLane carLane)
        {
            carLane.m_Flags &= ~Game.Net.CarLaneFlags.Forbidden;
            // No blockage (the game recalculates any real one, e.g. an accident, when the lane is refreshed).
            carLane.m_BlockageStart = byte.MaxValue;
            carLane.m_BlockageEnd = 0;
            return carLane;
        }
    }
}
