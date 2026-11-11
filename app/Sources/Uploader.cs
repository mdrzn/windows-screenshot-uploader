// Upload con l'OpenSSH incluso in Windows (C:\Windows\System32\OpenSSH\ssh.exe).
// Una sola connessione crea la cartella remota, scrive il file letto dallo stdin e restituisce il
// percorso assoluto. Chiavi di ogni tipo, ~/.ssh/config e ssh-agent funzionano come in un terminale;
// BatchMode: mai una password chiesta; accept-new: host fidato al primo contatto e poi fissato.
using System.Diagnostics;
using System.Text;

namespace ScreenshotUploader;

static class Uploader
{
    static string SshExe => Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");
    sealed record Result(int ExitCode, string Output, string Error);

    static string lastName = "";
    static int sameNameCount;

    // screenshot-20261111-143012.png (-2, -3… se nello stesso secondo)
    public static string NewName()
    {
        var name = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (name == lastName) return $"{name}-{++sameNameCount}.png";
        lastName = name;
        sameNameCount = 1;
        return name + ".png";
    }

    public static async Task<string> Upload(Server s, byte[] png, string name)
    {
        var dir = Quote(RemoteDir(s));
        var r = await Run(s, $"mkdir -p {dir} && cd {dir} && cat > {name} && pwd", png, 90);
        if (r.ExitCode != 0) throw new Exception(Explain(r, s));
        var pwd = r.Output.Split('\n').Last().Trim();
        return (pwd == "" ? RemoteDir(s) : pwd).TrimEnd('/') + "/" + name;
    }

    // Per il pulsante "Prova": stessa connessione dell'upload, controlla che la cartella sia scrivibile
    public static async Task<(bool ok, string message)> Test(Server s)
    {
        if (Store.Demo) { await Task.Delay(600); return (true, L("SSH connection works — {0} is writable", s.Path)); }
        try
        {
            var dir = Quote(RemoteDir(s));
            var r = await Run(s, $"mkdir -p {dir} && test -w {dir} && cd {dir} && pwd", null, 30);
            return r.ExitCode == 0
                ? (true, L("SSH connection works — {0} is writable", r.Output.Split('\n').Last().Trim()))
                : (false, Explain(r, s));
        }
        catch (Exception e) { return (false, e.Message); }
    }

    static async Task<Result> Run(Server s, string command, byte[]? input, int timeoutSeconds)
    {
        if (!File.Exists(SshExe))
            throw new Exception(L("The OpenSSH client isn't installed. Add it in Windows Settings → System → Optional features → OpenSSH Client."));
        var psi = new ProcessStartInfo(SshExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=10",
                                  "-o", "ServerAliveInterval=5", "-o", "ServerAliveCountMax=3" })
            psi.ArgumentList.Add(a);
        if (s.Port is int port) { psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(port.ToString()); }
        if (!string.IsNullOrWhiteSpace(s.IdentityFile))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(Environment.ExpandEnvironmentVariables(s.IdentityFile));
        }
        psi.ArgumentList.Add(s.Target);
        psi.ArgumentList.Add(command);

        using var p = Process.Start(psi) ?? throw new Exception("Can't start ssh.exe");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            try
            {
                if (input != null) await p.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
                p.StandardInput.Close();
            }
            catch (IOException) { /* ssh e' gia' uscito (connessione fallita): il motivo e' su stderr */ }
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return new Result(-1, "", L("No answer from the server after {0} seconds.", timeoutSeconds));
        }
        return new Result(p.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }

    // Messaggio comprensibile dall'errore di ssh
    static string Explain(Result r, Server s)
    {
        var lines = r.Error.Split('\n').Select(l => l.Trim())
            .Where(l => l != "" && !l.StartsWith("Warning: Permanently added") && !l.StartsWith("**"))
            .ToList();
        var all = string.Join("\n", lines);
        bool Has(string t) => all.Contains(t, StringComparison.OrdinalIgnoreCase);

        if (Has("REMOTE HOST IDENTIFICATION HAS CHANGED") || Has("Host key verification failed"))
            return L("The server's host key doesn't match the one saved in known_hosts. If you trust the change, remove the old key with: ssh-keygen -R {0}", s.Host);
        if (Has("Permission denied (publickey") || Has("Too many authentication failures"))
            return L("The server refused your SSH key (Permission denied). Install it with ssh-copy-id, or check the user name.");
        if (Has("Could not resolve hostname"))
            return L("Host not found: {0}", s.Host);
        if (Has("Connection timed out") || Has("timed out"))
            return L("{0} is not reachable (connection timed out).", s.Host);
        if (Has("Connection refused"))
            return L("{0} refused the connection: is SSH running on that port?", s.Host);
        if (r.ExitCode is not (255 or -1) && lines.Count == 0)
            return L("The remote folder {0} can't be created or isn't writable.", s.Path);
        return lines.LastOrDefault() ?? L("ssh exited with code {0}", r.ExitCode);
    }

    static string RemoteDir(Server s)
    {
        var d = s.Path.Trim().TrimEnd('/');
        return d == "" ? "/" : d;
    }

    // Percorso per la shell remota: ~ e ~/… restano espandibili, il resto tra apici singoli
    static string Quote(string path) =>
        path == "~" ? "\"$HOME\""
        : path.StartsWith("~/") ? "\"$HOME\"/" + SingleQuote(path[2..])
        : SingleQuote(path);

    static string SingleQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
