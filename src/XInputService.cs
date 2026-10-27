using WGI = Windows.Gaming.Input;

namespace XboxScarlet;

/// <summary>
/// Lee el botón Xbox directo del control (XInput), aunque la app esté detrás de un juego.
/// Toque doble = volver al menú / al juego · Mantener = menú rápido. También hace vibrar el mando.
/// </summary>
sealed class XInputService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_VIBRATION { public ushort wLeftMotorSpeed, wRightMotorSpeed; }

    // XInputGetStateEx (ordinal 100) es la única forma de leer el botón Xbox; no viene documentada pero existe desde Windows 8.
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int XInputGetStateEx(int userIndex, out XINPUT_STATE state);
    [DllImport("xinput1_4.dll")] static extern int XInputSetState(int userIndex, ref XINPUT_VIBRATION vibration);

    const ushort GUIDE = 0x0400;
    const int HOLD_MS = 550, DOUBLE_MS = 330;   // un toque se confirma cuando pasan 330 ms sin el segundo

    public event Action GuideSingle, GuideDouble, GuideHold, GuideDown;

    /// <summary>
    /// Mientras está activo, los botones del mando se mandan como teclas ("up", "a", "b"…) aunque la app no tenga el foco.
    /// Lo usa el menú rápido: si el juego se queda con el foco, el menú se sigue pudiendo usar y cerrar.
    /// </summary>
    public volatile bool ForwardKeys;
    bool wgiNoted;
    public event Action<string> PadKey;
    static readonly (ushort bit, string key)[] KeyMap =
    {
        (0x0001, "up"), (0x0002, "down"), (0x0004, "left"), (0x0008, "right"),
        (0x1000, "a"), (0x2000, "b"), (0x4000, "x"), (0x8000, "y"), (0x0100, "lb"), (0x0200, "rb"), (0x0010, "start")
    };
    const int REPEAT_FIRST_MS = 380, REPEAT_MS = 110;
    const short STICK = 16000;
    Thread thread;
    volatile bool running = true;
    volatile int lastIndex;

    public void Start()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "xinput-guide" };
        thread.Start();
    }

    // Respaldo del botón Xbox: en Windows.Gaming.Input el botón Xbox no tiene etiqueta propia; se busca el que Windows
    // marque como "Mode" (el botón de inicio en mandos genéricos). Además sirve para anotar en el log qué mandos hay.
    sealed class WgiPad { public WGI.RawGameController C; public int Guide; public bool[] B; public WGI.GameControllerSwitchPosition[] S; public double[] A; public string L; }
    List<WgiPad> wgi = new();

    void RefreshWgi()
    {
        var list = new List<WgiPad>();
        foreach (var c in WGI.RawGameController.RawGameControllers)
        {
            int guide = -1;
            var labels = new List<string>();
            for (int i = 0; i < c.ButtonCount; i++)
            {
                var l = c.GetButtonLabel(i);
                labels.Add(l.ToString());
                if ((int)l == 23 /* Mode */ && guide < 0) guide = i;
            }
            list.Add(new WgiPad { C = c, Guide = guide, B = new bool[c.ButtonCount], S = new WGI.GameControllerSwitchPosition[c.SwitchCount], A = new double[c.AxisCount], L = string.Join("/", labels) });
        }
        string desc = string.Join(", ", list.Select(p => $"{p.C.DisplayName} ({p.C.HardwareVendorId:x4}:{p.C.HardwareProductId:x4}, {p.B.Length} botones [{p.L}], inicio={(p.Guide >= 0 ? p.Guide.ToString() : "no")})"));
        if (desc != wgiDesc) { wgiDesc = desc; Log.Write("wgi: " + (desc.Length > 0 ? desc : "sin mandos")); }
        wgi = list;
    }
    string wgiDesc;

    bool WgiGuide()
    {
        foreach (var p in wgi)
        {
            if (p.Guide < 0) continue;
            try { p.C.GetCurrentReading(p.B, p.S, p.A); if (p.B[p.Guide]) return true; } catch { }
        }
        return false;
    }

    /// <summary>
    /// Botones del mando por Windows.Gaming.Input, en los mismos bits que XInput. Hace falta porque en algunas consolas
    /// XInput no da los botones (el botón Xbox ya se leía por aquí) y entonces el menú encima del juego no recibía nada.
    /// </summary>
    static ushort WgiButtons()
    {
        ushort b = 0;
        try
        {
            foreach (var g in WGI.Gamepad.Gamepads)
            {
                var r = g.GetCurrentReading();
                var x = r.Buttons;
                if (x.HasFlag(WGI.GamepadButtons.DPadUp) || r.LeftThumbstickY > 0.5) b |= 0x0001;
                if (x.HasFlag(WGI.GamepadButtons.DPadDown) || r.LeftThumbstickY < -0.5) b |= 0x0002;
                if (x.HasFlag(WGI.GamepadButtons.DPadLeft) || r.LeftThumbstickX < -0.5) b |= 0x0004;
                if (x.HasFlag(WGI.GamepadButtons.DPadRight) || r.LeftThumbstickX > 0.5) b |= 0x0008;
                if (x.HasFlag(WGI.GamepadButtons.Menu)) b |= 0x0010;
                if (x.HasFlag(WGI.GamepadButtons.View)) b |= 0x0020;
                if (x.HasFlag(WGI.GamepadButtons.LeftShoulder)) b |= 0x0100;
                if (x.HasFlag(WGI.GamepadButtons.RightShoulder)) b |= 0x0200;
                if (x.HasFlag(WGI.GamepadButtons.A)) b |= 0x1000;
                if (x.HasFlag(WGI.GamepadButtons.B)) b |= 0x2000;
                if (x.HasFlag(WGI.GamepadButtons.X)) b |= 0x4000;
                if (x.HasFlag(WGI.GamepadButtons.Y)) b |= 0x8000;
            }
        }
        catch { }
        return b;
    }

    const ushort START = 0x0010, BACK = 0x0020;
    const int CHORD_HOLD_MS = 600;

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        var connected = new bool[4];
        bool down = false, holdFired = false;
        long downAt = 0, lastTap = 0, tick = 0;
        bool chord = false, chordHold = false;
        long chordAt = 0;
        bool reported = false, xinputOk = true;
        ushort fwdPrev = 0;
        var fwdNext = new Dictionary<string, long>();
        while (running)
        {
          try
          {
            bool guide = false, viewMenu = false;
            ushort pad = 0;
            if (xinputOk)
            {
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (!connected[i] && tick % 60 != 0) continue;   // los puertos vacíos se revisan 1 vez por segundo
                        bool ok = XInputGetStateEx(i, out var s) == 0;
                        if (ok != connected[i]) Log.Write($"xinput: mando {(ok ? "conectado" : "desconectado")} en el puerto {i + 1}");
                        connected[i] = ok;
                        if (!ok) continue;
                        pad |= Directional(s.Gamepad);
                        if ((s.Gamepad.wButtons & GUIDE) != 0) { guide = true; lastIndex = i; }
                        if ((s.Gamepad.wButtons & (START | BACK)) == (START | BACK)) { viewMenu = true; lastIndex = i; }
                    }
                }
                catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
                {
                    Log.Error("xinput: este Windows no deja leer el botón Xbox por XInput", ex);
                    xinputOk = false;
                }
                catch (Exception ex) { if (!reported) { reported = true; Log.Error("xinput", ex); } Thread.Sleep(2000); }
            }
            try
            {
                if (tick % 120 == 0) RefreshWgi();   // cada 2 s
                if (!guide && WgiGuide()) { guide = true; if (!down) Log.Write("wgi: botón Xbox presionado"); }
            }
            catch (Exception ex) { if (tick % 600 == 0) Log.Error("wgi", ex); }
            tick++;

            long now = sw.ElapsedMilliseconds;
            if (ForwardKeys)
            {
                ushort w = WgiButtons();
                if (w != 0 && pad == 0 && !wgiNoted) { wgiNoted = true; Log.Write("mando → menú: los botones llegan por Windows.Gaming.Input (XInput no los da)"); }
                pad |= w;
            }
            Forward(pad, ref fwdPrev, fwdNext, now);
            if (guide && !down)
            {
                down = true; holdFired = false; downAt = now;
                Log.Write($"botón Xbox presionado (puerto {lastIndex + 1})");
                Raise(GuideDown);
            }
            else if (guide && down && !holdFired && now - downAt >= HOLD_MS) { holdFired = true; lastTap = 0; Raise(GuideHold); }
            else if (!guide && down)
            {
                down = false;
                if (!holdFired)
                {
                    if (lastTap != 0 && now - lastTap < DOUBLE_MS) { lastTap = 0; Raise(GuideDouble); }
                    else lastTap = now;
                }
            }
            else if (!guide && !down && lastTap != 0 && now - lastTap >= DOUBLE_MS) { lastTap = 0; Raise(GuideSingle); }

            // Respaldo por si Windows no deja leer el botón Xbox: View + Menú juntos.
            // Tocar = como doble toque (menú / regresar al juego) · mantener = menú rápido.
            if (viewMenu && !chord) { chord = true; chordHold = false; chordAt = now; }
            else if (viewMenu && chord && !chordHold && now - chordAt >= CHORD_HOLD_MS) { chordHold = true; Log.Write("View + Menú mantenidos: menú rápido"); Raise(GuideSingle); }
            else if (!viewMenu && chord)
            {
                chord = false;
                if (!chordHold) { Log.Write("View + Menú: menú / juego"); Raise(GuideDouble); }
            }
            Thread.Sleep(16);
          }
          catch (Exception ex)
          {
            // Un error aquí (p. ej. al cerrar Scarlet) no debe tumbar la app: este hilo no tiene quién lo atrape.
            Log.Error("botón Xbox (hilo)", ex);
            Thread.Sleep(500);
          }
        }
    }

    /// <summary>Botones + cruceta, con el stick izquierdo sumado a la cruceta.</summary>
    static ushort Directional(XINPUT_GAMEPAD g)
    {
        ushort b = g.wButtons;
        if (g.sThumbLY > STICK) b |= 0x0001; else if (g.sThumbLY < -STICK) b |= 0x0002;
        if (g.sThumbLX < -STICK) b |= 0x0004; else if (g.sThumbLX > STICK) b |= 0x0008;
        return b;
    }

    /// <summary>Manda cada botón recién apretado; las direcciones se repiten si se mantienen (como en la interfaz).</summary>
    void Forward(ushort pad, ref ushort prev, Dictionary<string, long> next, long now)
    {
        if (!ForwardKeys) { prev = pad; next.Clear(); return; }   // al activarse no se manda lo que ya estaba apretado
        foreach (var (bit, key) in KeyMap)
        {
            bool on = (pad & bit) != 0, was = (prev & bit) != 0;
            if (on && !was) { next[key] = now + REPEAT_FIRST_MS; RaiseKey(key); }
            else if (on && bit <= 0x0008 && next.TryGetValue(key, out long t) && now >= t) { next[key] = now + REPEAT_MS; RaiseKey(key); }
        }
        prev = pad;
    }

    void RaiseKey(string k)
    {
        try { PadKey?.Invoke(k); } catch (Exception ex) { Log.Error("mando → menú", ex); }
    }

    static void Raise(Action a)
    {
        try { a?.Invoke(); } catch (Exception ex) { Log.Error("guide", ex); }
    }

    public void Rumble(double strong, double weak, int ms)
    {
        try
        {
            int idx = lastIndex;
            var v = new XINPUT_VIBRATION { wLeftMotorSpeed = (ushort)(Math.Clamp(strong, 0, 1) * 65535), wRightMotorSpeed = (ushort)(Math.Clamp(weak, 0, 1) * 65535) };
            XInputSetState(idx, ref v);
            _ = Task.Delay(Math.Clamp(ms, 10, 2000)).ContinueWith(_ =>
            {
                var z = new XINPUT_VIBRATION();
                try { XInputSetState(idx, ref z); } catch { }
            });
        }
        catch (Exception ex) { Log.Error("vibración", ex); }
    }

    public void Dispose() { running = false; }
}
