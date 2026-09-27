using System.Collections.Generic;
using UnityEngine;

namespace TollPlaza
{
    /// <summary>
    /// Materials used by the plaza. Each one becomes its own mesh + SurfaceAsset, so a plaza is a
    /// handful of single-material meshes rather than one mesh with submeshes. Each has a baked
    /// texture (Textures/&lt;Paint&gt;.png, made by tools/PreviewExport) with its color already in it.
    /// </summary>
    public enum Paint
    {
        Concrete,
        CanopySteel,
        Fascia,
        FasciaStripe,
        BoothWall,
        Glass,
        SafetyYellow,
        DarkMetal,
        SignGreen,
        SignPurple,
        SignWhite,
        LaneLight,
        LaneLightRed,
        Asphalt,
        LaneMarking,
        GateArm,
        // Sign faces: one quad each, UV-mapped 0..1 so the baked sign artwork fits exactly.
        SignFaceEtc,
        SignFaceCash,
        SignFaceRate,
        SignFaceRoof,
        SignFaceSpeed,      // E-Toll lanes
        SignFaceSpeedCash,  // cash lanes
        // Lane number plates; all numbers share one 4x4 atlas texture (cell n-1 holds lane n).
        SignFaceLaneNumber,
    }

    public static class Paints
    {
        /// <summary>Sign faces carry artwork that fills the face; everything else tiles.</summary>
        public static bool IsSignFace(Paint paint) => paint >= Paint.SignFaceEtc;

        /// <summary>Real-world size of one texture tile, in meters.</summary>
        public static float TileMeters(Paint paint)
        {
            switch (paint)
            {
                case Paint.CanopySteel:
                case Paint.Fascia:
                    return 4f;
                case Paint.DarkMetal:
                case Paint.FasciaStripe:
                    return 1.5f;
                case Paint.Asphalt:
                    return 6f;
                case Paint.GateArm:
                    return 0.8f; // one red + one white band per 0.8 m
                default:
                    return 2f;
            }
        }

        /// <summary>
        /// Texture file (without .png) for a paint. The roof billboard's artwork depends on the plaza
        /// (its size and, later, its name), so it's per plaza.
        /// </summary>
        public static string TextureKey(Paint paint, string plazaId) =>
            paint == Paint.SignFaceRoof ? paint + "_" + plazaId : paint.ToString();

        /// <summary>
        /// Mask map (HDRP layout: R metallic, G occlusion, B detail mask, A smoothness) for a paint. Without
        /// one the game's standard material renders as polished metal: first in-game test came out as
        /// shiny, gold-tinted foil.
        /// </summary>
        public static string MaskKey(Paint paint)
        {
            switch (paint)
            {
                case Paint.Glass:
                    return "Mask_Glass";
                case Paint.DarkMetal:
                case Paint.FasciaStripe:
                    return "Mask_Metal";
                case Paint.CanopySteel:
                case Paint.Fascia:
                case Paint.SafetyYellow:
                case Paint.GateArm:
                case Paint.SignGreen:
                case Paint.SignPurple:
                case Paint.SignWhite:
                case Paint.LaneLight:
                case Paint.LaneLightRed:
                    return "Mask_Paint";
                default:
                    return IsSignFace(paint) ? "Mask_Paint" : "Mask_Matte";
            }
        }

        /// <summary>
        /// Normal map shared by a family of paints, or null for none. Disabled for now: in game the concrete
        /// normals made every surface look crinkled; revisit with a strength-reduced bake.
        /// </summary>
        public static string NormalKey(Paint paint) => null;

        private static string NormalKeyDisabled(Paint paint)
        {
            switch (paint)
            {
                case Paint.Concrete:
                case Paint.BoothWall:
                case Paint.CanopySteel:
                case Paint.Fascia:
                case Paint.Asphalt:
                    return "Normal_Concrete";
                case Paint.DarkMetal:
                case Paint.FasciaStripe:
                case Paint.SignGreen:
                case Paint.SignPurple:
                case Paint.SignWhite:
                    return "Normal_Metal";
                case Paint.SafetyYellow:
                    return "Normal_PaintedMetal";
                default:
                    return null;
            }
        }

