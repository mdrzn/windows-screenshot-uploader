// Impostazioni: server (aggiungi, modifica, rimuovi, prova, usa) e preferenze. Le modifiche valgono subito.
using System.Diagnostics;

namespace ScreenshotUploader;

sealed class SettingsForm : Form
{
    static readonly Color Ok = Color.FromArgb(0x2E, 0xA0, 0x43), Bad = Color.FromArgb(0xD9, 0x34, 0x2B);
    readonly TrayApp app;
    readonly ListView list = new()
    {
        View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4),
    };
    readonly Button add = new() { Text = L("Add…"), AutoSize = true };
    readonly Button edit = new() { Text = L("Edit…"), AutoSize = true };
    readonly Button remove = new() { Text = L("Remove"), AutoSize = true };
    readonly Button test = new() { Text = L("Test"), AutoSize = true };
    readonly Button use = new() { Text = L("Use for uploads"), AutoSize = true };
    readonly Label testResult = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    readonly CheckBox startup = new() { Text = L("Start with Windows"), AutoSize = true };
    readonly CheckBox notifications = new() { Text = L("Notify after every upload (errors are always shown)"), AutoSize = true };
    readonly CheckBox hotkeyOn = new() { Text = L("Shortcut to upload the clipboard image:"), AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    readonly HotkeyBox hotkey = new() { Width = 130, AccessibleName = L("Shortcut") };
    readonly Label hotkeyResult = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3), ForeColor = Bad };

    public SettingsForm(TrayApp app)
    {
        this.app = app;
        SuspendLayout();
        Text = L("Screenshot Uploader — Settings");
        Icon = Icons.App();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(560, 480);
        Padding = new Padding(14);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(Heading(L("Servers")));
        list.Columns.Add("", 26);
        list.Columns.Add(L("Name"), 140);
        list.Columns.Add(L("Address"), 400);
        root.Controls.Add(list);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = true };
        buttons.Controls.AddRange([add, edit, remove, test, use]);
        root.Controls.Add(buttons);
        root.Controls.Add(testResult);
        root.Controls.Add(Note(L("Uploads use the OpenSSH client built into Windows, with your SSH keys, ~/.ssh/config and ssh-agent, exactly like ssh in a terminal. No passwords are asked for or stored.")));

        root.Controls.Add(Heading(L("General")));
        root.Controls.Add(startup);
        root.Controls.Add(notifications);
        var hk = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        hk.Controls.AddRange([hotkeyOn, hotkey, hotkeyResult]);
        root.Controls.Add(hk);

        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 14, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var about = new LinkLabel { Text = $"Screenshot Uploader {TrayApp.Version} · made by mdrzn · GitHub", AutoSize = true, Anchor = AnchorStyles.Left, LinkColor = SystemColors.HotTrack };
        about.LinkArea = new LinkArea(about.Text.Length - 6, 6);
        about.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/mdrzn/windows-screenshot-uploader") { UseShellExecute = true });
        var close = new Button { Text = L("Close"), AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        footer.Controls.Add(about, 0, 0);
        footer.Controls.Add(close, 1, 0);
        root.Controls.Add(footer);
        Controls.Add(root);
        CancelButton = close;
        ResumeLayout();

        list.SelectedIndexChanged += (_, _) => UpdateButtons();
        list.DoubleClick += (_, _) => Edit();
        // la colonna dell'indirizzo occupa tutto lo spazio rimasto
        void Fill() => list.Columns[2].Width = Math.Max(120, list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width);
        list.Resize += (_, _) => Fill();
        Shown += (_, _) => Fill();
        add.Click += (_, _) => Add();
        edit.Click += (_, _) => Edit();
        remove.Click += (_, _) => Remove();
        test.Click += async (_, _) => { if (Selected is Server s) await RunTest(s, test, testResult, app); };
        use.Click += (_, _) => { if (Selected is Server s) app.SwitchServer(s.Key); };

        startup.CheckedChanged += (_, _) => { if (startup.Checked != AutoStart.Enabled) AutoStart.Enabled = startup.Checked; };
        notifications.CheckedChanged += (_, _) => { Store.NotifySuccess = notifications.Checked; Store.Save(); };
        hotkeyOn.CheckedChanged += (_, _) =>
        {
            hotkey.Enabled = hotkeyOn.Checked;
            SaveHotkey(hotkeyOn.Checked ? (hotkey.Text == "" ? "Ctrl+Alt+U" : hotkey.Text) : "");
        };
        hotkey.Changed += () => SaveHotkey(hotkey.Text);

        RefreshList();
        RefreshGeneral();
    }

    Server? Selected => list.SelectedItems.Count == 1 ? Store.Servers.FirstOrDefault(s => s.Key == (string)list.SelectedItems[0].Tag!) : null;

    public void RefreshList()
    {
        var selected = Selected?.Key ?? Store.ActiveServer?.Key;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var s in Store.Servers)
        {
            var item = new ListViewItem(s.Key == Store.ActiveServer?.Key ? "●" : "") { Tag = s.Key };
            item.SubItems.Add(s.Name);
            item.SubItems.Add(s.Display);
            list.Items.Add(item);
            if (s.Key == selected) item.Selected = true;
        }
        list.EndUpdate();
        UpdateButtons();
    }

    public void RefreshGeneral()
    {
        startup.Checked = AutoStart.Enabled;
        notifications.Checked = Store.NotifySuccess;
        hotkeyOn.Checked = Store.Hotkey != "";
        hotkey.Enabled = hotkeyOn.Checked;
        hotkey.Text = Store.Hotkey == "" ? "Ctrl+Alt+U" : Store.Hotkey;
    }

    void UpdateButtons()
    {
        var s = Selected;
        edit.Enabled = remove.Enabled = test.Enabled = s != null;
        use.Enabled = s != null && s.Key != Store.ActiveServer?.Key;
    }

    void SaveHotkey(string text)
    {
        var previous = Store.Hotkey;
        Store.Hotkey = text;
        if (app.ApplyHotkey())
        {
            hotkeyResult.Text = "";
            Store.Save();
        }
        else
        {
            hotkeyResult.Text = L("already used by another app");
            Store.Hotkey = previous;
            app.ApplyHotkey();
        }
    }

    void Add()
    {
        using var f = new ServerForm(null, app);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var s = f.Result!;
        s.Key = Store.NewKey(s.Name, Store.Servers.Select(x => x.Key));
        Store.Servers.Add(s);
        if (Store.Servers.Count == 1) Store.Active = s.Key;
        Saved();
    }

    void Edit()
    {
        if (Selected is not Server s) return;
        using var f = new ServerForm(s, app);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Store.Servers[Store.Servers.IndexOf(s)] = f.Result!;
        Saved();
    }

    void Remove()
    {
        if (Selected is not Server s) return;
        if (MessageBox.Show(this, L("Remove {0} ({1})?", s.Name, s.Target), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Store.Servers.Remove(s);
        if (Store.Active == s.Key) Store.Active = Store.Servers.FirstOrDefault()?.Key ?? "";
        Saved();
    }

    void Saved()
    {
        Store.Save();
        app.ServersChanged();
        RefreshList();
    }

    // Usato anche dalla finestra del server
    public static async Task RunTest(Server s, Button button, Label result, TrayApp app)
    {
        button.Enabled = false;
        result.ForeColor = SystemColors.GrayText;
        result.Text = L("Connecting to {0}…", s.Target);
        var (ok, message) = await Uploader.Test(s);
        if (result.IsDisposed) return;
        button.Enabled = true;
        result.ForeColor = ok ? Ok : Bad;
        result.Text = (ok ? "✓ " : "✗ ") + message;
        result.MaximumSize = new Size(result.Parent?.ClientSize.Width ?? 600, 0);
        Log.Info($"Test {s.Name} ({s.Target}): {(ok ? "OK" : "failed")} — {message}");
        if (ok && s.Key == Store.ActiveServer?.Key) app.ClearError();
    }

    public static Label Heading(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(0, 10, 0, 2),
        Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
    };

    public static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 4),
    };
}

