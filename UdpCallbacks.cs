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
            udpc.Connect (new IPEndPoint (IPAddress.Parse(remoteAddr), remotePort));
            endReceiveRemoteEP = new IPEndPoint(IPAddress.Any,0);

            PLog.VerboseWriteLine("After reusing existing sock:");
            PLog.VerboseWriteLine("UdpClient.Client.LocalEndPoint=" + udpc.Client.LocalEndPoint);
            PLog.VerboseWriteLine("UdpClient.Client.RemoteEndPoint=" + udpc.Client.RemoteEndPoint);
            PLog.VerboseWriteLine("underlyingSock.LocalEndPoint=" + underlyingSock.LocalEndPoint);

            BeginReceive();
            return true;
        }

        public void BeginReceive()
        {
            udpc.BeginReceive(new AsyncCallback(MessageReceived), null);
            PLog.DebugWriteLine("Listening on UDP endpoint {0}", underlyingSock.LocalEndPoint);
        }

        public void MessageReceived(IAsyncResult ar)
        {
            // Can happen during shutdown
            if (false==this.underlyingSock.Connected) {
                return;
            }

            byte[] receiveBytes = udpc.EndReceive(ar, ref endReceiveRemoteEP);
            PLog.DebugWriteLine("Received {0} bytes from {1}", receiveBytes.Length, endReceiveRemoteEP);
                
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
                PLog.DebugWriteLine("Sent {0} bytes to UDPClient at {1}:{2}", len, remoteAddr, remotePort);
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
            PLog.DebugWriteLine ("UdpCallbacks.Opened");
        }

        public void Closed(PseudoTcpSocket sock, uint err, object data)
        {
            PLog.DebugWriteLine ("UdpCallbacks.Closed: err={0}", err);
        }

        public void Writable (PseudoTcp.PseudoTcpSocket sock, object data)
        {
            PLog.DebugWriteLine ("UdpCallbacks.Writeable");
        }

        public static void AdjustClock(PseudoTcp.PseudoTcpSocket sock, Queue notifyClockQueue)
        {
            ulong timeout = 0;

            if (SyncPseudoTcpSocket.GetNextClock(sock, ref timeout))
            {
                PLog.DebugWriteLine ("AdjustClock: GetNextClock={0}", timeout);
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
                PLog.DebugWriteLine ("AdjustClock: didnt get timeout");

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