        public static Color ColorOf(Paint paint)
        {
            switch (paint)
            {
                case Paint.Concrete: return new Color(0.72f, 0.71f, 0.68f, 1f);
                case Paint.CanopySteel: return new Color(0.90f, 0.90f, 0.88f, 1f);
                case Paint.Fascia: return new Color(0.86f, 0.87f, 0.89f, 1f);
                case Paint.FasciaStripe: return new Color(0.10f, 0.18f, 0.36f, 1f);
                // Darker than the white canopy so the booths read as separate from the structure.
                case Paint.BoothWall: return new Color(0.46f, 0.43f, 0.39f, 1f);
                case Paint.Glass: return new Color(0.22f, 0.32f, 0.40f, 1f);
                case Paint.SafetyYellow: return new Color(1.00f, 0.78f, 0.05f, 1f);
                case Paint.DarkMetal: return new Color(0.22f, 0.22f, 0.24f, 1f);
                case Paint.SignGreen: return new Color(0.00f, 0.42f, 0.22f, 1f);
                case Paint.SignPurple: return new Color(0.36f, 0.20f, 0.55f, 1f);
                case Paint.SignWhite: return new Color(0.96f, 0.96f, 0.96f, 1f);
                case Paint.LaneLight: return new Color(0.15f, 1.00f, 0.25f, 1f);
                case Paint.LaneLightRed: return new Color(1.00f, 0.12f, 0.10f, 1f);
                case Paint.Asphalt: return new Color(0.27f, 0.27f, 0.28f, 1f);
                case Paint.LaneMarking: return new Color(0.93f, 0.93f, 0.9f, 1f);
                case Paint.GateArm: return new Color(0.85f, 0.1f, 0.08f, 1f);
                case Paint.SignFaceEtc: return new Color(0.36f, 0.20f, 0.55f, 1f);
                case Paint.SignFaceCash:
                case Paint.SignFaceRate:
                case Paint.SignFaceRoof:
                    return new Color(0.00f, 0.42f, 0.22f, 1f);
                case Paint.SignFaceSpeed:
                case Paint.SignFaceSpeedCash:
                    return new Color(0.96f, 0.96f, 0.96f, 1f);
                case Paint.SignFaceLaneNumber: return new Color(0.05f, 0.05f, 0.06f, 1f);
                default: return Color.magenta;
            }
        }

        /// <summary>
        /// Game material template per paint. Tested in game with flat colors and no textures:
        /// "Default" (VT) renders black, "DefaultGlass" renders black, and "Base" takes the color but is the
        /// building-foundation shader, which drags low vertices out into big sloped sheets. "DefaultPipeline"
        /// shows flat color cleanly but has no texture slots at all (logged: only lightmap slots), while
        /// "Default" has _BaseColorMap/_NormalMap/_MaskMap. ExtraAssetsImporter renders VT templates fine once
        /// real textures are attached (saved with vt:false). So: "Default" whenever the paint has a texture,
        /// "DefaultPipeline" as the flat-color fallback when its texture couldn't be loaded.
        /// </summary>
        public static string TemplateOf(Paint paint, bool hasTexture) => hasTexture ? "Default" : "DefaultPipeline";
    }

    /// <summary>The plaza sizes offered in the menu.</summary>
    public static class PlazaCatalog
    {
        public static readonly (string id, string title, PlazaLayout layout)[] Plazas =
        {
            ("TollPlaza_Plaza2", "Toll Plaza (2 lanes, ramp)", new PlazaLayout { LaneCount = 2, CashLanes = 1, IslandLength = 18f, CanopyDepth = 10f }),
            ("TollPlaza_Plaza4", "Toll Plaza (4 lanes)", new PlazaLayout { LaneCount = 4, CashLanes = 2 }),
            ("TollPlaza_Plaza6", "Toll Plaza (6 lanes)", new PlazaLayout { LaneCount = 6, CashLanes = 2 }),
            ("TollPlaza_Plaza8", "Toll Plaza (8 lanes)", new PlazaLayout { LaneCount = 8, CashLanes = 3 }),
            // 12-lane mainline plaza parked until users ask for it:
            // ("TollPlaza_Plaza12", "Toll Plaza (12 lanes, mainline)", new PlazaLayout { LaneCount = 12, CashLanes = 4, CanopyDepth = 14f }),
        };
    }

    /// <summary>Accumulates axis-aligned boxes into one mesh per paint.</summary>
    public class PlazaMeshBuilder
    {
        private class Part
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        private static readonly Vector3[] FaceNormals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        private readonly Dictionary<Paint, Part> m_Parts = new Dictionary<Paint, Part>();

        private Part GetPart(Paint paint)
        {
            if (!m_Parts.TryGetValue(paint, out Part part))
            {
                part = new Part();
                m_Parts.Add(paint, part);
            }
            return part;
        }

        /// <summary>
        /// A flat rectangle facing approaching traffic (-Z) with UVs 0..1 across it, for sign artwork.
        /// </summary>
        public void FaceQuad(Paint paint, float xMin, float xMax, float yMin, float yMax, float z) =>
            FaceQuad(paint, xMin, xMax, yMin, yMax, z, new Rect(0f, 0f, 1f, 1f));

