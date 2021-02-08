using System;
using System.IO;
using UdtSharp;

namespace p2pcopy
{
    static class Receiver
    {
        const Roles myRole = Roles.Receiver;

        static internal void Run(UdtSocket conn)
        {
            int ini = Environment.TickCount;

            using (var netStream = new UdtNetworkStream(conn))
            using (var writer = new BinaryWriter(netStream))
            using (var reader = new BinaryReader(netStream))
            {
                // transmit your role and check if connected peer has the opposite role
                writer.Write(myRole.ToString());
                string role = reader.ReadString();

                if (role == myRole.ToString())
                {
                    Console.Error.WriteLine("Peers can't have the same role.");
                    Environment.Exit(1);
                    return;
                }

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
