using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Udt;

namespace p2pcopy
{
    static class UdtHolePunch
    {
        static internal Udt.Socket PeerConnect(
            System.Net.Sockets.Socket socket,
            string remoteAddr,
            int remotePort)
        {
            bool bConnected = false;
            int retry = 0;

            Udt.Socket client = null;

            while (!bConnected)
            {
                try
                {
                    DateTime now = InternetTime.Get();

                    int sleepTimeToSync = SleepTime(now);

                    Console.WriteLine("[{0}] - Waiting {1} sec to sync with other peer",
                        now.ToLongTimeString(),
                        sleepTimeToSync);
                    System.Threading.Thread.Sleep(sleepTimeToSync * 1000);

                    ExternalEndPoint.Get(socket);

                    if (client != null)
                        client.Close();

                    client = new Udt.Socket(AddressFamily.InterNetwork, SocketType.Stream);

                    client.SetSocketOption(Udt.SocketOptionName.Rendezvous, true);

                    client.Bind(socket);

                    Console.Write("\r{0} - Trying to connect to {1}:{2}.  ",
                        retry++, remoteAddr, remotePort);

                    client.Connect(remoteAddr, remotePort);

                    Console.WriteLine("Connected successfully to {0}:{1}",
                        remoteAddr, remotePort);

                    bConnected = true;
                }
                catch (Exception e)
                {
                    Console.Write(e.Message.Replace(Environment.NewLine, ". "));
                }
            }

            return client;
        }

        static int SleepTime(DateTime now)
        {
            List<int> seconds = new List<int>() { 10, 20, 30, 40, 50, 60 };

            int next = seconds.Find(x => x > now.Second);

            return next - now.Second;
        }
    }
}
