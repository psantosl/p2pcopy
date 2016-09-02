using System;
using System.Net;
using System.Net.Sockets;

namespace p2pcopy.tcpholepunch
{
    static class TcpHolePunch
    {
        static internal Socket PeerConnect(
            string remoteAddr,
            int remotePort,
            int localPort)
        {
            Acceptor acceptor = new Acceptor();
            acceptor.Run(localPort);

            Connector connector = new Connector();
            connector.Run(remoteAddr, remotePort, localPort);

            while (true)
            {
                System.Threading.Thread.Sleep(100);

                if (acceptor.Accepted())
                {
                    Console.WriteLine("Acceptor correctly accepted a connection");
                    return acceptor.GetSocket();
                }

                if (connector.Connected())
                {
                    Console.WriteLine("Connector correctly connected");
                    return connector.GetSocket();
                }
            }
        }

        class Connector
        {
            internal void Run(string remoteAddr, int port, int localPort)
            {
                mSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                mSocket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                mSocket.Bind(new IPEndPoint(IPAddress.Any, localPort));

                mSocket.BeginConnect(remoteAddr, port,
                    (IAsyncResult ar) =>
                    {
                        mbConnected = true;
                    },
                    null);
            }

            internal bool Connected()
            {
                lock (mLock)
                {
                    return mbConnected;
                }
            }

            internal Socket GetSocket()
            {
                lock (mLock)
                {
                    return mSocket;
                }
            }

            Socket mSocket;
            bool mbConnected = false;
            object mLock = new object();
        }

        class Acceptor
        {
            internal void Run(int localPort)
            {
                Socket sock = new Socket(SocketType.Stream, ProtocolType.Tcp);

                sock.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                sock.Bind(new IPEndPoint(IPAddress.Any, localPort));

                sock.Listen(1);

                sock.BeginAccept(
                    (IAsyncResult ar) =>
                    {
                        lock (mLock)
                        {
                            mSocket = (Socket)ar.AsyncState;
                            mbAccepted = true;
                        }
                    },
                    null);
            }

            internal bool Accepted()
            {
                lock (mLock)
                {
                    return mbAccepted;
                }
            }

            internal Socket GetSocket()
            {
                lock(mLock)
                {
                    return mSocket;
                }
            }

            Socket mSocket;
            bool mbAccepted = false;
            object mLock = new object();
        }
    }
}