// Aggiungi / modifica un server
sealed class ServerForm : Form
{
    readonly TrayApp app;
    readonly string key;
    readonly TextBox name = new(), host = new(), user = new(), port = new() { Width = 70 }, path = new(), identity = new();
    readonly Label result = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    public Server? Result;

    public ServerForm(Server? s, TrayApp app)
    {
        this.app = app;
        key = s?.Key ?? "";
        SuspendLayout();
        Text = s == null ? L("New server") : L("Edit server");
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Row(string label, Control input, Control? extra = null)
        {
            var r = grid.RowCount++;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 0) }, 0, r);
            if (input is TextBox && input.Width != 70) input.Dock = DockStyle.Fill;
            input.AccessibleName = label;
            grid.Controls.Add(input, 1, r);
            if (extra != null) grid.Controls.Add(extra, 2, r);
        }
        var browse = new Button { Text = L("Browse…"), AutoSize = true };
        browse.Click += (_, _) => Browse();
        Row(L("Name"), name);
        Row(L("Host"), host);
        Row(L("User"), user);
        Row(L("Port"), port, new Label { Text = L("optional (default 22)"), AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left });
        Row(L("Remote folder"), path);
        Row(L("Key file"), identity, browse);

        var hint = SettingsForm.Note(L("Only Name and Host are required. User, port and key can come from ~/.ssh/config, and Host can be an alias defined there. The remote folder is created if it's missing; ~/… works too."));
        hint.MaximumSize = new Size(480, 0);
        grid.Controls.Add(hint, 0, grid.RowCount);
        grid.SetColumnSpan(hint, 3);
        grid.RowCount++;

        var testButton = new Button { Text = L("Test"), AutoSize = true };
        var save = new Button { Text = L("Save"), AutoSize = true };
        var cancel = new Button { Text = L("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var bar = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(testButton, 0, 0);
        bar.Controls.Add(save, 2, 0);
        bar.Controls.Add(cancel, 3, 0);
        grid.Controls.Add(bar, 0, grid.RowCount);
        grid.SetColumnSpan(bar, 3);
        grid.RowCount++;
        result.MaximumSize = new Size(480, 0);
        grid.Controls.Add(result, 0, grid.RowCount);
        grid.SetColumnSpan(result, 3);
        grid.RowCount++;
        Controls.Add(grid);
        AcceptButton = save;
        CancelButton = cancel;
        ResumeLayout();

        name.Text = s?.Name ?? "";
        host.Text = s?.Host ?? "";
        user.Text = s?.User ?? "";
        port.Text = s?.Port?.ToString() ?? "";
        path.Text = s?.Path ?? "/tmp/screenshots";
        identity.Text = s?.IdentityFile ?? "";

        testButton.Click += async (_, _) => { if (ReadFields() is Server v) await SettingsForm.RunTest(v, testButton, result, app); };
        save.Click += (_, _) =>
        {
            if (ReadFields() is not Server v) return;
            Result = v;
            DialogResult = DialogResult.OK;
        };
    }

    // I campi come Server, oppure null (con il motivo sotto i pulsanti)
    Server? ReadFields()
    {
        string? Fail(string message, Control c) { result.ForeColor = Color.FromArgb(0xD9, 0x34, 0x2B); result.Text = message; c.Focus(); return null; }
        var h = host.Text.Trim();
        var u = user.Text.Trim();
        int? p = null;
        if (name.Text.Trim() == "") { Fail(L("Enter a name."), name); return null; }
        if (h == "" || h.Any(char.IsWhiteSpace) || h.StartsWith('-')) { Fail(L("Enter a valid host (name, IP address or ~/.ssh/config alias)."), host); return null; }
        if (u.Any(char.IsWhiteSpace) || u.Contains('@') || u.StartsWith('-')) { Fail(L("The user name can't contain spaces or @."), user); return null; }
        if (port.Text.Trim() != "")
        {
            if (!int.TryParse(port.Text.Trim(), out var n) || n is < 1 or > 65535) { Fail(L("The port must be a number between 1 and 65535."), port); return null; }
            p = n;
        }
        if (path.Text.Trim() == "") { Fail(L("Enter the remote folder."), path); return null; }
        var id = identity.Text.Trim();
        if (id != "" && !File.Exists(Environment.ExpandEnvironmentVariables(id))) { Fail(L("The key file doesn't exist."), identity); return null; }
        return new Server { Key = key, Name = name.Text.Trim(), Host = h, User = u, Port = p, Path = path.Text.Trim(), IdentityFile = id == "" ? null : id };
    }

    void Browse()
    {
        using var d = new OpenFileDialog
        {
            Title = L("Private key"),
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
            Filter = L("All files") + "|*.*",
        };
        if (d.ShowDialog(this) == DialogResult.OK) identity.Text = d.FileName;
    }
}

// Casella che registra la combinazione premuta: "Ctrl+Alt+U"
sealed class HotkeyBox : TextBox
{
    public event Action? Changed;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;
        if (modifiers == Keys.None && key == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
        if (Hotkeys.Valid(modifiers, key))
        {
            Text = Hotkeys.Format(modifiers, key);
            Changed?.Invoke();
        }
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
    }
}
