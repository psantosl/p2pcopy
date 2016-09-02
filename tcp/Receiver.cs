using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace p2pcopy.tcp
{
    static class Receiver
    {
        static internal void Receive(int port)
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(new IPEndPoint(IPAddress.Any, port));

            socket.Listen(1);

            Console.WriteLine("Listening on {0}", port);

            Socket client = socket.Accept();

            Console.WriteLine("Connection accepted");

            int ini = Environment.TickCount;

            using (NetworkStream netStream = new NetworkStream(client))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            {
                string fileName = reader.ReadString();
                long size = reader.ReadInt64();

                byte[] buffer = new byte[4 * 1024 * 1024];

                int i = 0;

                ConsoleProgress.Draw(i++, 0, size, ini, Console.WindowWidth / 2);

                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    long read = 0;

                    while (read < size)
                    {
                        int toRecv = reader.ReadInt32();

                        ReadFragment(reader, toRecv, buffer);

                        fileStream.Write(buffer, 0, toRecv);

                        read += toRecv;

                        writer.Write(true);

                        ConsoleProgress.Draw(i++, read, size, ini, Console.WindowWidth / 2);
                    }
                }
            }

            Console.WriteLine("{0} ms", Environment.TickCount - ini);
        }

        static int ReadFragment(BinaryReader reader, int size, byte[] buffer)
        {
            int read = 0;

            while (read < size)
            {
                read += reader.Read(buffer, read, size - read);
            }

            return read;
        }
    }
}
