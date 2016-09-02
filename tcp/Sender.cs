using System;
using System.IO;
using System.Net.Sockets;

namespace p2pcopy.tcp
{
    static class Sender
    {
        static internal void Send(string host, int port, string file)
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

            socket.Connect(host, port);

            Console.WriteLine("Connected to {0}:{1}", host, port);

            int ini = Environment.TickCount;

            using (NetworkStream netStream = new NetworkStream(socket))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            using (FileStream fileReader = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
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
                    socket.Send(buffer, 0, toSend, SocketFlags.None);

                    if (!reader.ReadBoolean())
                    {
                        Console.WriteLine("Error in transmission");
                        return;
                    }

                    pos += toSend;

                    ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);
                }
            }

            Console.WriteLine("{0} ms", Environment.TickCount - ini);
        }
    }
}