        /// <summary>Same, mapping only part of the texture (an atlas cell) onto the quad.</summary>
        public void FaceQuad(Paint paint, float xMin, float xMax, float yMin, float yMax, float z, Rect uv)
        {
            Part part = GetPart(paint);
            int start = part.Vertices.Count;
            Vector3 n = Vector3.back;
            part.Vertices.Add(new Vector3(xMin, yMin, z)); part.Uvs.Add(new Vector2(uv.xMin, uv.yMin));
            part.Vertices.Add(new Vector3(xMax, yMin, z)); part.Uvs.Add(new Vector2(uv.xMax, uv.yMin));
            part.Vertices.Add(new Vector3(xMax, yMax, z)); part.Uvs.Add(new Vector2(uv.xMax, uv.yMax));
            part.Vertices.Add(new Vector3(xMin, yMax, z)); part.Uvs.Add(new Vector2(uv.xMin, uv.yMax));
            for (int i = 0; i < 4; i++)
            {
                part.Normals.Add(n);
            }
            part.Triangles.Add(start + 0); part.Triangles.Add(start + 2); part.Triangles.Add(start + 1);
            part.Triangles.Add(start + 0); part.Triangles.Add(start + 3); part.Triangles.Add(start + 2);
        }

        /// <summary>Upward-facing strip between two polylines of equal length (left edge, right edge; +Z order).</summary>
        public void Strip(Paint paint, List<Vector3> left, List<Vector3> right)
        {
            Part part = GetPart(paint);
            float tile = Paints.TileMeters(paint);
            for (int i = 0; i + 1 < left.Count; i++)
            {
                int start = part.Vertices.Count;
                foreach (Vector3 p in new[] { left[i], right[i], right[i + 1], left[i + 1] }) // bl, br, tr, tl
                {
                    part.Vertices.Add(p);
                    part.Normals.Add(Vector3.up);
                    part.Uvs.Add(new Vector2(p.x, p.z) / tile);
                }
                part.Triangles.Add(start + 0); part.Triangles.Add(start + 2); part.Triangles.Add(start + 1);
                part.Triangles.Add(start + 0); part.Triangles.Add(start + 3); part.Triangles.Add(start + 2);
            }
        }

        public void Box(Paint paint, Vector3 min, Vector3 max)
        {
            Part part = GetPart(paint);
            float tile = Paints.TileMeters(paint);

            Vector3 center = (min + max) * 0.5f;
            Vector3 half = (max - min) * 0.5f;

            foreach (Vector3 n in FaceNormals)
            {
                // v = face "up", u = face "right" as seen from outside the box. Unity is left-handed and
                // treats clockwise triangles as front-facing, so (bl, tr, br) + (bl, tl, tr) face outward.
                Vector3 v = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 u = Vector3.Cross(v, -n);
                float hu = Mathf.Abs(u.x) * half.x + Mathf.Abs(u.y) * half.y + Mathf.Abs(u.z) * half.z;
                float hv = Mathf.Abs(v.x) * half.x + Mathf.Abs(v.y) * half.y + Mathf.Abs(v.z) * half.z;
                Vector3 faceCenter = center + Vector3.Scale(n, half);

                int start = part.Vertices.Count;
                AddVertex(part, faceCenter - u * hu - v * hv, n, u, v, tile); // bl
                AddVertex(part, faceCenter + u * hu - v * hv, n, u, v, tile); // br
                AddVertex(part, faceCenter + u * hu + v * hv, n, u, v, tile); // tr
                AddVertex(part, faceCenter - u * hu + v * hv, n, u, v, tile); // tl
                part.Triangles.Add(start + 0); part.Triangles.Add(start + 2); part.Triangles.Add(start + 1);
                part.Triangles.Add(start + 0); part.Triangles.Add(start + 3); part.Triangles.Add(start + 2);
            }
        }

