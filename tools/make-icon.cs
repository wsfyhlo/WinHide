using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// 生成 app.ico（多尺寸 PNG 内嵌）+ 预览图
public class MakeIcon
{
    static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float pad = size * 0.06f;
            float r = size * 0.24f;
            var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

            using (var path = RoundedRect(rect, r))
            using (var brush = new LinearGradientBrush(
                new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
                Color.FromArgb(255, 59, 130, 246),   // #3B82F6
                Color.FromArgb(255, 29, 78, 216),    // #1D4ED8
                90f))
            {
                g.FillPath(brush, path);
            }

            // 白色字母 H
            float fontSize = size * 0.58f;
            using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat())
            using (var white = new SolidBrush(Color.White))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                var textRect = new RectangleF(0, size * 0.02f, size, size);
                g.DrawString("H", font, white, textRect, sf);
            }
        }
        return bmp;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Main(string[] args)
    {
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var bmp = Draw(s))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        }

        using (var fs = File.Create(args[0]))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            for (int i = 0; i < pngs.Count; i++) bw.Write(pngs[i]);
        }

        if (args.Length > 1)
        {
            using (var bmp = Draw(256))
            {
                bmp.Save(args[1], ImageFormat.Png);
            }
        }
        Console.WriteLine("OK " + args[0]);
    }
}
