// Versione precedente (Python, lanciata da ScreenshotUploader.vbs in Esecuzione automatica):
// importa il suo config.json e, su richiesta, la disattiva. Piu' l'avvio automatico di questa versione.
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ScreenshotUploader;

static class Legacy
{
    public static string StartupVbs => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ScreenshotUploader.vbs");
    public static bool Present => !Store.Demo && File.Exists(StartupVbs);

    // config.json della versione Python: accanto allo script lanciato dal .vbs, oppure vicino a questo .exe
    static IEnumerable<string> OldConfigs()
    {
        if (File.Exists(StartupVbs))
        {
            var m = Regex.Match(File.ReadAllText(StartupVbs), @"""+([^""]*screenshot_uploader\.pyw?)""+", RegexOptions.IgnoreCase);
            if (m.Success && Path.GetDirectoryName(Path.GetDirectoryName(m.Groups[1].Value)) is string repo)
                yield return Path.Combine(repo, "config.json");
        }
        var exeDir = AppContext.BaseDirectory;
        yield return Path.Combine(exeDir, "config.json");
        if (Path.GetDirectoryName(exeDir.TrimEnd('\\')) is string parent) yield return Path.Combine(parent, "config.json");
    }

    // Converte il vecchio formato (lista "servers" con ssh_host/ssh_user/…, o un solo server in cima al file)
    // e scrive il nuovo config; restituisce il file importato, o null
    public static string? ImportConfig(string target)
    {
        foreach (var path in OldConfigs().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) continue;
                var olds = o["servers"] is JsonArray a ? a.OfType<JsonObject>().ToList()
                         : o.ContainsKey("ssh_host") ? [o] : [];
                if (olds.Count == 0) continue;

                var activeId = Store.Str(o, "active_server_id");
                var servers = new JsonObject();
                string? active = null;
                foreach (var s in olds)
                {
                    var name = Store.Str(s, "name") ?? "Main";
                    var key = Store.NewKey(name, servers.Select(kv => kv.Key));
                    var path0 = (Store.Str(s, "remote_path") ?? "/tmp/screenshots").TrimEnd('/');
                    var n = new JsonObject
                    {
                        ["name"] = name,
                        ["host"] = Store.Str(s, "ssh_host") ?? "",
                        ["user"] = Store.Str(s, "ssh_user") ?? "",
                        ["path"] = path0 == "" ? "/" : path0,
                    };
                    if (Store.Int(s, "ssh_port") is int p && p != 22) n["port"] = p;
                    if (Store.Str(s, "ssh_key_path") is string k && !IsDefaultKey(k)) n["identity_file"] = k;
                    servers[key] = n;
                    if (activeId != null && Store.Str(s, "id") == activeId) active = key;
                }
                var config = new JsonObject
                {
                    ["active"] = active ?? servers.First().Key,
                    ["servers"] = servers,
                    ["hotkey"] = "Ctrl+Alt+U",
                    ["notifications"] = o["notification_enabled"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true,
                };
                Store.WriteAtomic(target, config.ToJsonString(Store.JsonOut));
                Log.Info($"Imported {servers.Count} server(s) from {path}");
                return path;
            }
            catch (Exception e) { Log.Warn($"Can't import {path}: {e.Message}"); }
        }
        return null;
    }

    // Le chiavi che ssh prova da solo non servono nel config
    static bool IsDefaultKey(string key)
    {
        var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(key.Replace('/', '\\')));
            return new[] { "id_rsa", "id_ecdsa", "id_ecdsa_sk", "id_ed25519", "id_ed25519_sk" }
                .Any(n => string.Equals(full, Path.Combine(ssh, n), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    // Sposta il .vbs in %APPDATA%\ScreenshotUploader\legacy-backup (reversibile), ferma lo script Python
    // ancora in esecuzione e attiva l'avvio automatico di questa versione al suo posto
    public static void Disable()
    {
        var backup = Path.Combine(Store.Dir, "legacy-backup");
        Directory.CreateDirectory(backup);
        if (File.Exists(StartupVbs)) File.Move(StartupVbs, Path.Combine(backup, "ScreenshotUploader.vbs"), true);
        try
        {
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command",
                "Get-CimInstance Win32_Process -Filter \"Name like 'python%'\" | " +
                "Where-Object { $_.CommandLine -like '*screenshot_uploader.py*' } | " +
                "ForEach-Object { Stop-Process -Id $_.ProcessId -Force }" })
                psi.ArgumentList.Add(a);
            Process.Start(psi)?.WaitForExit(20_000);
        }
        catch (Exception e) { Log.Warn($"Can't stop the previous version: {e.Message}"); }
        AutoStart.Enabled = true;
        Log.Info($"Previous version turned off (startup script moved to {backup})");
    }
}

// Avvio automatico: HKCU\…\Run, nessun permesso di amministratore
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "ScreenshotUploader";
    static string Exe => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool Enabled
    {
        get => Store.Demo || Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(Name) is string v
               && v.Contains(Exe, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (Store.Demo) return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, $"\"{Exe}\"");
            else key.DeleteValue(Name, false);
        }
    }
}
