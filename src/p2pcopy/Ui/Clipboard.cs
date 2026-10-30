using System.Text;

namespace P2pCopy.Ui;

/// <summary>
/// Copies text to the clipboard with the OSC 52 escape sequence. It needs no helper
/// program, works over SSH, and terminals that do not support it just ignore it.
/// </summary>
internal static class Clipboard
{
    public static bool TryCopy(string text)
    {
        try
        {
            if (Console.IsOutputRedirected) return false;
            var term = Environment.GetEnvironmentVariable("TERM");
            if (string.IsNullOrEmpty(term) || term == "dumb") return false;
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            var seq = "\u001b]52;c;" + payload + "\u0007";
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUX")))
                seq = "\u001bPtmux;\u001b" + seq.Replace("\u001b", "\u001b\u001b") + "\u001b\\";
            var stdout = Console.OpenStandardOutput();
            var bytes = Encoding.ASCII.GetBytes(seq);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
