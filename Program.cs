using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net;
using UdtSharp;

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

                P2pEndPoint p2pEndPoint = GetExternalEndPoint(socket);

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
                GetExternalEndPoint(socket);

                ParseRemoteAddr(peer, out remoteIp, out remotePort);

                UdtSocket connection = PeerConnect(socket, remoteIp, remotePort);

                if (connection == null)
                {
                    Console.WriteLine("Failed to establish P2P conn to {0}", remoteIp);
                    return;
                }

                try
                {
                    if (args[0] == "sender")
                    {
                        Sender.Run(connection, cla.File, cla.Verbose);
                        return;
                    }

                    Receiver.Run(connection);
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

            internal bool Verbose = false;

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
                        case "--verbose":
                            result.Verbose = true;
                            break;
                        case "--file":
                            if (args.Length == i) return null;
                            result.File = args[i++];
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

        class P2pEndPoint
        {
            internal IPEndPoint External;
            internal IPEndPoint Internal;
        }

        static P2pEndPoint GetExternalEndPoint(Socket socket)
        {
            // https://gist.github.com/zziuni/3741933

            StunResult externalEndPoint = StunClient.Query("stun.l.google.com", 19302, socket);

            if (externalEndPoint.NetType == StunNetType.UdpBlocked)
            {
                Console.WriteLine("Your external IP can't be obtained. You are blocked :-(");
                return null;
            }

            Console.WriteLine("Your firewall is {0}", externalEndPoint.NetType.ToString());

            return new P2pEndPoint()
            {
                External = externalEndPoint.PublicEndPoint,
                Internal = (socket.LocalEndPoint as IPEndPoint)
            };
        }

        static int SleepTime(DateTime now)
        {
            List<int> seconds = new List<int>() {10, 20, 30, 40, 50, 60};

            int next = seconds.Find(x => x > now.Second);

            return next - now.Second;
        }

        static UdtSocket PeerConnect(Socket socket, string remoteAddr, int remotePort)
        {
            bool bConnected = false;
            int retry = 0;

            UdtSocket client = null;

            while (!bConnected)
            {
                try
                {
                    DateTime now = InternetTime.Get();

                    int sleepTimeToSync = SleepTime(now);

                    Console.WriteLine("[{0}] - Waiting {1} sec to sync with other peer",
                        now.ToLongTimeString(),
                        sleepTimeToSync);
                    System.Threading.Thread.Sleep(sleepTimeToSync * 1000);

                    GetExternalEndPoint(socket);

                    if (client != null)
                        client.Close();

                    client = new UdtSocket(socket.AddressFamily, socket.SocketType);
                    client.Bind(socket);

                    Console.Write("\r{0} - Trying to connect to {1}:{2}.  ",
                        retry++, remoteAddr, remotePort);

                    client.Connect(new IPEndPoint(IPAddress.Parse(remoteAddr), remotePort));

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
