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

        /// <summary>The plaza's kept name: typed by the player, or picked automatically (district, else city)
        /// the first time and kept from then on; null until then.</summary>
        public string Name;

        /// <summary>Simulation frames in a minute at normal speed (60 per second), so vehicles/min stays true
        /// when the game is paused or sped up.</summary>
        public const uint FramesPerMinute = 3600;

        /// <summary>Frame counting started (city load), or null before the first update; stats aren't saved,
        /// so the first minute after loading is a partial one.</summary>
        public static uint? MeasuringSince;

        // The frames of the vehicles charged in the last minute (not saved).
        private readonly Queue<uint> m_Recent = new Queue<uint>();

        /// <summary>A booth lane closed by the player (kept with the toll data, PlazaPersistence): red X, cones, traffic kept out.</summary>
        public bool Closed { get; set; }

        /// <summary>A booth lane's exit gate and signal (not saved).</summary>
        public LaneGate Gate { get; } = new LaneGate();

        /// <summary>Frame of the last vehicle charged (not saved), or null.</summary>
        public uint? LastFrame { get; private set; }

        public void Record(uint frame)
        {
            LastFrame = frame;
            m_Recent.Enqueue(frame);
            Prune(frame);
        }

        /// <summary>Vehicles charged in the last minute of simulation time.</summary>
        public int PerMinute(uint now)
        {
            Prune(now);
            return m_Recent.Count;
        }

        private void Prune(uint now)
        {
            while (m_Recent.Count > 0 && now - m_Recent.Peek() >= FramesPerMinute)
            {
                m_Recent.Dequeue();
            }
        }

        /// <summary>
        /// The game's current "month" as a counter that goes up by one each period. The game's year is only
        /// daysPerYear (12) days long, so its DateTime is always in January: the period is the game day.
        /// </summary>
        public static int MonthKey(Game.Simulation.TimeSystem time)
        {
            System.DateTime date = time.GetCurrentDateTime();
            return date.Year * time.daysPerYear + date.DayOfYear;
        }

        /// <summary>Starts a new month when the game date moves on (month key = MonthKey).</summary>
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
            City.m_Recent.Clear();
            MeasuringSince = null;
        }

        /// <summary>Clears this plaza's (or booth's) monthly numbers and its vehicles/min, and those of its booths.</summary>
        private void ClearCounts()
        {
            Month = -1;
            ThisMonth = new MonthStats();
            LastMonth = new MonthStats();
            m_Recent.Clear();
            foreach (PlazaStats booth in m_Booths.Values)
            {
                booth.ClearCounts();
            }
        }

        /// <summary>
        /// Starts the city's toll numbers over (Options > Toll Plaza): every plaza's and booth's months, the city
        /// totals, the all-time vehicle counts and revenue. Names, lanes and rates stay.
        /// </summary>
        public static void ResetCounts()
        {
            foreach (PlazaStats plaza in s_Plazas.Values)
            {
                plaza.ClearCounts();
            }
            City.ClearCounts();
            City.m_Booths.Clear();
            TollState.RevenueCents = 0;
            TollState.Vehicles.Clear();
        }

        /// <summary>
        /// Starts the city's toll setup over too: the numbers (ResetCounts), plus every plaza's own rates, name,
        /// cash/E-Toll lanes and closed booths, and the city's rates and speeds (back to the Options defaults).
        /// Booths are reopened the way the panel does it, so route finding is told.
        /// </summary>
        public static void ResetEverything()
        {
            ResetCounts();
            foreach (PlazaStats plaza in s_Plazas.Values)
            {
                plaza.Rates = null;
                plaza.Name = null;
                plaza.Cash = null;
                foreach (PlazaStats booth in plaza.m_Booths.Values)
                {
                    booth.Closed = false;
                }
            }
            TollState.Reset();
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

        /// <summary>The plaza's own car, truck and bus rates in cents (kept with the toll data), or null to
        /// use the city-wide rates from the control panel.</summary>
        public int[] Rates;

        /// <summary>What a vehicle pays at this plaza: its own rates if set, else the city's.</summary>
        public static int RateCents(Entity plaza, VehicleClass c)
        {
            if (c == VehicleClass.Exempt)
            {
                return 0;
            }
            if (s_Plazas.TryGetValue(plaza, out PlazaStats stats) && stats.Rates != null && stats.Rates.Length == 3)
            {
                return stats.Rates[c == VehicleClass.Truck ? 1 : c == VehicleClass.Bus ? 2 : 0];
            }
            return TollState.RateCents(c);
        }

        /// <summary>A toll between $0 and $100, as the control panel allows.</summary>
        public static int ClampRate(int cents) => cents < 0 ? 0 : (cents > 10000 ? 10000 : cents);

        public static bool HasOwnRates(Entity plaza) =>
            s_Plazas.TryGetValue(plaza, out PlazaStats stats) && stats.Rates != null && stats.Rates.Length == 3;

        public static bool IsClosedLane(Entity plaza, int lane) =>
            s_Plazas.TryGetValue(plaza, out PlazaStats stats) && stats.m_Booths.TryGetValue(lane, out PlazaStats booth) && booth.Closed;

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
