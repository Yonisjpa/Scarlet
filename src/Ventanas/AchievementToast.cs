using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace XboxScarlet;

/// <summary>
/// beta 1.6: los logros estilo Xbox dibujados encima del juego (normal, raro y 100 %). Es la misma animación que la del
/// menú (ui/js/43-logros.js, ACH_FRAMES): cada cuadro se calcula del tiempo, con los mismos segundos y tamaños, y dura
/// lo mismo que su sonido. El sonido lo pone la interfaz. Medidas pensadas a 1080p y escaladas a la pantalla.
/// </summary>
sealed partial class AchievementToast : LayeredWindow
{
    static AchievementToast current;
    /// <summary>¿Hay un logro en pantalla? (las notificaciones esperan a que se vaya)</summary>
    public static bool Busy => current != null;

    readonly string kind, name, game, pos;
    readonly double? percent;
    readonly int total;
    readonly Image icon;
    readonly float k;            // escala (1 = 1080p)
    readonly float D, W;         // disco y barra, en px de 1080p
    readonly double ms;
    const float P = 110;         // margen dentro de la ventana para el halo y los cristales
    const float M = 38;          // separación con la orilla de la pantalla
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = new();
    Bitmap canvas;
    Rectangle screen;

    static Image xbtn;           // el botón Xbox (el mismo de la interfaz)
    static bool xbtnTried;

    AchievementToast(string kind, string name, string game, double? percent, int total, Image icon, string pos)
    {
        this.kind = kind is "rare" or "full" ? kind : "normal";
        this.name = name ?? ""; this.game = game ?? ""; this.percent = percent; this.total = total; this.icon = icon;
        this.pos = string.IsNullOrEmpty(pos) || pos.Length != 2 ? "tl" : pos;
        screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        k = Math.Max(.6f, screen.Height / 1080f);
        (D, W, ms) = this.kind switch { "rare" => (123f, 752f, 6000d), "full" => (122f, 640f, 7800d), _ => (112f, 640f, 2800d) };
        timer.Tick += (_, _) => Frame();
    }

