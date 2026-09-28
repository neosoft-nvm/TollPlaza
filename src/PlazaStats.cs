using System.Collections.Generic;
using Unity.Entities;

namespace TollPlaza
{
    /// <summary>Vehicles charged and money collected over one game month.</summary>
    public class MonthStats
    {
        public int Cars, Trucks, Buses, Exempt;
        public long Cents;

        public int Vehicles => Cars + Trucks + Buses + Exempt;

        public void Add(VehicleClass c, int cents)
        {
            switch (c)
            {
                case VehicleClass.Truck: Trucks++; break;
                case VehicleClass.Bus: Buses++; break;
                case VehicleClass.Exempt: Exempt++; break;
                default: Cars++; break;
            }
            Cents += cents;
        }
    }

    /// <summary>
    /// A plaza's (or the whole city's) traffic and revenue for the current and the previous game month,
    /// plus the booth lanes the player switched between cash and E-Toll.
    /// </summary>
    public class PlazaStats
    {
        public int Month = -1;
        public MonthStats ThisMonth = new MonthStats();
        public MonthStats LastMonth = new MonthStats();

        private readonly Dictionary<int, PlazaStats> m_Booths = new Dictionary<int, PlazaStats>();

        /// <summary>The booth lanes that have seen traffic, by lane index.</summary>
        public IReadOnlyDictionary<int, PlazaStats> Booths => m_Booths;

        /// <summary>One booth lane's own traffic and revenue.</summary>
        public PlazaStats Booth(int lane)
        {
            if (!m_Booths.TryGetValue(lane, out PlazaStats booth))
            {
                booth = new PlazaStats();
                m_Booths.Add(lane, booth);
            }
            return booth;
        }

        /// <summary>Per booth lane: true = cash, false = E-Toll; null until the player changes one.</summary>
        public bool[] Cash;

        /// <summary>Name the player gave the plaza; null = automatic (district, road or city name).</summary>
        public string Name;

        /// <summary>Starts a new month when the game date moves on (month key = year * 12 + month).</summary>
        public void Roll(int month)
        {
            if (month == Month)
            {
                return;
            }
            LastMonth = month == Month + 1 ? ThisMonth : new MonthStats();
            ThisMonth = new MonthStats();
            Month = month;
        }

        private static readonly Dictionary<Entity, PlazaStats> s_Plazas = new Dictionary<Entity, PlazaStats>();

        /// <summary>All plazas together (the control panel).</summary>
        public static readonly PlazaStats City = new PlazaStats();

        /// <summary>Every plaza with stats or a cash/E-Toll choice (for PlazaPersistence).</summary>
        public static IReadOnlyDictionary<Entity, PlazaStats> Plazas => s_Plazas;

        /// <summary>Forgets every plaza and the city totals (on every city load; entities are renumbered).</summary>
        public static void ResetAll()
        {
            s_Plazas.Clear();
            City.Month = -1;
            City.ThisMonth = new MonthStats();
            City.LastMonth = new MonthStats();
            City.m_Booths.Clear();
        }

        public static PlazaStats Of(Entity plaza)
        {
            if (!s_Plazas.TryGetValue(plaza, out PlazaStats stats))
            {
                stats = new PlazaStats();
                s_Plazas.Add(plaza, stats);
            }
            return stats;
        }

        /// <summary>Whether a booth lane takes cash: the player's choice, else the plaza's default.</summary>
        public static bool IsCashLane(Entity plaza, PlazaLayout layout, int lane)
        {
            return s_Plazas.TryGetValue(plaza, out PlazaStats stats) && stats.Cash != null && lane < stats.Cash.Length
                ? stats.Cash[lane]
                : layout.IsCashLane(lane);
        }

        public static void ToggleCashLane(Entity plaza, PlazaLayout layout, int lane)
        {
            PlazaStats stats = Of(plaza);
            if (stats.Cash == null)
            {
                stats.Cash = new bool[layout.LaneCount];
                for (int i = 0; i < stats.Cash.Length; i++)
                {
                    stats.Cash[i] = layout.IsCashLane(i);
                }
            }
            if (lane >= 0 && lane < stats.Cash.Length)
            {
                stats.Cash[lane] = !stats.Cash[lane];
            }
        }
    }
}
