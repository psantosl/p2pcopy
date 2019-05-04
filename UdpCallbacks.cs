using System;
using PseudoTcp;
using System.IO;
using System.Diagnostics;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;

namespace p2pcopy
{
    public class UdpCallbacks
    {
        const int NAT_TRAVERSAL_TRIES = 5;
        const int MAX_TRAVERSAL_TIME = 5000;
        const int PUNCH_PKTS_PER_TRY = 5;
        byte[] PUNCH_PKT = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        PseudoTcpSocket pseudoSock;
        string localAddr;
        int localPort;
        string remoteAddr;
        int remotePort;
        UdpClient udpc;
        IPEndPoint endReceiveRemoteEP;
        Socket underlyingSock;

        public bool Init (
            string localAddr, int localPort, string remoteAddr, int remotePort,
            PseudoTcpSocket pseudoSock, Socket underlyingSock,
            bool isSender)
        {
            this.localAddr = localAddr;
            this.localPort = localPort;
            this.remoteAddr = remoteAddr;
            this.remotePort = remotePort;
            this.pseudoSock = pseudoSock;
            this.underlyingSock = underlyingSock;

            udpc = new UdpClient();
            udpc.Client = underlyingSock;
            udpc.Connect (new IPEndPoint (IPAddress.Parse(remoteAddr), remotePort));
            endReceiveRemoteEP = new IPEndPoint(IPAddress.Any,0);

            Console.WriteLine("After reusing existing sock:");
            Console.WriteLine ("UdpClient.Client.LocalEndPoint=" + udpc.Client.LocalEndPoint);
            Console.WriteLine ("UdpClient.Client.RemoteEndPoint=" + udpc.Client.RemoteEndPoint);
            Console.WriteLine ("underlyingSock.LocalEndPoint=" + underlyingSock.LocalEndPoint);

            if (TryNatTraversal (isSender))
            {
                BeginReceive();
                return true;
            }
            else
            {
                Console.WriteLine("NAT traversal failed");
                return false;
            }
        }

        private bool TryNatTraversal(bool isSender)
        {
            Console.WriteLine ("Attempting NAT traversal:");
            bool success = false;
            int traversalStart = Environment.TickCount;
            for (int i = 0;
                    i < NAT_TRAVERSAL_TRIES && Environment.TickCount < traversalStart + MAX_TRAVERSAL_TIME;
                    i++) {
                try {
                    IPEndPoint ep = new IPEndPoint (IPAddress.Parse (localAddr), localPort);
                    if (false == isSender) {
                        SendPunchPackets();
                        success = ReceivePunchPackets(traversalStart);
                    } else {
                        success = ReceivePunchPackets(traversalStart);
                        SendPunchPackets();
                    }
                } catch (Exception e) {
                    PLog.DEBUG ("Exception {0}", e);
                }
            }

            Console.WriteLine("\nNAT traversal pass {0}", success ? "succeeded":"failed");
            return success;
        }

        private void SendPunchPackets()
        {
            for (int j=0; j<PUNCH_PKTS_PER_TRY; j++)
            {
                Console.Write (" >");
                udpc.Send (PUNCH_PKT, PUNCH_PKT.Length);
            }
            PLog.DEBUG ("\nSent punch packets");
        }

        private bool ReceivePunchPackets(int traversalStart)
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any,0);
            byte[] punch;
            int rxCount = 0;
            do {
                Console.Write (" <");
                punch = udpc.Receive (ref ep);
                rxCount += (ep.Equals(udpc.Client.RemoteEndPoint) && punch.Length == PUNCH_PKT.Length) ? 1:0;
                PLog.DEBUG ("\nReceived, endpoint now=" + ep + " received size=" + punch.Length);
            } while (rxCount < PUNCH_PKTS_PER_TRY && Environment.TickCount < traversalStart+MAX_TRAVERSAL_TIME);
            PLog.DEBUG ("Received punch packets");

            return rxCount >= PUNCH_PKTS_PER_TRY;
        }

        public void BeginReceive()
        {
            udpc.BeginReceive(new AsyncCallback(MessageReceived), null);
            PLog.DEBUG("Listening on UDP port {0}", localPort);
        }

        public void MessageReceived(IAsyncResult ar)
        {
            // Can happen during shutdown
            if (false==this.underlyingSock.Connected) {
                return;
            }

            byte[] receiveBytes = udpc.EndReceive(ar, ref endReceiveRemoteEP);
            PLog.DEBUG($"Received {0} bytes from {1}", receiveBytes.Length, endReceiveRemoteEP);
                
            SyncPseudoTcpSocket.NotifyPacket(pseudoSock, receiveBytes, (uint)receiveBytes.Length);
            SyncPseudoTcpSocket.NotifyClock(pseudoSock);

            BeginReceive(); // Listen again
        }
            
        public PseudoTcpSocket.WriteResult WritePacket(
            PseudoTcp.PseudoTcpSocket sock,
            byte[] buffer,
            uint len,
            object user_data)
        {
            try
            {
                this.udpc.Send(buffer, (int)len);
                PLog.DEBUG("Sent {0} bytes to UDPClient at {1}:{2}", len, remoteAddr, remotePort);
                return PseudoTcpSocket.WriteResult.WR_SUCCESS;
            }
            catch (Exception e)
            {
                Console.WriteLine (e.ToString ());
                return PseudoTcpSocket.WriteResult.WR_FAIL;
            }
        }

        public void Opened(PseudoTcp.PseudoTcpSocket sock, object data)
        {
            PLog.DEBUG ("UdpCallbacks.Opened");
        }

        public void Closed(PseudoTcpSocket sock, uint err, object data)
        {
            PLog.DEBUG ("UdpCallbacks.Closed: err={0}", err);
        }

        public void Writable (PseudoTcp.PseudoTcpSocket sock, object data)
        {
            PLog.DEBUG ("UdpCallbacks.Writeable");
        }

        public static void AdjustClock(PseudoTcp.PseudoTcpSocket sock)
        {
            ulong timeout = 0;

            if (SyncPseudoTcpSocket.GetNextClock(sock, ref timeout))
            {
                uint now = PseudoTcpSocket.GetMonotonicTime();

                if (now < timeout)
                    timeout -= now;
                else
                    timeout = now - timeout;

                if (timeout > 900)
                    timeout = 100;

                /// Console.WriteLine ("Socket {0}: Adjusting clock to {1} ms", sock, timeout);

                Timer timer = null;
                timer = new System.Threading.Timer(
                    (obj) =>
                    {
                        NotifyClock(sock);

                        // Very occasionally null (why?)
                        if (null!= timer) {
                            timer.Dispose();
                        }
                    },
                    null,
                    (long)timeout,
                    Timeout.Infinite);
            }
            else
            {
                /*left_closed = true;

                        if (left_closed && right_closed)
                            g_main_loop_quit (mainloop);*/
            }
        }

        static void NotifyClock(PseudoTcp.PseudoTcpSocket sock)
        {
            //g_debug ("Socket %p: Notifying clock", sock);
            SyncPseudoTcpSocket.NotifyClock(sock);
            AdjustClock(sock);
        }

        public static void PollingSleep(long value, long sleepIf)
        {
            CondSleep (50, value, sleepIf);
        }

        public static void CondSleep(int sleep, long value, long sleepIf)
        {
            if (value == sleepIf)
            {
                Thread.Sleep (sleep);
            }
        }
    }
}
