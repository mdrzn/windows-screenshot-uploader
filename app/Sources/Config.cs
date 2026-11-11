// Configurazione in %APPDATA%\ScreenshotUploader\config.json, stesso formato della versione Mac:
//   { "active": "main", "servers": { "main": { "name", "host", "user", "path", "port"? } } }
// piu' le preferenze di Windows ("hotkey", "notifications"). Le chiavi sconosciute restano intatte.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenshotUploader;

sealed class Server
{
    public string Key = "";                 // chiave nel config ("main", "dev"…)
    public string Name = "";
    public string Host = "";                // anche un alias di ~/.ssh/config
    public string User = "";                // vuoto = quello di ~/.ssh/config
    public string Path = "/tmp/screenshots";
    public int? Port;                       // vuoto = 22 o quello di ~/.ssh/config
    public string? IdentityFile;            // facoltativo: di norma bastano ~/.ssh/config e ssh-agent

    public string Target => User == "" ? Host : $"{User}@{Host}";
    public string Display => $"{Target}:{Path}" + (Port is int p ? $"  (port {p})" : "");
    public Server Clone() => (Server)MemberwiseClone();
}

sealed record Upload(string Remote, string Server, DateTime Date);

static class Store
{
    public static bool Demo;
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenshotUploader");
    static string ConfigPath => Path.Combine(Dir, "config.json");
    static string HistoryPath => Path.Combine(Dir, "history.json");

    public static List<Server> Servers = [];
    public static string Active = "";
    public static string Hotkey = "Ctrl+Alt+U";    // "" = nessuna scorciatoia
    public static bool NotifySuccess = true;       // gli errori si notificano sempre
    public static List<Upload> History = [];
    public static string? LoadError;               // config illeggibile: lo si dice nel menu invece di chiudersi
    public static string? ImportedFrom;            // config della versione Python importato a questo avvio

    public static Server? ActiveServer => Servers.FirstOrDefault(s => s.Key == Active) ?? Servers.FirstOrDefault();

    // JSON leggibile anche a mano: "Ctrl+Alt+U", non "Ctrl\u002BAlt\u002BU"
    public static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly JsonSerializerOptions historyJson = new(JsonOut) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly JsonDocumentOptions lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static void Load()
    {
        if (Demo) { LoadDemo(); return; }
        Directory.CreateDirectory(Dir);
        if (!File.Exists(ConfigPath)) ImportedFrom = Legacy.ImportConfig(ConfigPath);
        try
        {
            if (File.Exists(ConfigPath))
            {
                var root = JsonNode.Parse(File.ReadAllText(ConfigPath), documentOptions: lenient) as JsonObject
                           ?? throw new JsonException("not a JSON object");
                Servers = [];
                if (root["servers"] is JsonObject servers)
                    foreach (var (key, node) in servers)
                        if (node is JsonObject s)
                            Servers.Add(new Server
                            {
                                Key = key,
                                Name = Str(s, "name") ?? key,
                                Host = Str(s, "host") ?? "",
                                User = Str(s, "user") ?? "",
                                Path = Str(s, "path") ?? "/tmp/screenshots",
                                Port = Int(s, "port"),
                                IdentityFile = Str(s, "identity_file"),
                            });
                Active = Str(root, "active") ?? Servers.FirstOrDefault()?.Key ?? "";
                if (root.ContainsKey("hotkey")) Hotkey = Str(root, "hotkey") ?? "";
                NotifySuccess = Bool(root, "notifications") ?? true;
            }
            LoadError = null;
        }
        catch (Exception e)
        {
            LoadError = e.Message;
            Log.Error($"Can't read {ConfigPath}: {e.Message}");
        }
        try
        {
            if (File.Exists(HistoryPath))
                History = JsonSerializer.Deserialize<List<Upload>>(File.ReadAllText(HistoryPath), historyJson) ?? [];
        }
        catch (Exception e) { Log.Warn($"Can't read the upload history: {e.Message}"); }
    }

    // Riscrive il config tenendo le chiavi che non conosce; scrittura atomica (file temporaneo + rinomina)
    public static void Save()
    {
        if (Demo) return;
        try
        {
            JsonObject root;
            try { root = JsonNode.Parse(File.ReadAllText(ConfigPath), documentOptions: lenient) as JsonObject ?? []; }
            catch
            {
                // config illeggibile: prima di sovrascriverlo ne tiene una copia
                if (File.Exists(ConfigPath)) File.Copy(ConfigPath, Path.Combine(Dir, "config.broken.json"), true);
                root = [];
            }
            var old = root["servers"] as JsonObject;
            var servers = new JsonObject();
            foreach (var s in Servers)
            {
                var o = old?[s.Key]?.DeepClone() as JsonObject ?? [];
                o["name"] = s.Name;
                o["host"] = s.Host;
                o["user"] = s.User;
                o["path"] = s.Path;
                if (s.Port is int p) o["port"] = p; else o.Remove("port");
                if (!string.IsNullOrWhiteSpace(s.IdentityFile)) o["identity_file"] = s.IdentityFile; else o.Remove("identity_file");
                servers[s.Key] = o;
            }
            root["active"] = Active;
            root["servers"] = servers;
            root["hotkey"] = Hotkey;
            root["notifications"] = NotifySuccess;
            WriteAtomic(ConfigPath, root.ToJsonString(JsonOut));
            LoadError = null;
        }
        catch (Exception e) { Log.Error($"Can't save {ConfigPath}: {e.Message}"); }
    }

    public static void AddHistory(string remote, string server)
    {
        History.Insert(0, new Upload(remote, server, DateTime.Now));
        if (History.Count > 15) History.RemoveRange(15, History.Count - 15);
        if (Demo) return;
        try { WriteAtomic(HistoryPath, JsonSerializer.Serialize(History, historyJson)); }
        catch (Exception e) { Log.Warn($"Can't save the upload history: {e.Message}"); }
    }

    public static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    // Chiave per un nuovo server, ricavata dal nome: "Hetzner prod" -> "hetzner-prod"
    public static string NewKey(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        if (slug == "") slug = "server";
        var key = slug;
        for (int n = 2; used.Contains(key); n++) key = $"{slug}-{n}";
        return key;
    }

    public static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static int? Int(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
    static bool? Bool(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    // Dati finti per gli screenshot del README
    static void LoadDemo()
    {
        Servers =
        [
            new() { Key = "main", Name = "Hetzner", Host = "dev.example.com", User = "root", Path = "/tmp/screenshots" },
            new() { Key = "staging", Name = "Staging", Host = "staging.example.com", User = "deploy", Path = "/home/deploy/screenshots" },
        ];
        Active = "main";
        var now = DateTime.Now;
        History =
        [
            new("/tmp/screenshots/screenshot-20261111-143012.png", "Hetzner", now.AddMinutes(-2)),
            new("/tmp/screenshots/screenshot-20261111-141544.png", "Hetzner", now.AddMinutes(-17)),
            new("/tmp/screenshots/screenshot-20261111-140210.png", "Hetzner", now.AddMinutes(-32)),
            new("/tmp/screenshots/screenshot-20261111-134402.png", "Hetzner", now.AddMinutes(-47)),
        ];
    }
}
