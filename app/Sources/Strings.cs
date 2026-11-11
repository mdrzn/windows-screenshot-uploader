// Testi: inglese nel codice, italiano se Windows e' in italiano (--lang en|it per forzare la lingua).
using System.Globalization;

namespace ScreenshotUploader;

static class Strings
{
    public static bool Italian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";

    public static string L(string s) => Italian && it.TryGetValue(s, out var t) ? t : s;
    public static string L(string s, params object[] args) => string.Format(L(s), args);

    static readonly Dictionary<string, string> it = new()
    {
        // tray e notifiche
        ["Shortcut not available"] = "Scorciatoia non disponibile",
        ["{0} is already used by another app: choose another one in Settings."] = "{0} è già usata da un'altra app: scegline un'altra nelle impostazioni.",
        ["Settings file can't be read"] = "Impossibile leggere il file delle impostazioni",
        ["{0} — fix it or delete it, then restart the app."] = "{0} — correggilo o eliminalo, poi riavvia l'app.",
        ["Servers imported from the previous version"] = "Server importati dalla versione precedente",
        ["The previous version is still installed"] = "La versione precedente è ancora installata",
        ["Click here to turn off the Python version, so screenshots aren't uploaded twice."] = "Clic qui per disattivare la versione Python, così gli screenshot non vengono caricati due volte.",
        ["Screenshot Uploader is running"] = "Screenshot Uploader è attivo",
        ["Add a server to start uploading."] = "Aggiungi un server per iniziare a caricare.",
        ["Server not found"] = "Server non trovato",
        ["Nothing to upload"] = "Niente da caricare",
        ["The clipboard holds the path of the last upload, not an image."] = "Negli appunti c'è il percorso dell'ultimo upload, non un'immagine.",
        ["The clipboard doesn't contain an image."] = "Negli appunti non c'è un'immagine.",
        ["No server configured"] = "Nessun server configurato",
        ["Add a server in Settings first."] = "Aggiungi prima un server nelle impostazioni.",
        ["Path copied"] = "Percorso copiato",
        ["Uploaded"] = "Caricato",
        ["{0} → {1}. You copied something else in the meantime, so the clipboard was left alone: pick the upload under Recent uploads to copy its path."] =
            "{0} → {1}. Nel frattempo hai copiato altro, quindi gli appunti non sono stati toccati: scegli l'upload in Ultimi upload per copiarne il percorso.",
        ["Upload failed"] = "Upload non riuscito",
        ["Could not upload to {0}: {1}"] = "Impossibile caricare su {0}: {1}",
        ["Server switched"] = "Server cambiato",
        ["Uploads now go to {0} ({1})."] = "Ora gli upload vanno su {0} ({1}).",
        ["Uploading…"] = "Caricamento…",
        ["Paused — new images aren't uploaded"] = "In pausa — le nuove immagini non vengono caricate",
        ["Last upload failed"] = "L'ultimo upload non è riuscito",
        ["Active — new images are uploaded"] = "Attivo — le nuove immagini vengono caricate",
        ["Last upload failed — click to open the log"] = "L'ultimo upload non è riuscito — clic per aprire il log",
        ["Add a server in Settings"] = "Aggiungi un server nelle impostazioni",
        ["Upload clipboard image"] = "Carica l'immagine negli appunti",
        ["Recent uploads — click to copy the path"] = "Ultimi upload — clic per copiare il percorso",
        ["Server"] = "Server",
        ["Resume"] = "Riprendi",
        ["Pause"] = "Pausa",
        ["Turn off the previous version (Python)…"] = "Disattiva la versione precedente (Python)…",
        ["Settings…"] = "Impostazioni…",
        ["Open log"] = "Apri il log",
        ["Quit"] = "Esci",
        ["just now"] = "adesso",
        ["{0} min ago"] = "{0} min fa",
        ["{0} h ago"] = "{0} h fa",
        ["{0} d ago"] = "{0} g fa",
        ["The Python version of Screenshot Uploader is still set to start with Windows, so screenshots would be uploaded twice.\n\nTurn it off? Its startup script is moved to %APPDATA%\\ScreenshotUploader\\legacy-backup (not deleted), the running copy is stopped, and this version starts with Windows instead."] =
            "La versione Python di Screenshot Uploader parte ancora con Windows, quindi gli screenshot verrebbero caricati due volte.\n\nVuoi disattivarla? Il suo script di avvio viene spostato in %APPDATA%\\ScreenshotUploader\\legacy-backup (non eliminato), la copia in esecuzione viene fermata e al suo posto parte con Windows questa versione.",
        ["Previous version turned off"] = "Versione precedente disattivata",
        ["This version now starts with Windows."] = "Ora parte con Windows questa versione.",
        ["Can't turn off the previous version"] = "Impossibile disattivare la versione precedente",

        // upload
        ["SSH connection works — {0} is writable"] = "Connessione SSH funzionante — {0} è scrivibile",
        ["The OpenSSH client isn't installed. Add it in Windows Settings → System → Optional features → OpenSSH Client."] =
            "Il client OpenSSH non è installato. Aggiungilo da Impostazioni di Windows → Sistema → Funzionalità facoltative → Client OpenSSH.",
        ["No answer from the server after {0} seconds."] = "Nessuna risposta dal server dopo {0} secondi.",
        ["The server's host key doesn't match the one saved in known_hosts. If you trust the change, remove the old key with: ssh-keygen -R {0}"] =
            "La chiave dell'host non corrisponde a quella salvata in known_hosts. Se ti fidi del cambiamento, rimuovi la vecchia chiave con: ssh-keygen -R {0}",
        ["The server refused your SSH key (Permission denied). Install it with ssh-copy-id, or check the user name."] =
            "Il server ha rifiutato la tua chiave SSH (Permission denied). Installala sul server o controlla il nome utente.",
        ["Host not found: {0}"] = "Host non trovato: {0}",
        ["{0} is not reachable (connection timed out)."] = "{0} non è raggiungibile (connessione scaduta).",
        ["{0} refused the connection: is SSH running on that port?"] = "{0} ha rifiutato la connessione: SSH è attivo su quella porta?",
        ["The remote folder {0} can't be created or isn't writable."] = "La cartella remota {0} non si può creare o non è scrivibile.",
        ["ssh exited with code {0}"] = "ssh è uscito con codice {0}",
        ["The clipboard is in use by another app."] = "Gli appunti sono in uso da un'altra app.",

        // impostazioni
        ["Add…"] = "Aggiungi…",
        ["Edit…"] = "Modifica…",
        ["Remove"] = "Rimuovi",
        ["Test"] = "Prova",
        ["Use for uploads"] = "Usa per gli upload",
        ["Start with Windows"] = "Avvia con Windows",
        ["Notify after every upload (errors are always shown)"] = "Notifica dopo ogni upload (gli errori sono sempre mostrati)",
        ["Shortcut to upload the clipboard image:"] = "Scorciatoia per caricare l'immagine negli appunti:",
        ["Screenshot Uploader — Settings"] = "Screenshot Uploader — Impostazioni",
        ["Servers"] = "Server",
        ["Name"] = "Nome",
        ["Address"] = "Indirizzo",
        ["Uploads use the OpenSSH client built into Windows, with your SSH keys, ~/.ssh/config and ssh-agent, exactly like ssh in a terminal. No passwords are asked for or stored."] =
            "Gli upload usano il client OpenSSH incluso in Windows, con le tue chiavi SSH, ~/.ssh/config e ssh-agent, proprio come ssh in un terminale. Nessuna password viene chiesta o salvata.",
        ["General"] = "Generali",
        ["Close"] = "Chiudi",
        ["already used by another app"] = "già usata da un'altra app",
        ["Remove {0} ({1})?"] = "Rimuovere {0} ({1})?",
        ["Connecting to {0}…"] = "Connessione a {0}…",
        ["New server"] = "Nuovo server",
        ["Edit server"] = "Modifica server",
        ["Browse…"] = "Sfoglia…",
        ["Host"] = "Host",
        ["User"] = "Utente",
        ["Port"] = "Porta",
        ["optional (default 22)"] = "facoltativa (predefinita 22)",
        ["Remote folder"] = "Cartella remota",
        ["Key file"] = "File della chiave",
        ["Only Name and Host are required. User, port and key can come from ~/.ssh/config, and Host can be an alias defined there. The remote folder is created if it's missing; ~/… works too."] =
            "Servono solo Nome e Host. Utente, porta e chiave possono venire da ~/.ssh/config, e Host può essere un alias definito lì. La cartella remota viene creata se manca; va bene anche ~/…",
        ["Save"] = "Salva",
        ["Cancel"] = "Annulla",
        ["Enter a name."] = "Inserisci un nome.",
        ["Enter a valid host (name, IP address or ~/.ssh/config alias)."] = "Inserisci un host valido (nome, indirizzo IP o alias di ~/.ssh/config).",
        ["The user name can't contain spaces or @."] = "Il nome utente non può contenere spazi o @.",
        ["The port must be a number between 1 and 65535."] = "La porta deve essere un numero tra 1 e 65535.",
        ["Enter the remote folder."] = "Inserisci la cartella remota.",
        ["The key file doesn't exist."] = "Il file della chiave non esiste.",
        ["Private key"] = "Chiave privata",
        ["All files"] = "Tutti i file",
        ["Shortcut"] = "Scorciatoia",
        ["Active"] = "Attivo",
        ["Paused"] = "In pausa",
        ["Error"] = "Errore",
    };
}
