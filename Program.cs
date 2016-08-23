using System;
using System.Net.Sockets;
using System.Net;

namespace p2pcopy
{
    class Program
    {
        static void Main(string[] args)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // https://gist.github.com/zziuni/3741933

            StunResult result = StunClient.Query("stun.l.google.com", 19302, socket);
            Console.WriteLine(result.NetType.ToString());

            Console.WriteLine(socket.LocalEndPoint.ToString());

            if (result.NetType == StunNetType.UdpBlocked)
            {
                Console.WriteLine("Your external IP can't be obtained. You are blocked :-(");
                return;
            }

            Console.WriteLine("Public endpoint: {0}. Local port: {1}",
                result.PublicEndPoint.ToString(),
                socket.LocalEndPoint.ToString());
        }

    }
}
