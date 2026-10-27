using System.Collections.Concurrent;

namespace XboxScarlet;

/// <summary>
/// Todo lo que habla con el audio de Windows (volúmenes, apps con sonido, silenciar) pasa por este único hilo. Antes se
/// hacía desde hilos sueltos y, en algunos, Windows no dejaba abrir el audio ("Unable to cast MMDeviceEnumeratorCom…",
/// casi 3,000 veces en un log): las barras, el volumen y el silencio del juego fallaban. Aquí hay un solo hilo, siempre
/// del tipo correcto, con una sola conexión que se rehace sola si falla. Si Windows se tarda, quien pidió algo no se
/// queda esperando para siempre: sigue con el último valor.
/// </summary>
static class AudioThread
{
    static BlockingCollection<Action> queue;
    static Thread thread;
    static long busySince;   // ticks; 0 = libre
    static readonly object gate = new();

    /// <summary>Se avisa cuando hay que rehacer la conexión con el audio (cada clase borra lo que tenga guardado).</summary>
    public static event Action Reset;

    static void Ensure()
    {
        lock (gate)
        {
            if (thread != null && thread.IsAlive) return;
            var q = new BlockingCollection<Action>();
            var t = new Thread(() =>
            {
                foreach (var job in q.GetConsumingEnumerable())
                {
                    Interlocked.Exchange(ref busySince, DateTime.UtcNow.Ticks);
                    try { job(); } catch (Exception ex) { Log.Error("audio", ex); }
                    Interlocked.Exchange(ref busySince, 0);
                }
            }) { IsBackground = true, Name = "Scarlet · audio" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            queue = q; thread = t;
        }
    }

    /// <summary>Si el hilo se quedó trabado más de 10 s (Windows no contesta), se deja y se abre otro.</summary>
    static void CheckStuck()
    {
        long since = Interlocked.Read(ref busySince);
        if (since == 0 || DateTime.UtcNow.Ticks - since < TimeSpan.FromSeconds(10).Ticks) return;
        lock (gate)
        {
            Log.Write("audio: Windows no contesta desde hace más de 10 s; se abre una conexión nueva");
            try { queue?.CompleteAdding(); } catch { }
            thread = null; Interlocked.Exchange(ref busySince, 0);
        }
        try { Reset?.Invoke(); } catch { }
    }

    static bool OnAudioThread => Thread.CurrentThread == thread;

    /// <summary>Corre algo en el hilo del audio y espera el resultado (como mucho timeoutMs; si no, devuelve fallback).</summary>
    public static T Run<T>(Func<T> f, T fallback = default, int timeoutMs = 4000)
    {
        if (OnAudioThread) return Guarded(f, fallback);
        CheckStuck();
        Ensure();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { queue.Add(() => tcs.TrySetResult(Guarded(f, fallback))); }
        catch (InvalidOperationException) { Ensure(); queue.Add(() => tcs.TrySetResult(Guarded(f, fallback))); }
        if (tcs.Task.Wait(timeoutMs)) return tcs.Task.Result;
        Errors.Once("audio-lento", "audio: Windows tardó más de " + timeoutMs + " ms; se queda el último valor");
        return fallback;
    }

    /// <summary>Corre algo en el hilo del audio sin esperar (para no trabar la pantalla).</summary>
    public static void Post(Action a)
    {
        if (OnAudioThread) { Guarded(() => { a(); return true; }, false); return; }
        CheckStuck();
        Ensure();
        try { queue.Add(() => Guarded(() => { a(); return true; }, false)); } catch { }
    }

    /// <summary>Si la conexión con el audio se rompió, se rehace una vez y se vuelve a intentar.</summary>
    static T Guarded<T>(Func<T> f, T fallback)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return f(); }
            catch (Exception ex) when (attempt == 0 && (ex is InvalidCastException || ex is COMException))
            {
                Errors.Once("audio-reconexion", "audio: se rehace la conexión con Windows (" + ex.GetType().Name + ")");
                try { Reset?.Invoke(); } catch { }
            }
            catch (Exception ex) { Errors.Log("audio", ex); return fallback; }
        }
    }
}

/// <summary>Para que un mismo error no llene el log: se anota una vez por minuto (con cuántas veces se repitió).</summary>
static class Errors
{
    static readonly Dictionary<string, (DateTime at, int count)> seen = new();

    public static void Once(string key, string message)
    {
        int skipped;
        lock (seen)
        {
            if (seen.TryGetValue(key, out var s) && DateTime.Now - s.at < TimeSpan.FromMinutes(1)) { seen[key] = (s.at, s.count + 1); return; }
            skipped = seen.TryGetValue(key, out var old) ? old.count : 0;
            seen[key] = (DateTime.Now, 0);
        }
        XboxScarlet.Log.Write(message + (skipped > 0 ? $" (se repitió {skipped} vez/veces en el último minuto)" : ""));
    }

    public static void Log(string what, Exception ex) => XboxScarlet.Log.Error(what, ex);
}
