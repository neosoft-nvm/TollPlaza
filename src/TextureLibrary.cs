using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Colossal.AssetPipeline;
using Colossal.AssetPipeline.Importers;
using Colossal.IO.AssetDatabase;
using UnityEngine;
using Hash128 = Colossal.Hash128;
using TextureAsset = Colossal.IO.AssetDatabase.TextureAsset;

namespace TollPlaza
{
    internal enum TextureKind
    {
        Color,   // sRGB albedo
        Normal,  // tangent-space normal map
        Linear,  // data texture (mask map), no sRGB conversion
    }

    /// <summary>
    /// Turns the PNGs shipped in the mod's Textures folder (baked by tools/PreviewExport) into game
    /// TextureAssets, following ExtraAssetsImporter's recipe: import with the game's own PNG importer,
    /// add to the asset database under a stable GUID, save, unload.
    /// Asset names carry a hash of the PNG, so an unchanged texture is reused across launches and a
    /// rebaked one gets a fresh asset instead of piling up duplicates.
    /// </summary>
    internal static class TextureLibrary
    {
        private const string Folder = PlazaAssetSystem.GeneratedFolder + "/Textures";

        private static readonly Dictionary<string, TextureAsset> s_Cache = new Dictionary<string, TextureAsset>();

        public static string SourceDirectory => Path.Combine(Mod.ModDirectory ?? string.Empty, "Textures");

        /// <summary>Returns the TextureAsset for Textures/&lt;key&gt;.png, or null (logged) if it can't be made.</summary>
        public static TextureAsset Get(string key, TextureKind kind, bool clamp)
        {
            if (s_Cache.TryGetValue(key, out TextureAsset cached))
            {
                return cached;
            }

            TextureAsset result = null;
            try
            {
                result = Load(key, kind, clamp);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "[TollPlaza] Texture '" + key + "' failed; that part falls back to a flat color.");
            }
            s_Cache[key] = result;
            return result;
        }

        private static TextureAsset Load(string key, TextureKind kind, bool clamp)
        {
            string pngPath = Path.Combine(SourceDirectory, key + ".png");
            if (!File.Exists(pngPath))
            {
                Mod.Log.Warn("[TollPlaza] Missing texture " + pngPath);
                return null;
            }

            string assetName = key + "_" + ShortHash(pngPath);
            AssetDataPath path = AssetDataPath.Create(Folder, assetName);
            if (AssetDatabase.user.Exists(path, out TextureAsset existing))
            {
                return existing;
            }

            TextureWrapMode wrap = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            DefaultTextureImporter.ImportSettings settings;
            switch (kind)
            {
                case TextureKind.Normal:
                    settings = Settings.Defaults.Texture.GetNormal(wrap);
                    settings.normalMap = true;
                    break;
                case TextureKind.Linear:
                    settings = Settings.Defaults.Texture.GetLinear(wrap);
                    settings.linearTexture = true;
                    break;
                default:
                    settings = Settings.Defaults.Texture.GetDefault(wrap);
                    break;
            }
            settings.alphaIsTransparency = false;
            settings.wrapMode = wrap;

            var importer = (DefaultTextureImporter)ImporterCache.GetImporter(".png");
            TextureImporter.Texture texture = importer.Import(settings, pngPath);
            TextureAsset asset = AssetDatabase.user.AddAsset<TextureAsset, TextureImporter.ITexture>(
                path, texture, Hash128.CreateGuid("TollPlaza_" + assetName));
            asset.Save();
            asset.Unload();
            texture.Dispose();
            return asset;
        }

        private static string ShortHash(string file)
        {
            using (var md5 = MD5.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>Logs the texture slots a material template actually has, to confirm property names.</summary>
        public static void LogTemplateTextureSlots(string template)
        {
            try
            {
                var surface = new Surface("TollPlaza_Probe_" + template, template);
                Material material = surface.ToUnityMaterial();
                Mod.Log.Info("[TollPlaza] Template '" + template + "' shader '" + material.shader.name + "' textures: "
                    + string.Join(", ", material.GetTexturePropertyNames()));
                UnityEngine.Object.Destroy(material);
                surface.Dispose();
            }
            catch (Exception e)
            {
                Mod.Log.Warn("[TollPlaza] Couldn't probe template '" + template + "': " + e.Message);
            }
        }
    }
}
