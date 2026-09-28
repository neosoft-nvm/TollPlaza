using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using Transform = Game.Objects.Transform;

namespace TollPlaza
{
    /// <summary>
    /// Keeps each plaza's signs up to date. All plazas of a size share one prefab, so their baked signs are
    /// all the same ("TOLL PLAZA", the default cash lanes, 15/10 mph, the default rates); this draws new faces
    /// just in front of them:
    /// - roof billboard: the plaza's name, and where its cash lanes are;
    /// - each lane: its sign (CASH or E-TOLL, as switched in the plaza's panel), its speed limit (the control
    ///   panel's cash or E-Toll speed), a signal at the end of the island (red until its booth charges a
    ///   vehicle, then green for a moment), and a gate arm if it takes cash (the model itself has none);
    /// - the toll-rate sign: the control panel's rates.
    /// Artwork is drawn in game (SignArt) and redrawn only when something changes. Everything is drawn every
    /// frame like the game's road-name labels (Graphics.DrawMesh from RenderPipelineManager.beginContextRendering),
    /// is not an entity and is never saved. If anything fails it logs a warning and the baked signs stay.
    /// </summary>
    public partial class PlazaSignSystem : GameSystemBase
    {
        // How far each new face sits in front of the baked one (meters): enough to never flicker.
        private const float RoofOffset = 0.13f - 0.105f;
        private const float LaneOffset = 0.015f;
        private const float SpeedOffset = 0.012f;
        private const float RateOffset = 0.01f;
        // The redrawn lane sign is a shell slightly bigger than the baked panel, so its old colour can't show.
        private const float LaneInflate = 0.012f;

        // Signs are refreshed a few times a second; the drawing itself happens every frame.
        private const int RefreshEveryFrames = 15;

        private readonly struct Piece
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public Piece(Mesh mesh, Material material) { Mesh = mesh; Material = material; }
        }

        /// <summary>A lane's gate: its signal (red or green pieces) and arm, both set every frame from its state.</summary>
        private sealed class Gate
        {
            public LaneGate State;
            public PlazaStats Booth; // closed: signal stays red
            public List<Piece> Red, Green;
            public Piece? Arm;
            public Vector3 Hinge;
        }

        private readonly List<(Matrix4x4 matrix, List<Piece> pieces, List<Gate> gates)> m_Draw = new List<(Matrix4x4, List<Piece>, List<Gate>)>();
        private readonly Dictionary<string, Material> m_Materials = new Dictionary<string, Material>();
        private readonly HashSet<string> m_UsedMaterials = new HashSet<string>();
        private readonly Dictionary<string, Mesh> m_Meshes = new Dictionary<string, Mesh>();
        private readonly Dictionary<PlazaLayout, int> m_LayoutIds = new Dictionary<PlazaLayout, int>();

