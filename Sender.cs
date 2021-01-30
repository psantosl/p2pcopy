using System;
using System.IO;
using UdtSharp;

namespace p2pcopy
{
    static class Sender
    {
        static internal void Run(UdtSocket conn, string file, bool bVerbose)
        {
            int ini = Environment.TickCount;

            using (var netStream = new UdtNetworkStream(conn))
            using (var writer = new BinaryWriter(netStream))
            using (var reader = new BinaryReader(netStream))
            using (var fileReader = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                // transmit your role and check if connected peer has the correct role
                writer.Write(Program.SenderRole);
                string role = reader.ReadString();
                if (role == Program.SenderRole)
                {
                    Console.Error.WriteLine("Peers can't have the same role.");
                    return;
                }

                long fileSize = new FileInfo(file).Length;

                writer.Write(Path.GetFileName(file));
                writer.Write(fileSize);

                byte[] buffer = new byte[512 * 1024];

                long pos = 0;

                int i = 0;

                ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);

                while (pos < fileSize)
                {
                    int toSend = buffer.Length < (fileSize - pos)
                        ? buffer.Length
                        : (int)(fileSize - pos);

                    fileReader.Read(buffer, 0, toSend);

                    int iteration = Environment.TickCount;

                    writer.Write(toSend);
                    conn.Send(buffer, 0, toSend);

                    if (!reader.ReadBoolean())
                    {
                        Console.WriteLine("Error in transmission");
                        return;
                    }

                    pos += toSend;

                    ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);

                    if (bVerbose)
                    {
                        Console.WriteLine();

                        Console.WriteLine("Current: {0} / s",
                            SizeConverter.ConvertToSizeString(toSend / (Environment.TickCount - iteration) * 1000));
                    }
                }
            }
        }
    }
}
