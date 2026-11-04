namespace XboxScarlet;

/// <summary>
/// Ventana encima de todo (también del juego) que se dibuja con transparencia por píxel: bordes redondos suaves, brillos y
/// sombras sin recuadro. No roba el foco ni recibe clics. Quien la usa dibuja cada cuadro en un Bitmap y llama Present.
/// </summary>
abstract class LayeredWindow : Form
{
    protected LayeredWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 /* LAYERED */ | 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */ | 0x00000008 /* TOPMOST */ | 0x00000020 /* TRANSPARENT */;
            return cp;
        }
    }

    // Nada de pintar con WinForms: la imagen la pone Present.
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    [StructLayout(LayoutKind.Sequential)] struct LPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct LSize { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref LPoint pptDst, ref LSize psize, IntPtr hdcSrc, ref LPoint pptSrc, int crKey, ref Blend pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Pone la imagen (ARGB) en pantalla, con su esquina en (x, y) y opacidad general alpha (0–255).</summary>
    protected void Present(Bitmap bmp, int x, int y, byte alpha = 255)
    {
        if (!IsHandleCreated || IsDisposed) return;
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hbmp);
            var dst = new LPoint { X = x, Y = y };
            var size = new LSize { CX = bmp.Width, CY = bmp.Height };
            var src = new LPoint();
            var blend = new Blend { Op = 0 /* AC_SRC_OVER */, Flags = 0, Alpha = alpha, Format = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        catch (Exception ex) { Log.Error("ventana transparente", ex); }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Cápsula (rectángulo con las puntas redondas).</summary>
    protected static System.Drawing.Drawing2D.GraphicsPath Capsule(RectangleF r)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        float d = Math.Min(r.Height, r.Width);
        if (d <= 0) return p;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    protected static Color A(Color c, double a) => Color.FromArgb(Math.Clamp((int)Math.Round(c.A * a), 0, 255), c.R, c.G, c.B);
}
