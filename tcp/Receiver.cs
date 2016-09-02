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
                p2pcopy.Receiver.Run(reader, writer);
            }

            Console.WriteLine("{0} ms", Environment.TickCount - ini);
        }
    }
}
