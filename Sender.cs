using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PseudoTcp;
using System.Threading;
using System.Collections;

namespace p2pcopy
{
    static class Sender
    {
        static internal void Run(PseudoTcpSocket conn, string file, bool bVerbose)
        {
            int ini = Environment.TickCount;
            Queue notifyClockQueue = new Queue();

            using (FileStream fileReader = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                long fileSize = new FileInfo(file).Length;
                byte[] ackBuffer = new byte[1];

                PLog.DebugWriteLine ("Sending filename {0}", Path.GetFileName (file));
                byte[] fileNameBytes = Encoding.UTF8.GetBytes (Path.GetFileName (file));
                byte[] fileNameBytesLength = BitConverter.GetBytes (fileNameBytes.Length);
                PLog.DebugWriteLine ("Length of file name in bytes={0}", fileNameBytesLength);

                SyncPseudoTcpSocket.Send(conn, fileNameBytesLength, (uint)fileNameBytesLength.Length);
                SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                PLog.DebugWriteLine ("Sent filename length, ack={0}", ackBuffer [0]);

                SyncPseudoTcpSocket.Send(conn, fileNameBytes, (uint)fileNameBytes.Length);
                SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                PLog.DebugWriteLine ("Sent filename bytes {0}={1}, ack={2}",
                    fileNameBytes, BitConverter.ToString(fileNameBytes), ackBuffer[0]);

                SyncPseudoTcpSocket.Send(conn, BitConverter.GetBytes(fileSize), 8);
                SyncPseudoTcpSocket.Recv (conn, ackBuffer, 1);
                PLog.DebugWriteLine ("Sent file size, ack={0}", ackBuffer [0]);

                byte[] buffer = new byte[512 * 1024];

                long pos = 0;

                int i = 0;

                ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);

                while (pos < fileSize)
                {
                    ProcessNotifyClockQueue (conn, notifyClockQueue);
                    
                    int toSend = buffer.Length < (fileSize - pos)
                        ? buffer.Length
                        : (int)(fileSize - pos);

                    PLog.DebugWriteLine ("File reading: pos={0}, fileSize={1}, toSend={2}", pos, fileSize, toSend);

                    fileReader.Read(buffer, 0, toSend);

                    int iteration = Environment.TickCount;

                    int sent;
                    int totalSent = 0;
                    int fragmentSize = toSend;
                    while (totalSent < toSend) {
                        sent = SendFragment (conn, buffer, fragmentSize, notifyClockQueue);

                        totalSent += sent;
                        PLog.DebugWriteLine ("totalSent={0} sent={1} fragmentSize={2}",totalSent, sent, fragmentSize);
                        if (sent < fragmentSize) {
                            byte[] buffer2 = new byte[fragmentSize-sent];
                            Buffer.BlockCopy(buffer, sent, buffer2, 0, buffer2.Length);
                            fragmentSize = buffer2.Length;
                            buffer = buffer2;
                        }
                    }
                    PLog.DebugWriteLine("finished sending toSend={0}", toSend);

                    pos += toSend;

                    ConsoleProgress.Draw(i++, pos, fileSize, ini, Console.WindowWidth / 3);

                    PLog.VerboseWriteLine("");
                    PLog.VerboseWriteLine("Current: {0} / s",
                        SizeConverter.ConvertToSizeString(toSend / (Environment.TickCount - iteration + 1) * 1000));

                    PLog.VerboseWriteLine ("RTO={0} millis", conn.priv.rx_rto);
                    PLog.VerboseWriteLine ("Send buffer={0} bytes", conn.priv.sbuf.data_length);

                    //Console.WriteLine("BandwidthMbps {0} mbps.", conn.GetPerformanceInfo().Probe.BandwidthMbps);
                    //Console.WriteLine("RoundtripTime {0}.", conn.GetPerformanceInfo().Probe.RoundtripTime);
                    //Console.WriteLine("SendMbps {0}.", conn.GetPerformanceInfo().Local.SendMbps);
                    //Console.WriteLine("ReceiveMbps {0}.", conn.GetPerformanceInfo().Local.ReceiveMbps);
                }
            }

            Console.WriteLine ();
            while (conn.priv.sbuf.data_length != 0)
            {
                PLog.VerboseWriteLine ("Waiting for buffered data to finish sending...");
                for (int i = 0; i < 20; i++) 
                {
                    conn.NotifyClock();
                    Thread.Sleep (50);
                }
            }

            Console.WriteLine ();
            Console.WriteLine ("Done!");
        }

        static void ProcessNotifyClockQueue(PseudoTcpSocket conn, Queue notifyClockQueue)
        {
            PLog.DebugWriteLine ("Entering ProcessNotifyClockQueue with queue size={0}", notifyClockQueue.Count);
            if (notifyClockQueue.Count != 0) {
                PLog.DebugWriteLine ("...and head timestamp={0}, current time={1}", notifyClockQueue.Peek (), Environment.TickCount);
            }

            if (notifyClockQueue.Count == 0)
            {
                UdpCallbacks.AdjustClock (conn, notifyClockQueue);
                return;
            }

            bool keepChecking = true;
            while (keepChecking && notifyClockQueue.Count > 0)
            {
                int iTimestamp = (int)notifyClockQueue.Peek();
                if (Environment.TickCount > iTimestamp)
                {
                    SyncPseudoTcpSocket.NotifyClock (conn);
                    notifyClockQueue.Dequeue ();
                }
                else
                {
                    keepChecking = false;
                }                           
            }
        }

        static int SendFragment(PseudoTcpSocket conn, byte[] buffer, int fragmentSize, Queue notifyClockQueue)
        {
            int sent;
            do {
                PLog.DebugWriteLine ("Trying to send {0} bytes", fragmentSize);
                sent = SyncPseudoTcpSocket.Send (conn, buffer, (uint)fragmentSize);

                if (sent==-1) {
                    PLog.DebugWriteLine("sent==-1 so processing notifyClockQueue");
                    ProcessNotifyClockQueue(conn, notifyClockQueue);
                }
                else {
                    UdpCallbacks.AdjustClock(conn, notifyClockQueue);
                }
                UdpCallbacks.PollingSleep (sent, -1);
            } while (sent == -1);
            PLog.DebugWriteLine ("Tried sending fragment sized {0} with result {1}", fragmentSize, sent);

            return sent;
        }
    }
}
