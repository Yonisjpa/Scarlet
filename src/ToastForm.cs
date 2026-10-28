using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace XboxScarlet;

/// <summary>
/// Notificación arriba a la izquierda (fondo negro, borde blanco, letras blancas) que se dibuja encima del juego.
/// Igual que las del menú (beta 1.0): salen de una en una, con una línea abajo que se va acabando; las capturas llevan
/// su miniatura ancha (con ▶ y duración si es video). No roba el foco ni recibe clics.
/// </summary>
sealed class ToastForm : Form
{
    static readonly List<ToastForm> live = new();
    static readonly Queue<ToastForm> waiting = new();
    const double LIFE_MS = 4600;
    static float K => Math.Max(0.75f, (Screen.PrimaryScreen?.Bounds.Height ?? 1080) / 1080f) * 1.1f;

    readonly string title, text, letter, dur;
    readonly Color badge;
    readonly Image image;
    readonly bool wide, video, saving;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 16 };
    DateTime born = DateTime.Now;
    bool closing;
    double life = 1;

    ToastForm(string title, string text, string letter, Color badge, Image image, bool wide, bool video, string dur, bool saving)
    {
        this.title = title ?? ""; this.text = text ?? ""; this.letter = letter ?? ""; this.badge = badge; this.image = image;
        this.wide = wide; this.video = video; this.dur = dur ?? ""; this.saving = saving;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(10, 10, 12);
        DoubleBuffered = true;
        Opacity = 0;
        Size = new Size((int)((wide ? 480 : 440) * K), (int)(84 * K));
        anim.Tick += (_, _) => Step();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */ | 0x00000008 /* TOPMOST */ | 0x00000020 /* TRANSPARENT */;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        using var path = Round(new Rectangle(0, 0, Width, Height), (int)(14 * K));
        Region = new Region(path);
    }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float k = K;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var border = Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)(14 * k)))
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, k)))
            g.DrawPath(pen, border);

        int pad = (int)(12 * k), size = Height - pad * 2;
        var icon = wide ? new Rectangle(pad, pad, (int)(size * 16 / 9.0), size) : new Rectangle(pad, pad, size, size);
        using (var ip = Round(icon, (int)(9 * k)))
        {
            if (image != null || wide)
            {
                g.SetClip(ip);
                if (image != null) g.DrawImage(image, Cover(image.Size, icon)); else g.FillRectangle(Brushes.Black, icon);
                if (saving) using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillRectangle(dim, icon);
                g.ResetClip();
                if (video && !saving)
                {
                    // ▶ al centro y la duración abajo a la derecha
                    float r = 11 * k, cx = icon.X + icon.Width / 2f, cy = icon.Y + icon.Height / 2f;
                    using (var c = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillEllipse(c, cx - r, cy - r, r * 2, r * 2);
                    g.FillPolygon(Brushes.White, new[] { new PointF(cx - 3.5f * k, cy - 5.5f * k), new PointF(cx - 3.5f * k, cy + 5.5f * k), new PointF(cx + 6 * k, cy) });
                    if (dur.Length > 0)
                    {
                        using var df = new Font("Segoe UI", 10.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
                        var ds = g.MeasureString(dur, df);
                        var dr = new RectangleF(icon.Right - ds.Width - 5 * k, icon.Bottom - ds.Height - 3 * k, ds.Width + 2 * k, ds.Height);
                        using (var db = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillRectangle(db, dr);
                        g.DrawString(dur, df, Brushes.White, dr.X + 1 * k, dr.Y);
                    }
                }
                if (saving)
                {
                    // barrita que va y viene: todavía se está guardando
                    float bx = icon.X + 6 * k, bw = icon.Width - 12 * k, by = icon.Bottom - 8 * k, bh = 4 * k;
                    using (var bb = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) g.FillRectangle(bb, bx, by, bw, bh);
                    double ph = ((DateTime.Now - born).TotalMilliseconds % 1200) / 1200.0;
                    float sx = bx + (float)(ph * 1.4 - 0.4) * bw, sw = bw * 0.4f;
                    var seg = RectangleF.Intersect(new RectangleF(sx, by, sw, bh), new RectangleF(bx, by, bw, bh));
                    using (var ab = new SolidBrush(Color.FromArgb(34, 214, 227))) if (seg.Width > 0) g.FillRectangle(ab, seg);
                }
            }
            else
            {
                using var b = new SolidBrush(badge);
                g.FillPath(b, ip);
                using var f = new Font("Segoe UI", 24 * k, FontStyle.Bold, GraphicsUnit.Pixel);
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var dark = badge.GetBrightness() > 0.75f && badge.R > 200 && badge.G > 200;
                using var fg = new SolidBrush(dark ? Color.FromArgb(17, 17, 17) : Color.White);
                g.DrawString(letter, f, fg, icon, fmt);
            }
        }

        float x = icon.Right + 14 * k, w = Width - x - pad;
        using var tf = new Font("Segoe UI Semibold", 19 * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new Font("Segoe UI", 16 * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var one = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var sub = new SolidBrush(Color.FromArgb(205, 255, 255, 255));
        g.DrawString(title, tf, Brushes.White, new RectangleF(x, pad + 3 * k, w, 26 * k), one);
        g.DrawString(text, sf, sub, new RectangleF(x, pad + 31 * k, w, 24 * k), one);
        if (!saving)
            using (var lb = new SolidBrush(Color.FromArgb(34, 214, 227)))
                g.FillRectangle(lb, 0, Height - 3 * k, (float)(Width * life), 3 * k);   // la línea que se va acabando
    }

    static Rectangle Cover(Size img, Rectangle box)
    {
        double s = Math.Max((double)box.Width / img.Width, (double)box.Height / img.Height);
        int w = (int)(img.Width * s), h = (int)(img.Height * s);
        return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    void Step()
    {
        double age = (DateTime.Now - born).TotalMilliseconds;
        if (!closing)
        {
            Opacity = Math.Min(1, age / 220);
            life = Math.Max(0, 1 - age / LIFE_MS);
            Invalidate();
            if (age > (saving ? 30000 : LIFE_MS)) closing = true;
        }
        else
        {
            Opacity = Math.Max(0, Opacity - 0.08);
            if (Opacity <= 0.02) Kill();
        }
    }

    void Kill()
    {
        anim.Stop();
        live.Remove(this);
        Close();
        image?.Dispose();
        Dispose();
        ShowNext();
    }

    /// <summary>Una a la vez: cuando se va la de ahora, sale la siguiente.</summary>
    static void ShowNext()
    {
        if (live.Count > 0 || waiting.Count == 0) return;
        var t = waiting.Dequeue();
        t.born = DateTime.Now;
        live.Add(t);
        Arrange();
        t.Show();
        t.anim.Start();
    }

    static void Arrange()
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int m = (int)(22 * K), gap = (int)(10 * K), y = b.Top + m;
        foreach (var t in live)
        {
            t.Location = new Point(b.Left + m, y);
            y += t.Height + gap;
        }
    }

    /// <summary>Muestra una notificación. Debe llamarse en el hilo de la ventana principal.</summary>
    public static void Pop(string title, string text, string letter, string color, string imagePath,
        bool wide = false, bool video = false, string dur = null, bool saving = false)
    {
        Image img = null;
        try
        {
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                using var fs = File.OpenRead(imagePath);
                using var src = Image.FromStream(fs);
                int w = wide ? 320 : 160, h = Math.Max(1, (int)((double)w * src.Height / src.Width));
                img = new Bitmap(src, new Size(w, h));
            }
        }
        catch (Exception ex) { Log.Error("miniatura", ex); img = null; }

        // "Guardando video…" → "Video guardado": la nueva reemplaza a la que sigue en pantalla.
        if (!saving && video) foreach (var s in live.Where(x => x.saving).ToList()) s.closing = true;
        var t = new ToastForm(title, text, string.IsNullOrEmpty(letter) ? "•" : letter, ParseColor(color), img, wide, video, dur, saving);
        waiting.Enqueue(t);
        while (waiting.Count > 5) waiting.Dequeue().Dispose();
        ShowNext();
    }

    /// <summary>Acepta "#rrggbb" y "hsl(h, s%, l%)" (los que usa la interfaz).</summary>
    static Color ParseColor(string c)
    {
        var fallback = Color.White;
        if (string.IsNullOrWhiteSpace(c)) return fallback;
        c = c.Trim();
        try
        {
            if (c.StartsWith("#") && c.Length == 7) return ColorTranslator.FromHtml(c);
            var m = Regex.Match(c, @"hsl\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                double h = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 360.0;
                double s = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0;
                double l = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 100.0;
                double q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                int Ch(double t)
                {
                    if (t < 0) t += 1;
                    if (t > 1) t -= 1;
                    double v = t < 1.0 / 6 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
                    return (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
                }
                return Color.FromArgb(Ch(h + 1.0 / 3), Ch(h), Ch(h - 1.0 / 3));
            }
        }
        catch { }
        return fallback;
    }
}
