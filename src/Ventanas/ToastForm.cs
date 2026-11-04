using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace XboxScarlet;

/// <summary>
/// Notificación arriba a la izquierda que se dibuja encima del juego, igual que las del menú (beta 1.6): una cápsula
/// oscura con el ícono en círculo y un aro del color del tipo de aviso, la etiqueta («EN LÍNEA», «DESCARGA»…), el título,
/// el texto y una línea fina que se va acabando. Entra deslizándose con un rebote y se va encogiéndose. Salen de una en
/// una; las que esperan se asoman detrás con un globito +N. Las capturas llevan su miniatura ancha (con ▶ y duración si
/// es video). No roba el foco ni recibe clics.
/// </summary>
sealed class ToastForm : LayeredWindow
{
    static readonly List<ToastForm> live = new();
    static readonly Queue<ToastForm> waiting = new();
    const double LIFE_MS = 4600, IN_MS = 520, OUT_MS = 300;
    static float K => Math.Max(0.75f, (Screen.PrimaryScreen?.Bounds.Height ?? 1080) / 1080f);

    readonly string title, text, letter, dur, tag;
    readonly Color badge, accent;
    readonly Image image;
    readonly bool wide, video, saving;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 15 };
    readonly Stopwatch clock = new();
    double outAt = -1;
    bool closing;
    Bitmap canvas;
    // medidas a 1080p: la cápsula y el margen alrededor (para el rebote, la sombra y las cartas de atrás)
    const float H = 92, PAD = 40;
    readonly float bodyW;

    ToastForm(string title, string text, string letter, Color badge, Image image, bool wide, bool video, string dur, bool saving, string tag, Color accent)
    {
        this.title = title ?? ""; this.text = text ?? ""; this.letter = letter ?? ""; this.badge = badge; this.image = image;
        this.wide = wide; this.video = video; this.dur = dur ?? ""; this.saving = saving; this.tag = (tag ?? "").ToUpperInvariant(); this.accent = accent;
        bodyW = Measure();
        anim.Tick += (_, _) => Step();
    }

    /// <summary>Ancho de la cápsula: lo que pidan los textos, entre 520 y 760 px (a 1080p).</summary>
    float Measure()
    {
        try
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            using var tf = new Font("Segoe UI Semibold", 22, FontStyle.Regular, GraphicsUnit.Pixel);
            using var sf = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            float textW = Math.Max(g.MeasureString(title, tf).Width, g.MeasureString(text, sf).Width);
            float iconW = wide ? 138 : 74;
            return Math.Clamp(14 + iconW + 20 + textW + 36, 520, 760);
        }
        catch { return 600; }
    }

    void Start()
    {
        float k = K;
        int w = (int)Math.Ceiling((bodyW + PAD * 2 + 40) * k), h = (int)Math.Ceiling((H + PAD * 2 + 50) * k);
        canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Size = new Size(w, h);
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(b.Left + (int)((38 - PAD) * k), b.Top + (int)((36 - PAD) * k));
        Show();
        clock.Restart();
        Render();
        anim.Start();
    }

    static float Eo(double x) => (float)(1 - Math.Pow(1 - Math.Clamp(x, 0, 1), 3));

    void Step()
    {
        double age = clock.Elapsed.TotalMilliseconds;
        if (!closing && age > (saving ? 30000 : LIFE_MS)) closing = true;
        if (closing && outAt < 0) outAt = age;
        if (outAt >= 0 && age - outAt >= OUT_MS) { Kill(); return; }
        Render();
    }

    void Render()
    {
        if (canvas == null) return;
        double age = clock.Elapsed.TotalMilliseconds;
        float k = K;
        // entrada: desde la izquierda con un rebote (como nIn2); salida: sube un poco y se encoge
        double pin = Math.Clamp(age / IN_MS, 0, 1);
        float dx, sc = 1, op = 1;
        if (pin < .65) { double q = Eo(pin / .65); dx = (float)(-1.1 * bodyW * (1 - q) + .03 * bodyW * q); sc = (float)(.92 + .1 * q); op = (float)Math.Min(1, q * 1.4); }
        else { double q = Eo((pin - .65) / .35); dx = (float)(.03 * bodyW * (1 - q)); sc = (float)(1.02 - .02 * q); }
        float dy = 0;
        if (outAt >= 0) { double po = Math.Clamp((age - outAt) / OUT_MS, 0, 1); dy = (float)(-14 * po); sc *= (float)(1 - .14 * po); op *= (float)(1 - po); }
        double life = saving ? 1 : Math.Max(0, 1 - age / LIFE_MS);
        try
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(k, k);
                // las que esperan, asomándose detrás
                int n = Math.Min(2, waiting.Count);
                for (int i = n; i >= 1 && outAt < 0; i--)
                {
                    var st0 = g.Save();
                    g.TranslateTransform(PAD + 18 * i, PAD + 26 * i); g.ScaleTransform(1 - .06f * i, 1 - .06f * i);
                    using (var path = Shape(new RectangleF(0, 0, bodyW, H)))
                    using (var bb = new SolidBrush(Color.FromArgb((int)(237 * op), (int)(14 * (i == 1 ? .55 : .35)), (int)(14 * (i == 1 ? .55 : .35)), (int)(18 * (i == 1 ? .55 : .35)))))
                        g.FillPath(bb, path);
                    g.Restore(st0);
                }
                var st = g.Save();
                g.TranslateTransform(PAD + dx, PAD + dy);
                g.ScaleTransform(sc, sc);
                DrawBody(g, (float)life, op);
                g.Restore(st);
                if (waiting.Count > 0 && outAt < 0)
                {
                    string more = "+" + waiting.Count;
                    using var mf = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
                    var ms = g.MeasureString(more, mf);
                    var mr = new RectangleF(PAD + dx + bodyW - ms.Width + 4, PAD - 10, ms.Width + 16, 26);
                    using (var mp = Shape(mr)) using (var mb = new SolidBrush(Color.FromArgb((int)(255 * op), 230, 0, 18))) g.FillPath(mb, mp);
                    using var mt = new SolidBrush(Color.FromArgb((int)(255 * op), 255, 255, 255));
                    using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(more, mf, mt, mr, fmt);
                }
            }
            Present(canvas, Location.X, Location.Y);
        }
        catch (Exception ex) { Log.Error("notificación (dibujar)", ex); Kill(); }
    }

    /// <summary>Cápsula (o, con miniatura ancha, rectángulo muy redondeado).</summary>
    GraphicsPath Shape(RectangleF r)
    {
        if (!wide) return Capsule(r);
        float d = 60; var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    void DrawBody(Graphics g, float life, float op)
    {
        var body = new RectangleF(0, 0, bodyW, H);
        using (var shadow = Shape(new RectangleF(4, 10, bodyW - 8, H)))
        using (var sb = new SolidBrush(Color.FromArgb((int)(70 * op), 0, 0, 0))) g.FillPath(sb, shadow);
        using (var path = Shape(body))
        {
            using (var b = new SolidBrush(Color.FromArgb((int)(237 * op), 14, 14, 18))) g.FillPath(b, path);
            using (var pen = new Pen(Color.FromArgb((int)(36 * op), 255, 255, 255), 1)) g.DrawPath(pen, path);
        }
        float pad = 9, size = H - pad * 2;
        var icon = wide ? new RectangleF(pad + 4, pad + 4, (size - 8) * 16 / 9f, size - 8) : new RectangleF(pad + 4, pad + 4, size - 8, size - 8);
        if (wide)
        {
            using var ip = RoundRect(icon, 16);
            var s0 = g.Save(); g.SetClip(ip, CombineMode.Intersect);
            if (image != null) g.DrawImage(image, Cover(image.Size, icon)); else using (var bk = new SolidBrush(Color.FromArgb((int)(255 * op), 0, 0, 0))) g.FillRectangle(bk, icon);
            if (saving) using (var dim = new SolidBrush(Color.FromArgb((int)(110 * op), 0, 0, 0))) g.FillRectangle(dim, icon);
            g.Restore(s0);
            if (video && !saving)
            {
                float r = 12, cx = icon.X + icon.Width / 2, cy = icon.Y + icon.Height / 2;
                using (var c = new SolidBrush(Color.FromArgb((int)(150 * op), 0, 0, 0))) g.FillEllipse(c, cx - r, cy - r, r * 2, r * 2);
                using (var wb = new SolidBrush(Color.FromArgb((int)(255 * op), 255, 255, 255)))
                    g.FillPolygon(wb, new[] { new PointF(cx - 4, cy - 6), new PointF(cx - 4, cy + 6), new PointF(cx + 7, cy) });
                if (dur.Length > 0)
                {
                    using var df = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
                    var ds = g.MeasureString(dur, df);
                    var dr = new RectangleF(icon.Right - ds.Width - 6, icon.Bottom - ds.Height - 4, ds.Width + 2, ds.Height);
                    using (var db = new SolidBrush(Color.FromArgb((int)(160 * op), 0, 0, 0))) g.FillRectangle(db, dr);
                    using var wt = new SolidBrush(Color.FromArgb((int)(255 * op), 255, 255, 255));
                    g.DrawString(dur, df, wt, dr.X + 1, dr.Y);
                }
            }
            if (saving)
            {
                float bx = icon.X + 7, bw = icon.Width - 14, by = icon.Bottom - 9, bh = 4;
                using (var bb = new SolidBrush(Color.FromArgb((int)(80 * op), 255, 255, 255))) g.FillRectangle(bb, bx, by, bw, bh);
                double ph = (clock.Elapsed.TotalMilliseconds % 1200) / 1200.0;
                float sx = bx + (float)(ph * 1.4 - 0.4) * bw, sw = bw * 0.4f;
                var seg = RectangleF.Intersect(new RectangleF(sx, by, sw, bh), new RectangleF(bx, by, bw, bh));
                using (var ab = new SolidBrush(Color.FromArgb((int)(255 * op), 34, 214, 227))) if (seg.Width > 0) g.FillRectangle(ab, seg);
            }
        }
        else
        {
            // círculo con el aro del color del aviso
            using (var ring = new Pen(A(accent, op), 4)) g.DrawEllipse(ring, icon.X - 2, icon.Y - 2, icon.Width + 4, icon.Height + 4);
            using var cp = new GraphicsPath(); cp.AddEllipse(icon);
            if (image != null)
            {
                var s0 = g.Save(); g.SetClip(cp, CombineMode.Intersect);
                g.DrawImage(image, Cover(image.Size, icon)); g.Restore(s0);
            }
            else
            {
                using (var b = new SolidBrush(A(badge, op))) g.FillPath(b, cp);
                using var f = new Font("Segoe UI", 28, FontStyle.Bold, GraphicsUnit.Pixel);
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var dark = badge.GetBrightness() > 0.75f && badge.R > 200 && badge.G > 200;
                using var fg = new SolidBrush(A(dark ? Color.FromArgb(17, 17, 17) : Color.White, op));
                g.DrawString(letter, f, fg, icon, fmt);
            }
        }

        float x = icon.Right + 18, w = bodyW - x - 30;
        using var tgf = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tf = new Font("Segoe UI Semibold", 22, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        using var one = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var white = new SolidBrush(A(Color.White, op));
        using var sub = new SolidBrush(Color.FromArgb((int)(205 * op), 255, 255, 255));
        using var tb = new SolidBrush(A(accent, op));
        float y = tag.Length > 0 ? 12 : 18;
        if (tag.Length > 0) { g.DrawString(tag, tgf, tb, new RectangleF(x, y, w, 18), one); y += 17; }
        g.DrawString(title, tf, white, new RectangleF(x, y, w, 30), one);
        g.DrawString(text, sf, sub, new RectangleF(x, y + 29, w, 26), one);
        if (!saving && life > 0)
            using (var lb = new SolidBrush(A(accent, op)))
            using (var lp = Capsule(new RectangleF(30, H - 8, (bodyW - 60) * life, 4)))
                g.FillPath(lb, lp);   // la línea que se va acabando
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2; var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    static RectangleF Cover(Size img, RectangleF box)
    {
        double s = Math.Max(box.Width / img.Width, box.Height / img.Height);
        float w = (float)(img.Width * s), h = (float)(img.Height * s);
        return new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    void Kill()
    {
        anim.Stop();
        live.Remove(this);
        try { Close(); } catch { }
        image?.Dispose(); canvas?.Dispose(); canvas = null;
        Dispose();
        ShowNext();
    }

    /// <summary>Una a la vez: cuando se va la de ahora, sale la siguiente.</summary>
    static void ShowNext()
    {
        if (live.Count > 0 || waiting.Count == 0 || AchievementToast.Busy) return;   // con un logro en pantalla, esperan
        var t = waiting.Dequeue();
        live.Add(t);
        t.Start();
    }

    /// <summary>Se fue el logro: si había avisos esperando, sale el siguiente.</summary>
    public static void Resume() => ShowNext();

    /// <summary>Muestra una notificación. Debe llamarse en el hilo de la ventana principal.</summary>
    public static void Pop(string title, string text, string letter, string color, string imagePath,
        bool wide = false, bool video = false, string dur = null, bool saving = false, string tag = null, string accent = null)
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
        var t = new ToastForm(title, text, string.IsNullOrEmpty(letter) ? "•" : letter, ParseColor(color), img, wide, video, dur, saving,
            tag, string.IsNullOrEmpty(accent) ? Color.FromArgb(34, 214, 227) : ParseColor(accent));
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
