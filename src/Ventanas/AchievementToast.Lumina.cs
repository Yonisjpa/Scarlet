using System.Drawing.Drawing2D;

namespace XboxScarlet;

/// <summary>
/// Beta 1.7.2: los logros con Lúmina, dibujados encima del juego. Es la copia en GDI+ de achLPaint (45-logros-lumina.js):
/// aviso de cristal con el ícono del logro en el bloque, tira de color y, en el raro y el 100 %, su animación completa
/// (diamante que gira y porcentaje que baja; anillo de progreso y contador que sube; golpe, ondas, chispas, brasas).
/// Medidas a 1080p (1 rem = 10 px).
/// </summary>
partial class AchievementToast
{
    bool lumina, light;
    int unlocked;
    float bw;                                   // ancho del aviso
    const float HB = 104, RB = 24, BLK = 104;   // alto, radio y bloque de la izquierda
    const float PB = 160;                       // margen para el brillo, las ondas y las partículas

    sealed record Part(bool Spark, bool Ember, double A, double Dd, double S, double R, double T0, double C, double X, double Life, double H, double Dx, bool Top);
    List<Part> parts;

    static readonly Dictionary<string, (Color a, Color b, Color c, Color tag, Color glow, double hit, double exit)> LK = new()
    {
        ["normal"] = (Color.FromArgb(217, 154, 0), Color.FromArgb(255, 201, 60), Color.FromArgb(255, 241, 168), Color.FromArgb(224, 168, 0), Color.FromArgb(255, 190, 40), .3, 2.45),
        ["rare"] = (Color.FromArgb(10, 43, 255), Color.FromArgb(0, 168, 255), Color.FromArgb(143, 227, 255), Color.FromArgb(47, 157, 255), Color.FromArgb(0, 140, 255), 2.4, 5.6),
        ["full"] = (Color.FromArgb(255, 26, 26), Color.FromArgb(255, 122, 0), Color.FromArgb(255, 212, 0), Color.FromArgb(255, 106, 26), Color.FromArgb(255, 110, 0), 2.6, 7.2),
    };
    const double HIT2 = 3.46;

    /// <summary>Prepara el estilo Lúmina (lo llama Show antes de Start).</summary>
    void UseLumina(bool light, int unlocked)
    {
        lumina = true; this.light = light; this.unlocked = unlocked;
        var (tag, title, sub, _) = LTexts();
        try
        {
            using var bmp = new Bitmap(1, 1); using var g = Graphics.FromImage(bmp);
            using var tf = new Font("Segoe UI Semibold", 23, FontStyle.Regular, GraphicsUnit.Pixel);
            using var sf = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            float tw = Math.Max(g.MeasureString(title, tf).Width, g.MeasureString(sub, sf).Width);
            bw = Math.Clamp(BLK + 20 + tw + (kind == "normal" ? 30 : 150), 520, 820);
        }
        catch { bw = 640; }
        // partículas: el mismo generador que la interfaz
        parts = new();
        long s = kind == "rare" ? 7 : 23; double Rnd() { s = s * 16807 % 2147483647; return s / 2147483647.0; }
        if (kind == "rare") for (int i = 0; i < 14; i++) parts.Add(new(false, false, i / 14.0 * Math.PI * 2 + Rnd() * .3, 13 + Rnd() * 14, .7 + Rnd() * .9, Rnd() * 360, 2.4 + Rnd() * .06, 0, 0, 0, 0, 0, false));
        if (kind == "full")
        {
            for (int i = 0; i < 34; i++) { double a = Rnd() * Math.PI * 2, d = 16 + Rnd() * 26, sz = .5 + Rnd() * .9, t0 = 2.6 + Rnd() * .05, c = Rnd(); parts.Add(new(true, false, a, d, sz, 0, t0, c, 0, 0, 0, 0, false)); }
            for (int i = 0; i < 40; i++) { double x = Rnd(), t0 = .9 + Rnd() * 5.6, life = 1.1 + Rnd() * 1.1, h = 6 + Rnd() * 10, dx = (Rnd() - .5) * 5, sz = .35 + Rnd() * .55, c = Rnd(); bool top = Rnd() < .5; parts.Add(new(false, true, 0, 0, sz, 0, t0, c, x, life, h, dx, top)); }
        }
    }

