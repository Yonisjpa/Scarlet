namespace XboxScarlet;

/// <summary>
/// Rendimiento mientras juegas (beta 1.2).
/// - Eco: con un juego al frente y el menú cerrado, los procesos de las vistas web de Scarlet bajan de prioridad y
///   entran en modo eficiencia de Windows (EcoQoS): si el juego necesita el procesador, ellos esperan. Al abrir el menú
///   vuelven a lo normal antes de mostrarse, así que el menú responde igual que siempre.
/// - Muestras: cuánto procesador y memoria usa Scarlet (con todas sus vistas web) mientras juegas, con el menú cerrado y
///   con el menú abierto. Se ve en Diagnóstico › Rendimiento y se anota en el registro.
/// </summary>
static class Perf
{
    [StructLayout(LayoutKind.Sequential)]
    struct PowerThrottling { public uint Version, ControlMask, StateMask; }
    const int ProcessPowerThrottling = 4;
    const uint ExecutionSpeed = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessInformation(IntPtr h, int cls, ref PowerThrottling info, int size);

    static bool ecoOn;
    public static bool EcoOn => ecoOn;

    /// <summary>Modo eficiencia para esos procesos (on) o de vuelta a lo normal (off).</summary>
    public static void Eco(bool on, IEnumerable<int> pids, string why)
    {
        if (on == ecoOn) return;
        ecoOn = on;
        int n = 0;
        foreach (int pid in pids.Distinct())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var s = new PowerThrottling { Version = 1, ControlMask = ExecutionSpeed, StateMask = on ? ExecutionSpeed : 0 };
                SetProcessInformation(p.Handle, ProcessPowerThrottling, ref s, Marshal.SizeOf<PowerThrottling>());
                p.PriorityClass = on ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
                n++;
            }
            catch { }   // un proceso que ya se cerró o que Windows no deja tocar
        }
        Log.Write($"rendimiento: modo eficiencia {(on ? "activado" : "desactivado")} en {n} procesos de Scarlet ({why})");
    }

    // ───── Muestras de uso ─────
    sealed class Bucket { public double Cpu; public long Mem; public int N; public double Peak; }
    static readonly Dictionary<string, Bucket> buckets = new();
    static readonly Dictionary<int, TimeSpan> lastCpu = new();
    static DateTime lastAt;
    static DateTime lastLog = DateTime.MinValue;

    /// <summary>
    /// Toma una muestra: % de procesador (de toda la máquina) y memoria de Scarlet + sus procesos web, y la suma a
    /// «menú cerrado» o «menú abierto». Se llama cada pocos segundos mientras hay un juego.
    /// </summary>
    public static void Sample(IEnumerable<int> pids, bool menuOpen, bool count = true)
    {
        var now = DateTime.UtcNow;
        double secs = lastAt == default ? 0 : (now - lastAt).TotalSeconds;
        lastAt = now;
        TimeSpan total = TimeSpan.Zero, delta = TimeSpan.Zero; long mem = 0;
        var seen = new HashSet<int>();
        foreach (int pid in pids.Distinct())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var cpu = p.TotalProcessorTime;
                if (lastCpu.TryGetValue(pid, out var was) && cpu >= was) delta += cpu - was;
                lastCpu[pid] = cpu; seen.Add(pid);
                mem += p.PrivateMemorySize64;
                total += cpu;
            }
            catch { }
        }
        foreach (var k in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(k);
        if (secs <= 0 || secs > 30 || !count) return;   // count=false: el rato justo al abrir/cerrar el menú no cuenta en ninguno
        double pct = delta.TotalSeconds / secs / Environment.ProcessorCount * 100;
        string key = menuOpen ? "open" : "closed";
        lock (buckets)
        {
            if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = new Bucket();
            b.Cpu += pct; b.Mem += mem; b.N++; b.Peak = Math.Max(b.Peak, pct);
        }
        if (DateTime.Now - lastLog > TimeSpan.FromMinutes(5)) { lastLog = DateTime.Now; Log.Write("rendimiento: " + Describe()); }
    }

    public static void Reset() { lock (buckets) buckets.Clear(); lastCpu.Clear(); lastAt = default; }

    static string Part(string key, string label)
    {
        if (!buckets.TryGetValue(key, out var b) || b.N == 0) return $"{label}: sin datos todavía";
        return $"{label}: {b.Cpu / b.N:0.00} % del procesador (máx. {b.Peak:0.0} %), {b.Mem / b.N / 1048576.0:0} MB";
    }

    /// <summary>El resumen para Diagnóstico y el registro.</summary>
    public static string Describe()
    {
        lock (buckets) return $"{Part("closed", "jugando con el menú cerrado")} · {Part("open", "con el menú abierto")}{(ecoOn ? " · modo eficiencia activo" : "")}";
    }

}
