using System;
using PseudoTcp;
using System.IO;
using System.Diagnostics;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections;

namespace p2pcopy
{
    public class UdpCallbacks
    {
        byte[] PUNCH_PKT = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        PseudoTcpSocket pseudoSock;
        string externalAddr;
        int externalPort;
        string remoteAddr;
        int remotePort;
        UdpClient udpc;
        IPEndPoint endReceiveRemoteEP;
        Socket underlyingSock;

        public bool Init (
            string externalAddr, int externalPort, string remoteAddr, int remotePort,
            PseudoTcpSocket pseudoSock, Socket underlyingSock,
            bool isSender, int nextTimeToSync)
        {
            this.externalAddr = externalAddr;
            this.externalPort = externalPort;
            this.remoteAddr = remoteAddr;
            this.remotePort = remotePort;
            this.pseudoSock = pseudoSock;
            this.underlyingSock = underlyingSock;

            udpc = new UdpClient();
            udpc.Client = underlyingSock;
            IPAddress remoteIP = Dns.GetHostEntry (remoteAddr).AddressList[0];
            udpc.Connect (new IPEndPoint (remoteIP, remotePort));
            endReceiveRemoteEP = new IPEndPoint(IPAddress.Any,0);

            Console.WriteLine("After reusing existing sock:");
            Console.WriteLine ("UdpClient.Client.LocalEndPoint=" + udpc.Client.LocalEndPoint);
            Console.WriteLine ("UdpClient.Client.RemoteEndPoint=" + udpc.Client.RemoteEndPoint);
            Console.WriteLine ("underlyingSock.LocalEndPoint=" + underlyingSock.LocalEndPoint);

            if (TryNatTraversal (isSender, nextTimeToSync))
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

        private bool TryNatTraversal(bool isSender, int timeToSync)
        {
            Console.WriteLine ("Attempting NAT traversal:");
            bool success = false;
            int traversalStart = Environment.TickCount;
            for (int i = 0;
                    i < Config.NAT_TRAVERSAL_TRIES && Environment.TickCount < traversalStart + Config.MAX_TRAVERSAL_TIME;
                    i++) {
                try {
                    if (false == isSender) {
                        SendPunchPackets(timeToSync);
                        success = ReceivePunchPackets(traversalStart, timeToSync);
                    } else {
                        success = ReceivePunchPackets(traversalStart, timeToSync);
                        SendPunchPackets(timeToSync);
                    }
                } catch (Exception e) {
                    PLog.DEBUG ("Exception {0}", e);
                }
            }

            Console.WriteLine("\nNAT traversal pass {0}", success ? "succeeded":"failed");
            return success;
        }

        private void SendPunchPackets(int timeToSync)
        {
            // Label punch packet with starting time of sync attempt
            byte[] punch = new byte[PUNCH_PKT.Length];
            Buffer.BlockCopy(PUNCH_PKT, 0, punch, 0, PUNCH_PKT.Length);
            punch[0] = (byte)timeToSync;

            for (int j=0; j<Config.PUNCH_PKTS_PER_TRY; j++)
            {
                Console.Write (" >");
                udpc.Send (punch, punch.Length);
            }
            PLog.DEBUG ("\nSent punch packets");
        }

        private bool ReceivePunchPackets(int traversalStart, int timeToSync)
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any,0);
            byte[] punch;
            int rxCount = 0;
            do {
                Console.Write (" <");
                try
                {
                    punch = udpc.Receive (ref ep);
                    rxCount += (ep.Equals(udpc.Client.RemoteEndPoint) && IsPunchPacket(punch, timeToSync)) ? 1:0;
                    PLog.DEBUG ("\nReceived pkt, endpoint now={0}, received size={1}", punch, punch.Length);
                }
                catch (Exception e)
                {
                    Console.Write("E");
                    PLog.DEBUG("ReceivePunchPackets: Exception from UdpClient.Receive: {0}", e);
                }
            } while (rxCount < Config.PUNCH_PKTS_PER_TRY && Environment.TickCount < traversalStart+Config.MAX_TRAVERSAL_TIME);
            PLog.DEBUG ("Received {0} punch packets", rxCount);

            return rxCount >= Config.PUNCH_PKTS_PER_TRY;
        }

        private bool IsPunchPacket(byte[] pkt, int timeToSync)
        {
            if (pkt.Length != PUNCH_PKT.Length || pkt [0] != (byte)timeToSync)
            {
                return false;
            }
            else
            {
                for (int i = 1; i < pkt.Length; i++)
                {
                    if (pkt [i] != PUNCH_PKT [i])
                        return false;
                }
            }

            return true;
        }

        public void BeginReceive()
        {
            udpc.BeginReceive(new AsyncCallback(MessageReceived), null);
            PLog.DEBUG("Listening on UDP endpoint {0}", underlyingSock.LocalEndPoint);
        }

        public void MessageReceived(IAsyncResult ar)
        {
            // Can happen during shutdown
            if (false==this.underlyingSock.Connected) {
                return;
            }

            byte[] receiveBytes = udpc.EndReceive(ar, ref endReceiveRemoteEP);
            PLog.DEBUG("Received {0} bytes from {1}", receiveBytes.Length, endReceiveRemoteEP);
                
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

        public static void AdjustClock(PseudoTcp.PseudoTcpSocket sock, Queue notifyClockQueue)
        {
            ulong timeout = 0;

            if (SyncPseudoTcpSocket.GetNextClock(sock, ref timeout))
            {
                PLog.DEBUG ("AdjustClock: GetNextClock={0}", timeout);
                uint now = PseudoTcpSocket.GetMonotonicTime();

                if (now < timeout)
                    timeout -= now;
                else
                    timeout = now - timeout;

                //Console.WriteLine ("...original timeout={0}", timeout);

                //if (timeout > 900)
                //    timeout = 100;

                /// Console.WriteLine ("Socket {0}: Adjusting clock to {1} ms", sock, timeout);

                notifyClockQueue.Enqueue (Environment.TickCount + (int)timeout);
            }
            else
            {
                PLog.DEBUG ("AdjustClock: didnt get timeout");

                /*left_closed = true;

                        if (left_closed && right_closed)
                            g_main_loop_quit (mainloop);*/
            }
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