    (string tag, string title, string sub, (string tag, string title, string sub)? pre) LTexts()
    {
        string pct = Pct();
        if (kind == "rare") return ("LOGRO RARO", name, string.Join(" · ", new[] { game, pct.Length > 0 ? $"lo tiene el {pct} de los jugadores" : "" }.Where(x => x.Length > 0)), null);
        if (kind == "full") return ("JUEGO COMPLETADO", game.Length > 0 ? $"{game} al 100 %" : "Juego al 100 %", $"Último logro: {name}{(pct.Length > 0 ? " · " + pct : "")}", ("LOGROS", game.Length > 0 ? game : "Juego", "Completando el juego…"));
        return ("LOGRO", name, string.Join(" · ", new[] { game, total > 0 ? $"{Math.Min(unlocked, total)} de {total}" : "" }.Where(x => x.Length > 0)), null);
    }

    Size LSize() => new((int)Math.Ceiling((bw + PB * 2) * k), (int)Math.Ceiling((HB + PB * 2) * k));

    Point LOrigin()
    {
        float m = M * k, p = PB * k;
        float x = pos[1] == 'l' ? screen.Left + m - p : pos[1] == 'r' ? screen.Right - m + p - canvas.Width : screen.Left + (screen.Width - canvas.Width) / 2f;
        float y = pos[0] == 'b' ? screen.Bottom - m - HB * k - p : screen.Top + m - p;
        return new Point((int)x, (int)y);
    }