        private PrefabSystem m_PrefabSystem;
        private Game.Simulation.SimulationSystem m_SimulationSystem;
        private Shader m_Shader;
        private int m_Frame;
        private bool m_Failed;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();
            RenderPipelineManager.beginContextRendering += Render;
        }

        protected override void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= Render;
            foreach (string key in m_Materials.Keys.ToList())
            {
                ReleaseMaterial(key);
            }
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (m_Failed || m_Frame++ % RefreshEveryFrames != 0)
            {
                return;
            }
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                TurnOff(e);
            }
        }

        private void TurnOff(Exception e)
        {
            m_Failed = true;
            m_Draw.Clear();
            Mod.Log.Warn(e, "[TollPlaza] Live plaza signs turned off (the baked signs stay)");
        }

        /// <summary>Works out every plaza's signs and where they are (main thread; Render only draws).</summary>
        private void Refresh()
        {
            m_Draw.Clear();
            m_UsedMaterials.Clear();
            foreach (Entity plaza in PlazaNames.Placed)
            {
                if (!EntityManager.Exists(plaza) || EntityManager.HasComponent<Deleted>(plaza) || EntityManager.HasComponent<Temp>(plaza)
                    || !EntityManager.HasComponent<Transform>(plaza) || !EntityManager.HasComponent<PrefabRef>(plaza)
                    || !m_PrefabSystem.TryGetPrefab(EntityManager.GetComponentData<PrefabRef>(plaza), out PrefabBase prefab)
                    || !PlazaAssetSystem.PlazaLayouts.TryGetValue(prefab, out PlazaLayout layout))
                {
                    continue;
                }
                Transform transform = EntityManager.GetComponentData<Transform>(plaza);
                var gates = new List<Gate>();
                m_Draw.Add((Matrix4x4.TRS(transform.m_Position, transform.m_Rotation, Vector3.one), PiecesFor(plaza, layout, gates), gates));
            }

            // Artwork no plaza shows any more (renamed, bulldozed, rates or speeds changed): free it.
            foreach (string key in m_Materials.Keys.Where(k => !m_UsedMaterials.Contains(k)).ToList())
            {
                ReleaseMaterial(key);
            }
        }

        private List<Piece> PiecesFor(Entity plaza, PlazaLayout layout, List<Gate> gates)
        {
            int id = LayoutId(layout);
            var pieces = new List<Piece>();
            var cash = new bool[layout.LaneCount];
            for (int lane = 0; lane < layout.LaneCount; lane++)
            {
                cash[lane] = PlazaStats.IsCashLane(plaza, layout, lane);
            }

            if (layout.RoofSign)
            {
                string name = PlazaNames.Shown(plaza);
                string subtitle = CashSubtitle(cash);
                pieces.Add(new Piece(
                    MeshOf($"roof|{id}", () => RoofMesh(layout)),
                    MaterialOf($"roof|{id}|{name}|{subtitle}", () => SignArt.RoofSign(layout.RoofSignSize, name, subtitle))));
            }

            for (int lane = 0; lane < layout.LaneCount; lane++)
            {
                bool isCash = cash[lane];
                int l = lane;
                pieces.Add(new Piece(
                    MeshOf($"lane|{id}|{lane}", () => LaneMesh(layout, l)),
                    MaterialOf(isCash ? "lane|cash" : "lane|etc", () => SignArt.LaneSign(layout.LaneSignSize, isCash))));

                // A closed lane's signal beside the sign: a red X over the model's green arrow.
                if (PlazaStats.IsClosedLane(plaza, lane))
                {
                    var led = new PlazaMeshBuilder();
                    layout.BuildClosedLaneLed(led, lane);
                    pieces.AddRange(PiecesOf(led, $"closedled|{id}|{lane}"));
                }

                int mph = isCash ? TollState.CashMph : TollState.EtcMph;
                pieces.Add(new Piece(
                    MeshOf($"speed|{id}|{lane}", () => SpeedMesh(layout, l)),
                    MaterialOf($"speed|{mph}", () => SignArt.SpeedSign(PlazaLayout.SpeedSignSize, mph))));

                // The working plaza's model has no exit gates (PlazaLayout.Build): every lane that takes cash gets
                // one here, so switching a lane to E-Toll removes its gate and switching it to cash adds one.
                // Every lane gets a signal (green while a vehicle goes through, see LaneGate); cash lanes also
                // get the gate cabinet and an arm that swings up for each vehicle.
                if (isCash)
                {
                    var cabinet = new PlazaMeshBuilder();
                    layout.BuildGateCabinet(cabinet, lane);
                    pieces.AddRange(PiecesOf(cabinet, $"cabinet|{id}|{lane}"));
                }
                gates.Add(GateFor(plaza, layout, id, lane, isCash));
            }

            int car = TollState.CarRateCents, truck = TollState.TruckRateCents, bus = TollState.BusRateCents;
            pieces.Add(new Piece(
                MeshOf($"rate|{id}", () => RateMesh(layout)),
                MaterialOf($"rate|{car}|{truck}|{bus}", () => SignArt.RateSign(PlazaLayout.RateSignSize, car, truck, bus))));
            return pieces;
        }

        /// <summary>The roof sign's second line, from where the plaza's cash lanes actually are.</summary>
        private static string CashSubtitle(bool[] cash)
        {
            int n = cash.Length;
            int cashCount = cash.Count(c => c);
            if (cashCount == 0)
            {
                return "E-TOLL ONLY   ·   NO CASH";
            }
            if (cashCount == n)
            {
                return "ALL LANES TAKE CASH";
            }
            bool right = cash.Skip(n - cashCount).All(c => c); // all cash lanes on the right
            bool left = cash.Take(cashCount).All(c => c);
            if (right)
            {
                return "E-TOLL LANES LEFT   ·   CASH LANES RIGHT";
            }
            if (left)
            {
                return "CASH LANES LEFT   ·   E-TOLL LANES RIGHT";
            }
            return "CASH AND E-TOLL LANES AS SIGNED";
        }

        /// <summary>A lane's signal (both colours, one shown at a time) and, on a cash lane, its swinging arm.</summary>
        private Gate GateFor(Entity plaza, PlazaLayout layout, int id, int lane, bool arm)
        {
            List<Piece> Signal(bool green)
            {
                var builder = new PlazaMeshBuilder();
                layout.BuildLaneSignal(builder, lane, closed: !green);
                return PiecesOf(builder, $"signal|{id}|{lane}|{green}").ToList();
            }
            var gate = new Gate
            {
                Booth = PlazaStats.Of(plaza).Booth(lane),
                State = PlazaStats.Of(plaza).Booth(lane).Gate,
                Red = Signal(false),
                Green = Signal(true),
                Hinge = layout.GateHinge(lane),
            };
            if (arm)
            {
                var builder = new PlazaMeshBuilder();
                layout.BuildGateBar(builder, lane);
                gate.Arm = PiecesOf(builder, $"arm|{id}|{lane}").First();
            }
            return gate;
        }

        /// <summary>One piece per paint in the builder, meshes cached under the key.</summary>
        private IEnumerable<Piece> PiecesOf(PlazaMeshBuilder builder, string key)
        {
            foreach (var part in builder.RawParts)
            {
                var p = part;
                Mesh mesh = MeshOf($"{key}|{part.paint}", () => ToMesh(p.vertices, p.normals, p.uvs, p.triangles, $"TollPlaza gate {p.paint}"));
                Material material = part.paint == Paint.GateArm
                    ? MaterialOf("gate|stripes", SignArt.GateStripes)
                    : MaterialOf($"color|{part.paint}", null, Paints.ColorOf(part.paint));
                yield return new Piece(mesh, material);
            }
        }

        // ---------- meshes (plaza-local, one per layout and lane) ----------

        private static Mesh RoofMesh(PlazaLayout layout)
        {
            layout.RoofSignPanel(out float hw, out float y0, out float y1, out float z);
            return Quad(-hw, hw, y0, y1, z - 0.105f - RoofOffset, "TollPlaza roof sign");
        }

        private static Mesh SpeedMesh(PlazaLayout layout, int lane)
        {
            layout.SpeedSignFace(lane, out float x0, out float x1, out float y0, out float y1, out float z);
            return Quad(x0, x1, y0, y1, z - SpeedOffset, "TollPlaza speed sign");
        }

        private static Mesh RateMesh(PlazaLayout layout)
        {
            layout.RateSignPanel(out float x0, out float x1, out float y0, out float y1, out float z);
            return Quad(x0, x1, y0, y1, z - 0.205f - RateOffset, "TollPlaza rate sign");
        }

        /// <summary>
        /// A shell over the lane sign's panel: the lettered front, and four sides back to the fascia textured with
        /// the sign's edge colour, so a switched sign shows no trace of the baked panel's colour.
        /// </summary>
        private static Mesh LaneMesh(PlazaLayout layout, int lane)
        {
            layout.LaneSignPanel(lane, out float x0, out float x1, out float y0, out float y1, out float faceZ);
            float e = LaneInflate;
            x0 -= e; x1 += e; y0 -= e; y1 += e;
            float zf = faceZ - 0.105f - LaneOffset; // front
            float zb = faceZ;                       // back, against the fascia
            const float edge = 0.002f;              // UV of the sign's background at its edge

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int s = v.Count;
                v.AddRange(new[] { a, b, c, d });
                n.AddRange(new[] { normal, normal, normal, normal });
                uv.AddRange(new[] { ua, ub, uc, ud });
                t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            // Front (facing -Z): bottom-left, bottom-right, top-right, top-left.
            Face(new Vector3(x0, y0, zf), new Vector3(x1, y0, zf), new Vector3(x1, y1, zf), new Vector3(x0, y1, zf), Vector3.back,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            Vector2 u0 = new Vector2(edge, 0.5f), u1 = new Vector2(1 - edge, 0.5f), vb = new Vector2(0.5f, edge), vt = new Vector2(0.5f, 1 - edge);
            // Left (facing -X), right (+X), bottom (-Y), top (+Y); each wound to face outward.
            Face(new Vector3(x0, y0, zb), new Vector3(x0, y0, zf), new Vector3(x0, y1, zf), new Vector3(x0, y1, zb), Vector3.left, u0, u0, u0, u0);
            Face(new Vector3(x1, y0, zf), new Vector3(x1, y0, zb), new Vector3(x1, y1, zb), new Vector3(x1, y1, zf), Vector3.right, u1, u1, u1, u1);
            Face(new Vector3(x0, y0, zb), new Vector3(x1, y0, zb), new Vector3(x1, y0, zf), new Vector3(x0, y0, zf), Vector3.down, vb, vb, vb, vb);
            Face(new Vector3(x0, y1, zf), new Vector3(x1, y1, zf), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), Vector3.up, vt, vt, vt, vt);
            return ToMesh(v, n, uv, t, "TollPlaza lane sign");
        }

        private static Mesh Quad(float x0, float x1, float y0, float y1, float z, string name) =>
            ToMesh(
                new List<Vector3> { new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z) },
                new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                new List<int> { 0, 2, 1, 0, 3, 2 },
                name);

        private static Mesh ToMesh(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private int LayoutId(PlazaLayout layout)
        {
            if (!m_LayoutIds.TryGetValue(layout, out int id))
            {
                m_LayoutIds[layout] = id = m_LayoutIds.Count;
            }
            return id;
        }

        private Mesh MeshOf(string key, Func<Mesh> build)
        {
            if (!m_Meshes.TryGetValue(key, out Mesh mesh))
            {
                m_Meshes[key] = mesh = build();
            }
            return mesh;
        }

        /// <summary>A material showing the artwork (drawn once per key), or a plain colour.</summary>
        private Material MaterialOf(string key, Func<Texture2D> art, Color? color = null)
        {
            m_UsedMaterials.Add(key);
            if (m_Materials.TryGetValue(key, out Material material))
            {
                return material;
            }
            if (m_Shader == null)
            {
                m_Shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit shader not found");
            }
            material = new Material(m_Shader) { name = "TollPlaza " + key };
            material.SetColor("_BaseColor", color ?? Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.3f);
            if (art != null)
            {
                material.SetTexture("_BaseColorMap", art());
                Mod.Log.Info($"[TollPlaza] Sign drawn: {key}");
            }
            m_Materials[key] = material;
            return material;
        }

        private void ReleaseMaterial(string key)
        {
            Material material = m_Materials[key];
            m_Materials.Remove(key);
            Texture texture = material.GetTexture("_BaseColorMap");
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            UnityEngine.Object.Destroy(material);
        }

        private void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (m_Failed || m_Draw.Count == 0)
            {
                return;
            }
            try
            {
                // Simulation time including the fraction of the current frame, so the arms move smoothly at any
                // game speed and stop when paused.
                double now = m_SimulationSystem.frameIndex + (double)m_SimulationSystem.frameTime;
                foreach (Camera camera in cameras)
                {
                    if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                    {
                        continue;
                    }
                    foreach (var (matrix, pieces, gates) in m_Draw)
                    {
                        foreach (Piece piece in pieces)
                        {
                            Draw(piece, matrix, camera);
                        }
                        foreach (Gate gate in gates)
                        {
                            foreach (Piece piece in !gate.Booth.Closed && gate.State.Green(now) ? gate.Green : gate.Red)
                            {
                                Draw(piece, matrix, camera);
                            }
                            if (gate.Arm is Piece arm)
                            {
                                // Built down (across the lane); open is +90° about Z (pointing up).
                                float angle = 90f * gate.State.Openness(now);
                                Matrix4x4 swing = Matrix4x4.Translate(gate.Hinge) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)) * Matrix4x4.Translate(-gate.Hinge);
                                Draw(arm, matrix * swing, camera);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TurnOff(e);
            }
        }

        private static void Draw(Piece piece, Matrix4x4 matrix, Camera camera) =>
            Graphics.DrawMesh(piece.Mesh, matrix, piece.Material, 0, camera, 0, null, castShadows: false, receiveShadows: true);
    }
}
