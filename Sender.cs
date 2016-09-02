using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Net.Sockets;

namespace p2pcopy
{
    static class Sender
    {
        static SendOptions Split(long size, int parts, int fragmentNo)
        {
            long fragment = size / parts;

            var result = new SendOptions();

            result.FromFilePos = fragmentNo * fragment;
            result.ToFilePos = (fragmentNo + 1) * fragment - 1;

            if (fragmentNo == parts -1)
            {
                result.ToFilePos = size - 1;
            }

            return result;
        }

        static internal void Run(
            Udt.Socket conn,
            string file,
            int parts,
            bool bVerbose)
        {
            int ini = Environment.TickCount;

            long fileSize = new FileInfo(file).Length;

            int spinAnimator = 0;

            ConsoleProgress.Draw(spinAnimator++, 0, fileSize, ini, Console.WindowWidth / 3);

            List<Thread> threads = new List<Thread>();

            for (int j = 0; j < parts; ++j )
            {
                Thread t = new Thread(Send);
                threads.Add(t);

                SendOptions opts = Split(fileSize, parts, j);

                opts.Id = j;

                opts.FileName = file;

                if (j == 0)
                {
                    opts.UdtSocket = conn;
                }
                else
                {
                    opts.UdtSocket = new Udt.Socket(
                        AddressFamily.InterNetwork, SocketType.Stream);

                    //opts.UdtSocket.SetSocketOption(Udt.SocketOptionName.Rendezvous, true);

                    opts.UdtSocket.ReuseAddress = true;

                    opts.UdtSocket.Bind(conn.LocalEndPoint);

                    opts.UdtSocket.Connect(conn.RemoteEndPoint);

                    Console.WriteLine("Sender {0} connected!", j);
                }

                t.Start(opts);
            }

            while (true)
            {
                long sent;

                lock (mLock)
                {
                    sent = mSent;
                }

                foreach (var t in threads)
                {
                    t.Join(100);
                }

                ConsoleProgress.Draw(spinAnimator++, sent, fileSize, ini, Console.WindowWidth / 3);

                if (sent == fileSize)
                    return;

                if (bVerbose)
                {
                    Console.WriteLine();

                    /*Console.WriteLine("Current: {0} / s",
                        SizeConverter.ConvertToSizeString(toSend / (Environment.TickCount - iteration) * 1000));*/

                    Console.WriteLine("BandwidthMbps {0} mbps.", conn.GetPerformanceInfo().Probe.BandwidthMbps);
                    Console.WriteLine("RoundtripTime {0}.", conn.GetPerformanceInfo().Probe.RoundtripTime);
                    Console.WriteLine("SendMbps {0}.", conn.GetPerformanceInfo().Local.SendMbps);
                    Console.WriteLine("ReceiveMbps {0}.", conn.GetPerformanceInfo().Local.ReceiveMbps);
                }
            }
        }

        class SendOptions
        {
            internal int Id;
            internal Udt.Socket UdtSocket;
            internal string FileName;
            internal long FromFilePos;
            internal long ToFilePos;
        }

        static void Send(object o)
        {
            SendOptions opts = o as SendOptions;

            using (Udt.NetworkStream netStream = new Udt.NetworkStream(opts.UdtSocket))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            using (FileStream fileReader = new FileStream(opts.FileName, FileMode.Open, FileAccess.Read))
            {
                Console.WriteLine("Sender {0} going to start", opts.Id);
                writer.Write(Path.GetFileName(opts.FileName) + "_" + opts.Id);

                long fragmentSize = 1 + opts.ToFilePos - opts.FromFilePos;

                writer.Write(fragmentSize);

                Console.WriteLine("Sender {0} wrote fragment size {1}", opts.Id, fragmentSize);

                byte[] buffer = new byte[512 * 1024];

                long pos = 0;

                while (pos < fragmentSize)
                {
                    int toSend = buffer.Length < (fragmentSize - pos)
                        ? buffer.Length
                        : (int)(fragmentSize - pos);

                    fileReader.Read(buffer, 0, toSend);

                    int iteration = Environment.TickCount;

                    writer.Write(toSend);
                    opts.UdtSocket.Send(buffer, 0, toSend);

                    if (!reader.ReadBoolean())
                    {
                        Console.WriteLine("Error in transmission");
                        return;
                    }

                    pos += toSend;

                    lock (mLock)
                    {
                        mSent += toSend;
                    }
                }
            }
        }

        static long mSent = 0;
        static object mLock = new object();
    }
}
