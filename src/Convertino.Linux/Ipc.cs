using System.Net.Sockets;
using System.Text;

namespace Convertino;

/// <summary>Single instance: later launches ("convertino menu files…") hand their request to the running app over a Unix socket.</summary>
public static class Ipc
{
    public static string SocketPath
    {
        get
        {
            // a LaunchAgent and a Terminal don't share $TMPDIR on macOS; the settings folder is stable
            if (OperatingSystem.IsMacOS()) return Path.Combine(Paths.AppData, "convertino.sock");
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
            return Path.Combine(dir, $"convertino-{Environment.UserName}.sock");
        }
    }

    /// <summary>Hand a request to the running instance. False when none is running.</summary>
    public static bool Send(string cmd, IEnumerable<string> files)
    {
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            var payload = cmd + "\n" + string.Join("\n", files) + "\n\n";
            s.Send(Encoding.UTF8.GetBytes(payload));
            return true;
        }
        catch { return false; }
    }
}
