using System;
using System.Net.Sockets;
using System.Net;
using System.IO;

namespace p2pcopy
{
    class Program
    {
        static void Main(string[] args)
        {
            P2pEndPoint p2pEndPoint = GetExternalEndPoint();

            if (p2pEndPoint == null)
                return;

            Console.WriteLine("Tell this ip:port to your peer: {0}", p2pEndPoint.External.ToString());

            Console.Write("Enter the ip:port of your peer: ");
            string peer = Console.ReadLine();

            string remoteIp;
            int remotePort;

            ParseRemoteAddr(peer, out remoteIp, out remotePort);

            Udt.Socket connection = PeerConnect(
                p2pEndPoint.Internal.Port, remoteIp, remotePort);

            if (connection == null)
            {
                Console.WriteLine("Failed to establish P2P conn to {0}", peer);
                return;
            }

            if (args[0] == "sender")
            {
                RunSender(connection);
                return;
            }

            RunReceiver(connection);
        }

        static void RunSender(Udt.Socket conn)
        {
            using (Udt.NetworkStream st = new Udt.NetworkStream(conn))
            using (BinaryWriter writer = new BinaryWriter(st))
            {
                while (true)
                {
                    writer.Write("This is the info I'm sending. Isn't it cool :P " + Environment.MachineName);
                }
            }
        }

        static void RunReceiver(Udt.Socket conn)
        {
            using (Udt.NetworkStream st = new Udt.NetworkStream(conn))
            using (BinaryReader reader = new BinaryReader(st))
            {
                while (true)
                {
                    Console.WriteLine(reader.ReadString());
                }
            }
        }

        static void ParseRemoteAddr(string addr, out string remoteIp, out int port)
        {
            string[] split = addr.Split(':');

            remoteIp = split[0];
            port = int.Parse(split[1]);
        }

        class P2pEndPoint
        {
            internal IPEndPoint External;
            internal IPEndPoint Internal;
        }

        static P2pEndPoint GetExternalEndPoint()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                // https://gist.github.com/zziuni/3741933

                StunResult externalEndPoint = StunClient.Query("stun.l.google.com", 19302, socket);
                Console.WriteLine(externalEndPoint.NetType.ToString());

                Console.WriteLine(socket.LocalEndPoint.ToString());

                if (externalEndPoint.NetType == StunNetType.UdpBlocked)
                {
                    Console.WriteLine("Your external IP can't be obtained. You are blocked :-(");
                    return null;
                }

                Console.WriteLine("Public endpoint: {0}. Local port: {1}",
                    externalEndPoint.PublicEndPoint.ToString(),
                    socket.LocalEndPoint.ToString());

                return new P2pEndPoint()
                {
                    External = externalEndPoint.PublicEndPoint,
                    Internal = (socket.LocalEndPoint as IPEndPoint)
                };
            }
            finally
            {
                socket.Close();
            }
        }

        static Udt.Socket PeerConnect(int localPort, string remoteAddr, int remotePort)
        {
            bool bConnected = false;
            int retry = 0;

            Udt.Socket client = null;

            while (!bConnected)
            {
                try
                {
                    if (client != null)
                        client.Close();

                    client = new Udt.Socket(AddressFamily.InterNetwork, SocketType.Stream);
                    client.ReuseAddress = true;

                    client.SetSocketOption(Udt.SocketOptionName.Rendezvous, true);

                    client.Bind(IPAddress.Any, localPort);

                    Console.WriteLine("{0} - Trying to connect to {1}:{2}. ",
                        retry++, remoteAddr, remotePort);

                    client.Connect(remoteAddr, remotePort);

                    Console.WriteLine("Connected successfully to {0}:{1}",
                        remoteAddr, remotePort);

                    bConnected = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            return client;
        }

    }
}
