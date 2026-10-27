using System.IO.Compression;

namespace XboxScarlet;

/// <summary>
/// Paquetes de sonidos con el formato de Steam Deck (Audio Loader / deckthemes): una carpeta o un .zip con pack.json,
/// que dice qué archivo suena en cada evento del menú de Steam. Se buscan en Descargas, Escritorio y Documentos, y se
/// copian a los datos de Scarlet con el nombre de cada sonido de Scarlet. Ahí no los toca ninguna actualización.
/// </summary>
static class SoundPack
{
    public static string Dir => Path.Combine(Paths.Local, "sounds");
    static string NameFile => Path.Combine(Dir, "paquete.txt");

    /// <summary>Evento de Steam Deck → sonido de Scarlet.</summary>
    static readonly Dictionary<string, string> Deck = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deck_ui_misc_10"] = "move",              // mover entre mosaicos
        ["deck_ui_tile_scroll"] = "move",
        ["deck_ui_navigation"] = "movelist",       // mover dentro de listas (Ajustes, menús)
        ["deck_ui_default_activation"] = "select",
        ["deck_ui_out_of_game_detail"] = "back",
        ["deck_ui_into_game_detail"] = "detail",
        ["deck_ui_bumper_end_02"] = "bump",
        ["bumper_end"] = "bump",
        ["deck_ui_typing"] = "type",
        ["deck_ui_launch_game"] = "launch",
        ["deck_ui_toast"] = "notif",
        ["deck_ui_message_toast"] = "notif",
        ["deck_ui_achievement_toast"] = "achieve",
        ["deck_ui_switch_toggle_on"] = "on",
        ["deck_ui_switch_toggle_off"] = "off",
        ["deck_ui_slider_up"] = "up",
        ["deck_ui_slider_down"] = "down",
        ["deck_ui_volume"] = "volume",
        ["deck_ui_show_modal"] = "popup",
        ["deck_ui_hide_modal"] = "popupclose",
        ["deck_ui_side_menu_fly_in"] = "qopen",
        ["deck_ui_side_menu_fly_out"] = "qclose",
        ["deck_ui_tab_transition_01"] = "tab",
        ["ui_steam_smoother_friend_online"] = "friend",
        ["ui_steam_smoother_friend_join"] = "friendgame",
    };

    /// <summary>Sonidos que pack.json no trae (Steam no los tiene) pero que vienen sueltos en algunos paquetes.</summary>
    static readonly (string pattern, string key)[] Loose =
    {
        (@"^(camera|screenshot)", "capture"), (@"^recording_start", "recstart"), (@"^recording_stop", "recstop"), (@"error", "error")
    };

    static readonly string[] Audio = { ".wav", ".mp3", ".ogg", ".m4a" };

    public static string Current() { try { return File.Exists(NameFile) ? File.ReadAllText(NameFile).Trim() : null; } catch { return null; } }

    public static void Remove()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); Log.Write("sonidos: se quitó el paquete; vuelven los de Scarlet"); }
        catch (Exception ex) { Log.Error("sonidos (quitar)", ex); }
    }

    public sealed record Result(bool ok, string name, int count, string error);

    public static Result Import()
    {
        string tmp = Path.Combine(Paths.Local, "sounds-import");
        try
        {
            var found = Find();
            if (found == null) return new Result(false, null, 0, "No encontré ningún paquete. Descárgalo (.zip) en Descargas y vuelve a intentar.");
            string packJson = found;
            if (found.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                string root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
                using (var zip = ZipFile.OpenRead(found))
                    foreach (var e in zip.Entries)
                    {
                        string ext = Path.GetExtension(e.Name);
                        if (e.Length > 30_000_000 || !(e.Name.Equals("pack.json", StringComparison.OrdinalIgnoreCase) || Audio.Contains(ext, StringComparer.OrdinalIgnoreCase))) continue;
                        string dest = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // rutas raras dentro del zip
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                packJson = Directory.EnumerateFiles(tmp, "pack.json", SearchOption.AllDirectories).FirstOrDefault();
                if (packJson == null) return new Result(false, null, 0, "Ese .zip no trae pack.json");
            }
            string folder = Path.GetDirectoryName(packJson);
            string name = Path.GetFileName(folder);
            var pick = new Dictionary<string, string>();
            using (var doc = JsonDocument.Parse(File.ReadAllText(packJson)))
            {
                if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() is string nn && nn.Length > 0) name = nn;
                if (doc.RootElement.TryGetProperty("mappings", out var maps) && maps.ValueKind == JsonValueKind.Object)
                    foreach (var m in maps.EnumerateObject())
                    {
                        if (!Deck.TryGetValue(Path.GetFileNameWithoutExtension(m.Name), out var key) || pick.ContainsKey(key)) continue;
                        string file = m.Value.ValueKind == JsonValueKind.Array ? m.Value.EnumerateArray().Select(x => x.GetString()).FirstOrDefault() : m.Value.GetString();
                        string path = file == null ? null : Path.GetFullPath(Path.Combine(folder, file));
                        if (path != null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) pick[key] = path;
                    }
            }
            // Archivos con el nombre del evento de Steam (paquetes sin "mappings") y sonidos sueltos (cámara, grabar, error).
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!Audio.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)) continue;
                if (Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(p => p.Equals("unused", StringComparison.OrdinalIgnoreCase))) continue;
                string stem = Path.GetFileNameWithoutExtension(f);
                if (Deck.TryGetValue(stem, out var dk) && !pick.ContainsKey(dk)) pick[dk] = f;
                foreach (var (pattern, key) in Loose)
                    if (!pick.ContainsKey(key) && Regex.IsMatch(stem, pattern, RegexOptions.IgnoreCase)) pick[key] = f;
            }
            if (pick.Count == 0) return new Result(false, name, 0, "El paquete no trae sonidos que Scarlet reconozca");

            Remove();
            Directory.CreateDirectory(Dir);
            foreach (var (key, src) in pick) File.Copy(src, Path.Combine(Dir, key + Path.GetExtension(src).ToLowerInvariant()), true);
            File.WriteAllText(NameFile, name);
            Log.Write($"sonidos: paquete «{name}» importado de {found} ({pick.Count}: {string.Join(", ", pick.Keys)})");
            return new Result(true, name, pick.Count, null);
        }
        catch (Exception ex) { Log.Error("sonidos (importar)", ex); return new Result(false, null, 0, "No se pudo importar: " + ex.Message); }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>El paquete más reciente (carpeta con pack.json o .zip que lo traiga) en Descargas, Escritorio o Documentos.</summary>
    static string Find()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[] { Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }
            .Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase);
        var cands = new List<(string path, DateTime when)>();
        var opt = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };
        foreach (var r in roots)
        {
            try { foreach (var f in Directory.EnumerateFiles(r, "pack.json", opt)) if (LooksLikeSoundPack(f)) cands.Add((f, File.GetLastWriteTimeUtc(f))); } catch { }
            try
            {
                foreach (var z in Directory.EnumerateFiles(r, "*.zip", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true }))
                {
                    if (new FileInfo(z).Length > 500_000_000) continue;
                    try { using var zip = ZipFile.OpenRead(z); if (zip.Entries.Any(e => e.Name.Equals("pack.json", StringComparison.OrdinalIgnoreCase))) cands.Add((z, File.GetLastWriteTimeUtc(z))); }
                    catch { }
                }
            }
            catch { }
        }
        return cands.OrderByDescending(c => c.when).Select(c => c.path).FirstOrDefault();
    }

    static bool LooksLikeSoundPack(string packJson)
    {
        try { string t = File.ReadAllText(packJson); return t.Contains("\"mappings\"") || t.Contains("deck_ui_"); }
        catch { return false; }
    }
}