    /// <summary>Muestra un logro (si había otro, se va).</summary>
    public static void Show(string kind, string name, string game, double? percent, int total, string imagePath, string pos,
        bool lumina = false, bool light = false, int unlocked = 0)   // 1.7.2: con Lúmina, el estilo de cristal (AchievementToast.Lumina.cs)
    {
        Image img = null;
        try
        {
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                using var fs = File.OpenRead(imagePath);
                using var src = Image.FromStream(fs);
                img = new Bitmap(src, new Size(128, 128));
            }
        }
        catch (Exception ex) { Log.Error("ícono del logro", ex); }
        try { current?.Finish(); } catch { }
        var t = new AchievementToast(kind, name, game, percent, total, img, pos);
        if (lumina) t.UseLumina(light, unlocked);
        current = t;
        t.Start();
        Log.Write($"logro encima del juego: {t.kind} «{name}» ({game})");
    }

    void Start()
    {
        int w = (int)Math.Ceiling((P * 2 + D / 2 + W) * k), h = (int)Math.Ceiling((P * 2 + D) * k);
        if (lumina) { var ls = LSize(); w = ls.Width; h = ls.Height; }
        canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Size = new Size(w, h);
        Location = Origin();
        base.Show();
        clock.Start();
        Frame();
        timer.Start();
    }

    /// <summary>Esquina de la ventana según la posición elegida en Ajustes.</summary>
    Point Origin()
    {
        if (lumina) return LOrigin();
        float m = M * k, p = P * k;
        float x = pos[1] == 'l' ? screen.Left + m - p : pos[1] == 'r' ? screen.Right - m + p - canvas.Width : screen.Left + (screen.Width - canvas.Width) / 2f;
        float y = pos[0] == 'b' ? screen.Bottom - m - D * k - p : screen.Top + m - p;
        return new Point((int)x, (int)y);
    }

    void Finish()
    {
        timer.Stop();
        if (current == this) current = null;
        try { Close(); } catch { }
        canvas?.Dispose(); icon?.Dispose();
        Dispose();
        if (current == null) ToastForm.Resume();
    }

    void Frame()
    {
        double t = clock.Elapsed.TotalSeconds;
        if (t * 1000 >= ms) { Finish(); return; }
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
                if (lumina) DrawB(g, (float)t); else Draw(g, (float)t);
            }
            var o = Origin();
            Present(canvas, o.X, o.Y);
        }
        catch (Exception ex) { Log.Error("logro (dibujar)", ex); Finish(); }
    }

    // ───── curvas (iguales a las de la interfaz) ─────
    static float Cl(double x, double a = 0, double b = 1) => (float)Math.Min(b, Math.Max(a, x));
    static float Seg(double t, double a, double b) => Cl((t - a) / (b - a));
    static float Eo(double x) => (float)(1 - Math.Pow(1 - x, 3));
    static float Ei(double x) => (float)(x * x * x);
    static float Eio(double x) => (float)(x < .5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2);
    static float Back(double x) { const double c = 1.9; return (float)(1 + (c + 1) * Math.Pow(x - 1, 3) + c * Math.Pow(x - 1, 2)); }

    string Pct() => percent is double p ? (p < 10 ? p.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(p).ToString(CultureInfo.InvariantCulture)) + " %" : "";

    void Draw(Graphics g, float t)
    {
        // estado del cuadro (como ACH_FRAMES)
        float pop, fl, w; float[] tx;
        if (kind == "normal")
        {
            pop = t < 2.6 ? Back(Seg(t, .05, .32)) : 1 - Eo(Seg(t, 2.6, 2.8)); fl = Eio(Seg(t, .4, .7)) - Eio(Seg(t, 2.42, 2.6));
            w = Eo(Seg(t, .6, .95)) - Ei(Seg(t, 2.12, 2.42)); tx = new[] { Seg(t, .8, 1) - Seg(t, 2.05, 2.15) };
        }
        else if (kind == "rare")
        {
            pop = t < 5.6 ? Back(Seg(t, .03, .28)) : 1 - Eo(Seg(t, 5.65, 6.0)); fl = Eio(Seg(t, .83, 1.3)) - Eio(Seg(t, 5.42, 5.62));
            w = Eo(Seg(t, 1.37, 2.03)) - Ei(Seg(t, 4.95, 5.4)); tx = new[] { Seg(t, 1.85, 2.15) - Seg(t, 4.8, 4.95) };
        }
        else
        {
            pop = 1; fl = 0; w = Eo(Seg(t, 1.45, 1.9)) - Ei(Seg(t, 6.4, 6.9));
            tx = new[] { Seg(t, 1.65, 2) - Seg(t, 3.2, 3.4), Seg(t, 3.45, 3.75) - Seg(t, 4.85, 5), Seg(t, 5.05, 5.35) - Seg(t, 6.25, 6.4) };
        }
        w = Cl(w);

        // dónde va el aviso dentro de la ventana: la orilla elegida se queda fija mientras la barra crece
        float vis = Math.Max(D, D / 2 + w * W), cw = canvas.Width / k;
        float x0 = pos[1] == 'l' ? P : pos[1] == 'r' ? cw - P - vis : (cw - vis) / 2;
        float y0 = P;

        if (kind == "full") DrawFullBack(g, t, x0, y0);
        DrawPill(g, t, x0, y0, w, tx);
        if (kind == "full") DrawFullDisc(g, t, x0, y0);
        else DrawFlipDisc(g, t, x0, y0, pop, fl);
    }

    // ───── barra ─────
    void DrawPill(Graphics g, float t, float x0, float y0, float w, float[] tx)
    {
        if (w <= .001f) return;
        float ph = kind == "full" ? 96 : D, py = kind == "full" ? y0 + (D - 96) / 2 : y0, px = x0 + D / 2;
        var r = new RectangleF(px, py, W, ph);
        var st = g.Save();
        g.SetClip(new RectangleF(px, py - 2, W * w, ph + 4));
        using (var path = Capsule(r))
        {
            if (kind == "normal")
            {
                using var b = new LinearGradientBrush(r, Color.FromArgb(30, 30, 30), Color.FromArgb(43, 43, 43), LinearGradientMode.Horizontal);
                g.FillPath(b, path);
                using var pen = new Pen(Color.FromArgb(31, 255, 255, 255), 1);
                g.DrawPath(pen, path);
            }
            else if (kind == "rare")
            {
                using var b = new LinearGradientBrush(r, Color.FromArgb(11, 53, 146), Color.FromArgb(3, 31, 101), LinearGradientMode.Vertical)
                { InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(11, 53, 146), Color.FromArgb(3, 35, 111), Color.FromArgb(3, 31, 101) }, Positions = new[] { 0f, .4f, 1f } } };
                g.FillPath(b, path);
                // brillo del borde de arriba
                g.SetClip(path, CombineMode.Intersect);
                using (var hl = new Pen(Color.FromArgb(140, 150, 180, 255), 2)) g.DrawLine(hl, px + ph / 2, py + 1, px + W - ph / 2, py + 1);
                // los dos destellos que cruzan la barra
                float sh = t < 3.95 ? Seg(t, 2.85, 3.95) : Seg(t, 4.0, 4.9);
                if (sh > 0 && sh < 1)
                {
                    float sx = px - 256 + sh * (W + 320), sw = 256;
                    var sr = new RectangleF(sx, py, sw, ph);
                    using var sb = new LinearGradientBrush(sr, Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal)
                    { InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, 160, 190, 255), Color.FromArgb(90, 160, 190, 255), Color.FromArgb(0, 160, 190, 255) }, Positions = new[] { 0f, .5f, 1f } } };
                    g.FillRectangle(sb, sr);
                }
            }
            else
            {
                using var b = new LinearGradientBrush(r, Color.FromArgb(242, 28, 110, 28), Color.FromArgb(230, 12, 64, 12), LinearGradientMode.Horizontal)
                { InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(242, 28, 110, 28), Color.FromArgb(235, 16, 82, 16), Color.FromArgb(230, 12, 64, 12) }, Positions = new[] { 0f, .6f, 1f } } };
                g.FillPath(b, path);
            }
        }
        g.Restore(st);

        // textos
        float tx0 = x0 + D + 27, tw = W - D / 2 - 60;
        var clip = new RectangleF(px, py, W * w, ph);
        void Two(float a, string top, bool topBold, string bottom, bool bottomBold, float size = 0)
        {
            if (a <= .01f) return;
            float s1 = size > 0 ? size : (topBold ? 26 : 25), s2 = size > 0 ? size : (bottomBold ? 25 : 22);
            using var f1 = new Font(topBold ? "Segoe UI Semibold" : "Segoe UI", s1, FontStyle.Regular, GraphicsUnit.Pixel);
            using var f2 = new Font(bottomBold ? "Segoe UI Semibold" : "Segoe UI", s2, FontStyle.Regular, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using var b1 = new SolidBrush(Color.FromArgb((int)(255 * a), 255, 255, 255));
            using var b2 = new SolidBrush(Color.FromArgb((int)(255 * a * (bottomBold || kind == "full" ? 1 : .88)), 255, 255, 255));
            float h1 = s1 * 1.35f, h2 = s2 * 1.35f, top0 = py + (ph - h1 - h2) / 2;
            var st2 = g.Save(); g.SetClip(clip);
            g.DrawString(top, f1, b1, new RectangleF(tx0, top0, tw, h1), fmt);
            g.DrawString(bottom, f2, b2, new RectangleF(tx0, top0 + h1, tw, h2), fmt);
            g.Restore(st2);
        }
        if (kind == "normal") Two(tx[0], "Logro desbloqueado", true, game.Length > 0 ? $"{name} · {game}" : name, false);
        else if (kind == "rare") Two(tx[0], "Logro raro desbloqueado", true, percent.HasValue ? $"{name} · {Pct()} lo tiene" : name, false);
        else
        {
            Two(tx[0], name, true, percent.HasValue ? $"Último logro · {Pct()} lo tiene" : "Último logro", false);
            Two(tx[1], "Juego completado al 100 %", false, game, true, 25);
            Two(tx[2], $"Desbloqueaste los {total} logros", false, "del juego", false, 25);
        }
    }

    // ───── disco que se voltea (normal y raro) ─────
    void DrawFlipDisc(Graphics g, float t, float x0, float y0, float pop, float fl)
    {
        if (pop <= .001f) return;
        float cx = x0 + D / 2, cy = y0 + D / 2, ang = fl * 180, sx = (float)Math.Cos(ang * Math.PI / 180);
        bool backFace = ang > 90;
        float scaleX = Math.Max(.02f, Math.Abs(sx)) * pop;
        var st = g.Save();
        g.TranslateTransform(cx, cy);
        g.ScaleTransform(scaleX, pop);
        var face = new RectangleF(-D / 2, -D / 2, D, D);
        bool rare = kind == "rare";
        Color fill = rare ? Color.FromArgb(3, 31, 101) : Color.FromArgb(16, 124, 16), ring = rare ? Color.FromArgb(10, 23, 71) : Color.FromArgb(11, 90, 11);
        if (backFace && rare)
        {
            using var path = new GraphicsPath(); path.AddEllipse(face);
            using var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(11, 47, 134), SurroundColors = new[] { Color.FromArgb(3, 26, 85) } };
            g.FillEllipse(pb, face);
        }
        else using (var b = new SolidBrush(fill)) g.FillEllipse(b, face);
        float rw = rare ? 6 : 5;
        using (var rp = new Pen(ring, rw)) g.DrawEllipse(rp, -D / 2 + rw / 2, -D / 2 + rw / 2, D - rw, D - rw);
        if (!backFace) DrawXbox(g, D * (rare ? .64f : .62f));
        else if (rare) DrawGem(g, t, D * .78f);
        else DrawTrophy(g, D * .52f, Color.White);
        g.Restore(st);
    }

    static void DrawXbox(Graphics g, float size)
    {
        if (!xbtnTried)
        {
            xbtnTried = true;
            try { string f = Path.Combine(Paths.Web, "xboxbtn.png"); if (File.Exists(f)) using (var s = File.OpenRead(f)) using (var im = Image.FromStream(s)) xbtn = new Bitmap(im); }
            catch (Exception ex) { Log.Error("botón Xbox (imagen)", ex); }
        }
        if (xbtn == null) { using var p = new Pen(Color.White, size * .08f); g.DrawEllipse(p, -size / 2, -size / 2, size, size); return; }
        // la imagen es una máscara: se pinta de blanco conservando su transparencia
        var cm = new ColorMatrix(new[]
        {
            new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 }
        });
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(cm);
        float ar = (float)xbtn.Width / xbtn.Height, w = ar >= 1 ? size : size * ar, h = ar >= 1 ? size / ar : size;
        g.DrawImage(xbtn, Rectangle.Round(new RectangleF(-w / 2, -h / 2, w, h)), 0, 0, xbtn.Width, xbtn.Height, GraphicsUnit.Pixel, ia);
    }

    static void DrawTrophy(Graphics g, float size, Color c)
    {
        // el mismo trofeo de la interfaz (viewBox 24×24)
        float s = size / 24f;
        var st = g.Save();
        g.TranslateTransform(-size / 2, -size / 2);
        g.ScaleTransform(s, s);
        using var pen = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using (var cup = new GraphicsPath())
        {
            cup.AddLine(8, 4, 16, 4); cup.AddLine(16, 4, 16, 9); cup.AddArc(8, 5, 8, 8, 0, 180); cup.AddLine(8, 9, 8, 4); cup.CloseFigure();
            g.DrawPath(pen, cup);
        }
        g.DrawArc(pen, 5, 6, 6, 4, 90, 180);    // asas
        g.DrawArc(pen, 13, 6, 6, 4, 270, 180);
        g.DrawLine(pen, 12, 13, 12, 17); g.DrawLine(pen, 8, 20, 16, 20); g.DrawLine(pen, 10, 17, 14, 17);
        g.Restore(st);
    }

    // ───── diamante 3D (igual que gemFaces de la interfaz) ─────
    static readonly float[][] GemV;
    static readonly int[][] GemF;
    static AchievementToast()
    {
        const int n = 8; var v = new List<float[]>(); var f = new List<int[]>();
        void Ring(float y, float r, float off) { for (int i = 0; i < n; i++) { double a = (i + off) / n * Math.PI * 2; v.Add(new[] { (float)Math.Cos(a) * r, y, (float)Math.Sin(a) * r }); } }
        Ring(-.42f, .55f, .5f); Ring(0, 1, 0); v.Add(new float[] { 0, 1, 0 });
        int T(int i) => i % n;
        int G(int i) => n + (i % n);
        int C = 2 * n;
        f.Add(Enumerable.Range(0, n).Select(T).Reverse().ToArray());
        for (int i = 0; i < n; i++) { f.Add(new[] { T(i), G(i + 1), G(i) }); f.Add(new[] { T(i), T(i + 1), G(i + 1) }); f.Add(new[] { G(i), G(i + 1), C }); }
        GemV = v.ToArray(); GemF = f.ToArray();
    }

    static void DrawGem(Graphics g, float t, float size)
    {
        double spin = Math.Max(0, t - 1) * 1.3, pitch = -1.45 + 1.1 * Eio(Seg(t, 1.3, 1.75));
        double cs = Math.Cos(spin), sn = Math.Sin(spin), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        double[] L = { -.4, -.8, .45 }; double ln = Math.Sqrt(L[0] * L[0] + L[1] * L[1] + L[2] * L[2]);
        var pts3 = GemV.Select(p =>
        {
            double x1 = p[0] * cs + p[2] * sn, z1 = -p[0] * sn + p[2] * cs;
            return new[] { x1, p[1] * cp - z1 * sp, p[1] * sp + z1 * cp };
        }).ToArray();
        var faces = new List<(PointF[] pts, double z, double b)>();
        foreach (var ix in GemF)
        {
            var q = ix.Select(i => pts3[i]).ToArray();
            double ux = q[1][0] - q[0][0], uy = q[1][1] - q[0][1], uz = q[1][2] - q[0][2], wx = q[2][0] - q[0][0], wy = q[2][1] - q[0][1], wz = q[2][2] - q[0][2];
            double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl == 0) continue;
            if (nz / nl >= 0) continue;   // de espaldas
            double b = Math.Max(0, -(nx * L[0] + ny * L[1] + nz * L[2]) / nl / ln);
            float sc = size / 2.5f;
            faces.Add((q.Select(c => new PointF((float)c[0] * sc, (float)c[1] * sc)).ToArray(), q.Average(c => c[2]), b));
        }
        using var edge = new Pen(Color.FromArgb(179, 235, 245, 255), Math.Max(1, size / 2.5f * .03f)) { LineJoin = LineJoin.Round };
        foreach (var f in faces.OrderByDescending(x => x.z))
        {
            double s = Math.Pow(f.b, 10);
            var col = Color.FromArgb(Math.Min(255, (int)(60 + 170 * f.b + 30 * s)), Math.Min(255, (int)(120 + 120 * f.b + 20 * s)), Math.Min(255, (int)(215 + 40 * f.b)));
            using var br = new SolidBrush(col);
            g.FillPolygon(br, f.pts); g.DrawPolygon(edge, f.pts);
        }
    }

    // ───── 100 %: rayos y halo detrás, disco/emblema/cristales adelante ─────
    void DrawFullBack(Graphics g, float t, float x0, float y0)
    {
        float cx = x0 + D / 2, cy = y0 + D / 2;
        float rays = Seg(t, 2, 2.6) * (1 - Seg(t, 6.2, 6.8));
        if (rays > 0)
        {
            // franjas verticales tenues que se desvanecen hacia las orillas
            float left = x0 - 40, width = W + 200, top = y0 - 120, height = D + 240, fx = left + width * .3f;
            for (float x = left + 38; x < left + width; x += 46)
            {
                float d = Math.Abs(x - fx) / (width * .7f); if (d >= 1) continue;
                int a = (int)(18 * (1 - d) * rays); if (a <= 0) continue;
                var rr = new RectangleF(x, top, 8, height);
                using var lb = new LinearGradientBrush(rr, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical)
                { InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, 220, 235, 220), Color.FromArgb(a, 220, 235, 220), Color.FromArgb(0, 220, 235, 220) }, Positions = new[] { 0f, .5f, 1f } } };
                g.FillRectangle(lb, rr);
            }
        }
        float hp = Seg(t, 1.95, 3), halo = t < 1.95 ? 0 : (hp < .4f ? hp / .4f : 1) * (1 - Seg(t, 2.9, 3.7));
        if (halo > 0)
        {
            float r = 160 * (.6f + .55f * Eo(hp));
            using var path = new GraphicsPath(); path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using var pb = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(cx, cy),
                InterpolationColors = new ColorBlend
                {
                    // posiciones desde la orilla (0) hasta el centro (1): anillo claro entre el 32 % y el 66 % del radio
                    Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(140 * halo), 235, 245, 235), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                    Positions = new[] { 0f, .34f, .58f, .68f, 1f }
                }
            };
            g.FillPath(pb, path);
        }
    }

    void DrawFullDisc(Graphics g, float t, float x0, float y0)
    {
        float cx = x0 + D / 2, cy = y0 + D / 2, out_ = Seg(t, 7.35, 7.8);
        float dot = (t < 6.9 ? .12f + .88f * Eo(Seg(t, 0, .55)) : 1 - .82f * Eio(Seg(t, 6.9, 7.35))) * (1 - out_);
        float dotOp = t < 2 ? 1 - .7f * Seg(t, .8, 1.2) : t < 6.85 ? 0 : 1 - out_;
        if (dot > .001f && dotOp > .01f)
        {
            float r = D / 2 * dot;
            using (var glow = new SolidBrush(Color.FromArgb((int)(60 * dotOp), 40, 200, 40))) g.FillEllipse(glow, cx - r - 10, cy - r - 10, r * 2 + 20, r * 2 + 20);
            using var path = new GraphicsPath(); path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using var pb = new PathGradientBrush(path) { CenterPoint = new PointF(cx, cy), CenterColor = A(Color.FromArgb(47, 181, 47), dotOp), SurroundColors = new[] { A(Color.FromArgb(11, 90, 11), dotOp) } };
            g.FillPath(pb, path);
        }
        float emb = t < .8 ? .35f * Seg(t, .55, .8) : t < 1.95 ? .15f : t < 3.4 ? 0 : t < 7.2 ? 1 : 0;
        float embScale = t < 1.95 ? .8f : t < 6.9 ? 1 : 1 - Eio(Seg(t, 6.9, 7.2));
        float embTurn = t >= 3.4 && t < 3.7 ? (1 - Seg(t, 3.4, 3.7)) * 90 : 0;
        if (emb > .01f && embScale > .01f) DrawEmblem(g, cx, cy, D * embScale, Math.Max(.02f, (float)Math.Cos(embTurn * Math.PI / 180)), emb);
        // el ícono del último logro (1.95–3.55 s), que se voltea para dar paso al emblema
        if (t >= 1.95 && t < 3.55)
        {
            float sc = t < 3.25 ? Back(Seg(t, 1.95, 2.25)) : 1, turn = Seg(t, 3.25, 3.55) * 90, sx = Math.Max(.02f, (float)Math.Cos(turn * Math.PI / 180));
            var st = g.Save();
            g.TranslateTransform(cx, cy); g.ScaleTransform(sx * sc, sc);
            float r = D / 2 - 6;
            using (var ring = new SolidBrush(Color.FromArgb(215, 221, 227))) g.FillEllipse(ring, -r - 4, -r - 4, (r + 4) * 2, (r + 4) * 2);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-r, -r, r * 2, r * 2);
                using (var bg = new SolidBrush(Color.FromArgb(21, 24, 27))) g.FillPath(bg, path);
                if (icon != null) { var s2 = g.Save(); g.SetClip(path, CombineMode.Intersect); g.DrawImage(icon, -r, -r, r * 2, r * 2); g.Restore(s2); }
                else DrawTrophy(g, r * 1.1f, Color.White);
            }
            g.Restore(st);
        }
        // cristales que giran (0.8–2.05 s)
        if (t > .8 && t < 2.05)
        {
            float kk = Seg(t, .8, 1.1), c = Seg(t, 1.8, 2.05), R = 44 * Eo(kk) * (1 - Eio(c)) + 4, u = D / 120f;
            var shard = new[] { new PointF(-14, -6), new PointF(4, -16), new PointF(16, 2), new PointF(-2, 14) };
            var facet = new[] { new PointF(-14, -6), new PointF(4, -16), new PointF(2, 0) };
            using var edge = new Pen(Color.White, 1.2f);
            using var hi = new SolidBrush(Color.FromArgb(153, 255, 255, 255));
            for (int i = 0; i < 11; i++)
            {
                double an = i / 11.0 * Math.PI * 2 + t * 2.2 + (i % 2) * .4, sp = t * (3 + i % 3) + i, fx = Math.Cos(sp);
                float sc = (.6f + .5f * Eo(kk)) * (1 - .6f * c);
                float x = (float)(Math.Cos(an) * R * (i % 3 == 0 ? .7 : 1)), y = (float)(Math.Sin(an) * R * .9);
                int l = (int)(150 + 105 * Math.Abs(fx));
                float sxx = (float)(fx * sc); if (Math.Abs(sxx) < .02f) sxx = sxx < 0 ? -.02f : .02f;
                var st = g.Save();
                g.TranslateTransform(cx, cy); g.ScaleTransform(u, u);
                g.TranslateTransform(x, y); g.RotateTransform((float)(an * 57 + sp * 40)); g.ScaleTransform(sxx, sc);
                using (var b = new SolidBrush(Color.FromArgb(l, l, Math.Min(255, l + 8)))) g.FillPolygon(b, shard);
                g.DrawPolygon(edge, shard);
                g.FillPolygon(hi, facet);
                g.Restore(st);
            }
        }
    }

    /// <summary>Estrella de 4 puntas plateada con laurel (viewBox 100×100), centrada en (cx, cy).</summary>
    static void DrawEmblem(Graphics g, float cx, float cy, float size, float sx, float alpha)
    {
        var st = g.Save();
        g.TranslateTransform(cx, cy); g.ScaleTransform(sx * size / 100f, size / 100f); g.TranslateTransform(-50, -50);
        Color Al(int a, int r, int gg, int b) => Color.FromArgb((int)(a * alpha), r, gg, b);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(3, 3, 94, 94);
            using var pb = new PathGradientBrush(path) { CenterPoint = new PointF(50, 40), CenterColor = Al(255, 58, 63, 68), SurroundColors = new[] { Al(255, 21, 24, 27) } };
            g.FillPath(pb, path);
        }
        using (var ring = new LinearGradientBrush(new RectangleF(0, 0, 100, 100), Al(255, 255, 255, 255), Al(255, 139, 149, 158), LinearGradientMode.ForwardDiagonal))
        using (var rp = new Pen(ring, 4)) g.DrawEllipse(rp, 3, 3, 94, 94);
        // laurel
        using (var leaf = new LinearGradientBrush(new RectangleF(-8, -4, 16, 8), Al(255, 191, 230, 191), Al(255, 63, 143, 63), LinearGradientMode.ForwardDiagonal))
        using (var lp = new Pen(Al(255, 214, 239, 214), .6f))
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 6; i++)
                {
                    double th = (side < 0 ? 100 + i * 17 : 80 - i * 17) * Math.PI / 180;
                    float x = (float)(50 + 36 * Math.Cos(th)), y = (float)(50 + 36 * Math.Sin(th)), rot = (float)(th * 180 / Math.PI + (side < 0 ? 90 : -90) - side * 18);
                    var s2 = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(rot);
                    g.FillEllipse(leaf, -7.5f, -3.2f, 15, 6.4f); g.DrawEllipse(lp, -7.5f, -3.2f, 15, 6.4f);
                    g.Restore(s2);
                }
        // estrella
        var star = new[] { new PointF(50, 14), new PointF(57, 43), new PointF(86, 50), new PointF(57, 57), new PointF(50, 86), new PointF(43, 57), new PointF(14, 50), new PointF(43, 43) };
        using (var sb = new LinearGradientBrush(new RectangleF(14, 14, 72, 72), Al(255, 255, 255, 255), Al(255, 139, 149, 158), LinearGradientMode.ForwardDiagonal))
        {
            sb.InterpolationColors = new ColorBlend { Colors = new[] { Al(255, 255, 255, 255), Al(255, 215, 221, 227), Al(255, 139, 149, 158) }, Positions = new[] { 0f, .55f, 1f } };
            g.FillPolygon(sb, star);
        }
        using (var hi = new SolidBrush(Al(140, 255, 255, 255)))
        {
            g.FillPolygon(hi, new[] { new PointF(50, 14), new PointF(57, 43), new PointF(50, 50) });
            g.FillPolygon(hi, new[] { new PointF(86, 50), new PointF(57, 57), new PointF(50, 50) });
            g.FillPolygon(hi, new[] { new PointF(50, 86), new PointF(43, 57), new PointF(50, 50) });
            g.FillPolygon(hi, new[] { new PointF(14, 50), new PointF(43, 43), new PointF(50, 50) });
        }
        using (var sp = new Pen(Al(255, 255, 255, 255), .8f)) g.DrawPolygon(sp, star);
        g.Restore(st);
    }
}
