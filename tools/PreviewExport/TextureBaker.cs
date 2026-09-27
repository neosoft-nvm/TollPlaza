using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using TollPlaza;
using Color = System.Drawing.Color;

namespace PreviewExport
{
    /// <summary>
    /// Bakes every texture the mod ships, into src/Textures (packed with the mod) and preview/textures:
    /// - one tiling texture per paint: the paint color modulated by a real CC0 surface photo (ambientCG),
    ///   so concrete looks like concrete and the bollards have scuffs;
    /// - lettered sign faces (lane signs, rate sign, roof billboard per plaza size);
    /// - normal maps copied from the same CC0 sources.
    /// </summary>
    internal static class TextureBaker
    {
        private const int TileSize = 512;

        private sealed class Detail
        {
            public float[,] Luma;   // normalized so the mean is 1
        }

        public static void BakeAll(string sourceDir, IEnumerable<string> outputDirs)
        {
            var dirs = new List<string>(outputDirs);
            foreach (string d in dirs)
            {
                Directory.CreateDirectory(d);
            }

            Detail concrete = LoadDetail(Path.Combine(sourceDir, "Concrete034", "Concrete034_1K-JPG_Color.jpg"));
            Detail metal = LoadDetail(Path.Combine(sourceDir, "Metal027", "Metal027_1K-JPG_Color.jpg"));
            Detail painted = LoadDetail(Path.Combine(sourceDir, "PaintedMetal004", "PaintedMetal004_1K-JPG_Color.jpg"));

            // (paint, detail source, strength of the detail)
            var tiles = new (Paint paint, Detail detail, float strength)[]
            {
                (Paint.Concrete, concrete, 1.0f),
                (Paint.CanopySteel, concrete, 0.35f),
                (Paint.Fascia, concrete, 0.3f),
                (Paint.BoothWall, concrete, 0.6f),
                (Paint.DarkMetal, metal, 1.0f),
                (Paint.FasciaStripe, metal, 0.6f),
                (Paint.SafetyYellow, painted, 0.25f),
                (Paint.SignGreen, metal, 0.35f),
                (Paint.SignPurple, metal, 0.35f),
                (Paint.SignWhite, metal, 0.25f),
                (Paint.Asphalt, concrete, 0.9f),
                (Paint.LaneMarking, concrete, 0.15f),
                (Paint.Glass, null, 0f),
                (Paint.LaneLight, null, 0f),
                (Paint.LaneLightRed, null, 0f),
            };
            foreach (var t in tiles)
            {
                using (Bitmap bmp = BakeTile(Paints.ColorOf(t.paint), t.detail, t.strength, t.paint == Paint.Glass))
                {
                    Save(bmp, dirs, t.paint.ToString());
                }
            }

            using (Bitmap bmp = LaneSign(PlazaCatalog.Plazas[1].layout.LaneSignSize, cash: false)) Save(bmp, dirs, Paint.SignFaceEtc.ToString());
            using (Bitmap bmp = LaneSign(PlazaCatalog.Plazas[1].layout.LaneSignSize, cash: true)) Save(bmp, dirs, Paint.SignFaceCash.ToString());
            using (Bitmap bmp = RateSign(PlazaLayout.RateSignSize)) Save(bmp, dirs, Paint.SignFaceRate.ToString());
            using (Bitmap bmp = GateStripes()) Save(bmp, dirs, Paint.GateArm.ToString());
            using (Bitmap bmp = LaneNumberAtlas()) Save(bmp, dirs, Paint.SignFaceLaneNumber.ToString());
            using (Bitmap bmp = SpeedSign(PlazaLayout.SpeedSignSize, PlazaLayout.EtcSpeedMph)) Save(bmp, dirs, Paint.SignFaceSpeed.ToString());
            using (Bitmap bmp = SpeedSign(PlazaLayout.SpeedSignSize, PlazaLayout.CashSpeedMph)) Save(bmp, dirs, Paint.SignFaceSpeedCash.ToString());
            foreach (var plaza in PlazaCatalog.Plazas)
            {
                if (!plaza.layout.RoofSign)
                {
                    continue;
                }
                using (Bitmap bmp = RoofSign(plaza.layout.RoofSignSize, "TOLL PLAZA"))
                {
                    Save(bmp, dirs, Paints.TextureKey(Paint.SignFaceRoof, plaza.id));
                }
            }

            // Mask maps: R metallic, G occlusion, B detail mask, A smoothness.
            SaveMask(dirs, "Mask_Matte", metallic: 0f, smoothness: 0.15f);
            SaveMask(dirs, "Mask_Paint", metallic: 0f, smoothness: 0.35f);
            SaveMask(dirs, "Mask_Metal", metallic: 0.6f, smoothness: 0.4f);
            SaveMask(dirs, "Mask_Glass", metallic: 0f, smoothness: 0.9f);

            CopyNormal(Path.Combine(sourceDir, "Concrete034", "Concrete034_1K-JPG_NormalGL.jpg"), dirs, "Normal_Concrete");
            CopyNormal(Path.Combine(sourceDir, "Metal027", "Metal027_1K-JPG_NormalGL.jpg"), dirs, "Normal_Metal");
            CopyNormal(Path.Combine(sourceDir, "PaintedMetal004", "PaintedMetal004_1K-JPG_NormalGL.jpg"), dirs, "Normal_PaintedMetal");
        }

