using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net;
using System.IO;

namespace p2pcopy
{
    class Program
    {
        static void Main(string[] args)
        {
            CommandLineArguments cla = CommandLineArguments.Parse(args);

            if (cla == null || (!cla.Sender && !cla.Receiver))
            {
                CommandLineArguments.ShowUsage();
                return;
            }

            if (cla.Tcp)
            {
                RunSimpleTcp(cla);
                return;
            }

            if (cla.TcpHolePunch)
            {
                RunTcpHolePunch(cla);
                return;
            }

            RunP2P(cla);
        }

        static void RunSimpleTcp(CommandLineArguments cla)
        {
            // tcp
            if (cla.Receiver)
            {
                tcp.Receiver.Receive(cla.LocalPort);
                return;
            }

            string remoteIp;
            int port;

            ParseRemoteAddr(cla.TcpRemotePeer, out remoteIp, out port);

            tcp.Sender.Send(remoteIp, port, cla.File);
        }

        static void RunTcpHolePunch(CommandLineArguments cla)
        {
            Console.WriteLine("Running tcp hole punch");

            string remoteIp;
            int port;

            ParseRemoteAddr(cla.TcpRemotePeer, out remoteIp, out port);

            Socket sock = tcpholepunch.TcpHolePunch.PeerConnect(
                remoteIp, port, cla.LocalPort);

            using (NetworkStream netStream = new NetworkStream(sock))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            {
                if (cla.Sender)
                    p2pcopy.Sender.Run(reader, writer, cla.File);
                else
                    p2pcopy.Receiver.Run(reader, writer);
            }
        }

        static void RunP2P(CommandLineArguments cla)
        {
            string remoteIp;
            int remotePort;

            Socket socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram, ProtocolType.Udp);

            try
            {

                if (cla.LocalPort != -1)
                {
                    Console.WriteLine("Using local port: {0}", cla.LocalPort);
                }
                else
                {
                    cla.LocalPort = 0;
                }

                socket.Bind(new IPEndPoint(IPAddress.Any, cla.LocalPort));

                ExternalEndPoint.P2pEndPoint p2pEndPoint = ExternalEndPoint.Get(socket);

                if (p2pEndPoint == null)
                    return;

                Console.WriteLine("Tell this to your peer: {0}", p2pEndPoint.External.ToString());

                Console.WriteLine();
                Console.WriteLine();

                Console.Write("Enter the ip:port of your peer: ");
                string peer = Console.ReadLine();

                if (string.IsNullOrEmpty(peer))
                {
                    Console.WriteLine("Invalid ip:port entered");
                    return;
                }

                // try again to connect to external to "reopen" port
                ExternalEndPoint.Get(socket);

                ParseRemoteAddr(peer, out remoteIp, out remotePort);

                Udt.Socket connection = UdtHolePunch.PeerConnect(socket, remoteIp, remotePort);

                if (connection == null)
                {
                    Console.WriteLine("Failed to establish P2P conn to {0}", remoteIp);
                    return;
                }

                try
                {
                    using (Udt.NetworkStream netStream = new Udt.NetworkStream(connection))
                    using (BinaryWriter writer = new BinaryWriter(netStream))
                    using (BinaryReader reader = new BinaryReader(netStream))
                    {
                        if (cla.Sender)
                        {
                            Sender.Run(reader, writer, cla.File);
                            return;
                        }

                        Receiver.Run(reader, writer);
                    }
                }
                finally
                {
                    connection.Close();
                }
            }
            finally
            {
                socket.Close();
            }
        }

        class CommandLineArguments
        {
            internal bool Sender = false;

            internal bool Receiver = false;

            internal string File;

            internal int LocalPort = -1;

            internal bool Tcp = false;
            internal string TcpRemotePeer;
            internal bool TcpHolePunch = false;

            static internal CommandLineArguments Parse(string[] args)
            {
                CommandLineArguments result = new CommandLineArguments();

                int i = 0;

                while (i < args.Length)
                {
                    string arg = args[i++];

                    switch (arg)
                    {
                        case "sender":
                            result.Sender = true;
                            break;
                        case "receiver":
                            result.Receiver = true;
                            break;
                        case "--tcp":
                            result.Tcp = true;
                            break;
                        case "--tcpholepunch":
                            result.TcpHolePunch = true;
                            break;
                        case "--file":
                            if (args.Length == i) return null;
                            result.File = args[i++];
                            break;
                        case "--tcpremotepeer":
                            if (args.Length == i) return null;
                            result.TcpRemotePeer = args[i++];
                            break;
                        case "--localport":
                            if (args.Length == i) return null;
                            result.LocalPort = int.Parse(args[i++]);
                            break;
                        case "help":
                            return null;
                    }
                }

                return result;
            }

            static internal void ShowUsage()
            {
                Console.WriteLine("p2pcopy [sender --file file_to_send |receiver]");
            }
        }

        static void ParseRemoteAddr(string addr, out string remoteIp, out int port)
        {
            string[] split = addr.Split(':');

            remoteIp = split[0];
            port = int.Parse(split[1]);
        }
    }
}
