# p2pcopy
Small command line application to do p2p file copy behind firewalls without a central server.

It uses the [UDT protocol](https://en.wikipedia.org/wiki/UDP-based_Data_Transfer_Protocol) via [UdtSharp](https://github.com/PlasticSCM/UdtSharp) (a previous version of p2pcopy was using the Windows-only native library [UDT](http://udt.sourceforge.net) under the hood, but now it's cross-platform thanks to 100% fully-managed code).

# Motivation
You are on a Slack/Skype/whatever session and need to send a 10GB virtual machine to a team mate. Uploading it to a central server doesn't seem to be a good option, so you would love to just start a P2P private connection between the two to send a file.

This is exactly what p2pcopy is all about.

And I guess I'm just yet another one looking at the xkcd:File Transfer thing:

![xdcd:File Transfer](http://imgs.xkcd.com/comics/file_transfer.png)

Other (nicer) alternatives exist, web based (WebRTC in fact) (even serverless [like this one] (http://blog.printf.net/articles/2013/05/17/webrtc-without-a-signaling-server)), but I wanted to go for a command line solution. Also, most p2p options need a central server to do the exchange of the public IPs before starting the "hole punching". I also wanted to avoid this, so the exchange is done manually, sharing the public IPs using your favourite messaging platform (like Slack).

It is built on top of UDT, the famous library to speed up data transfer on high bandwidth, high latency networks. It includes a "rendezvous" mode to perform UDP hole punching, and that's what I use.

# How to use it
The two peers will need a copy of p2p.exe, then one will act as "sender" and the other as "receiver" (in fact, using the commands with these names).

## Sender
I'm specifiying a local port, which is not mandatory, you can skip the --localport.

```
>p2pcopy.exe sender --localport 4300 --file 03183u.tif
Using local port: 4300
Your firewall is FullCone
Tell this to your peer: 223.154.44.121:4300


Enter the ip:port of your peer: 188.44.136.7:21300
Your firewall is FullCone
[17:51:55] - Waiting 5 sec to sync with other peer
Your firewall is FullCone
0 - Trying to connect to 188.44.136.7:21300.  Connected successfully to 188.44.136.7:21300
\[##################------------------------------------------]    54.5 MB / 181.64 MB.  569.47 KB/s
```

## Receiver
```
>p2pcopy.exe receiver --localport 21300
Using local port: 21300
Your firewall is FullCone
Tell this to your peer: 188.44.136.7:21300


Enter the ip:port of your peer: 223.154.44.121:4300
Your firewall is FullCone
[5:51:56 PM] - Waiting 4 sec to sync with other peer
Your firewall is FullCone
0 - Trying to connect to 223.154.44.121:4300.  Connected successfully to 223.154.44.121:4300
-[##########################################---------------]      86 MB / 181.64 MB.     688 KB/s

```

## Potential connection problems
Sometimes the two peers try to punch a hole on their routers but they don't succeed. If that happens, simply retry and eventually (normally works well) it will work.

A sample failed session looks as follows:
```
>p2pcopy.exe sender --localport 60300 --file 03183u.tif
Using local port: 60300
Your firewall is FullCone
Tell this to your peer: 2xx.2xx.84.121:60300


Enter the ip:port of your peer: 188.14.136.87:21300
Your firewall is FullCone
[17:47:50] - Waiting 10 sec to sync with other peer
Your firewall is FullCone
0 - Trying to connect to 188.14.136.87:21300.  Error connecting to 188.14.136.87:21300. Connection setup failure: connection time out. UDT Error Code: 1001
[17:48:30] - Waiting 10 sec to sync with other peer
Your firewall is FullCone
1 - Trying to connect to 188.14.136.87:21300.  Error connecting to 188.14.136.87:21300. Connection setup failure: connection time out. UDT Error Code: 1001
```

It is a pain when it happens, but... well, this is p2p like it is 1995 :P

Once you get an open port that works, you can reuse it both on sender and receiver by using the --localport option:

```p2pcopy.exe sender --localport 60300 --file 03183u.tif```

# How does it work
The implementation is extremly simple:

* Both peers connect to an external public STUN server to get their public IPs and ports. This is the only connection to an external server, and it doesn't require you to have any account or login or anything.
* Then each peer reuses the UDP socket used for STUN to create a UDT socket.
* On both sides, each peer tries to connect (socket.connect) with the other one using the UDT ''rendezvous'' mode:

```
                    client = new Udt.Socket(AddressFamily.InterNetwork, SocketType.Stream);

                    client.SetSocketOption(Udt.SocketOptionName.Rendezvous, true);

                    client.Bind(socket);

                    Console.Write("\r{0} - Trying to connect to {1}:{2}.  ",
                        retry++, remoteAddr, remotePort);

                    client.Connect(remoteAddr, remotePort);

                    Console.WriteLine("Connected successfully to {0}:{1}",
                        remoteAddr, remotePort);
```

Yes, each socket simply does ''connect'' and nobody is doing ''listen'' or ''accept'' but it works. This is how hole punching goes.

* Once the connection is established, regular socket stuff happens, and the file is sent in chunks to the receiver.

## Interesting point: use internet time to synchronize
There is one interesting point to highlight: when you exchange public IPs using a central server, synchronization is rather easy because each peer receives the IP of the other side, then tries to connect, and more likely the connection attempt happens at the same time.

But here we do not use a central server, the exchange is done manually by the user (to avoid any sort of central 'directory service').

So, in initial versions, users had to be very careful to "try to start at the same time" (basically type the IP:port of the other side and hit ENTER almost at the same time), which was painful.

The solution (that works pretty well on most cases) is as follows:
* Each peer gets the internet time via NTP.
* Then they decide to "start" on second 0, 10, 20, 30... of every minute, so they work synchronized even when the users don't hit ENTER at the same time (which, as I said, basically rendered it unusable).