        // ---------- tiles ----------

        private static Detail LoadDetail(string path)
        {
            using (var src = new Bitmap(path))
            using (var small = new Bitmap(src, new Size(TileSize, TileSize)))
            {
                var luma = new float[TileSize, TileSize];
                double sum = 0;
                for (int y = 0; y < TileSize; y++)
                {
                    for (int x = 0; x < TileSize; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        float l = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
                        luma[x, y] = l;
                        sum += l;
                    }
                }
                float mean = (float)(sum / (TileSize * TileSize));
                for (int y = 0; y < TileSize; y++)
                {
                    for (int x = 0; x < TileSize; x++)
                    {
                        luma[x, y] /= Math.Max(mean, 0.01f);
                    }
                }
                return new Detail { Luma = luma };
            }
        }

        private static Bitmap BakeTile(UnityEngine.Color color, Detail detail, float strength, bool glass)
        {
            var bmp = new Bitmap(TileSize, TileSize, PixelFormat.Format24bppRgb);
            for (int y = 0; y < TileSize; y++)
            {
                for (int x = 0; x < TileSize; x++)
                {
                    float k = 1f;
                    if (detail != null)
                    {
                        k = 1f + (detail.Luma[x, y] - 1f) * strength;
                    }
                    else if (glass)
                    {
                        // Soft diagonal sky reflection streaks so the windows read as glass.
                        float t = (x + (TileSize - y)) / (2f * TileSize);
                        k = 0.85f + 0.35f * (float)Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 3)), 6);
                    }
                    bmp.SetPixel(x, y, Color.FromArgb(ToByte(color.r * k), ToByte(color.g * k), ToByte(color.b * k)));
                }
            }
            return bmp;
        }

        // ---------- signs ----------

        private static readonly Color Purple = Color.FromArgb(92, 51, 140);
        private static readonly Color Green = Color.FromArgb(0, 107, 56);

        private static Bitmap NewSign(UnityEngine.Vector2 meters, int widthPx, Color background, out Graphics g)
        {
            int heightPx = Math.Max(64, (int)Math.Round(widthPx * meters.y / meters.x));
            var bmp = new Bitmap(widthPx, heightPx, PixelFormat.Format24bppRgb);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(background);
            // Rounded white border inset from the edge, as on US guide signs.
            float inset = heightPx * 0.06f;
            float stroke = heightPx * 0.035f;
            using (var pen = new Pen(Color.White, stroke))
            using (GraphicsPath border = RoundedRect(new RectangleF(inset, inset, widthPx - 2 * inset, heightPx - 2 * inset), heightPx * 0.08f))
            {
                g.DrawPath(pen, border);
            }
            return bmp;
        }

        private static Bitmap LaneSign(UnityEngine.Vector2 meters, bool cash)
        {
            Bitmap bmp = NewSign(meters, 1024, cash ? Green : Purple, out Graphics g);
            using (g)
            {
                float w = bmp.Width;
                float h = bmp.Height;
                if (cash)
                {
                    DrawCentered(g, "CASH", new RectangleF(0, h * 0.14f, w, h * 0.5f), h * 0.42f);
                    DrawCentered(g, "ALL VEHICLES", new RectangleF(0, h * 0.62f, w, h * 0.24f), h * 0.16f);
                }
                else
                {
                    DrawCentered(g, "E-TOLL", new RectangleF(0, h * 0.14f, w, h * 0.5f), h * 0.42f);
                    DrawCentered(g, "ONLY  ·  NO CASH", new RectangleF(0, h * 0.62f, w, h * 0.24f), h * 0.16f);
                }
            }
            return bmp;
        }

        private static Bitmap RateSign(UnityEngine.Vector2 meters)
        {
            Bitmap bmp = NewSign(meters, 1024, Green, out Graphics g);
            using (g)
            {
                float w = bmp.Width;
                float h = bmp.Height;
                // White header band with green lettering.
                using (var white = new SolidBrush(Color.White))
                {
                    g.FillRectangle(white, w * 0.1f, h * 0.12f, w * 0.8f, h * 0.2f);
                }
                DrawCentered(g, "TOLL RATES", new RectangleF(0, h * 0.12f, w, h * 0.2f), h * 0.13f, Green);

                string[,] rows = { { "CARS", "$2.50" }, { "TRUCKS", "$6.00" }, { "BUSES", "$4.00" } };
                for (int i = 0; i < 3; i++)
                {
                    float top = h * (0.38f + i * 0.17f);
                    DrawLeft(g, rows[i, 0], new RectangleF(w * 0.14f, top, w * 0.5f, h * 0.15f), h * 0.11f);
                    DrawRight(g, rows[i, 1], new RectangleF(w * 0.5f, top, w * 0.36f, h * 0.15f), h * 0.11f);
                }
            }
            return bmp;
        }

        /// <summary>Red and white bands (one of each per tile, along V) for the gate arm.</summary>
        private static Bitmap GateStripes()
        {
            var bmp = new Bitmap(64, 256, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var red = new SolidBrush(Color.FromArgb(217, 26, 20)))
            {
                g.Clear(Color.FromArgb(245, 245, 245));
                g.FillRectangle(red, 0, 0, 64, 128);
            }
            return bmp;
        }

        /// <summary>4x4 atlas of lane number plates 1..16 (white on black), cell layout per PlazaLayout.LaneNumberCell.</summary>
        private static Bitmap LaneNumberAtlas()
        {
            const int size = 1024;
            const int cell = size / 4;
            var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(13, 13, 15));
                for (int n = 1; n <= 16; n++)
                {
                    int i = n - 1;
                    var box = new RectangleF((i % 4) * cell, (i / 4) * cell, cell, cell);
                    using (var pen = new Pen(Color.White, cell * 0.04f))
                    using (GraphicsPath border = RoundedRect(RectangleF.Inflate(box, -cell * 0.08f, -cell * 0.08f), cell * 0.08f))
                    {
                        g.DrawPath(pen, border);
                    }
                    DrawCentered(g, n.ToString(), box, cell * 0.62f);
                }
            }
            return bmp;
        }

        /// <summary>US regulatory speed limit sign (R2-1): white, black border, black legend.</summary>
        private static Bitmap SpeedSign(UnityEngine.Vector2 meters, int mph)
        {
            int w = 512;
            int h = (int)Math.Round(w * meters.y / meters.x);
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.White);
                float inset = w * 0.05f;
                using (var pen = new Pen(Color.Black, w * 0.035f))
                using (GraphicsPath border = RoundedRect(new RectangleF(inset, inset, w - 2 * inset, h - 2 * inset), w * 0.06f))
                {
                    g.DrawPath(pen, border);
                }
                DrawCentered(g, "SPEED", new RectangleF(0, h * 0.08f, w, h * 0.2f), h * 0.16f, Color.Black);
                DrawCentered(g, "LIMIT", new RectangleF(0, h * 0.27f, w, h * 0.2f), h * 0.16f, Color.Black);
                DrawCentered(g, mph.ToString(), new RectangleF(0, h * 0.47f, w, h * 0.44f), h * 0.4f, Color.Black);
            }
            return bmp;
        }

        private static Bitmap RoofSign(UnityEngine.Vector2 meters, string name)
        {
            Bitmap bmp = NewSign(meters, 2048, Green, out Graphics g);
            using (g)
            {
                float w = bmp.Width;
                float h = bmp.Height;
                DrawCentered(g, name, new RectangleF(0, h * 0.14f, w, h * 0.5f), h * 0.4f);
                DrawCentered(g, "E-TOLL LANES LEFT   ·   CASH LANES RIGHT", new RectangleF(0, h * 0.64f, w, h * 0.2f), h * 0.12f);
            }
            return bmp;
        }

        private static FontFamily SignFont()
        {
            // Closest widely-installed match to US highway lettering (FHWA "Highway Gothic").
            foreach (string name in new[] { "Arial Narrow", "Bahnschrift SemiBold Condensed", "Arial" })
            {
                try
                {
                    return new FontFamily(name);
                }
                catch (ArgumentException)
                {
                }
            }
            return FontFamily.GenericSansSerif;
        }

        private static readonly FontFamily Family = SignFont();

        private static void DrawCentered(Graphics g, string text, RectangleF box, float px, Color? color = null) =>
            Draw(g, text, box, px, StringAlignment.Center, color);

        private static void DrawLeft(Graphics g, string text, RectangleF box, float px) =>
            Draw(g, text, box, px, StringAlignment.Near, null);

        private static void DrawRight(Graphics g, string text, RectangleF box, float px) =>
            Draw(g, text, box, px, StringAlignment.Far, null);

        private static void Draw(Graphics g, string text, RectangleF box, float px, StringAlignment align, Color? color)
        {
            // Shrink to fit the box width if needed.
            float size = px;
            using (var probe = new Font(Family, size, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF measured = g.MeasureString(text, probe);
                if (measured.Width > box.Width * 0.9f)
                {
                    size *= box.Width * 0.9f / measured.Width;
                }
            }
            using (var font = new Font(Family, size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color ?? Color.White))
            using (var format = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(text, font, brush, box, format);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------- output ----------

        private static void SaveMask(List<string> dirs, string key, float metallic, float smoothness)
        {
            using (var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(ToByte(smoothness), ToByte(metallic), 255, 0));
                }
                foreach (string d in dirs)
                {
                    bmp.Save(Path.Combine(d, key + ".png"), ImageFormat.Png);
                }
            }
        }

        private static void CopyNormal(string path, List<string> dirs, string key)
        {
            using (var src = new Bitmap(path))
            using (var small = new Bitmap(src, new Size(TileSize, TileSize)))
            {
                Save(small, dirs, key);
            }
        }

        /// <summary>
        /// Saves as a power-of-two PNG. The game block-compresses textures (BC7), which fails on sizes that
        /// aren't multiples of 4 ("Error when initializing batch"); power-of-two also keeps every mip valid.
        /// Sign faces are UV-mapped 0..1, so squeezing the artwork into a square-ish texture is undone on the
        /// sign and the lettering keeps its proportions.
        /// </summary>
        private static void Save(Bitmap bmp, List<string> dirs, string key)
        {
            int w = NextPowerOfTwo(bmp.Width);
            int h = NextPowerOfTwo(bmp.Height);
            Bitmap output = bmp;
            if (w != bmp.Width || h != bmp.Height)
            {
                output = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(output))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
                        g.DrawImage(bmp, new Rectangle(0, 0, w, h), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
            }
            foreach (string d in dirs)
            {
                output.Save(Path.Combine(d, key + ".png"), ImageFormat.Png);
            }
            if (!ReferenceEquals(output, bmp))
            {
                output.Dispose();
            }
        }

        private static int NextPowerOfTwo(int v)
        {
            int p = 4;
            while (p < v)
            {
                p <<= 1;
            }
            return p;
        }

        private static int ToByte(float linearish) => Math.Max(0, Math.Min(255, (int)Math.Round(linearish * 255f)));
    }
}
