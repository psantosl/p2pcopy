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
        PseudoTcpSocket pseudoSock;
        string localAddr;
        int localPort;
        string remoteAddr;
        int remotePort;
        UdpClient udpc;
        IPEndPoint rxEndpoint;
        Socket underlyingSock;

        public void Init (
            string localAddr, int localPort, string remoteAddr, int remotePort,
			PseudoTcpSocket pseudoSock, Socket underlyingSock)
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
            rxEndpoint = (IPEndPoint)underlyingSock.LocalEndPoint;

            Console.WriteLine("After reusing existing sock:");
            Console.WriteLine ("UdpClient.Client.LocalEndPoint=" + udpc.Client.LocalEndPoint);
            Console.WriteLine ("UdpClient.Client.RemoteEndPoint=" + udpc.Client.RemoteEndPoint);
            Console.WriteLine ("underlyingSock.LocalEndPoint=" + underlyingSock.LocalEndPoint);

            BeginReceive();
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

            byte[] receiveBytes = udpc.EndReceive(ar, ref rxEndpoint);
            PLog.DEBUG($"Received {0} bytes", receiveBytes.Length);
                
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

            if (sock.GetNextClock(ref timeout))
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

        public static void CondSleep(int sleep, long value, long sleepIf)
        {
            if (value == sleepIf)
            {
                Thread.Sleep (sleep);
            }
        }
    }
}
