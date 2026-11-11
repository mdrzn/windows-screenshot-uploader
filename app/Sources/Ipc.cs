// Comandi da riga di comando inoltrati all'istanza gia' avviata, su una named pipe del solo utente corrente
using System.IO.Pipes;

namespace ScreenshotUploader;

static class Ipc
{
    static string Name => $"ScreenshotUploader-{Environment.UserName}{(Store.Demo ? "-demo" : "")}";

    public static void Send(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe);
            w.Write(command);
        }
        catch (Exception e) { Log.Warn($"Can't reach the running instance: {e.Message}"); }
    }

    public static void Listen(Action<string> onCommand) => new Thread(() =>
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                pipe.WaitForConnection();
                using var r = new StreamReader(pipe);
                var command = r.ReadToEnd().Trim();
                if (command != "") onCommand(command);
            }
            catch (Exception e)
            {
                Log.Warn($"Command pipe: {e.Message}");
                Thread.Sleep(1000);
            }
        }
    }) { IsBackground = true, Name = "ipc" }.Start();
}
