namespace XboxScarlet;

/// <summary>
/// Pausa un juego mientras estás en el Inicio (como en una consola): congela su proceso y lo descongela al volver.
/// Solo para los juegos que tú marques: en juegos en línea, congelarlos puede desconectarte.
/// Si Scarlet se cerrara con un juego congelado, al volver a abrir lo descongela (se guarda en congelado.txt).
/// </summary>
static class GameFreezer
{
    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr process);
    const uint PROCESS_SUSPEND_RESUME = 0x0800;

    static uint frozenPid;
    static string StateFile => Path.Combine(Paths.Local, "congelado.txt");

    public static bool IsFrozen => frozenPid != 0;

    public static bool Suspend(uint pid, long appid)
    {
        if (pid == 0 || frozenPid != 0) return false;
        if (!Call(pid, true)) return false;
        frozenPid = pid;
        try { File.WriteAllText(StateFile, pid.ToString()); } catch { }
        Log.Write($"juego {appid} en pausa (pid {pid})");
        return true;
    }

    /// <summary>Descongela el juego si estaba en pausa. Devuelve si había algo que descongelar.</summary>
    public static bool Resume()
    {
        if (frozenPid == 0) return false;
        uint pid = frozenPid;
        frozenPid = 0;
        Call(pid, false);
        try { File.Delete(StateFile); } catch { }
        Log.Write($"juego reanudado (pid {pid})");
        return true;
    }

    /// <summary>El juego ya se cerró: no hay nada que descongelar.</summary>
    public static void Forget()
    {
        frozenPid = 0;
        try { File.Delete(StateFile); } catch { }
    }

    /// <summary>Al arrancar Scarlet: si quedó un juego congelado (la app se cerró de golpe), se descongela.</summary>
    public static void RecoverAfterCrash()
    {
        try
        {
            if (!File.Exists(StateFile)) return;
            if (uint.TryParse(File.ReadAllText(StateFile).Trim(), out uint pid) && pid != 0 && Call(pid, false))
                Log.Write($"se reanudó un juego que había quedado en pausa (pid {pid})");
            File.Delete(StateFile);
        }
        catch (Exception ex) { Log.Error("pausa (recuperar)", ex); }
    }

    static bool Call(uint pid, bool suspend)
    {
        IntPtr h = Native.OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (h == IntPtr.Zero) { Log.Write($"no se pudo {(suspend ? "pausar" : "reanudar")} el proceso {pid}"); return false; }
        try { return (suspend ? NtSuspendProcess(h) : NtResumeProcess(h)) == 0; }
        finally { Native.CloseHandle(h); }
    }
}
