using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace p2pcopy
{
    static class Receiver
    {
        static internal void Run(Udt.Socket conn, int parts)
        {
            int ini = Environment.TickCount;

            int spinAnimator = 0;

            ConsoleProgress.Draw(spinAnimator++, 0, 0, ini, Console.WindowWidth / 2);

            List<Thread> threads = new List<Thread>();

            for (int j = 0; j < parts; ++j )
            {
                Thread t = new Thread(Receive);

                threads.Add(t);

                Udt.Socket sock = conn;

                if (j > 0)
                {
                    var accept = new Udt.Socket(
                        AddressFamily.InterNetwork, SocketType.Stream);

                    accept.ReuseAddress = true;

                    accept.Bind(conn.LocalEndPoint);

                    accept.Listen(1);

                    sock = accept.Accept();

                    Console.WriteLine("Receiver {0} connected!", j);
                }

                t.Start(sock);
            }

            while (true)
            {
                long size;
                long read;

                lock (mLock)
                {
                    size = mTotalSize;
                    read = mTotalRead;
                }

                bool allJoined = true;

                foreach (var t in threads)
                {
                    allJoined &= t.Join(100);
                }

                ConsoleProgress.Draw(spinAnimator++, read, size, ini, Console.WindowWidth / 2);

                if (allJoined)
                    break;
            }
        }

        static void Receive(object o)
        {
            Udt.Socket conn = o as Udt.Socket;

            using (Udt.NetworkStream netStream = new Udt.NetworkStream(conn))
            using (BinaryWriter writer = new BinaryWriter(netStream))
            using (BinaryReader reader = new BinaryReader(netStream))
            {
                string fileName = reader.ReadString();

                Console.WriteLine("Going to write [{0}]", fileName);

                long size = reader.ReadInt64();

                lock (mLock)
                {
                    mTotalSize += size;
                }

                byte[] buffer = new byte[4 * 1024 * 1024];

                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    long read = 0;

                    while (read < size)
                    {
                        int toRecv = reader.ReadInt32();

                        ReadFragment(reader, toRecv, buffer);

                        fileStream.Write(buffer, 0, toRecv);

                        read += toRecv;

                        writer.Write(true);

                        lock (mLock)
                        {
                            mTotalRead += toRecv;
                        }
                    }
                }
            }
        }

        static int ReadFragment(BinaryReader reader, int size, byte[] buffer)
        {
            int read = 0;

            while (read < size)
            {
                read += reader.Read(buffer, read, size - read);
            }

            return read;
        }

        static long mTotalSize = 0;
        static long mTotalRead = 0;
        static object mLock = new object();
    }
}
