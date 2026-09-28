using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Entities;
using UnityEngine;

namespace TollPlaza
{
    /// <summary>
    /// Builds the toll plaza prefabs (one per PlazaCatalog size) entirely at runtime (no Asset Editor
    /// import) and registers them with PrefabSystem. One mesh + SurfaceAsset + RenderPrefab per paint
    /// color, all referenced by a StaticObjectPrefab placed in Roads > Road Services ('RoadsServices').
    /// </summary>
    public partial class PlazaAssetSystem : GameSystemBase
    {
        // Relative to the user asset database root (the game's LocalLow data folder).
        internal const string GeneratedFolder = "ModsData/TollPlaza/Generated";

        protected override void OnUpdate()
        {
            // All the work happens once, in OnGamePreload below.
        }

        // Preload runs after the game's own prefabs exist but before the city loads and the toolbar
        // menus are built. Adding the prefab after loading (OnGameLoadingComplete) registered it fine,
        // but it never appeared in the Road Services menu.
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            if (!mode.IsGame())
            {
                return;
            }

            try
            {
                BuildPlazaPrefabs();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "[TollPlaza] BuildPlazaPrefabs failed");
            }
        }

        private void BuildPlazaPrefabs()
        {
            var prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            TextureLibrary.LogTemplateTextureSlots("Default");
            TextureLibrary.LogTemplateTextureSlots("DefaultPipeline");

            UIAssetCategoryPrefab roadsServices = null;
            if (prefabSystem.TryGetPrefab(new PrefabID(nameof(UIAssetCategoryPrefab), "RoadsServices"), out PrefabBase roadsServicesBase))
            {
                roadsServices = roadsServicesBase as UIAssetCategoryPrefab;
            }
            if (roadsServices == null)
            {
                Mod.Log.Warn("[TollPlaza] 'RoadsServices' category not found - prefabs will register but won't show in a menu.");
            }

            // Compose and measure the plaza highways first: the islands must sit exactly on their medians.
            // Per booth count: the island piece, and the ends, which are the base game's one-way highway with
            // the same lane count, so the player's matching highway connects straight on.
            var plazaHighways = new Dictionary<(int lanes, RoadPiece kind), NetPrefab>();
            try
            {
                foreach (var plaza in PlazaCatalog.Plazas)
                {
                    foreach (int lanes in plaza.layout.RoadGroups)
                    {
                        if (plazaHighways.ContainsKey((lanes, RoadPiece.Islands)))
                        {
                            continue;
                        }
                        plazaHighways[(lanes, RoadPiece.Islands)] = PlazaRoad.CreatePlazaHighway(prefabSystem, EntityManager, lanes, true);
                        // A nameless copy of the N-lane one-way highway (or composed when the game has none).
                        NetPrefab end = PlazaRoad.CreatePlazaHighway(prefabSystem, EntityManager, lanes, false);
                        plazaHighways[(lanes, RoadPiece.End)] = end;
                        Mod.Log.Info($"[TollPlaza] {lanes}-booth plaza ends: {end?.name ?? "none"}");
                    }
                }
                foreach (var plaza in PlazaCatalog.Plazas)
                {
                    plaza.layout.Invalidate();
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "[TollPlaza] Setting up the plaza highways failed");
            }

            int priority = 1;
            foreach (var plaza in PlazaCatalog.Plazas)
            {
                try
                {
                    // Already added in a previous load (e.g. re-entering the game without restarting)?
                    if (prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), plaza.id), out _))
                    {
                        Mod.Log.Info("[TollPlaza] " + plaza.id + " already registered, skipping.");
                        continue;
                    }

                    ObjectMeshInfo[] meshes = BuildPlazaMeshes(prefabSystem, plaza.id, plaza.id, plaza.layout);
                    if (meshes == null)
                    {
                        continue;
                    }

                    // The working version carries its own highway through the booths; the decorative one is the
                    // same model without a road, to place anywhere.
                    ObjectSubNetInfo[] roads = PlazaRoad.Build(plaza.layout,
                        (lanes, kind) => plazaHighways.TryGetValue((lanes, kind), out NetPrefab road) ? road : null);
                    if (roads != null)
                    {
                        AddPlazaPrefab(prefabSystem, plaza.id + RoadSuffix, meshes, roadsServices, priority++, roads, plaza.layout.LaneCount);
                    }
                    AddPlazaPrefab(prefabSystem, plaza.id, meshes, roadsServices, priority++, null, 0);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "[TollPlaza] Building " + plaza.id + " failed");
                }
            }
        }

        /// <summary>Menu variant that carries its own road through the plaza.</summary>
        public const string RoadSuffix = "_Road";

        /// <summary>Layout of each working plaza prefab, so runtime systems can tell cash lanes from E-Toll lanes.</summary>
        public static readonly Dictionary<PrefabBase, PlazaLayout> PlazaLayouts = new Dictionary<PrefabBase, PlazaLayout>();

        /// <param name="prefabName">Names the meshes/assets (unique per menu entry).</param>
        /// <param name="plazaId">The catalog size the artwork belongs to (roof billboard texture).</param>
        private ObjectMeshInfo[] BuildPlazaMeshes(PrefabSystem prefabSystem, string prefabName, string plazaId, PlazaLayout layout)
        {
            var builder = new PlazaMeshBuilder();
            layout.Build(builder);

            var meshInfos = new List<ObjectMeshInfo>();
            foreach (KeyValuePair<Paint, Mesh> part in builder.BuildMeshes(prefabName))
            {
                RenderPrefab renderPrefab = CreateRenderPrefab(part.Value, part.Key, plazaId);
                if (!prefabSystem.AddPrefab(renderPrefab))
                {
                    Mod.Log.Error("[TollPlaza] Failed to add RenderPrefab " + renderPrefab.name);
                    return null;
                }
                meshInfos.Add(new ObjectMeshInfo { m_Mesh = renderPrefab });
            }
            return meshInfos.ToArray();
        }

        private void AddPlazaPrefab(PrefabSystem prefabSystem, string prefabName, ObjectMeshInfo[] meshes,
            UIAssetCategoryPrefab category, int priority, ObjectSubNetInfo[] subNets, int boothLanes)
        {
            var objectPrefab = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            objectPrefab.name = prefabName;
            objectPrefab.m_Meshes = meshes;
            objectPrefab.AddComponent<PlaceableObject>();

            if (subNets != null)
            {
                var objectSubNets = objectPrefab.AddComponent<ObjectSubNets>();
                objectSubNets.m_SubNets = subNets;
            }

            if (category != null)
            {
                var uiObject = objectPrefab.AddComponent<UIObject>();
                uiObject.m_Group = category;
                uiObject.m_Priority = priority;
                uiObject.m_Icon = Mod.IconUri;
            }

            if (!prefabSystem.AddPrefab(objectPrefab))
            {
                Mod.Log.Error("[TollPlaza] Failed to add StaticObjectPrefab " + prefabName + " (see PrefabSystem error above).");
                return;
            }
            if (boothLanes > 0)
            {
                TollUISystem.PlazaLaneCounts[objectPrefab] = boothLanes;
                PlazaLayouts[objectPrefab] = PlazaCatalog.Plazas.First(p => prefabName.StartsWith(p.id)).layout;
            }
            Mod.Log.Info("[TollPlaza] " + prefabName + " added OK (" + meshes.Length + " meshes"
                + (subNets != null ? ", " + subNets.Length + " road segments" : string.Empty) + ").");
        }

        private static RenderPrefab CreateRenderPrefab(Mesh mesh, Paint paint, string plazaId)
        {
            string name = mesh.name;

            // The renderer streams geometry and surfaces from their files (GeometryAsset.RequestDataAsync
            // reads through the database's read descriptor), so in-memory assets are never picked up and it
            // draws a default box the size of the mesh bounds instead. Save both to real files, overwriting
            // the copies from the previous launch.
            AssetDataPath geometryPath = AssetDataPath.Create(GeneratedFolder, name + "_Geometry");
            if (AssetDatabase.user.Exists(geometryPath, out GeometryAsset geometryAsset))
            {
                geometryAsset.SetData(new[] { mesh });
            }
            else
            {
                geometryAsset = AssetDatabase.user.AddAsset(geometryPath, new[] { mesh });
            }
            geometryAsset.Save(force: true);

            // Capture what the RenderPrefab needs before dropping the in-memory copy: the loader refuses
            // an asset whose data is already filled in ("GeometryData header should only have to be read
            // once!"), which stalled the city load, so the renderer must start from the file.
            Bounds meshBounds = mesh.bounds;
            int vertexCount = mesh.vertexCount;
            int indexCount = (int)mesh.GetIndexCount(0);
            geometryAsset.Unload(force: true);
            geometryAsset.UnloadPartial(force: true);

            // Described by template name + properties, the same form the asset importer produces.
            // Textures follow ExtraAssetsImporter's proven recipe: TextureAssets attached with
            // UpdateTexture, surface saved without virtual texturing. The color is baked into the texture,
            // so _BaseColor goes white when a texture is attached.
            TextureAsset colorMap = TextureLibrary.Get(Paints.TextureKey(paint, plazaId), TextureKind.Color, clamp: Paints.IsSignFace(paint));
            string normalKey = Paints.NormalKey(paint);
            TextureAsset normalMap = normalKey != null ? TextureLibrary.Get(normalKey, TextureKind.Normal, clamp: false) : null;
            TextureAsset maskMap = colorMap != null ? TextureLibrary.Get(Paints.MaskKey(paint), TextureKind.Linear, clamp: false) : null;

            var surface = new Colossal.AssetPipeline.Surface(name + "_Surface", Paints.TemplateOf(paint, colorMap != null));
            surface.AddProperty("_BaseColor", colorMap != null ? Color.white : Paints.ColorOf(paint));
            if (paint == Paint.Glass)
            {
                surface.AddProperty("_Smoothness", 0.9f);
            }
            AssetDataPath surfacePath = AssetDataPath.Create(GeneratedFolder, name + "_Surface");
            if (AssetDatabase.user.Exists(surfacePath, out SurfaceAsset surfaceAsset))
            {
                surfaceAsset.SetData(surface);
            }
            else
            {
                surfaceAsset = AssetDatabase.user.AddAsset(surfacePath, surface);
            }
            if (colorMap != null)
            {
                surfaceAsset.UpdateTexture("_BaseColorMap", colorMap);
            }
            if (normalMap != null)
            {
                surfaceAsset.UpdateTexture("_NormalMap", normalMap);
            }
            if (maskMap != null)
            {
                surfaceAsset.UpdateTexture("_MaskMap", maskMap);
            }
            surfaceAsset.Save(0, force: true, saveTextures: false, vt: false);
            surfaceAsset.Unload();

            var renderPrefab = ScriptableObject.CreateInstance<RenderPrefab>();
            renderPrefab.name = name + "_Render";
            renderPrefab.geometryAsset = geometryAsset;
            renderPrefab.surfaceAssets = new[] { surfaceAsset };
            renderPrefab.bounds = new Colossal.Mathematics.Bounds3
            {
                min = meshBounds.min,
                max = meshBounds.max,
            };
            renderPrefab.vertexCount = vertexCount;
            renderPrefab.indexCount = indexCount;
            renderPrefab.meshCount = 1;
            renderPrefab.manualVTRequired = false;
            return renderPrefab;
        }
    }
}
