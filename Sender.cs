using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PseudoTcp;
using System.Threading;

namespace p2pcopy
{
    static class Sender
    {
        static byte[] ackBuffer = new byte[1];

        static internal void Run(PseudoTcpSocket conn, string file, bool bVerbose)
        {
            int ini = Environment.TickCount;

            using (FileStream fileReader = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                long fileSize = new FileInfo(file).Length;
                byte[] ackBuffer = new byte[1];

                PLog.DEBUG ("Sending filename {0}", Path.GetFileName (file));
                byte[] fileNameBytes = Encoding.UTF8.GetBytes (Path.GetFileName (file));
                byte[] fileNameBytesLength = BitConverter.GetBytes (fileNameBytes.Length);
                PLog.DEBUG ("Length of file name in bytes={0}", fileNameBytesLength);

                SyncPseudoTcpSocket.Send(conn, fileNameBytesLength, (uint)fileNameBytesLength.Length);
                SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                PLog.DEBUG ("Sent filename length, ack={0}", ackBuffer [0]);

                SyncPseudoTcpSocket.Send(conn, fileNameBytes, (uint)fileNameBytes.Length);
                SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                PLog.DEBUG ("Sent filename bytes {0}={1}, ack={2}",
                    fileNameBytes, BitConverter.ToString(fileNameBytes), ackBuffer[0]);

                SyncPseudoTcpSocket.Send(conn, BitConverter.GetBytes(fileSize), 8);
                SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                PLog.DEBUG ("Sent file size, ack={0}", ackBuffer [0]);

                byte[] buffer = new byte[512 * 1024];

                long pos = 0;

                int i = 0;

                ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);

                while (pos < fileSize)
                {
                    int toSend = buffer.Length < (fileSize - pos)
                        ? buffer.Length
                        : (int)(fileSize - pos);

                    PLog.DEBUG ("File reading: pos={0}, fileSize={1}, toSend={2}", pos, fileSize, toSend);

                    fileReader.Read(buffer, 0, toSend);

                    int iteration = Environment.TickCount;

                    int sent;
                    int totalSent = 0;
                    int fragmentSize = toSend;
                    while (totalSent < toSend) {
                        sent = SendFragment (conn, buffer, fragmentSize);

                        totalSent += sent;
                        if (sent < fragmentSize) {
                            byte[] buffer2 = new byte[fragmentSize-sent];
                            Buffer.BlockCopy(buffer, sent, buffer2, 0, buffer2.Length);
                            fragmentSize = buffer2.Length;
                            buffer = buffer2;
                        }
                    }

                    pos += toSend;

                    ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);

                    if (bVerbose)
                    {
                        Console.WriteLine();

                        Console.WriteLine("Current: {0} / s",
                            SizeConverter.ConvertToSizeString(toSend / (Environment.TickCount - iteration) * 1000));

                        //Console.WriteLine("BandwidthMbps {0} mbps.", conn.GetPerformanceInfo().Probe.BandwidthMbps);
                        //Console.WriteLine("RoundtripTime {0}.", conn.GetPerformanceInfo().Probe.RoundtripTime);
                        //Console.WriteLine("SendMbps {0}.", conn.GetPerformanceInfo().Local.SendMbps);
                        //Console.WriteLine("ReceiveMbps {0}.", conn.GetPerformanceInfo().Local.ReceiveMbps);
                    }
                }
            }

            // TODO wait for confirmation all packets sent, either check PseudoTcpSocket queue,
            // or a final ack from receiver
            Thread.Sleep (15000); 

            Console.WriteLine ();
            Console.WriteLine ("Done!");
        }

        static int SendFragment(PseudoTcpSocket conn, byte[] buffer, int fragmentSize)
        {
            int got;
            int sent;
            do {
                do {
                    PLog.DEBUG ("Trying to send {0} bytes", fragmentSize);
                    sent = SyncPseudoTcpSocket.Send (conn, buffer, (uint)fragmentSize);
                    UdpCallbacks.AdjustClock(conn);
                    UdpCallbacks.CondSleep (50, sent, -1);
                } while (sent == -1);
                PLog.DEBUG ("Tried sending fragment sized {0} with result {1}", fragmentSize, sent);

                int start = Environment.TickCount;
                do {
                    PLog.DEBUG ("Waiting for ack for fragment");
                    got = SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                    UdpCallbacks.CondSleep (50, got, -1);
                } while (got == -1 && Environment.TickCount < start + 5000);

                if (1 == ackBuffer [0] && 1 == got) {
                    PLog.DEBUG ("Received ack==1 ok");
                } else {
                    Console.WriteLine ("Error in transmission, will retry");
                }
            } while (-1 == got);

            return sent;
        }
    }
}
