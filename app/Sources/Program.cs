// Avvio: una sola istanza per utente; i comandi da riga di comando vanno all'istanza gia' avviata.
//   ScreenshotUploader.exe [--upload | --pause | --resume | --toggle | --server <nome> | --settings | --quit]
//   --lang en|it    forza la lingua
//   --demo [menu|settings]   server e upload finti (screenshot del README): non legge ne' scrive il config
//   --render-docs <cartella> con --demo: salva menu.png, settings.png e states.png per il README ed esce
//   --export-icon <file.ico> scrive l'icona dell'app
global using static ScreenshotUploader.Strings;

namespace ScreenshotUploader;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string? command = null, demo = null, docs = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i].ToLowerInvariant())
            {
                case "--lang": Strings.Italian = Next() == "it"; break;
                case "--export-icon": Icons.ExportAppIcon(Next()); return 0;
                case "--demo": Store.Demo = true; demo = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? Next() : ""; break;
                case "--render-docs": docs = Next(); Store.Demo = true; break;
                case "--server": command = "server:" + Next(); break;
                case "--upload": command = "upload"; break;
                case "--pause": command = "pause"; break;
                case "--resume": command = "resume"; break;
                case "--toggle": command = "toggle"; break;
                case "--settings": command = "settings"; break;
                case "--menu": command = "menu"; break;
                case "--quit": command = "quit"; break;
            }
        }

        using var mutex = new Mutex(true, Store.Demo ? @"Local\ScreenshotUploader.Demo" : @"Local\ScreenshotUploader", out bool first);
        if (!first)
        {
            // gia' in esecuzione: lanciarla di nuovo apre le impostazioni
            Ipc.Send(command ?? "settings");
            return 0;
        }
        if (command == "quit") return 0;

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // tema chiaro/scuro di Windows (API ancora "experimental" in .NET 10)
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        var app = new TrayApp();
        Ipc.Listen(app.Run);
        if (docs != null) app.RenderDocs(docs);
        else if (demo is "menu" or "settings") app.Run(demo);
        else if (command != null) app.Run(command);
        Application.Run(app);
        return 0;
    }
}
