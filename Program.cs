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
            CommandLineArguments cla = CommandLineArguments.Parse(args);

            if (cla == null || (!cla.Sender && !cla.Receiver))
            {
                CommandLineArguments.ShowUsage();
                return;
            }

            P2pEndPoint p2pEndPoint = GetExternalEndPoint();

            if (p2pEndPoint == null)
                return;

            Console.WriteLine("Tell this to your peer: {0}", p2pEndPoint.External.ToString());

            Console.WriteLine();
            Console.WriteLine();

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
                RunSender(connection, cla.File);
                return;
            }

            RunReceiver(connection);
        }

        class CommandLineArguments
        {
            internal bool Sender = false;

            internal bool Receiver = false;

            internal string File;

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
                        case "--file":
                            if (args.Length == i) return null;
                            result.File = args[i++];
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

        static void RunSender(Udt.Socket conn, string file)
        {
            using (Udt.NetworkStream netStream = new Udt.NetworkStream(conn))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (FileStream reader = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                long fileSize = new FileInfo(file).Length;

                writer.Write(Path.GetFileName(file));
                writer.Write(fileSize);

                byte[] buffer = new byte[512 * 1024];

                long pos = 0;

                int i = 0;

                DrawProgress(i++, pos, fileSize, 20);

                while (pos < fileSize)
                {
                    int toSend = buffer.Length < (fileSize - pos)
                        ? buffer.Length
                        : (int) (fileSize - pos);

                    reader.Read(buffer, 0, toSend);

                    writer.Write(buffer, 0, toSend);

                    pos += toSend;

                    DrawProgress(i++, pos, fileSize, 20);
                }
            }
        }

        static void DrawProgress(int i, long transferred, long total, int width)
        {
            Console.Write("\r");

            char[] progress = new char[] { '-', '\\', '|', '/' };

            Console.Write(progress[i % 4]);

            int fillPos = (int)(((float)transferred) / ((float)total)) * width;
            string filled = new string('#', fillPos);
            string empty = new string('-', width - fillPos);
            Console.Write("[" + filled + empty + "]");
        }

        static void RunReceiver(Udt.Socket conn)
        {
            using (Udt.NetworkStream netStream = new Udt.NetworkStream(conn))
            using (BinaryReader reader = new BinaryReader(netStream))
            {
                string fileName = reader.ReadString();
                long size = reader.ReadInt64();

                byte[] buffer = new byte[512 + 1024];

                int i = 0;

                DrawProgress(i++, 0, size, 20);

                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    long read = 0;

                    while (read < size)
                    {
                        int fragmentRead = reader.Read(buffer, 0, buffer.Length);

                        fileStream.Write(buffer, 0, fragmentRead);

                        read += fragmentRead;

                        DrawProgress(i++, read, size, 20);
                    }
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

                    Console.Write("\r{0} - Trying to connect to {1}:{2}.  ",
                        retry++, remoteAddr, remotePort);

                    client.Connect(remoteAddr, remotePort);

                    Console.WriteLine("Connected successfully to {0}:{1}",
                        remoteAddr, remotePort);

                    bConnected = true;
                }
                catch (Exception e)
                {
                    Console.Write(e.Message.Replace(Environment.NewLine, ". "));
                }
            }

            return client;
        }

    }
}
