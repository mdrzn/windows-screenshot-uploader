// Icone disegnate nel codice: riquadro arrotondato (colore dello stato nella tray, arancione per l'app)
// con gli angoli di una selezione "screenshot" e una freccia in su, come l'icona della versione Mac.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenshotUploader;

static class Icons
{
    public static Color ColorOf(State s) => s switch
    {
        State.Uploading => Color.FromArgb(0xF0, 0x8A, 0x24),
        State.Paused => Color.FromArgb(0x8C, 0x91, 0x99),
        State.Error => Color.FromArgb(0xD9, 0x34, 0x2B),
        _ => Color.FromArgb(0x2E, 0xA0, 0x43),
    };

    static readonly int[] traySizes = [16, 20, 24, 32, 40, 48, 64];

    public static Icon Tray(State s)
    {
        var c = ColorOf(s);
        return FromIco(Ico(traySizes.Select(px => Draw(px, Lighter(c), c, margin: false))), SystemInformation.SmallIconSize);
    }

    public static Icon App() => FromIco(Ico(new[] { 16, 24, 32, 48, 64 }.Select(AppBitmap)), new Size(32, 32));

    public static void ExportAppIcon(string path) =>
        File.WriteAllBytes(path, Ico(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }.Select(AppBitmap)));

    static Bitmap AppBitmap(int px) => Draw(px, Color.FromArgb(0xFA, 0x8C, 0x5C), Color.FromArgb(0xD9, 0x57, 0x3D), margin: px >= 32);

    // Le icone degli stati con la didascalia, per il README (sfondo trasparente: va bene col tema chiaro e scuro)
    public static void SaveStates(string path, IEnumerable<(State state, string label)> states)
    {
        const int icon = 48, cell = 150, height = 96;
        var list = states.ToList();
        using var bmp = new Bitmap(cell * list.Count, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", 15f, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.FromArgb(0x80, 0x86, 0x8E));
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        for (int i = 0; i < list.Count; i++)
        {
            var c = ColorOf(list[i].state);
            using var b = Draw(icon, Lighter(c), c, margin: false);
            g.DrawImage(b, i * cell + (cell - icon) / 2, 10);
            g.DrawString(list[i].label, font, text, new RectangleF(i * cell, 66, cell, 24), center);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // Pallino colorato per la riga di stato del menu
    public static Bitmap Dot(Color c, int px)
    {
        var b = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(c);
        var d = px * 0.55f;
        g.FillEllipse(brush, (px - d) / 2, (px - d) / 2, d, d);
        return b;
    }

    static Bitmap Draw(int px, Color top, Color bottom, bool margin)
    {
        var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float m = margin ? px * 0.08f : 0, size = px - 2 * m;
        var box = new RectangleF(m, m, size, size);
        using (var path = Rounded(box, size * 0.22f))
        using (var fill = new LinearGradientBrush(box, top, bottom, LinearGradientMode.Vertical))
            g.FillPath(fill, path);

        PointF P(float x, float y) => new(m + x * size, m + y * size);
        var small = size <= 24;
        using var pen = new Pen(Color.White, Math.Max(1.6f, size * 0.085f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        // angoli della selezione (da piccola solo quelli in basso, se no si confondono con la freccia)
        float l = 0.2f, r = 0.8f, t = 0.52f, b = 0.82f, k = small ? 0.2f : 0.14f;
        if (!small)
        {
            g.DrawLines(pen, [P(l, t + k), P(l, t), P(l + k, t)]);
            g.DrawLines(pen, [P(r - k, t), P(r, t), P(r, t + k)]);
        }
        g.DrawLines(pen, [P(l, b - k), P(l, b), P(l + k, b)]);
        g.DrawLines(pen, [P(r - k, b), P(r, b), P(r, b - k)]);
        // freccia in su
        g.DrawLine(pen, P(0.5f, small ? 0.66f : 0.7f), P(0.5f, 0.16f));
        g.DrawLines(pen, [P(0.31f, 0.35f), P(0.5f, 0.16f), P(0.69f, 0.35f)]);
        return bmp;
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Color Lighter(Color c) => Color.FromArgb(Math.Min(255, c.R + 28), Math.Min(255, c.G + 28), Math.Min(255, c.B + 28));

    // File .ico con le immagini in PNG (supportato da Windows Vista in poi)
    static byte[] Ico(IEnumerable<Bitmap> images)
    {
        var pngs = images.Select(b => { using (b) { var ms = new MemoryStream(); b.Save(ms, ImageFormat.Png); return (b.Width, data: ms.ToArray()); } }).ToList();
        using var o = new MemoryStream();
        using var w = new BinaryWriter(o);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)pngs.Count);
        var offset = 6 + 16 * pngs.Count;
        foreach (var (px, data) in pngs)
        {
            w.Write((byte)(px >= 256 ? 0 : px)); w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in pngs) w.Write(data);
        return o.ToArray();
    }

    static Icon FromIco(byte[] ico, Size size)
    {
        using var ms = new MemoryStream(ico);
        return new Icon(ms, size);
    }
}
