using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TollPlaza;
using UnityEngine;

namespace PreviewExport
{
    /// <summary>
    /// Builds the plaza with the same code the mod uses and writes preview/plaza.html,
    /// a standalone three.js page. Usage: PreviewExport [outputPath]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("..", "..", "preview", "plaza.html"));
            string root = Path.GetDirectoryName(Path.GetDirectoryName(output)); // <repo>/preview/plaza.html

            // Textures first: the mod packs src/Textures, the preview page loads preview/textures.
            TextureBaker.BakeAll(Path.Combine(root, "textures", "src"),
                new[] { Path.Combine(root, "src", "Textures"), Path.Combine(root, "preview", "textures") });

            var json = new StringBuilder("[");
            for (int i = 0; i < PlazaCatalog.Plazas.Length; i++)
            {
                var plaza = PlazaCatalog.Plazas[i];
                if (i > 0) json.Append(',');
                json.Append("{\"id\":\"").Append(plaza.id).Append("\",\"title\":\"").Append(plaza.title)
                    .Append("\",\"width\":").Append(F(plaza.layout.TotalWidth))
                    .Append(",\"lane0\":").Append(F(plaza.layout.LaneCenterX(0)))
                    .Append(",\"laneWidth\":").Append(F(plaza.layout.LaneWidth))
                    .Append(",\"medianWidth\":").Append(F(plaza.layout.IslandWidth))
                    .Append(",\"roadWidth\":").Append(F(plaza.layout.RoadWidth))
                    .Append(",\"roads\":[");
                // Straight highways through the plaza: [center x, start z, end z, lanes] in three.js space.
                var segments = plaza.layout.RoadSegments();
                for (int s = 0; s < segments.Count; s++)
                {
                    if (s > 0) json.Append(',');
                    json.Append('[').Append(F(segments[s].a.x)).Append(',').Append(F(-segments[s].a.z)).Append(',')
                        .Append(F(-segments[s].d.z)).Append(',').Append(segments[s].lanes).Append(',')
                        .Append(segments[s].kind == RoadPiece.Islands ? 1 : 0).Append(']');
                }
                json.Append("],\"laneXs\":[");
                for (int l = 0; l < plaza.layout.LaneCount; l++)
                {
                    if (l > 0) json.Append(',');
                    json.Append(F(plaza.layout.LaneCenterX(l)));
                }
                json.Append("],\"parts\":");
                var staticBuilder = new PlazaMeshBuilder();
                plaza.layout.BuildStatic(staticBuilder);
                AppendParts(json, staticBuilder, plaza.id);

                // Every lane in every state, so the page can switch lanes without regenerating.
                json.Append(",\"lanes\":[");
                for (int lane = 0; lane < plaza.layout.LaneCount; lane++)
                {
                    if (lane > 0) json.Append(',');
                    json.Append("{\"cash\":").Append(plaza.layout.IsCashLane(lane) ? "true" : "false").Append(",\"variants\":{");
                    bool firstVariant = true;
                    foreach (bool cash in new[] { false, true })
                    {
                        foreach (bool closed in new[] { false, true })
                        {
                            if (!firstVariant) json.Append(',');
                            firstVariant = false;
                            var laneBuilder = new PlazaMeshBuilder();
                            plaza.layout.BuildLaneSign(laneBuilder, lane, cash, closed);
                            json.Append('"').Append(cash ? "cash" : "etc").Append(closed ? "_closed" : "_open").Append("\":");
                            AppendParts(json, laneBuilder, plaza.id);
                        }
                    }
                    json.Append("}}");
                }
                json.Append("]}");
            }
            json.Append(']');

            // Embed every texture as a data URI so the page works when opened straight from disk
            // (browsers refuse file:// images in WebGL) or sent as a single file.
            var textures = new StringBuilder("{");
            bool firstTex = true;
            foreach (string png in Directory.GetFiles(Path.Combine(root, "preview", "textures"), "*.png"))
            {
                if (!firstTex) textures.Append(',');
                firstTex = false;
                textures.Append('"').Append(Path.GetFileNameWithoutExtension(png)).Append("\":\"data:image/png;base64,")
                    .Append(Convert.ToBase64String(File.ReadAllBytes(png))).Append('"');
            }
            textures.Append('}');

            string template;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("preview-template.html"))
            using (var reader = new StreamReader(s))
            {
                template = reader.ReadToEnd();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, template.Replace("/*PLAZA_DATA*/[]", json.ToString())
                .Replace("/*TEXTURE_DATA*/{}", textures.ToString())
                .Replace("/*GENERATED_AT*/", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            Console.WriteLine("Wrote " + Path.GetFullPath(output));
            return 0;
        }

        private static void AppendParts(StringBuilder json, PlazaMeshBuilder builder, string plazaId)
        {
            json.Append('[');
            bool first = true;
            foreach (var part in builder.RawParts)
            {
                if (!first) json.Append(',');
                first = false;
                Color c = Paints.ColorOf(part.paint);
                string normal = Paints.NormalKey(part.paint);
                json.Append("{\"name\":\"").Append(part.paint)
                    .Append("\",\"tex\":\"").Append(Paints.TextureKey(part.paint, plazaId))
                    .Append("\",\"normalTex\":").Append(normal == null ? "null" : "\"" + normal + "\"")
                    .Append(",\"color\":[")
                    .Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b)).Append("],\"uv\":[");
                for (int i = 0; i < part.uvs.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    json.Append(F(part.uvs[i].x)).Append(',').Append(F(part.uvs[i].y));
                }
                json.Append("],\"pos\":[");
                // Unity is left-handed; mirroring Z gives three.js's right-handed space with the same
                // left/right as seen by an approaching driver.
                AppendVectors(json, part.vertices);
                json.Append("],\"nrm\":[");
                AppendVectors(json, part.normals);
                // Mirroring Z keeps what an approaching driver sees on screen, so Unity's clockwise front
                // faces stay clockwise; three.js wants counter-clockwise, so flip each triangle.
                json.Append("],\"idx\":[");
                for (int t = 0; t < part.triangles.Count; t += 3)
                {
                    if (t > 0) json.Append(',');
                    json.Append(part.triangles[t]).Append(',').Append(part.triangles[t + 2]).Append(',').Append(part.triangles[t + 1]);
                }
                json.Append("]}");
            }
            json.Append(']');
        }

        private static void AppendVectors(StringBuilder sb, System.Collections.Generic.List<Vector3> vectors)
        {
            for (int i = 0; i < vectors.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Vector3 v = vectors[i];
                sb.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(-v.z));
            }
        }

        private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
