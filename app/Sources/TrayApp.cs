// L'app nella tray: immagine nuova negli appunti -> upload -> il percorso remoto prende il suo posto.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ScreenshotUploader;

enum State { Ready, Uploading, Paused, Error }

sealed class TrayApp : ApplicationContext
{
    readonly SynchronizationContext ui;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new() { ShowItemToolTips = true };
    readonly MessageWindow window = new();
    readonly System.Windows.Forms.Timer settle = new() { Interval = 250 };
    readonly SemaphoreSlim queue = new(1, 1);
    readonly Dictionary<State, Icon> icons = [];
    SettingsForm? settings;
    bool paused;
    int busy;
    string? lastError;
    uint lastSequence;
    // ultima immagine rilevata: hash dei byte, quando, l'ultima volta che e' stata scritta negli appunti, il suo upload
    string lastHash = "";
    DateTime lastHashTime;
    uint lastHashSequence;
    string? lastHashRemote;
    Action? onBalloonClick;

    public State State => busy > 0 ? State.Uploading : paused ? State.Paused : lastError != null ? State.Error : State.Ready;

    public TrayApp()
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        ui = SynchronizationContext.Current!;
        Store.Load();
        foreach (var s in Enum.GetValues<State>()) icons[s] = Icons.Tray(s);

        // all'avvio non si carica quello che e' gia' negli appunti: conta solo cio' che cambia da adesso
        lastSequence = Clip.Sequence;
        window.ClipboardChanged += () =>
        {
            // alcune app (Strumento di cattura…) scrivono gli appunti in piu' passaggi: si legge quando si calmano
            if (paused || Store.Demo) return;
            settle.Stop();
            settle.Start();
        };
        settle.Tick += (_, _) => { settle.Stop(); OnClipboardChanged(); };
        window.HotkeyPressed += () => { Log.Info($"Shortcut {Store.Hotkey}"); UploadClipboard(); };

