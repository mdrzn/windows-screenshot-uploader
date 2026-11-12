// Appunti via Win32, senza polling: una finestra nascosta riceve WM_CLIPBOARDUPDATE (e WM_HOTKEY).
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenshotUploader;

// Finestra message-only: AddClipboardFormatListener + RegisterHotKey
sealed class MessageWindow : NativeWindow, IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312, HotkeyId = 1;
    public event Action? ClipboardChanged;
    public event Action? HotkeyPressed;

    public MessageWindow()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        if (!AddClipboardFormatListener(Handle))
            Log.Error($"AddClipboardFormatListener failed ({Marshal.GetLastWin32Error()})");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE) ClipboardChanged?.Invoke();
        else if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId) HotkeyPressed?.Invoke();
        base.WndProc(ref m);
    }

    // "" toglie la scorciatoia; false se il testo non e' valido o un'altra app la usa gia'
    public bool SetHotkey(string text)
    {
        UnregisterHotKey(Handle, HotkeyId);
        if (text == "") return true;
        return Hotkeys.TryParse(text, out var mods, out var key)
               && RegisterHotKey(Handle, HotkeyId, mods | 0x4000 /* MOD_NOREPEAT */, (uint)key);
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(Handle);
        UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

static class Clip
{
    const uint CF_BITMAP = 2, CF_UNICODETEXT = 13;
    static readonly uint CF_PNG = RegisterClipboardFormat("PNG");
    static readonly uint CF_OWN = RegisterClipboardFormat("ScreenshotUploader.Path"); // marca il testo scritto da noi

    // Cambia a ogni modifica degli appunti (di qualsiasi app)
    public static uint Sequence => GetClipboardSequenceNumber();
    public static bool IsOwn => IsClipboardFormatAvailable(CF_OWN);
    // Immagine vera: non il nostro percorso e non file copiati da Esplora risorse
    // Conta l'immagine anche se accanto c'e' il suo file (CF_HDROP): lo Strumento di cattura con il
    // salvataggio automatico e la cronologia degli appunti copiano entrambi. I file copiati da Esplora
    // risorse non hanno dati immagine, quindi restano esclusi comunque.
    public static bool HasImage => !IsOwn && (IsClipboardFormatAvailable(CF_PNG) || IsClipboardFormatAvailable(CF_BITMAP));

    // L'immagine negli appunti come PNG (il formato "PNG" se c'e', altrimenti la bitmap); null se non c'e'
    public static byte[]? ReadImage(IntPtr owner)
    {
        if (!HasImage) return null;
        Bitmap? bitmap = null;
        if (!Open(owner)) throw new IOException(L("The clipboard is in use by another app."));
        try
        {
            if (IsClipboardFormatAvailable(CF_PNG) && Global(GetClipboardData(CF_PNG)) is { Length: > 8 } png
                && png[0] == 0x89 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G')
                return png;
            var hbitmap = GetClipboardData(CF_BITMAP);
            if (hbitmap == IntPtr.Zero) return null;
            bitmap = Image.FromHbitmap(hbitmap); // copia: l'handle resta degli appunti
        }
        finally { CloseClipboard(); }
        using (bitmap)
        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    public static bool SetText(IntPtr owner, string text)
    {
        if (!Open(owner)) return false;
        try
        {
            EmptyClipboard();
            return Put(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")) && Put(CF_OWN, [1]);
        }
        finally { CloseClipboard(); }
    }

    // Un'altra app puo' tenere gli appunti aperti per qualche millisecondo: riprova
    static bool Open(IntPtr owner)
    {
        for (int i = 0; i < 20; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(25);
        }
        Log.Warn("The clipboard is in use by another app");
        return false;
    }

    static byte[]? Global(IntPtr h)
    {
        if (h == IntPtr.Zero) return null;
        var size = (int)GlobalSize(h);
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(p, bytes, 0, size);
            return bytes;
        }
        finally { GlobalUnlock(h); }
    }

    static bool Put(uint format, byte[] bytes)
    {
        var h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)bytes.Length);
        if (h == IntPtr.Zero) return false;
        Marshal.Copy(bytes, 0, GlobalLock(h), bytes.Length);
        GlobalUnlock(h);
        if (SetClipboardData(format, h) != IntPtr.Zero) return true; // da qui la memoria e' degli appunti
        GlobalFree(h);
        return false;
    }

    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);
}

// Scorciatoie come testo: "Ctrl+Alt+U"
static class Hotkeys
{
    public static bool Valid(Keys modifiers, Keys key) =>
        (modifiers & (Keys.Control | Keys.Alt)) != 0
        && key is not (Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None);

    public static string Format(Keys modifiers, Keys key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((char)key).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var m in parts[..^1])
            modifiers |= m.ToLowerInvariant() switch
            {
                "ctrl" or "control" => 0x2u,
                "alt" => 0x1u,
                "shift" => 0x4u,
                "win" => 0x8u,
                _ => 0x10000u,
            };
        var last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0])) key = Keys.D0 + (last[0] - '0');
        else if (!Enum.TryParse(last, true, out key)) return false;
        return modifiers is > 0 and < 0x10000;
    }
}