    static Color Al(Color c, double a) => Color.FromArgb(Math.Clamp((int)Math.Round(255 * a), 0, 255), c.R, c.G, c.B);
    static GraphicsPath RR(RectangleF r, float rad)
    {
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height)); var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    static string PctOf(double p) => (p < 10 ? p.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(p).ToString(CultureInfo.InvariantCulture)) + " %";

    void DrawB(Graphics g, float tf)
    {
        double t = tf;
        var L = LK[kind]; double hit = L.hit, dur = ms / 1000.0;
        Color fg = light ? Color.FromArgb(22, 22, 26) : Color.White;
        // ── entrada / golpe / salida ──
        int side = pos[1] == 'r' ? 1 : pos[1] == 'c' ? 0 : -1;
        double pin = Seg(t, 0, kind == "normal" ? .45 : .5);
        double x = pin < 1 ? side * (1.1 - 1.1 * Back(pin)) * bw : 0, y = 0, sc = side == 0 && pin < 1 ? .6 + .4 * Back(pin) : 1, op = Cl(pin * 2.2);
        double pu = Seg(t, hit, hit + (kind == "normal" ? .35 : .4));
        if (t >= hit && pu < 1) sc *= 1 + (kind == "normal" ? .035 : kind == "rare" ? .06 : .1) * Math.Sin(pu * Math.PI) * (1 - pu * .4);
        if (kind == "full")
        {
            double p2 = Seg(t, HIT2, HIT2 + .34); if (t >= HIT2 && p2 < 1) sc *= 1 + .045 * Math.Sin(p2 * Math.PI);
            double sh = Seg(t, 1.5, hit); if (t < hit && sh > 0) { x += Math.Sin(t * 91) * sh * .005 * bw; y += Math.Cos(t * 77) * sh * 2.8; }
        }
        double po = Seg(t, L.exit, dur); if (po > 0) { y += (pos[0] == 'b' ? 12 : -12) * po; sc *= 1 - .14 * po; op *= 1 - po; }
        if (op <= .002) return;
        float ox = pos[1] == 'l' ? PB : pos[1] == 'r' ? PB + bw : PB + bw / 2, oy = pos[0] == 'b' ? PB + HB : PB;
        var st0 = g.Save();
        g.TranslateTransform((float)(ox + x), (float)(oy + y)); g.ScaleTransform((float)sc, (float)sc); g.TranslateTransform(-(ox - PB), -(oy - PB));
        g.TranslateTransform(PB, PB);
        // a partir de aquí: el aviso en (0,0)–(bw,HB)
        var body = new RectangleF(0, 0, bw, HB);
        double At(double t0, double r) => t >= t0 ? Math.Exp(-(t - t0) * r) : 0;
        double pulse = kind == "normal" ? At(hit, 3) : Math.Max(Math.Max(kind == "rare" ? At(.45, 2.2) : 0, At(hit, 1.6)), kind == "full" ? At(HIT2, 2.4) * .8 : 0);
        double baseG = kind == "normal" ? .12 : t >= hit ? .45 + .12 * Math.Sin(t * 5) : .2 + (kind == "full" ? Seg(t, .5, hit) * .5 : 0);
        double glowA = Cl(baseG + pulse * (kind == "normal" ? .55 : .6)) * op, glowR = (kind == "normal" ? 20 + 30 * pulse : 30 + 50 * pulse);
        // brillo alrededor
        for (int i = 5; i >= 1; i--)
        {
            float gr = (float)(glowR * i / 5);
            using var gp = RR(new RectangleF(-gr, -gr, bw + gr * 2, HB + gr * 2), RB + gr);
            using var gb = new SolidBrush(Al(L.glow, glowA * .16 * (1 - (i - 1) / 5.0)));
            g.FillPath(gb, gp);
        }
        // sombra
        using (var shp = RR(new RectangleF(4, 12, bw - 8, HB), RB)) using (var sb = new SolidBrush(Color.FromArgb((int)((light ? 40 : 80) * op), 0, 0, 0))) g.FillPath(sb, shp);
        using var path = RR(body, RB);
        var st1 = g.Save(); g.SetClip(path, CombineMode.Intersect);
        using (var bg = new SolidBrush(light ? Color.FromArgb((int)(236 * op), 248, 248, 250) : Color.FromArgb((int)(232 * op), 40, 40, 44))) g.FillRectangle(bg, body);
        using (var hi = new LinearGradientBrush(new RectangleF(0, 0, bw, HB * .45f + 1), Color.FromArgb((int)((light ? 150 : 34) * op), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillRectangle(hi, 0, 0, bw, HB * .45f);
        // ── bloque ──
        var blk = new RectangleF(0, 0, BLK, HB);
        using (var bb = new LinearGradientBrush(new RectangleF(0, 0, BLK + 1, HB + 1), Al(L.a, op), Al(L.c, op), 60f)) g.FillRectangle(bb, blk);
        double angle = 0, frontScale = 1, frontOp = 1, prog = 0; double? pct = null; double pop = 1;
        if (kind == "normal") { double p = Seg(t, hit, hit + .4); angle = 180; pop = t < hit ? .82 + .18 * Seg(t, 0, hit) : 1 + .2 * Math.Sin(p * Math.PI) * (1 - p * .5); }
        else if (kind == "rare")
        {
            double app = Seg(t, .45, .78), spin = Eio(Seg(t, .6, 2.3)) * 720, flip = Eo(Seg(t, hit - .06, hit + .22));
            frontOp = Cl(app * 4); frontScale = app < 1 ? 2.6 - 1.6 * Back(app) : 1; angle = spin + flip * 180; pop = 1 + .14 * Math.Sin(Seg(t, hit + .05, hit + .45) * Math.PI);
            prog = Seg(t, .62, 2.22); pct = percent is double pr ? (prog >= 1 ? pr : 100 - (100 - pr) * (1 - Math.Pow(1 - prog, 2.2))) : null;
        }
        else { prog = Math.Pow(Seg(t, .55, hit), 1.6); angle = Eo(Seg(t, hit, hit + .28)) * 180; pop = 1 + .16 * Math.Sin(Seg(t, hit + .05, hit + .5) * Math.PI); pct = Math.Round(100 * prog); }
        double cosA = Math.Cos(angle * Math.PI / 180); bool backFace = cosA < 0; float sx = (float)(Math.Abs(cosA) * pop), sy = (float)pop;
        float cx = BLK / 2, cy = HB / 2;
        var st2 = g.Save(); g.SetClip(blk, CombineMode.Intersect);
        g.TranslateTransform(cx, cy); g.ScaleTransform(Math.Max(.001f, sx), sy); g.TranslateTransform(-cx, -cy);
        if (backFace)
        {
            if (icon != null) g.DrawImage(icon, new RectangleF(0, 0, BLK, HB));
            else
            {
                using var ib = new LinearGradientBrush(new RectangleF(0, 0, BLK + 1, HB + 1), Al(L.a, op), Al(L.c, op), 60f); g.FillRectangle(ib, blk);
                DrawTrophy(g, cx, cy, 30, Al(Color.White, op));
            }
        }
        else if (kind == "rare")
        {
            var s3 = g.Save(); g.TranslateTransform(cx, cy); g.ScaleTransform((float)frontScale, (float)frontScale);
            DrawGemIcon(g, 29, Al(Color.White, op * frontOp)); g.Restore(s3);
        }
        else if (kind == "full")
        {
            float r = 24 * BLK / 64f * .78f; var rc = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var tr = new Pen(Al(Color.White, .22 * op), 6 * BLK / 64f * .78f)) g.DrawEllipse(tr, rc);
            if (prog > 0) using (var pr = new Pen(Al(Color.White, op), 6 * BLK / 64f * .78f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pr, rc, -90, (float)(360 * prog));
        }
        g.Restore(st2);
        using (var sep = new Pen(Color.FromArgb((int)(60 * op), 255, 255, 255), 1)) g.DrawLine(sep, BLK, 0, BLK, HB);
        // diamantito (raro)
        if (kind == "rare")
        {
            double bp = Seg(t, hit + .22, hit + .55);
            if (bp > 0)
            {
                float bs = (float)Back(bp), br = 14 * bs, bx = BLK - 5 - 14, by = HB - 8 - 14;
                using (var gl = new SolidBrush(Al(Color.FromArgb(0, 140, 255), .5 * op))) g.FillEllipse(gl, bx - br - 5, by - br - 5, (br + 5) * 2, (br + 5) * 2);
                using (var cb = new SolidBrush(Al(Color.FromArgb(10, 43, 255), op))) g.FillEllipse(cb, bx - br, by - br, br * 2, br * 2);
                using (var cp = new Pen(Al(Color.White, op), 2)) g.DrawEllipse(cp, bx - br, by - br, br * 2, br * 2);
                var s4 = g.Save(); g.TranslateTransform(bx, by); DrawGemIcon(g, 9 * bs, Al(Color.White, op)); g.Restore(s4);
            }
        }
        // ── tira ──
        double lv = kind == "normal" ? 1 - Seg(t, .45, L.exit) : t < hit + .3 ? prog : 1 - Seg(t, hit + .3, L.exit);
        if (lv > 0)
            using (var lb = new LinearGradientBrush(new RectangleF(BLK, HB - 6, bw - BLK + 1, 6), Al(L.a, op), Al(L.c, op), 0f))
            {
                lb.InterpolationColors = new ColorBlend { Colors = new[] { Al(L.a, op), Al(L.b, op), Al(L.c, op) }, Positions = new[] { 0f, .5f, 1f } };
                g.FillRectangle(lb, BLK, HB - 6, (float)((bw - BLK) * Cl(lv)), 6);
            }
        // ── destello y brillo que cruza ──
        double f1 = kind == "rare" && t >= .45 ? Math.Exp(-(t - .45) * 9) * .55 : 0, f2 = kind == "normal" ? pulse * .35 : At(hit, 5.5) * .95;
        double fa = Cl(Math.Max(f1, f2)) * op;
        if (fa > .01)
        {
            var fc = kind == "rare" ? Color.FromArgb(223, 243, 255) : kind == "full" ? Color.FromArgb(255, 243, 176) : Color.FromArgb(255, 243, 196);
            using var fb = new SolidBrush(Al(fc, fa)); g.FillRectangle(fb, body);
        }
        double[] sweeps = kind == "normal" ? new[] { hit + .05 } : new[] { hit + .15, hit + 1.7, hit + 3.3 };
        foreach (var s0 in sweeps)
        {
            double u = Seg(t, s0, s0 + (kind == "normal" ? .65 : .75)); if (u <= 0 || u >= 1) continue;
            float sxw = 220, sx0 = (float)((-.25 + u * 1.35) * bw);
            using var shb = new LinearGradientBrush(new RectangleF(sx0, 0, sxw, HB), Color.Transparent, Color.Transparent, 10f);
            shb.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(140 * op * (kind == "normal" ? .8 : 1)), 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }, Positions = new[] { 0f, .5f, 1f } };
            g.FillRectangle(shb, sx0, 0, sxw, HB);
        }
        g.Restore(st1);
        // borde (fino) y borde de color que gira
        using (var edge = new Pen(light ? Color.FromArgb((int)(20 * op), 0, 0, 0) : Color.FromArgb((int)(40 * op), 255, 255, 255), 1.2f)) g.DrawPath(edge, path);
        double ringOp = kind == "normal" ? Cl(pulse * 1.2) * .9 : kind == "rare" ? Cl(.35 * Seg(t, .45, .7) + .65 * Seg(t, hit, hit + .2)) : Cl(.3 * Seg(t, .6, hit) + .7 * Seg(t, hit, hit + .15));
        if (ringOp * op > .01)
        {
            var rr = new RectangleF(-3.5f, -3.5f, bw + 7, HB + 7);
            using var rp = RR(rr, RB + 3.5f);
            using var rb = new LinearGradientBrush(new RectangleF(-4, -4, bw + 8, HB + 8), Color.Transparent, Color.Transparent, (float)(t * (kind == "rare" ? 150 : kind == "full" ? 210 : 120) % 360));
            var ca = Al(L.a, ringOp * op); var cbb = Al(L.b, ringOp * op); var cc = Al(L.c, ringOp * op); var cw = Al(Color.White, ringOp * op);
            rb.InterpolationColors = new ColorBlend { Colors = new[] { ca, cbb, cc, kind == "rare" ? cw : cc, cbb, ca }, Positions = new[] { 0f, .2f, .45f, .6f, .8f, 1f } };
            using var pen = new Pen(rb, 4f);
            g.DrawPath(pen, rp);
        }
        // ── textos ──
        var (tag, title, sub, pre) = LTexts();
        float tx0 = BLK + 20, right = bw - 24;
        using var pf = new Font("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel);
        if (kind != "normal" && pct is double pv)
        {
            string ptxt = kind == "full" ? $"{pv:0} %" : PctOf(pv);
            var ps = g.MeasureString(ptxt, pf);
            double pp = Seg(t, hit, hit + .45), pp2 = kind == "full" ? Seg(t, HIT2, HIT2 + .34) : 1;
            float psc = (float)(1 + (t >= hit && pp < 1 ? .4 * (1 - Eo(pp)) : 0) + (kind == "full" && t >= HIT2 && pp2 < 1 ? .16 * Math.Sin(pp2 * Math.PI) : 0));
            double pop2 = Cl(Seg(t, kind == "rare" ? .55 : .45, kind == "rare" ? .8 : .7)) * op;
            var s5 = g.Save(); g.TranslateTransform(right + 6, HB / 2); g.ScaleTransform(psc, psc);
            var pr = new RectangleF(-ps.Width, -ps.Height / 2 - 2, ps.Width, ps.Height);
            using (Brush pb = t >= hit ? (Brush)new LinearGradientBrush(pr, Al(kind == "rare" ? Color.FromArgb(47, 123, 255) : L.a, pop2), Al(kind == "rare" ? L.c : L.c, pop2), 0f) : new SolidBrush(Al(fg, pop2)))
                g.DrawString(ptxt, pf, pb, pr.X, pr.Y);
            g.Restore(s5);
            right -= 130;
        }
        float w = right - tx0;
        using var tgf = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tf2 = new Font("Segoe UI Semibold", 23, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        using var one = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        void Lines(string a, string b, string c, double[] o, double[] dx, double dy)
        {
            float yy = 14 + (float)dy;
            using (var t1 = new SolidBrush(Al(L.tag, o[0] * op))) g.DrawString(a, tgf, t1, new RectangleF(tx0 + (float)dx[0], yy, w, 18), one);
            using (var t2 = new SolidBrush(Al(fg, o[1] * op))) g.DrawString(b, tf2, t2, new RectangleF(tx0 + (float)dx[1], yy + 18, w, 32), one);
            using (var t3 = new SolidBrush(Al(fg, o[2] * op * .85))) g.DrawString(c, sf, t3, new RectangleF(tx0 + (float)dx[2], yy + 49, w, 26), one);
        }
        if (kind == "full")
        {
            double u = Seg(t, hit, hit + .22), pa = Cl(Seg(t, .45, .8)) * (1 - u);
            if (pa > .01 && pre is { } pz) Lines(pz.tag, pz.title, pz.sub, new[] { pa, pa, pa }, new[] { 0.0, 0, 0 }, 0);
            if (u > 0) Lines(tag, title, sub, new[] { u, u, u }, new[] { 0.0, 0, 0 }, (1 - Eo(u)) * 8);
        }
        else
        {
            double d0 = kind == "normal" ? .22 : .5, d1 = kind == "normal" ? .5 : .85, step = kind == "normal" ? .07 : .09, off = kind == "normal" ? 14 : 16;
            var o = new double[3]; var dx = new double[3];
            for (int i = 0; i < 3; i++) { double u = Eo(Seg(t, d0 + i * step, d1 + i * step)); o[i] = u; dx[i] = (1 - u) * off; }
            Lines(tag, title, sub, o, dx, 0);
        }
        // ── ondas ──
        var rings = new List<(double t0, double rad, Color col, double o)>();
        if (kind == "normal") rings.Add((hit, 40, L.b, .6));
        else if (kind == "rare") { rings.Add((.45, 30, L.b, .75)); rings.Add((hit, 52, L.c, .95)); rings.Add((hit + .1, 38, Color.White, .7)); }
        else { rings.Add((hit, 46, L.a, .95)); rings.Add((hit + .1, 62, L.c, .85)); rings.Add((hit + .2, 34, Color.White, .7)); rings.Add((HIT2, 40, L.b, .7)); }
        foreach (var (t0, rad, col, o) in rings)
        {
            double u = Seg(t, t0, t0 + (kind == "normal" ? .8 : .9)); if (u <= 0 || u >= 1) continue;
            float r = (float)(Eo(u) * rad * 10);
            using var rp = new Pen(Al(col, o * (1 - u) * op), 3.5f); g.DrawEllipse(rp, cx - r, cy - r, r * 2, r * 2);
        }
        // ── partículas ──
        foreach (var p in parts)
        {
            if (p.Ember)
            {
                double u = (t - p.T0) / p.Life; if (u <= 0 || u >= 1) continue;
                float px = (float)(p.X * bw + p.Dx * 10 * u), py = (float)(p.Top ? -p.H * 10 * Eo(u) * .8 : HB + p.H * 10 * Eo(u) * .35), sz = (float)(p.S * 10);
                var col = p.C < .4 ? Color.FromArgb(255, 59, 31) : p.C < .75 ? Color.FromArgb(255, 154, 31) : Color.FromArgb(255, 225, 74);
                double a = Math.Sin(u * Math.PI) * (t < hit ? Seg(t, .9, hit) : 1) * op;
                using (var gb = new SolidBrush(Al(col, a * .3))) g.FillEllipse(gb, px - sz, py - sz, sz * 2, sz * 2);
                using (var cb = new SolidBrush(Al(col, a))) g.FillEllipse(cb, px - sz / 2, py - sz / 2, sz, sz);
            }
            else if (p.Spark)
            {
                double u = (t - p.T0) / .95; if (u <= 0 || u >= 1) continue;
                double d = Eo(u) * p.Dd * 10; float px = (float)(cx + Math.Cos(p.A) * d), py = (float)(cy + Math.Sin(p.A) * d + u * u * 50), len = (float)(22 * p.S * (1 - u * .6));
                var col = p.C < .35 ? Color.FromArgb(255, 59, 31) : p.C < .7 ? Color.FromArgb(255, 179, 0) : Color.FromArgb(255, 243, 160);
                using var sp = new Pen(Al(col, (1 - u * u) * op), 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(sp, px - (float)Math.Cos(p.A) * len, py - (float)Math.Sin(p.A) * len, px, py);
            }
            else
            {
                double u = (t - p.T0) / .95; if (u <= 0 || u >= 1) continue;
                double d = Eo(u) * p.Dd * 10; float px = (float)(cx + Math.Cos(p.A) * d), py = (float)(cy + Math.Sin(p.A) * d), w2 = (float)(7.5 * p.S), h2 = (float)(9.5 * p.S);
                var s6 = g.Save(); g.TranslateTransform(px, py); g.RotateTransform((float)(p.R + u * 260));
                var pts = new[] { new PointF(0, -h2), new PointF(w2, -h2 * .3f), new PointF(0, h2), new PointF(-w2, -h2 * .3f) };
                using (var shb = new LinearGradientBrush(new RectangleF(-w2, -h2, w2 * 2 + 1, h2 * 2 + 1), Al(Color.White, (1 - u * u) * op), Al(Color.FromArgb(0, 168, 255), (1 - u * u) * op), 70f)) g.FillPolygon(shb, pts);
                g.Restore(s6);
            }
        }
        g.Restore(st0);
    }

    /// <summary>Diamante de líneas (como el de la interfaz), centrado en (0,0) con medio ancho r.</summary>
    static void DrawGemIcon(Graphics g, float r, Color c)
    {
        float s = r / 18f;
        PointF P(float x, float y) => new((x - 24) * s, (y - 25) * s);
        using var fill = new SolidBrush(Color.FromArgb(c.A * 56 / 255, 255, 255, 255));
        var outline = new[] { P(14, 8), P(34, 8), P(42, 18), P(24, 42), P(6, 18) };
        g.FillPolygon(fill, outline);
        using var pen = new Pen(c, 2.4f * s) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, outline);
        using var thin = new Pen(Color.FromArgb(c.A * 230 / 255, c.R, c.G, c.B), 1.6f * s) { LineJoin = LineJoin.Round };
        g.DrawLine(thin, P(6, 18), P(42, 18)); g.DrawLine(thin, P(14, 8), P(24, 42)); g.DrawLine(thin, P(34, 8), P(24, 42));
        g.DrawLines(thin, new[] { P(20, 18), P(24, 8), P(28, 18) });
    }

    /// <summary>Trofeo sencillo (cuando el logro no trae ícono).</summary>
    static void DrawTrophy(Graphics g, float cx, float cy, float r, Color c)
    {
        float s = r / 12f;
        PointF P(float x, float y) => new(cx + (x - 12) * s, cy + (y - 12) * s);
        using var pen = new Pen(c, 2f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var cup = new GraphicsPath();
        cup.AddLine(P(8, 4), P(16, 4)); cup.AddLine(P(16, 4), P(16, 9)); cup.AddArc(cx + (8 - 12) * s, cy + (5 - 12) * s, 8 * s, 8 * s, 0, 180); cup.CloseFigure();
        g.DrawPath(pen, cup);
        g.DrawLine(pen, P(12, 13), P(12, 17)); g.DrawLine(pen, P(8, 20), P(16, 20)); g.DrawLine(pen, P(10, 17), P(14, 17));
        g.DrawBezier(pen, P(8, 6), P(4, 6), P(4, 10), P(8, 10)); g.DrawBezier(pen, P(16, 6), P(20, 6), P(20, 10), P(16, 10));
    }
}