        tray = new NotifyIcon { ContextMenuStrip = menu };
        menu.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenuAtCursor(); };
        tray.BalloonTipClicked += (_, _) => { var a = onBalloonClick; onBalloonClick = null; a?.Invoke(); };
        tray.BalloonTipClosed += (_, _) => onBalloonClick = null;
        UpdateState();
        tray.Visible = true;

        Log.Info($"Started {Version}, active server: {Store.ActiveServer?.Name ?? "none"}");
        if (!ApplyHotkey())
            Notify(L("Shortcut not available"), L("{0} is already used by another app: choose another one in Settings.", Store.Hotkey), true, ShowSettings);
        StartupNotice();
    }

    public static string Version => Application.ProductVersion.Split('+')[0];

    void StartupNotice()
    {
        if (Store.Demo) return;
        if (Store.LoadError != null)
        {
            lastError = Store.LoadError;
            UpdateState();
            Notify(L("Settings file can't be read"), L("{0} — fix it or delete it, then restart the app.", Store.LoadError), true, OpenFolder);
        }
        else if (Legacy.Present)
            Notify(Store.ImportedFrom != null ? L("Servers imported from the previous version") : L("The previous version is still installed"),
                   L("Click here to turn off the Python version, so screenshots aren't uploaded twice."), false, AskDisableLegacy);
        else if (Store.Servers.Count == 0)
        {
            Notify(L("Screenshot Uploader is running"), L("Add a server to start uploading."), false, ShowSettings);
            ShowSettings();
        }
    }

    // ------------------------------------------------------------ comandi (menu, riga di comando)
    public void Run(string command) => ui.Post(_ => Execute(command), null);

    void Execute(string command)
    {
        Log.Info($"Command: {command}");
        switch (command)
        {
            case "settings": ShowSettings(); break;
            case "upload": UploadClipboard(); break;
            case "pause": SetPaused(true); break;
            case "resume": SetPaused(false); break;
            case "toggle": SetPaused(!paused); break;
            case "menu": ShowMenuAtIcon(); break;
            case "quit": Quit(); break;
            default:
                if (command.StartsWith("server:"))
                {
                    var name = command[7..];
                    var s = Store.Servers.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                            ?? Store.Servers.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (s != null) SwitchServer(s.Key);
                    else Notify(L("Server not found"), name, true);
                }
                break;
        }
    }

    // ------------------------------------------------------------ appunti
    void OnClipboardChanged()
    {
        var sequence = Clip.Sequence;
        if (paused || sequence == lastSequence) return;
        lastSequence = sequence;
        byte[]? png;
        try { png = Clip.ReadImage(window.Handle); }
        catch (Exception e) { Log.Warn($"Can't read the clipboard: {e.Message}"); return; }
        if (png == null) return; // testo, file, o il percorso scritto da noi

        // immagine nuova = byte diversi. La stessa immagine riscritta subito dopo (alcune app lo fanno) non si
        // ricarica: il percorso dell'upload gia' fatto, o di quello in corso, va al posto di quest'ultima copia
        var hash = Convert.ToHexString(SHA256.HashData(png));
        if (hash == lastHash && DateTime.Now - lastHashTime < TimeSpan.FromSeconds(3))
        {
            Log.Info("Same image written again: not uploaded twice");
            lastHashSequence = sequence;
            if (lastHashRemote != null) CopyIfUnchanged(lastHashRemote, sequence);
            return;
        }
        lastHash = hash;
        lastHashTime = DateTime.Now;
        lastHashSequence = sequence;
        lastHashRemote = null;
        Log.Info($"New image in the clipboard ({png.Length / 1024} KB)");
        _ = Upload(png, sequence, hash);
    }

    bool SameImage(string hash)
    {
        try { return Clip.ReadImage(window.Handle) is byte[] png && Convert.ToHexString(SHA256.HashData(png)) == hash; }
        catch { return false; }
    }

    bool CopyIfUnchanged(string remote, uint sequence)
    {
        if (Clip.Sequence != sequence || !Clip.SetText(window.Handle, remote)) return false;
        lastSequence = Clip.Sequence;
        return true;
    }

    // A mano (menu, scorciatoia, --upload): funziona anche in pausa
    void UploadClipboard()
    {
        byte[]? png = null;
        try { png = Clip.ReadImage(window.Handle); }
        catch (Exception e) { Log.Warn($"Can't read the clipboard: {e.Message}"); }
        if (png == null)
        {
            Notify(L("Nothing to upload"), Clip.IsOwn ? L("The clipboard holds the path of the last upload, not an image.") : L("The clipboard doesn't contain an image."), false);
            return;
        }
        lastSequence = Clip.Sequence;
        _ = Upload(png, lastSequence);
    }

    async Task Upload(byte[] png, uint sequence, string? hash = null)
    {
        var server = Store.ActiveServer?.Clone();
        if (server == null || server.Host == "")
        {
            Notify(L("No server configured"), L("Add a server in Settings first."), true, ShowSettings);
            return;
        }
        busy++;
        UpdateState();
        await queue.WaitAsync();
        var name = Uploader.NewName();
        try
        {
            Log.Info($"Uploading {name} ({png.Length / 1024} KB) to {server.Name} ({server.Target})");
            var remote = await Uploader.Upload(server, png, name);
            lastError = null;
            Store.AddHistory(remote, server.Name);
            if (hash != null && hash == lastHash)
            {
                lastHashRemote = remote;
                sequence = lastHashSequence; // l'immagine potrebbe essere stata riscritta durante l'upload
            }
            // il percorso prende il posto dell'immagine solo se nel frattempo non e' stato copiato altro
            // (la stessa immagine riscritta da un'app non conta come "altro")
            if (CopyIfUnchanged(remote, sequence)
                || hash != null && Clip.Sequence is var now && SameImage(hash) && CopyIfUnchanged(remote, now))
            {
                Log.Info($"Uploaded {remote}, path copied");
                if (Store.NotifySuccess) Notify(L("Path copied"), $"{remote} → {server.Name}", false);
            }
            else
            {
                Log.Info($"Uploaded {remote}; the clipboard changed meanwhile, left alone");
                Notify(L("Uploaded"), L("{0} → {1}. You copied something else in the meantime, so the clipboard was left alone: pick the upload under Recent uploads to copy its path.", remote, server.Name), false);
            }
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Log.Error($"Upload to {server.Name} failed: {e.Message}");
            Notify(L("Upload failed"), L("Could not upload to {0}: {1}", server.Name, e.Message), true, ShowSettings);
        }
        finally
        {
            queue.Release();
            busy--;
            UpdateState();
        }
    }

    // ------------------------------------------------------------ stato
    void SetPaused(bool value)
    {
        paused = value;
        // alla ripresa non si carica quello che e' stato copiato durante la pausa
        if (!paused) lastSequence = Clip.Sequence;
        Log.Info(paused ? "Paused" : "Resumed");
        UpdateState();
    }

    public void SwitchServer(string key)
    {
        var s = Store.Servers.FirstOrDefault(x => x.Key == key);
        if (s == null) return;
        Store.Active = key;
        Store.Save();
        lastError = null;
        Log.Info($"Active server: {s.Name} ({s.Target})");
        UpdateState();
        settings?.RefreshList();
        if (Store.NotifySuccess) Notify(L("Server switched"), L("Uploads now go to {0} ({1}).", s.Name, s.Target), false);
    }

    public void ServersChanged()
    {
        if (Store.ActiveServer is null || Store.ActiveServer.Key != Store.Active) Store.Active = Store.ActiveServer?.Key ?? "";
        UpdateState();
    }

    public bool ApplyHotkey()
    {
        if (Store.Demo) return true;
        var ok = window.SetHotkey(Store.Hotkey);
        if (!ok) Log.Warn($"Shortcut {Store.Hotkey} not available");
        else if (Store.Hotkey != "") Log.Info($"Shortcut {Store.Hotkey} registered");
        return ok;
    }

    public void ClearError()
    {
        lastError = null;
        UpdateState();
    }

    string StatusText => State switch
    {
        State.Uploading => L("Uploading…"),
        State.Paused => L("Paused — new images aren't uploaded"),
        State.Error => L("Last upload failed"),
        _ => Store.ActiveServer == null ? L("No server configured") : L("Active — new images are uploaded"),
    };

    void UpdateState()
    {
        tray.Icon = icons[State];
        var text = $"Screenshot Uploader — {StatusText}" + (Store.ActiveServer is Server s ? $"\n{s.Name}" : "");
        tray.Text = text.Length > 127 ? text[..127] : text;
    }

    // ------------------------------------------------------------ menu
    void BuildMenu()
    {
        menu.Items.Clear();
        var size = SystemInformation.SmallIconSize.Width;

        // intestazione: stato (pallino colorato come l'icona) e server attivo. Etichette, non voci disattivate,
        // perche' una voce disattivata ingrigisce anche il pallino
        var dot = Icons.Dot(Icons.ColorOf(State), size);
        if (State == State.Error && lastError != null)
            menu.Items.Add(new ToolStripMenuItem(L("Last upload failed — click to open the log"), dot, (_, _) => OpenLog()) { ToolTipText = lastError });
        else
            menu.Items.Add(new ToolStripLabel(StatusText, dot) { ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 0, 1) });
        // immagine trasparente grande quanto il pallino: il testo si allinea a quello dello stato
        menu.Items.Add(new ToolStripLabel(Store.ActiveServer is Server a ? $"{a.Name} — {a.Display}" : L("Add a server in Settings"), new Bitmap(size, size))
        {
            ForeColor = SystemColors.GrayText,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 3),
        });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem(L("Upload clipboard image"), null, (_, _) => UploadClipboard())
        {
            ShortcutKeyDisplayString = Store.Hotkey,
            Enabled = Store.Demo || Clip.HasImage && Store.ActiveServer != null,
        });

        if (Store.History.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(L("Recent uploads — click to copy the path")) { Enabled = false });
            foreach (var u in Store.History.Take(10))
                menu.Items.Add(new ToolStripMenuItem(u.Remote.Split('/').Last(), null, (_, _) => CopyPath(u))
                {
                    ShortcutKeyDisplayString = Ago(u.Date),
                    ToolTipText = $"{u.Remote}\n{u.Server}",
                });
        }

        menu.Items.Add(new ToolStripSeparator());
        if (Store.Servers.Count > 0)
        {
            var servers = new ToolStripMenuItem(L("Server"));
            foreach (var s in Store.Servers)
            {
                var key = s.Key;
                servers.DropDownItems.Add(new ToolStripMenuItem(s.Name, null, (_, _) => SwitchServer(key))
                {
                    Checked = s.Key == Store.ActiveServer?.Key,
                    ShortcutKeyDisplayString = s.Target,
                });
            }
            menu.Items.Add(servers);
        }
        menu.Items.Add(new ToolStripMenuItem(paused ? L("Resume") : L("Pause"), null, (_, _) => SetPaused(!paused)));

        menu.Items.Add(new ToolStripSeparator());
        if (Legacy.Present)
            menu.Items.Add(new ToolStripMenuItem(L("Turn off the previous version (Python)…"), null, (_, _) => AskDisableLegacy()));
        menu.Items.Add(new ToolStripMenuItem(L("Settings…"), null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripMenuItem(L("Open log"), null, (_, _) => OpenLog()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L("Quit"), null, (_, _) => Quit()));
    }

    static string Ago(DateTime d)
    {
        var t = DateTime.Now - d;
        return t.TotalMinutes < 1 ? L("just now")
             : t.TotalHours < 1 ? L("{0} min ago", (int)t.TotalMinutes)
             : t.TotalDays < 1 ? L("{0} h ago", (int)t.TotalHours)
             : L("{0} d ago", (int)t.TotalDays);
    }

    void CopyPath(Upload u)
    {
        if (Clip.SetText(window.Handle, u.Remote))
        {
            lastSequence = Clip.Sequence;
            Log.Info($"Copied again: {u.Remote}");
        }
    }

    // Clic sinistro: lo stesso menu del clic destro
    void ShowMenuAtCursor() =>
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(tray, null);

    // --menu: apre il menu accanto all'icona (o nell'angolo della barra se l'icona e' nascosta nell'overflow),
    // verso il basso se la barra delle applicazioni e' in alto
    void ShowMenuAtIcon()
    {
        var screen = Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        var top = area.Top > screen.Bounds.Top;
        var point = top ? new Point(area.Right - 8, area.Top + 4) : new Point(area.Right - 8, area.Bottom - 4);
        if (IconRect() is Rectangle r && r.Width > 0)
        {
            top = r.Top < screen.Bounds.Height / 2;
            point = new Point(r.Left + r.Width / 2, top ? r.Bottom : r.Top);
        }
        SetForegroundWindow(window.Handle);
        menu.Show(point, top ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.AboveLeft);
    }

    Rectangle? IconRect()
    {
        try
        {
            var fields = typeof(NotifyIcon).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            var id = fields.First(f => f.FieldType == typeof(uint) && f.Name.Contains("id", StringComparison.OrdinalIgnoreCase)).GetValue(tray);
            var w = fields.First(f => typeof(NativeWindow).IsAssignableFrom(f.FieldType)).GetValue(tray) as NativeWindow;
            var nid = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = w!.Handle, uID = (uint)id! };
            return Shell_NotifyIconGetRect(ref nid, out var rect) == 0 ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
        }
        catch { return null; }
    }

    // --render-docs: immagini del README con i dati finti (menu, impostazioni, stati dell'icona), poi esce
    public void RenderDocs(string dir)
    {
        Directory.CreateDirectory(dir);
        var steps = new Queue<Action>();
        steps.Enqueue(() =>
        {
            var a = Screen.PrimaryScreen!.WorkingArea;
            BuildMenu();
            menu.Show(new Point(a.Left + a.Width / 2, a.Top + a.Height / 6));
        });
        steps.Enqueue(() =>
        {
            Capture(menu.Bounds, Path.Combine(dir, "menu.png"));
            menu.Close();
            ShowSettings();
            settings!.TopMost = true;
        });
        steps.Enqueue(() =>
        {
            Capture(WindowRect(settings!.Handle), Path.Combine(dir, "settings.png"));
            settings.Close();
            Icons.SaveStates(Path.Combine(dir, "states.png"), Enum.GetValues<State>().Select(s => (s, s switch
            {
                State.Ready => L("Active"),
                State.Uploading => L("Uploading…"),
                State.Paused => L("Paused"),
                _ => L("Error"),
            })));
            Quit();
        });
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => { if (steps.Count > 0) steps.Dequeue()(); else timer.Stop(); };
        timer.Start();
    }

    static void Capture(Rectangle r, string file)
    {
        using var b = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        b.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    // Rettangolo visibile della finestra (senza i bordi invisibili di Windows 10/11)
    static Rectangle WindowRect(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var r, Marshal.SizeOf<RECT>()) == 0
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
            : Control.FromHandle(hwnd)!.Bounds;

    // ------------------------------------------------------------ finestre e azioni
    public void ShowSettings()
    {
        if (settings == null || settings.IsDisposed) settings = new SettingsForm(this);
        settings.Show();
        if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
        settings.Activate();
    }

    void AskDisableLegacy()
    {
        var answer = MessageBox.Show(
            L("The Python version of Screenshot Uploader is still set to start with Windows, so screenshots would be uploaded twice.\n\nTurn it off? Its startup script is moved to %APPDATA%\\ScreenshotUploader\\legacy-backup (not deleted), the running copy is stopped, and this version starts with Windows instead."),
            "Screenshot Uploader", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        try
        {
            Legacy.Disable();
            settings?.RefreshGeneral();
            Notify(L("Previous version turned off"), L("This version now starts with Windows."), false);
        }
        catch (Exception e)
        {
            Log.Error($"Can't turn off the previous version: {e.Message}");
            Notify(L("Can't turn off the previous version"), e.Message, true);
        }
    }

    void OpenLog()
    {
        if (!File.Exists(Log.File)) Log.Info("Log opened");
        Process.Start(new ProcessStartInfo(Log.File) { UseShellExecute = true });
    }

    void OpenFolder() => Process.Start(new ProcessStartInfo(Store.Dir) { UseShellExecute = true });

    public void Notify(string title, string text, bool error, Action? onClick = null)
    {
        if (Store.Demo) return;
        onBalloonClick = onClick;
        tray.ShowBalloonTip(6000, title, text.Length > 250 ? text[..250] + "…" : text, error ? ToolTipIcon.Error : ToolTipIcon.None);
    }

    void Quit()
    {
        Log.Info("Quit");
        tray.Visible = false;
        window.Dispose();
        settings?.Close();
        ExitThread();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
}
