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
    static class Receiver
    {
        static internal void Run(PseudoTcpSocket conn)
        {
            PLog.DEBUG ("In Receiver.Run");
            int ini = Environment.TickCount;

            byte[] ackBuffer = new byte[1];
            ackBuffer [0] = 1;
            long got = 0;

            byte[] fnlBytes = new byte[4];
            do {
                got = SyncPseudoTcpSocket.Recv(conn,fnlBytes,4);
                PLog.DEBUG ("Reading filename length, got={0}", got);
                UdpCallbacks.PollingSleep (got, -1);
            } while (got == -1);
            SyncPseudoTcpSocket.Send(conn,ackBuffer,1);
            uint fileNameLengthBytes = (uint)BitConverter.ToInt32 (fnlBytes, 0);
            PLog.DEBUG ("Got filename length=" + fileNameLengthBytes);

            byte[] fnBytes = new byte[fileNameLengthBytes];
            do {
                got = SyncPseudoTcpSocket.Recv (conn,fnBytes, fileNameLengthBytes);
                PLog.DEBUG ("Reading filename, got={0}", got);
                UdpCallbacks.PollingSleep (got, -1);
            } while (got == -1);
            SyncPseudoTcpSocket.Send(conn,ackBuffer,1);
            PLog.DEBUG ("filename bytes=" + BitConverter.ToString(fnBytes));
            string fileName = System.Text.Encoding.UTF8.GetString(fnBytes);
            Console.WriteLine ("Receiving file: {0}", fileName);

            byte[] fileSizeBytes = new byte[sizeof(long)];
            do {
                got = SyncPseudoTcpSocket.Recv(conn,fileSizeBytes, (uint)fileSizeBytes.Length);
                PLog.DEBUG ("Reading file size, got={0}", got);
                UdpCallbacks.PollingSleep(got, -1);
            } while (got == -1);
            SyncPseudoTcpSocket.Send(conn, ackBuffer,1);
            long size = (long)BitConverter.ToInt64 (fileSizeBytes, 0);
            Console.WriteLine ("File size={0} bytes", size);

            byte[] buffer = new byte[4 * 1024 * 1024];
            int i = 0;

            ConsoleProgress.Draw(i++, 0, size, ini, Console.WindowWidth / 2);

            using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
            {
                long len = 0;
                long read = 0;

                while (read < size)
                {
                    do {
                        PLog.DEBUG("{0} Reading file data... so far total read={1}", Environment.TickCount, read);
                        len = SyncPseudoTcpSocket.Recv (conn, buffer, (uint)buffer.Length);
                        UdpCallbacks.PollingSleep(len, -1);
                    } while (len == -1);

                    PLog.DEBUG ("Read {0} bytes of file data", len);
                    fileStream.Write(buffer, 0, (int)len);
                    read += len;

                    ConsoleProgress.Draw(i++, read, size, ini, Console.WindowWidth / 2);
                }

                double MB = (double)size * 8.0 / (1024.0*1024.0);
                double rxTime = (Environment.TickCount - ini) / 1000.0;
                Console.WriteLine ();
                Console.WriteLine("Receive time=" + rxTime + " secs");
                Console.WriteLine("Av bandwidth=" + MB/rxTime + " Mbits/sec");
            }
        }
    }
}
