namespace XboxScarlet;

/// <summary>
/// Esconde el puntero del mouse en toda la consola (Scarlet, juegos, apps) cambiando los cursores de Windows por uno
/// transparente, como en una consola. Se devuelve al cerrar Scarlet, al abrir el escritorio / Explorador / Ajustes de
/// Windows (ahí sí hace falta) y al arrancar (por si la app se cerró de golpe con el puntero escondido).
/// </summary>
static class CursorHider
{
    [DllImport("user32.dll")] static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);
    [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr hcur, uint id);
    [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr hCursor);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);

    const uint SPI_SETCURSORS = 0x0057;
    // Flecha, texto, espera, cruz, arriba, tamaños, mover, prohibido, mano, ocupado en segundo plano, ayuda, pluma y ubicación.
    static readonly uint[] Ids = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651, 32631, 32671, 32672 };

    public static bool Hidden { get; private set; }

    public static void Hide()
    {
        if (Hidden) return;
        try
        {
            var and = Enumerable.Repeat((byte)0xFF, 32 * 32 / 8).ToArray();   // 1 = transparente
            var xor = new byte[32 * 32 / 8];
            IntPtr blank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, and, xor);
            if (blank == IntPtr.Zero) return;
            foreach (var id in Ids) SetSystemCursor(CopyIcon(blank), id);   // Windows se queda con cada copia
            DestroyCursor(blank);
            Hidden = true;
        }
        catch (Exception ex) { Log.Error("ocultar puntero", ex); }
    }

    /// <summary>Vuelve a cargar los cursores normales de Windows.</summary>
    public static void Restore()
    {
        try { SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0); } catch { }
        Hidden = false;
    }
}