        private static void AddVertex(Part part, Vector3 p, Vector3 n, Vector3 u, Vector3 v, float tileMeters)
        {
            part.Vertices.Add(p);
            part.Normals.Add(n);
            // World-space projection, so textures keep real-world scale on every box: 1 UV unit = one tile.
            part.Uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) / tileMeters);
        }

        /// <summary>Raw per-paint geometry, for exporters that run outside the game (no Unity Mesh).</summary>
        public IEnumerable<(Paint paint, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)> RawParts
        {
            get
            {
                foreach (KeyValuePair<Paint, Part> entry in m_Parts)
                {
                    yield return (entry.Key, entry.Value.Vertices, entry.Value.Normals, entry.Value.Uvs, entry.Value.Triangles);
                }
            }
        }

        public IEnumerable<KeyValuePair<Paint, Mesh>> BuildMeshes(string namePrefix)
        {
            foreach (KeyValuePair<Paint, Part> entry in m_Parts)
            {
                var mesh = new Mesh { name = namePrefix + "_" + entry.Key };
                mesh.SetVertices(entry.Value.Vertices);
                mesh.SetNormals(entry.Value.Normals);
                mesh.SetUVs(0, entry.Value.Uvs);
                mesh.SetTriangles(entry.Value.Triangles, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                yield return new KeyValuePair<Paint, Mesh>(entry.Key, mesh);
            }
        }
    }

    /// <summary>
    /// Parametric American mainline toll plaza: canopy on columns spanning every lane, raised
    /// islands between lanes with attendant booths, yellow nose attenuators and bollards, a lane
    /// sign plus lane-status light over each lane, and a toll-rate sign on the approach.
    /// Coordinates: X across the lanes (left to right for approaching drivers), Y up from the road
    /// surface, Z along travel. Traffic approaches from -Z.
    /// </summary>
    public class PlazaLayout
    {
        public int LaneCount = 4;
        /// <summary>Lanes counted from the right that are cash lanes (green signs); the rest are electronic (purple).</summary>
        public int CashLanes = 2;
        /// <summary>Lane spacing of the highway through the plaza; replaced by the measured value in game.</summary>
        public float LaneWidth = 3.75f;
        /// <summary>
        /// Islands sit on the lane lines of the highway (as on real plazas), so they're narrow and every lane
        /// keeps its own straight path through the booths.
        /// </summary>
        public float IslandWidth = 1.2f;
        public float IslandLength = 22f;
        public float IslandHeight = 0.25f;
        public float CanopyDepth = 12f;
        public float CanopyClearance = 5.5f; // ~18 ft

        /// <summary>Large name billboard on the canopy roof; the 2-lane ramp plaza is too small to carry one.</summary>
        public bool RoofSign => LaneCount >= 4;

        /// <summary>Straight highway through the plaza: long approach before the island noses, short exit after.</summary>
        public const float ApproachLength = 45f;
        public const float ExitLength = 12f;

        /// <summary>
        /// Lanes per highway through the plaza. The game's widest one-way highway is 5 lanes, so 6- and 8-lane
        /// plazas use two highways side by side, with a wide island between them.
        /// </summary>
        public int[] RoadGroups => LaneCount <= 4 ? new[] { LaneCount } : new[] { LaneCount / 2, LaneCount - LaneCount / 2 };

        /// <summary>
        /// Measured lane centers (relative to the road's center line) and road width of the game's one-way
        /// highways, by lane count; filled in game by RoadLaneProbe. Without a measurement, lanes are assumed
        /// evenly spaced with 1.5 m shoulders.
        /// </summary>
        public static readonly Dictionary<int, (float[] lanes, float width)> MeasuredRoads = new Dictionary<int, (float[], float)>();

        private float[] m_LaneX;
        private float[] m_RoadCenters;

        /// <summary>Recompute lane positions (after MeasuredRoads changes).</summary>
        public void Invalidate() => m_LaneX = null;

        private float[] LaneXs
        {
            get
            {
                if (m_LaneX == null)
                {
                    ComputeLanes();
                }
                return m_LaneX;
            }
        }

        private void ComputeLanes()
        {
            int[] groups = RoadGroups;
            var offsets = new List<float[]>();
            var widths = new List<float>();
            foreach (int n in groups)
            {
                if (MeasuredRoads.TryGetValue(n, out var measured) && measured.lanes.Length == n)
                {
                    offsets.Add(measured.lanes);
                    widths.Add(measured.width);
                    if (n >= 2)
                    {
                        LaneWidth = measured.lanes[1] - measured.lanes[0];
                    }
                }
                else
                {
                    var o = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        o[i] = (i + 0.5f - n * 0.5f) * LaneWidth;
                    }
                    offsets.Add(o);
                    widths.Add(n * LaneWidth + 3f);
                }
            }

            float total = 0f;
            foreach (float w in widths)
            {
                total += w;
            }
            float cursor = -total * 0.5f;
            var lanes = new List<float>();
            m_RoadCenters = new float[groups.Length];
            for (int g = 0; g < groups.Length; g++)
            {
                float center = cursor + widths[g] * 0.5f;
                m_RoadCenters[g] = center;
                foreach (float o in offsets[g])
                {
                    lanes.Add(center + o);
                }
                cursor += widths[g];
            }
            m_LaneX = lanes.ToArray();
        }

        public float LaneCenterX(int lane) => LaneXs[lane];

        /// <summary>Islands 0..LaneCount: outer islands on the road edges' lane lines, inner ones between lanes.</summary>
        public float IslandCenterX(int island)
        {
            float[] x = LaneXs;
            if (island <= 0) return x[0] - LaneWidth * 0.5f;
            if (island >= LaneCount) return x[LaneCount - 1] + LaneWidth * 0.5f;
            return (x[island - 1] + x[island]) * 0.5f;
        }

        /// <summary>Normal islands are narrow; the one between two side-by-side highways fills the gap.</summary>
        public float IslandWidthAt(int island)
        {
            if (island <= 0 || island >= LaneCount) return IslandWidth;
            float[] x = LaneXs;
            return x[island] - x[island - 1] - LaneWidth + IslandWidth;
        }

        public float TotalWidth => LaneXs[LaneCount - 1] - LaneXs[0] + LaneWidth + IslandWidth;

        /// <summary>
        /// The plaza's own road: one straight one-way highway per road group, as cubic Béziers between
        /// numbered nodes (group g runs from node 2g to node 2g+1). Players connect their highway to the
        /// ends like any road end.
        /// </summary>
        public List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int startNode, int endNode, int lanes)> RoadSegments()
        {
            float[] unused = LaneXs; // ensure road centers are computed
            float hl = IslandLength * 0.5f;
            int[] groups = RoadGroups;
            var segments = new List<(Vector3, Vector3, Vector3, Vector3, int, int, int)>();
            for (int g = 0; g < groups.Length; g++)
            {
                var start = new Vector3(m_RoadCenters[g], 0f, -hl - ApproachLength);
                var end = new Vector3(m_RoadCenters[g], 0f, hl + ExitLength);
                segments.Add((start, Vector3.Lerp(start, end, 1f / 3f), Vector3.Lerp(start, end, 2f / 3f), end, 2 * g, 2 * g + 1, groups[g]));
            }
            return segments;
        }

        /// <summary>
        /// White delineator posts on the lane lines just ahead of each island nose and behind each tail
        /// (the first 10 ft), keeping drivers in their lane into and out of the booths.
        /// </summary>
        private void BuildDelineators(PlazaMeshBuilder b)
        {
            float hl = IslandLength * 0.5f;
            for (int island = 1; island < LaneCount; island++)
            {
                if (IslandWidthAt(island) > IslandWidth + 0.5f)
                {
                    continue; // the wide middle island between two highways has its own crash cushion
                }
                float x = IslandCenterX(island);
                for (int k = 0; k < 3; k++)
                {
                    float offset = 0.8f + k * 1.1f;
                    Delineator(b, new Vector3(x, 0f, -hl - 1.8f - offset));
                    Delineator(b, new Vector3(x, 0f, hl + offset));
                }
            }
        }

        private static void Delineator(PlazaMeshBuilder b, Vector3 at)
        {
            b.Box(Paint.DarkMetal, new Vector3(at.x - 0.1f, 0.02f, at.z - 0.1f), new Vector3(at.x + 0.1f, 0.07f, at.z + 0.1f));
            b.Box(Paint.SignWhite, new Vector3(at.x - 0.05f, 0.07f, at.z - 0.05f), new Vector3(at.x + 0.05f, 0.95f, at.z + 0.05f));
            // Reflective bands near the top.
            b.Box(Paint.SafetyYellow, new Vector3(at.x - 0.055f, 0.72f, at.z - 0.055f), new Vector3(at.x + 0.055f, 0.78f, at.z + 0.055f));
            b.Box(Paint.SafetyYellow, new Vector3(at.x - 0.055f, 0.84f, at.z - 0.055f), new Vector3(at.x + 0.055f, 0.9f, at.z + 0.055f));
        }

        /// <summary>Default lane setup: the rightmost <see cref="CashLanes"/> lanes take cash.</summary>
        public bool IsCashLane(int lane) => lane >= LaneCount - CashLanes;

        public void Build(PlazaMeshBuilder b)
        {
            BuildStatic(b);
            for (int lane = 0; lane < LaneCount; lane++)
            {
                BuildLaneSign(b, lane, IsCashLane(lane), closed: false);
            }
        }

        /// <summary>Everything except the per-lane signs, which change with the lane setup.</summary>
        public void BuildStatic(PlazaMeshBuilder b)
        {
            for (int i = 0; i <= LaneCount; i++)
            {
                BuildIsland(b, IslandCenterX(i), IslandWidthAt(i));
            }
            BuildDelineators(b);
            BuildCanopy(b);
            if (RoofSign)
            {
                BuildRoofSign(b);
            }
            BuildRateSign(b);
        }

        private void BuildIsland(PlazaMeshBuilder b, float x, float width)
        {
            float hw = width * 0.5f;
            float hl = IslandLength * 0.5f;
            float top = IslandHeight;

            // Raised concrete island.
            b.Box(Paint.Concrete, new Vector3(x - hw, 0f, -hl), new Vector3(x + hw, top, hl));

            // Crash attenuator on the approach nose, with the plaza speed limit sign on its face.
            float aw = Mathf.Min(0.5f, hw - 0.05f);
            b.Box(Paint.SafetyYellow, new Vector3(x - aw, 0f, -hl - 1.8f), new Vector3(x + aw, 0.9f, -hl));
            b.Box(Paint.DarkMetal, new Vector3(x - aw - 0.02f, 0.6f, -hl - 1.82f), new Vector3(x + aw + 0.02f, 0.7f, -hl - 1.2f));
            // The speed limit sign on this nose belongs to the lane on its right, so BuildLaneSign adds it.

            // Bollards protecting the booth, and a pair at the far end.
            foreach (float z in new[] { -hl + 0.6f, -hl + 1.8f, -hl + 3.0f, hl - 0.6f })
            {
                b.Box(Paint.SafetyYellow, new Vector3(x - 0.13f, top, z - 0.13f), new Vector3(x + 0.13f, top + 1.1f, z + 0.13f));
            }

            // Canopy columns.
            float colZ = CanopyDepth * 0.5f - 1.0f;
            foreach (float z in new[] { -colZ, colZ })
            {
                b.Box(Paint.CanopySteel, new Vector3(x - 0.3f, top, z - 0.3f), new Vector3(x + 0.3f, CanopyClearance, z + 0.3f));
            }

            // Attendant booth: solid lower walls, glazed band, overhanging roof.
            const float boothZ = 1.2f;
            float bw = Mathf.Min(0.75f, hw - 0.08f); // half width, fits the island
            const float bl = 1.4f;                   // half length
            b.Box(Paint.BoothWall, new Vector3(x - bw, top, boothZ - bl), new Vector3(x + bw, top + 1.0f, boothZ + bl));
            b.Box(Paint.Glass, new Vector3(x - bw + 0.03f, top + 1.0f, boothZ - bl + 0.03f), new Vector3(x + bw - 0.03f, top + 2.2f, boothZ + bl - 0.03f));
            // Corner posts framing the glass.
            foreach (float px in new[] { x - bw, x + bw - 0.08f })
            {
                foreach (float pz in new[] { boothZ - bl, boothZ + bl - 0.08f })
                {
                    b.Box(Paint.BoothWall, new Vector3(px, top + 1.0f, pz), new Vector3(px + 0.08f, top + 2.2f, pz + 0.08f));
                }
            }
            b.Box(Paint.BoothWall, new Vector3(x - bw - 0.12f, top + 2.2f, boothZ - bl - 0.15f), new Vector3(x + bw + 0.12f, top + 2.5f, boothZ + bl + 0.15f));
        }

        private void BuildCanopy(PlazaMeshBuilder b)
        {
            float hw = TotalWidth * 0.5f + 0.5f;
            float hd = CanopyDepth * 0.5f;
            float y0 = CanopyClearance;

            // Roof slab.
            b.Box(Paint.CanopySteel, new Vector3(-hw, y0, -hd), new Vector3(hw, y0 + 0.5f, hd));

            // Fascia band on both faces and both ends, with a navy stripe along the top.
            const float t = 0.3f;
            float fy0 = y0 - 0.3f;
            float fy1 = y0 + 1.2f;
            b.Box(Paint.Fascia, new Vector3(-hw - t, fy0, -hd - t), new Vector3(hw + t, fy1, -hd));
            b.Box(Paint.Fascia, new Vector3(-hw - t, fy0, hd), new Vector3(hw + t, fy1, hd + t));
            b.Box(Paint.Fascia, new Vector3(-hw - t, fy0, -hd), new Vector3(-hw, fy1, hd));
            b.Box(Paint.Fascia, new Vector3(hw, fy0, -hd), new Vector3(hw + t, fy1, hd));

            // Stripe is a slightly larger ring so it sits proud of the fascia (no z-fighting).
            float sy0 = fy1 - 0.3f;
            float sy1 = fy1 + 0.02f;
            const float s = 0.02f;
            b.Box(Paint.FasciaStripe, new Vector3(-hw - t - s, sy0, -hd - t - s), new Vector3(hw + t + s, sy1, -hd - t + 0.1f));
            b.Box(Paint.FasciaStripe, new Vector3(-hw - t - s, sy0, hd + t - 0.1f), new Vector3(hw + t + s, sy1, hd + t + s));
            b.Box(Paint.FasciaStripe, new Vector3(-hw - t - s, sy0, -hd - t), new Vector3(-hw - t + 0.1f, sy1, hd + t));
            b.Box(Paint.FasciaStripe, new Vector3(hw + t - 0.1f, sy0, -hd - t), new Vector3(hw + t + s, sy1, hd + t));
        }

        private void BuildRoofSign(PlazaMeshBuilder b)
        {
            // Billboard centered on the roof, facing approaching traffic; the plaza name goes on it later.
            float roofTop = CanopyClearance + 0.5f;
            float hw = Mathf.Min(TotalWidth * 0.3f, 16f);
            float y0 = roofTop + 1.2f;
            float y1 = y0 + 3.6f;
            const float z = 0f;

            // Steel legs and a horizontal strongback behind the panel.
            for (int i = 0; i < 4; i++)
            {
                float lx = -hw * 0.8f + i * (hw * 1.6f / 3f);
                b.Box(Paint.DarkMetal, new Vector3(lx - 0.15f, roofTop, z + 0.15f), new Vector3(lx + 0.15f, y1 - 0.3f, z + 0.45f));
            }
            b.Box(Paint.DarkMetal, new Vector3(-hw * 0.85f, y0 + 0.4f, z + 0.15f), new Vector3(hw * 0.85f, y0 + 0.7f, z + 0.45f));

            // Panel body (back stays plain green) with the lettered face on the approach side.
            b.Box(Paint.SignGreen, new Vector3(-hw, y0, z - 0.1f), new Vector3(hw, y1, z + 0.15f));
            b.FaceQuad(Paint.SignFaceRoof, -hw, hw, y0, y1, z - 0.105f);
        }

        /// <summary>Roof billboard face size in meters (width, height), for baking its artwork.</summary>
        public Vector2 RoofSignSize => new Vector2(2f * Mathf.Min(TotalWidth * 0.3f, 16f), 3.6f);

        /// <summary>Lane sign face size in meters (width, height).</summary>
        public Vector2 LaneSignSize => new Vector2(LaneWidth * 0.56f, 1.1f);

        /// <summary>Speed limit sign (US R2-1 proportions, 24x30 in scaled to fit the attenuator face).</summary>
        public static readonly Vector2 SpeedSignSize = new Vector2(0.62f, 0.76f);

        /// <summary>Speed limits through the booth lanes, in mph (defaults; adjustable later in the control panel).</summary>
        public const int CashSpeedMph = 10;
        public const int EtcSpeedMph = 35;

        /// <summary>Toll-rate sign face size in meters (width, height).</summary>
        public static readonly Vector2 RateSignSize = new Vector2(4.0f, 2.6f);

        /// <summary>
        /// Cash-lane exit at the downstream end of the island (by the end bollards), well clear of the booth:
        /// a gate cabinet with a red and white striped arm, and a small two-lamp signal (green = paid / go,
        /// red = stop or lane closed). The arm is shown raised; once the toll logic runs it will drop until
        /// the vehicle has paid.
        /// </summary>
        private void BuildExitGate(PlazaMeshBuilder b, int lane, bool closed)
        {
            float top = IslandHeight;
            float islandRight = IslandCenterX(lane) + IslandWidthAt(lane) * 0.5f;
            float gz = IslandLength * 0.5f - 2.4f;

            // Cabinet.
            float cx = islandRight - 0.3f;
            b.Box(Paint.SafetyYellow, new Vector3(cx - 0.18f, top, gz - 0.18f), new Vector3(cx + 0.18f, top + 1.0f, gz + 0.18f));
            b.Box(Paint.DarkMetal, new Vector3(cx - 0.2f, top + 1.0f, gz - 0.2f), new Vector3(cx + 0.2f, top + 1.06f, gz + 0.2f));
            // Arm, raised.
            b.Box(Paint.GateArm, new Vector3(cx + 0.05f, top + 0.8f, gz - 0.05f), new Vector3(cx + 0.14f, top + 0.8f + 3.2f, gz + 0.05f));

            // Signal on a short post just past the gate, facing the driver.
            float sx = islandRight - 0.35f;
            float sz = gz + 0.9f;
            b.Box(Paint.DarkMetal, new Vector3(sx - 0.05f, top, sz - 0.05f), new Vector3(sx + 0.05f, top + 1.8f, sz + 0.05f));
            b.Box(Paint.DarkMetal, new Vector3(sx - 0.16f, top + 1.8f, sz - 0.12f), new Vector3(sx + 0.16f, top + 2.45f, sz + 0.1f));
            // Upper lamp red, lower lamp green; only one is lit.
            b.Box(closed ? Paint.LaneLightRed : Paint.DarkMetal, new Vector3(sx - 0.1f, top + 2.18f, sz - 0.14f), new Vector3(sx + 0.1f, top + 2.38f, sz - 0.12f));
            b.Box(closed ? Paint.DarkMetal : Paint.LaneLight, new Vector3(sx - 0.1f, top + 1.88f, sz - 0.14f), new Vector3(sx + 0.1f, top + 2.08f, sz - 0.12f));
        }

        /// <summary>UV rect of lane number n (1..16) in the 4x4 number atlas, row 0 at the top.</summary>
        public static Rect LaneNumberCell(int number)
        {
            int i = (number - 1) % 16;
            int col = i % 4;
            int row = i / 4;
            return new Rect(col * 0.25f, 1f - (row + 1) * 0.25f, 0.25f, 0.25f);
        }

        // 5x5 LED matrix patterns for the lane signal, top row first: down arrow (open) and X (closed).
        private static readonly string[] OpenPattern = { "..#..", "..#..", "..#..", "#####", ".###." };
        private static readonly string[] ClosedPattern = { "#...#", ".#.#.", "..#..", ".#.#.", "#...#" };

        /// <summary>
        /// Sign and LED lane signal over one lane: purple (electronic only) or green (cash) sign, and a
        /// green down-arrow when open or a red X when closed, like real plaza lane-use signals.
        /// </summary>
        public void BuildLaneSign(PlazaMeshBuilder b, int lane, bool cash, bool closed)
        {
            float x = LaneCenterX(lane);
            float faceZ = -CanopyDepth * 0.5f - 0.3f; // front face of the fascia
            float y0 = CanopyClearance - 0.2f;
            float y1 = y0 + 1.1f;
            float hw = LaneWidth * 0.28f; // leaves room for the lane number and signal beside it

            // Sign panel mounted on the approach face of the fascia, lettered face in front.
            b.Box(cash ? Paint.SignGreen : Paint.SignPurple, new Vector3(x - hw, y0, faceZ - 0.1f), new Vector3(x + hw, y1, faceZ));
            b.FaceQuad(cash ? Paint.SignFaceCash : Paint.SignFaceEtc, x - hw, x + hw, y0, y1, faceZ - 0.105f);

            if (cash)
            {
                BuildExitGate(b, lane, closed);
            }

            // Lane speed limit on the crash cushion at the nose of the lane's left island.
            float ix = IslandCenterX(lane);
            float noseZ = -IslandLength * 0.5f - 1.84f;
            b.FaceQuad(cash ? Paint.SignFaceSpeedCash : Paint.SignFaceSpeed,
                ix - SpeedSignSize.x * 0.5f, ix + SpeedSignSize.x * 0.5f, 0.1f, 0.1f + SpeedSignSize.y, noseZ);

            // Lane number plate to the left of the sign (lanes numbered 1.. from the left).
            float nx = x - hw - 0.36f;
            float ny = (y0 + y1) * 0.5f;
            b.Box(Paint.DarkMetal, new Vector3(nx - 0.33f, ny - 0.33f, faceZ - 0.1f), new Vector3(nx + 0.33f, ny + 0.33f, faceZ));
            b.FaceQuad(Paint.SignFaceLaneNumber, nx - 0.3f, nx + 0.3f, ny - 0.3f, ny + 0.3f, faceZ - 0.105f, LaneNumberCell(lane + 1));

            // Lane signal to the right of the sign: LED matrix in a dark housing.
            float lx = x + hw + 0.36f;
            float ly = (y0 + y1) * 0.5f;
            b.Box(Paint.DarkMetal, new Vector3(lx - 0.33f, ly - 0.33f, faceZ - 0.15f), new Vector3(lx + 0.33f, ly + 0.33f, faceZ));
            string[] pattern = closed ? ClosedPattern : OpenPattern;
            Paint led = closed ? Paint.LaneLightRed : Paint.LaneLight;
            const float pitch = 0.11f;
            const float px = 0.045f; // half pixel size
            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (pattern[row][col] != '#')
                    {
                        continue;
                    }
                    float cx = lx + (col - 2) * pitch;
                    float cy = ly + (2 - row) * pitch;
                    b.Box(led, new Vector3(cx - px, cy - px, faceZ - 0.17f), new Vector3(cx + px, cy + px, faceZ - 0.15f));
                }
            }
        }

        private void BuildRateSign(PlazaMeshBuilder b)
        {
            // Freestanding toll-rate sign on the right shoulder ahead of the plaza.
            float x = TotalWidth * 0.5f + 5f; // on the shoulder, clear of the highway
            float z = -IslandLength * 0.5f - 12f;
            const float hw = 2.0f;
            const float y0 = 2.8f;
            const float y1 = 5.4f;

            foreach (float px in new[] { x - 1.4f, x + 1.4f })
            {
                b.Box(Paint.DarkMetal, new Vector3(px - 0.1f, 0f, z - 0.1f), new Vector3(px + 0.1f, y1, z + 0.1f));
            }
            b.Box(Paint.SignGreen, new Vector3(x - hw, y0, z - 0.2f), new Vector3(x + hw, y1, z - 0.1f));
            b.FaceQuad(Paint.SignFaceRate, x - hw, x + hw, y0, y1, z - 0.205f);
        }
    }
}
