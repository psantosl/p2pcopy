using System.Text;

namespace P2pCopy.Ui;

/// <summary>
/// A line reader that can be cancelled, so we can wait for the peer to connect and for the
/// user to paste a code at the same time. Falls back to a blocking read when stdin is a pipe.
/// </summary>
internal static class ConsoleInput
{
    public static async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        if (Console.IsInputRedirected)
        {
            var reader = Console.In;
            var task = Task.Run(() => reader.ReadLine());
            var done = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            if (done != task) return null;
            return await task.ConfigureAwait(false);
        }

        var sb = new StringBuilder();
        while (true)
        {
            while (!Console.KeyAvailable)
            {
                if (ct.IsCancellationRequested) return null;
                await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
            }
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.Out.WriteLine();
                    return sb.ToString();
                case ConsoleKey.Backspace:
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Out.Write("\b \b");
                    }
                    break;
                case ConsoleKey.Escape:
                    break;
                default:
                    if (key.KeyChar == '\u0003') throw new OperationCanceledException();
                    if (key.KeyChar == '\u0016') break; // ctrl+v is handled by the terminal itself
                    if (!char.IsControl(key.KeyChar))
                    {
                        sb.Append(key.KeyChar);
                        Console.Out.Write(key.KeyChar);
                    }
                    break;
            }
        }
    }
}
