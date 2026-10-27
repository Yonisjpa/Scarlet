namespace XboxScarlet;

/// <summary>
/// Compatibilidad de pantalla para juegos que no tienen "ventana sin bordes":
///  1) opciones de Windows que dejan dibujar el menú encima de casi todos los juegos en "pantalla completa"
///     (optimizaciones de pantalla completa del .exe y optimizaciones para juegos en ventana de Windows 11);
///  2) quitarle los bordes a la ventana del juego y estirarla a toda la pantalla (como Borderless Gaming).
/// No se mete dentro del juego (eso con antitrampas puede traer un baneo): solo opciones de Windows y su ventana.
/// </summary>
static class Compat
{
    const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    const string GpuPrefs = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public sealed record Result(bool fso, bool windowedOpt, string exe, string message);

    /// <summary>Prende lo que haga falta en Windows para ese .exe. Devuelve qué cambió y un texto para mostrar.</summary>
    public static Result Fix(string exe)
    {
        bool fso = false, opt = false;
        var notes = new List<string>();
        try
        {
            // 1) "Deshabilitar optimizaciones de pantalla completa" (Propiedades del .exe › Compatibilidad): se quita.
            if (!string.IsNullOrEmpty(exe))
                using (var k = Registry.CurrentUser.OpenSubKey(Layers, true))
                {
                    if (k?.GetValue(exe) is string flags && flags.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase))
                    {
                        string rest = string.Join(" ", flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(t => !t.Equals("DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase)));
                        if (rest.Trim() == "~" || rest.Trim().Length == 0) k.DeleteValue(exe, false); else k.SetValue(exe, rest);
                        fso = true;
                        notes.Add("Se volvieron a prender las optimizaciones de pantalla completa del juego.");
                    }
                }
            using (var k = Registry.LocalMachine.OpenSubKey(Layers, false))
                if (!string.IsNullOrEmpty(exe) && k?.GetValue(exe) is string f2 && f2.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase))
                    notes.Add("Ojo: las optimizaciones de pantalla completa están apagadas para todos los usuarios (Propiedades del .exe › Compatibilidad); desmárcalo ahí.");

            // 2) Windows 11 › Pantalla › Gráficos › "Optimizaciones para juegos en ventana".
            using (var k = Registry.CurrentUser.CreateSubKey(GpuPrefs, true))
            {
                string cur = k.GetValue("DirectXUserGlobalSettings") as string ?? "";
                var parts = cur.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
                               .ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);
                if (!parts.TryGetValue("SwapEffectUpgradeEnable", out var v) || v != "1")
                {
                    parts["SwapEffectUpgradeEnable"] = "1";
                    k.SetValue("DirectXUserGlobalSettings", string.Join("", parts.Select(p => $"{p.Key}={p.Value};")));
                    opt = true;
                    notes.Add("Se prendieron las optimizaciones para juegos en ventana de Windows.");
                }
            }
        }
        catch (Exception ex) { Log.Error("compatibilidad", ex); notes.Add("No se pudo cambiar todo: " + ex.Message); }
        Log.Write($"compatibilidad: {exe ?? "(sin .exe)"} · optimizaciones del juego {(fso ? "prendidas" : "ya estaban")} · juegos en ventana {(opt ? "prendido" : "ya estaba")}");
        if (notes.Count == 0) notes.Add("Las opciones de Windows ya estaban bien.");
        return new Result(fso, opt, exe, string.Join("\n", notes));
    }

    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    const long WS_EX_DLGMODALFRAME = 0x1, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000, WS_EX_WINDOWEDGE = 0x100;
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    public static bool HasBorders(IntPtr h) => h != IntPtr.Zero && Native.IsWindow(h) && (Native.GetWindowLongPtr(h, GWL_STYLE).ToInt64() & (WS_CAPTION | WS_THICKFRAME)) != 0;

    /// <summary>Le quita la barra de título y los bordes a la ventana del juego y la estira a su pantalla.</summary>
    public static bool MakeBorderless(IntPtr h)
    {
        if (!HasBorders(h) || Native.IsIconic(h)) return false;
        long st = Native.GetWindowLongPtr(h, GWL_STYLE).ToInt64() & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        long ex = Native.GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() & ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE);
        SetWindowLongPtr(h, GWL_STYLE, new IntPtr(st));
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        var b = Screen.FromHandle(h).Bounds;
        Native.SetWindowPos(h, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, 0x0020 /* FRAMECHANGED */ | 0x0004 /* NOZORDER */ | 0x0040 /* SHOWWINDOW */);
        Log.Write($"compatibilidad: {Native.Describe(h)} sin bordes, a {b.Width}×{b.Height}");
        return true;
    }
}
