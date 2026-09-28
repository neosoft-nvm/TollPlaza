using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace TollPlaza
{
    /// <summary>
    /// Keeps toll rates, booth speeds, cash/E-Toll lanes and the revenue stats with each saved city, without
    /// writing anything into the save file itself: if the mod is removed or broken, saves load as if it was
    /// never there.
    ///
    /// Every time the game saves, a snapshot is written to ModsData/TollPlaza/Cities/&lt;city name&gt;/&lt;frame&gt;.json,
    /// named by the simulation frame the save was made at. Loading a save restores the snapshot made at the
    /// same frame, so going back to an older save brings back that save's numbers, not later ones. Plazas are
    /// matched by prefab and position (entity numbers change between loads). A save with no snapshot (made
    /// before this version, or with the mod off) keeps the newest snapshot's rates, speeds and lanes but starts
    /// its stats at zero.
    /// </summary>
    public partial class PlazaPersistence : GameSystemBase
    {
        private const int Version = 1;
        /// <summary>Snapshot kept per city; older ones are deleted.</summary>
        private const int KeepSnapshots = 100;
        /// <summary>The snapshot is taken as the save starts; the saved frame can be a few frames later.</summary>
        private const uint MatchFrames = 1024;
        private const float SamePlaceMeters = 0.5f;

        private PrefabSystem m_PrefabSystem;
        private SimulationSystem m_SimulationSystem;
        private CityConfigurationSystem m_CityConfiguration;
        private EntityQuery m_ObjectQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CityConfiguration = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_ObjectQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Objects.Object>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            GameManager.instance.onGameSaveLoad += OnGameSave;
        }

        protected override void OnDestroy()
        {
            if (GameManager.instance != null)
            {
                GameManager.instance.onGameSaveLoad -= OnGameSave;
            }
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            // Work happens on load and save only.
        }

        // ---- Snapshot format ----

        private class Snapshot
        {
            public int Version;
            public string City;
            public string SaveName;
            public uint Frame;
            public string WrittenAt;
            public int CarRateCents, TruckRateCents, BusRateCents;
            public int CashMph, EtcMph;
            public long RevenueCents;
            public Dictionary<VehicleClass, int> Vehicles = new Dictionary<VehicleClass, int>();
            public StatsDto CityStats;
            public List<PlazaDto> Plazas = new List<PlazaDto>();
        }

        private class PlazaDto
        {
            public string Prefab;
            public float X, Y, Z;
            public bool[] Cash;
            public string Name;
            public StatsDto Stats;
            public Dictionary<int, StatsDto> Booths = new Dictionary<int, StatsDto>();
        }

        private class StatsDto
        {
            public int Month;
            public MonthStats ThisMonth, LastMonth;

            public static StatsDto From(PlazaStats s) =>
                new StatsDto { Month = s.Month, ThisMonth = s.ThisMonth, LastMonth = s.LastMonth };

            public void To(PlazaStats s)
            {
                s.Month = Month;
                s.ThisMonth = ThisMonth ?? new MonthStats();
                s.LastMonth = LastMonth ?? new MonthStats();
            }
        }

        // ---- Save ----

        private void OnGameSave(string saveName, string previewUri, bool start, bool success)
        {
            if (!start || !GameManager.instance.gameMode.IsGame())
            {
                return;
            }
            try
            {
                Snapshot snapshot = Capture(saveName);
                string folder = CityFolder(snapshot.City);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, snapshot.Frame.ToString("D10") + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                Prune(folder);
                Mod.Log.Info($"[TollPlaza] Saved toll data for '{saveName}' ({snapshot.Plazas.Count} plazas, "
                    + $"${snapshot.RevenueCents / 100.0:F2} total) to {path}");
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "[TollPlaza] Saving toll data failed (the game save itself is unaffected)");
            }
        }

        private Snapshot Capture(string saveName)
        {
            var snapshot = new Snapshot
            {
                Version = Version,
                City = m_CityConfiguration.cityName,
                SaveName = saveName,
                Frame = m_SimulationSystem.frameIndex,
                WrittenAt = DateTime.Now.ToString("s"),
                CarRateCents = TollState.CarRateCents,
                TruckRateCents = TollState.TruckRateCents,
                BusRateCents = TollState.BusRateCents,
                CashMph = TollState.CashMph,
                EtcMph = TollState.EtcMph,
                RevenueCents = TollState.RevenueCents,
                Vehicles = new Dictionary<VehicleClass, int>(TollState.Vehicles),
                CityStats = StatsDto.From(PlazaStats.City),
            };
            foreach (var entry in PlazaStats.Plazas)
            {
                Entity plaza = entry.Key;
                if (!EntityManager.Exists(plaza) || EntityManager.HasComponent<Deleted>(plaza)
                    || !EntityManager.HasComponent<Game.Objects.Transform>(plaza)
                    || !EntityManager.HasComponent<PrefabRef>(plaza)
                    || !m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(plaza), out PrefabBase prefab))
                {
                    continue; // bulldozed
                }
                float3 at = EntityManager.GetComponentData<Game.Objects.Transform>(plaza).m_Position;
                var dto = new PlazaDto
                {
                    Prefab = prefab.name,
                    X = at.x,
                    Y = at.y,
                    Z = at.z,
                    Cash = entry.Value.Cash,
                    Name = entry.Value.Name,
                    Stats = StatsDto.From(entry.Value),
                };
                foreach (var booth in entry.Value.Booths)
                {
                    dto.Booths[booth.Key] = StatsDto.From(booth.Value);
                }
                snapshot.Plazas.Add(dto);
            }
            return snapshot;
        }

        private static void Prune(string folder)
        {
            foreach (string old in Directory.GetFiles(folder, "*.json").OrderByDescending(f => f).Skip(KeepSnapshots))
            {
                File.Delete(old);
            }
        }

        // ---- Load ----

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Every load starts clean, so one city's numbers never show up in another.
            TollState.Reset();
            PlazaStats.ResetAll();
            World.GetOrCreateSystemManaged<TollCollectionSystem>().ResetForNewCity();

            if (!mode.IsGame() || purpose == Purpose.NewGame)
            {
                return;
            }
            try
            {
                Restore();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "[TollPlaza] Restoring toll data failed; starting from defaults");
                TollState.Reset();
                PlazaStats.ResetAll();
            }
        }

        private void Restore()
        {
            string city = m_CityConfiguration.cityName;
            string folder = CityFolder(city);
            uint frame = m_SimulationSystem.frameIndex;
            if (!Directory.Exists(folder))
            {
                Mod.Log.Info($"[TollPlaza] No saved toll data for '{city}'; starting fresh.");
                return;
            }

            // The snapshot taken as this save was made; otherwise the newest one, for the settings only.
            var candidates = new List<(uint frame, string path)>();
            foreach (string path in Directory.GetFiles(folder, "*.json"))
            {
                if (uint.TryParse(Path.GetFileNameWithoutExtension(path), out uint f))
                {
                    candidates.Add((f, path));
                }
            }
            var match = candidates
                .Where(c => (long)c.frame <= (long)frame + 16 && (long)frame - c.frame <= MatchFrames)
                .OrderBy(c => Math.Abs((long)frame - c.frame))
                .FirstOrDefault();
            bool exact = match.path != null;
            string chosen = exact ? match.path
                : candidates.OrderByDescending(c => File.GetLastWriteTimeUtc(c.path)).Select(c => c.path).FirstOrDefault();
            if (chosen == null)
            {
                Mod.Log.Info($"[TollPlaza] No saved toll data for '{city}'; starting fresh.");
                return;
            }

            Snapshot snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(chosen));
            if (snapshot == null)
            {
                return;
            }
            TollState.CarRateCents = snapshot.CarRateCents;
            TollState.TruckRateCents = snapshot.TruckRateCents;
            TollState.BusRateCents = snapshot.BusRateCents;
            TollState.CashMph = snapshot.CashMph > 0 ? snapshot.CashMph : TollState.CashMph;
            TollState.EtcMph = snapshot.EtcMph > 0 ? snapshot.EtcMph : TollState.EtcMph;
            if (exact)
            {
                TollState.RevenueCents = snapshot.RevenueCents;
                foreach (var v in snapshot.Vehicles ?? new Dictionary<VehicleClass, int>())
                {
                    TollState.Vehicles[v.Key] = v.Value;
                }
                snapshot.CityStats?.To(PlazaStats.City);
            }

            // Plazas by prefab and position.
            var placed = new List<(Entity entity, string prefab, float3 at)>();
            using (NativeArray<Entity> entities = m_ObjectQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in entities)
                {
                    if (m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(e), out PrefabBase prefab)
                        && PlazaAssetSystem.PlazaLayouts.ContainsKey(prefab))
                    {
                        placed.Add((e, prefab.name, EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position));
                    }
                }
            }
            int matched = 0;
            foreach (PlazaDto dto in snapshot.Plazas ?? new List<PlazaDto>())
            {
                var here = new float3(dto.X, dto.Y, dto.Z);
                var found = placed.FirstOrDefault(p => p.prefab == dto.Prefab && math.distance(p.at, here) < SamePlaceMeters);
                if (found.entity == Entity.Null)
                {
                    continue;
                }
                matched++;
                PlazaStats stats = PlazaStats.Of(found.entity);
                stats.Cash = dto.Cash;
                stats.Name = dto.Name;
                if (exact)
                {
                    dto.Stats?.To(stats);
                    foreach (var booth in dto.Booths ?? new Dictionary<int, StatsDto>())
                    {
                        booth.Value.To(stats.Booth(booth.Key));
                    }
                }
            }
            Mod.Log.Info(exact
                ? $"[TollPlaza] Restored toll data for '{city}' from {Path.GetFileName(chosen)} (save '{snapshot.SaveName}'): "
                    + $"${TollState.RevenueCents / 100.0:F2} total, {matched} of {snapshot.Plazas?.Count ?? 0} plazas matched."
                : $"[TollPlaza] No toll data for this save of '{city}' (frame {frame}); kept rates, speeds and lanes from "
                    + $"{Path.GetFileName(chosen)} ({matched} plazas matched), stats start at zero.");
        }

        private static string CityFolder(string city)
        {
            string name = string.IsNullOrWhiteSpace(city) ? "Unnamed city" : city.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return Path.Combine(Application.persistentDataPath, "ModsData", "TollPlaza", "Cities", name);
        }
    }
}
