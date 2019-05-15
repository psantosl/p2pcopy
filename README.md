# p2pcopy
Small command line application to do p2p file copy behind firewalls without a central server. It uses [PseudoTcpSharp](https://github.com/psantosl/PseudoTcpSharp) on top of [UdpClient](https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient).

# Motivation
You are on a Slack/Skype/whatever session and need to send a 10GB virtual machine to a team mate. Uploading it to a central server doesn't seem to be a good option, so you would love to just start a P2P private connection between the two to send a file.

This is exactly what p2pcopy is all about.

And I guess I'm just yet another one looking at the xkcd:File Transfer thing:

![xdcd:File Transfer](http://imgs.xkcd.com/comics/file_transfer.png)

Other (nicer) alternatives exist, web based (WebRTC in fact) (even serverless [like this one] (http://blog.printf.net/articles/2013/05/17/webrtc-without-a-signaling-server)), but I wanted to go for a command line solution. Also, most p2p options need a central server to do the exchange of the public IPs before starting the "hole punching". I also wanted to avoid this, so the exchange is done manually, sharing the public IPs using your favourite messaging platform (like Slack).

To perform UDP hole punching, custom punch packets are exchanged before the TCP handshake.

# How to use it
The two peers will need a copy of p2pcopy.exe, then one will act as "sender" and the other as "receiver" (in fact, using the commands with these names).

## Sender
```
p2pcopy sender --file someFile.zip 
Your firewall is FullCone
Tell this to your peer: 109.x.x.x:23705

Enter the ip:port of your peer: 3.x.x.x:61235
Your firewall is FullCone
[11:59:36 AM] - Waiting 4 sec to sync with other peer
Attempting NAT traversal
[11:59:52 AM] - Waiting 8 sec to sync with other peer
Attempting NAT traversal
0 - TCP: Trying to connect to 3.x.x.x:61235. Connected successfully to 3.x.x.x:61235
\[##################################]    56.59 KB / 56.59 KB. 

Done!
```

## Receiver
```
p2pcopy receiver
Your firewall is FullCone
Tell this to your peer: 3.x.x.x:61235

Enter the ip:port of your peer: 109.x.x.x:23705
Your firewall is FullCone
[9:59:33 AM] - Waiting 7 sec to sync with other peer
Attempting NAT traversal
[9:59:52 AM] - Waiting 8 sec to sync with other peer
Attempting NAT traversal
0 - TCP: Trying to connect to 109.x.x.x:23705. Connected successfully to 109.x.x.x:23705
|[############################################################]    56.59 KB / 56.59 KB.   28.29 KB/s
Receive time=2.625 secs
Av bandwidth=0.168413434709821 Mbits/sec
```

## Potential connection problems
Sometimes the two peers try to punch a hole on their routers but they don't succeed. If that happens, simply leave the tool running and eventually (normally works well) it will work. If it continues to fail, restart the tool.

In the above example you can see the first attempt by the sender fails, because more punch packets are needed to establish the firewall holes on both sides. On the second attempt, it succeeds.

It is a pain when it happens, but... well, this is p2p like it is 1995 :P

Once you get an open port that works, you can reuse it both on sender and receiver by using the --localport option:

```p2pcopy.exe sender --localport 60300 --file 03183u.tif```

# How does it work
The implementation is extremely simple:

* Both peers connect to an external public STUN server to get their public IPs and ports. This is the only connection to an external server, and it doesn't require you to have any account or login or anything.
* Then each peer reuses the UDP socket used for STUN to create a `UdpClient` for file transfer.
* On both sides, each peer tries to connect (socket.connect) with the other one using custom hole punching UDP packets:

```
if (false == isSender) {
    SendPunchPackets(timeToSync);
    success = ReceivePunchPackets(traversalStart, timeToSync);
} else {
    success = ReceivePunchPackets(traversalStart, timeToSync);
    SendPunchPackets(timeToSync);
}

```
* Once the connection is established, regular socket stuff happens, and the file is sent in chunks to the receiver.

## Interesting point: use internet time to synchronize
There is one interesting point to highlight: when you exchange public IPs using a central server, synchronization is rather easy because each peer receives the IP of the other side, then tries to connect, and more likely the connection attempt happens at the same time.

But here we do not use a central server, the exchange is done manually by the user (to avoid any sort of central 'directory service').

So, in initial versions, users had to be very careful to "try to start at the same time" (basically type the IP:port of the other side and hit ENTER almost at the same time), which was painful.

The solution (that works pretty well on most cases) is as follows:
* Each peer gets the internet time (using a simple class from StackOverflow).
* Then they decide to "start" on second 0, 15, 30, 45... of every minute, so they work synchronized even when the users don't hit ENTER at the same time (which, as I said, basically rendered it unusable).

## Building on Linux (Debian)
```
sudo apt install mono-devel mono-complete monodevelop monodevelop-nunit xterm
git clone https://github.com/psantosl/p2pcopy
cd p2pcopy
xbuild p2pcopy.sln /v:diag
```

## Running on Linux (Debian)
```
sudo apt install mono-runtime
mono p2pcopy.exe receiver
# or
mono p2pcopy.exe sender --file 5MB.zip 
```
Tested so far on mono 4.2.1 and 4.6.2.

## Troubleshooting UDP hole punching
The NAT traversal analyzer at http://nattest.net.in.tum.de/test.php is useful. For security, most browsers no longer support Java, but you can run it with `appletviewer` and the attached security policy:
```
appletviewer -J-Djava.security.manager -J-Djava.security.policy=NATAnalyzer.security.policy http://nattest.net.in.tum.de/test.php
```

## Limitations of TCP for file transfer
TCP is not really designed for transferring large files, so will always fail to exploit the maximum bandwidth available. Because TCP doesn't just ensure all the data gets transferred correctly, it ensures correctness *at any point during the transfer* (up to the beginning of the receive window). Needed for say online chat, overkill for file transfer. A much faster way is to note down any mistakes and fix them later. The UDP [FASP protocol](https://en.wikipedia.org/wiki/Fast_and_Secure_Protocol) does this, but is patent protected until 2031. It's not clear if it would be legally possible to implement a tool on similar principles which doesn't impinge the patent. More background [here.](https://www.ccdatalab.org/blog/a-desperate-plea-for-a-free-software-alternative-to-aspera)

