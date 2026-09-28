using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using UnityEngine;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;

namespace TollPlaza
{
    /// <summary>
    /// Draws sign artwork in game (System.Drawing, which the game ships), in the same style as the signs
    /// baked by tools/PreviewExport/TextureBaker: US guide-sign green, rounded white border, bold condensed
    /// lettering shrunk to fit.
    /// </summary>
    public static class SignArt
    {
        private static readonly Color Green = Color.FromArgb(0, 107, 56);
        private static readonly Color Purple = Color.FromArgb(92, 51, 140);

        /// <summary>The roof billboard: the plaza's name, and a line saying where the cash lanes are.</summary>
        public static Texture2D RoofSign(Vector2 meters, string name, string subtitle)
        {
            using (Bitmap bmp = NewSign(meters, 2048, Green, out Graphics g))
            {
                using (g)
                {
                    float w = bmp.Width;
                    float h = bmp.Height;
                    DrawCentered(g, name.ToUpperInvariant(), new RectangleF(0, h * 0.14f, w, h * 0.5f), h * 0.4f);
                    DrawCentered(g, subtitle, new RectangleF(0, h * 0.64f, w, h * 0.2f), h * 0.12f);
                }
                return ToTexture(bmp, "TollPlaza roof sign: " + name);
            }
        }

        /// <summary>A booth's lane sign: green CASH / ALL VEHICLES or purple E-TOLL / ONLY · NO CASH.</summary>
        public static Texture2D LaneSign(Vector2 meters, bool cash)
        {
            using (Bitmap bmp = NewSign(meters, 1024, cash ? Green : Purple, out Graphics g))
            {
                using (g)
                {
                    float w = bmp.Width;
                    float h = bmp.Height;
                    DrawCentered(g, cash ? "CASH" : "E-TOLL", new RectangleF(0, h * 0.14f, w, h * 0.5f), h * 0.42f);
                    DrawCentered(g, cash ? "ALL VEHICLES" : "ONLY  ·  NO CASH", new RectangleF(0, h * 0.62f, w, h * 0.24f), h * 0.16f);
                }
                return ToTexture(bmp, cash ? "TollPlaza lane sign: cash" : "TollPlaza lane sign: E-Toll");
            }
        }

        /// <summary>US regulatory speed limit sign (R2-1): white, black border, black legend.</summary>
        public static Texture2D SpeedSign(Vector2 meters, int mph)
        {
            int w = 512;
            int h = (int)Math.Round(w * meters.y / meters.x);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
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
                return ToTexture(bmp, "TollPlaza speed sign: " + mph);
            }
        }

        /// <summary>The toll-rate sign ahead of the plaza, with the rates set in the control panel.</summary>
        public static Texture2D RateSign(Vector2 meters, int carCents, int truckCents, int busCents)
        {
            using (Bitmap bmp = NewSign(meters, 1024, Green, out Graphics g))
            {
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
                    var rows = new[] { ("CARS", carCents), ("TRUCKS", truckCents), ("BUSES", busCents) };
                    for (int i = 0; i < rows.Length; i++)
                    {
                        float top = h * (0.38f + i * 0.17f);
                        Draw(g, rows[i].Item1, new RectangleF(w * 0.14f, top, w * 0.5f, h * 0.15f), h * 0.11f, StringAlignment.Near, Color.White);
                        Draw(g, Dollars(rows[i].Item2), new RectangleF(w * 0.5f, top, w * 0.36f, h * 0.15f), h * 0.11f, StringAlignment.Far, Color.White);
                    }
                }
                return ToTexture(bmp, "TollPlaza rate sign");
            }
        }

        /// <summary>Red and white bands (one of each per tile, along V) for a gate arm.</summary>
        public static Texture2D GateStripes()
        {
            using (var bmp = new Bitmap(64, 256, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (var red = new SolidBrush(Color.FromArgb(217, 26, 20)))
                {
                    g.Clear(Color.FromArgb(245, 245, 245));
                    g.FillRectangle(red, 0, 0, 64, 128);
                }
                // Repeats along the arm (one red and one white band per 0.8 m; the arm's UVs run past 1).
                Texture2D texture = ToTexture(bmp, "TollPlaza gate stripes");
                texture.wrapMode = TextureWrapMode.Repeat;
                return texture;
            }
        }

        private static string Dollars(int cents) => cents == 0 ? "FREE" : $"${cents / 100}.{cents % 100:00}";

        private static Bitmap NewSign(Vector2 meters, int widthPx, Color background, out Graphics g)
        {
            int heightPx = Math.Max(64, (int)Math.Round(widthPx * meters.y / meters.x));
            var bmp = new Bitmap(widthPx, heightPx, PixelFormat.Format32bppArgb);
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

        private static FontFamily s_Family;

        private static void DrawCentered(Graphics g, string text, RectangleF box, float px, Color? color = null) =>
            Draw(g, text, box, px, StringAlignment.Center, color ?? Color.White);

        private static void Draw(Graphics g, string text, RectangleF box, float px, StringAlignment align, Color color)
        {
            s_Family = s_Family ?? SignFont();
            // Shrink to fit the box width if needed.
            float size = px;
            using (var probe = new Font(s_Family, size, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF measured = g.MeasureString(text, probe);
                if (measured.Width > box.Width * 0.9f)
                {
                    size *= box.Width * 0.9f / measured.Width;
                }
            }
            using (var font = new Font(s_Family, size, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
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

        /// <summary>Copies the bitmap into a mipmapped sRGB texture (Unity's rows run bottom-up).</summary>
        private static Texture2D ToTexture(Bitmap bmp, string name)
        {
            int w = bmp.Width, h = bmp.Height;
            var bytes = new byte[w * h * 4];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, bytes, (h - 1 - y) * w * 4, w * 4);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                // GDI+ stores BGRA.
                pixels[i] = new Color32(bytes[i * 4 + 2], bytes[i * 4 + 1], bytes[i * 4], 255);
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
