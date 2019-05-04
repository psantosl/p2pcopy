using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Threading;
using PseudoTcp;

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
                socket.SetSocketOption (SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

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

                Console.WriteLine("p2pEndPoint external=" + p2pEndPoint.External);
                Console.WriteLine("p2pEndPoint internal=" + p2pEndPoint.Internal);

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

                PseudoTcpSocket connection = PeerConnect(
                    p2pEndPoint.External.Address.ToString(), p2pEndPoint.External.Port, 
                    socket, remoteIp, remotePort, cla);
                Console.WriteLine("Called PeerConnect");

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
                    }
                    else
                    {
                        Receiver.Run(connection);
                    }
                }
                finally
                {
                    connection.Close(false);
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

        static PseudoTcpSocket PeerConnect(string localAddr, int localPort,
                                            Socket socket, string remoteAddr, int remotePort,
                                            CommandLineArguments cla)
        {
            bool bConnected = false;
            int retry = 0;

            PseudoTcpSocket client = null;

            while (!bConnected)
            {
                try
                {
                    Console.WriteLine("Getting internet time");
                    DateTime now = InternetTime.Get();

                    int sleepTimeToSync = SleepTime(now);

                    Console.WriteLine("[{0}] - Waiting {1} sec to sync with other peer",
                        now.ToLongTimeString(),
                        sleepTimeToSync);
                    System.Threading.Thread.Sleep(sleepTimeToSync * 1000);

                    Console.WriteLine ("Before 2nd call to GetExternalEndPoint, socket.LocalEndPoint=" + socket.LocalEndPoint);
                    P2pEndPoint sanity = GetExternalEndPoint(socket);
                    Console.WriteLine ("After 2nd call to GetExternalEndPoint, socket.localEndPoint=" + socket.LocalEndPoint);
                    Console.WriteLine(" and external endpoint=" + ((sanity!=null) ? sanity.External.ToString():"null"));
                        
                    PseudoTcpSocket.Callbacks cbs = new PseudoTcpSocket.Callbacks();
                    UdpCallbacks icbs = new UdpCallbacks();
                    cbs.WritePacket = icbs.WritePacket;
                    cbs.PseudoTcpOpened = icbs.Opened;
                    cbs.PseudoTcpWritable = icbs.Writable;
                    cbs.PseudoTcpClosed = icbs.Closed;
                    client = PseudoTcpSocket.Create(0, cbs);
                    client.NotifyMtu(1496); // Per PseudoTcpTests
                    bool success = icbs.Init(localAddr, localPort, remoteAddr, remotePort, client, socket, cla.Sender);
                    if (false==success)
                    {
                        continue;
                    }
                    PLog.DEBUG("Created PseudoTcpSocket");

                    Console.WriteLine("\r{0} - Trying to connect to {1}:{2}.  ",
                        retry++, remoteAddr, remotePort);

                    if (cla.Sender) {
                        PLog.DEBUG("Sender: calling PseudoTcpSocket.Connect");
                        client.Connect();
                    }

                    int startTime = Environment.TickCount;
                    while (false==bConnected) {
                        Console.WriteLine("priv.state=={0}", client.priv.state);
                        if (PseudoTcpSocket.PseudoTcpState.Values.TCP_ESTABLISHED == client.priv.state) {
                            Console.WriteLine("Connected successfully to {0}:{1}",
                                remoteAddr, remotePort);

                            bConnected = true;
                        }
                        else {
                            if (Environment.TickCount > startTime+5000) {
                                Console.WriteLine("5 secs timed out with priv.state={0}", client.priv.state);
                                break;
                            }
                            else {
                                Console.WriteLine("Waiting for TCP_ESTABLISHED...");
                                Thread.Sleep(500);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message.Replace(Environment.NewLine, ". "));
                    Console.WriteLine (e.StackTrace);
                    Console.WriteLine ("Inner exception=" + e.InnerException);
                }
            }

            return client;
        }
    }
}
