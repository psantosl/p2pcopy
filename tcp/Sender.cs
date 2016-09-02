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
            {
                p2pcopy.Sender.Run(reader, writer, file);
            }

            Console.WriteLine("{0} ms", Environment.TickCount - ini);
        }
    }
}
