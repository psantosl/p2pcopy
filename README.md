# p2pcopy

Peer-to-peer file copy behind firewalls, with **no server in between**. Two people run
`p2pcopy`, swap the code it prints over whatever chat they already have open, and the
files flow directly from one machine to the other: encrypted, checksummed, resumable
enough for real life, and fast.

```
 ____  ____  ____   ___  ___  _ __  _   _
|  _ \|___ \|  _ \ / __|/ _ \| '_ \| | | |
| |_) | __) | |_) | (__| (_) | |_) | |_| |
| .__/|____/| .__/ \___|\___/| .__/ \__, |
|_|         |_|              |_|    |___/
```

One small native binary per platform. No account, no relay, no port forwarding, no
installer.

## How it looks

Alice sends:

```
$ p2pcopy send ubuntu-vm.ova
✔ Ready to send 1 file(s), 10.2 GB
╭─ Candidate ─┬─ Address ─────────────────────┬─ Note ────────────────────╮
│ LAN         │ 192.168.1.23:41222            │ same network only         │
│ IPv6        │ [2a02:9b0:41c:1f3e::23]:41222 │ direct, no NAT            │
│ Public      │ 83.44.120.9:41222             │ via STUN, cone NAT        │
╰─────────────┴───────────────────────────────┴───────────────────────────╯
╭─ Your code ──────────────────────────────────────────────────╮
│ 044j86pzew6tbs7re7m3nw812jqwkg000819cdg                      │
│ copied to your clipboard                                     │
╰──────────────────────────────────────────────────────────────╯
Send it to your peer. You look directly reachable, so they probably only need this.
? Peer's code (or just wait if they only needed yours): █
```

Bob receives:

```
$ p2pcopy receive
…
? Peer's code: 044j86pzew6tbs7re7m3nw812jqwkg000819cdg
✔ Connected to alice-desktop at [2a02:…::23]:41222 via UDT over UDP  AES-256-GCM
╭─ Incoming 1 file(s), 10.2 GB ────────────────────────────────╮
│ /home/bob                                                    │
│ └── ubuntu-vm.ova 10.2 GB                                    │
╰──────────────────────────────────────────────────────────────╯
Accept? [y/n] (y): y
Receiving 1 file(s) ━━━━━━━━━━━━━━━━━━━━━━━━━━━╸━━━━━━━━ 72%  7.3/10.2 GB  112 MB/s  00:26
```

Whoever presses Enter second triggers the connection; there is nothing to time.

## Install

Grab the binary for your platform from the
[releases page](https://github.com/psantosl/p2pcopy/releases), unpack it and put
`p2pcopy` somewhere on your `PATH`. Linux (x64, arm64, static musl), macOS (Intel and
Apple silicon) and Windows (x64, arm64) are built on every release.

Or build it yourself with the .NET 10 SDK:

```bash
dotnet publish src/p2pcopy -c Release -r linux-x64 -o out
```

## Usage

```
p2pcopy send <paths>...        send files and/or directories
p2pcopy receive [--out DIR]    receive into DIR (default: current directory)

  -c, --code <code>   the other side's code (skips the prompt)
  -p, --port <port>   local UDP/TCP port (default: random)
  -y, --yes           receive: accept without asking, overwrite existing files
  -o, --out <dir>     receive: destination directory
      --stun <host[:port]>  STUN server to use (repeatable; also P2PCOPY_STUN env var)
      --timeout <s>   how long to keep trying to connect (default 120)
      --plain         no colours, no live progress (automatic when piped)
      --json          one JSON event per line, prompts answered on stdin
      --no-tcp        UDP hole punching only
      --ipv4          do not use IPv6
      --no-clipboard  do not copy the code to the clipboard
  -v, --verbose       show candidates, probes and transport details
```

Exit codes: `0` success, `1` usage, `2` file problem, `3` could not connect,
`4` transfer failed or declined, `130` cancelled.

Scripting example, no prompts on either side:

```bash
p2pcopy receive --out ~/Downloads --yes --json --code 044j86…
```

```bash
p2pcopy send backup.tar.zst --plain --code 044bbae…
```

## One code or two?

The code carries every address you might be reachable at (LAN, public IPv4 learned via
STUN, global IPv6), what kind of NAT you are behind, and a random secret.

- If the person whose code was pasted is **directly reachable** (public IPv6, no NAT,
  or a full-cone NAT), the other side connects and nothing else is needed.
- If they are behind a **restricted or symmetric NAT**, their router only lets packets
  in from addresses they have already sent to, so they need your code too. p2pcopy tells
  you when that is the case and prints the code to send back; the first side is already
  sitting at a prompt waiting for it.

Home connections with IPv6 or UPnP usually need one paste. Office networks usually need
two. Either way nobody types an IP address.

## How it works

1. **Probe.** Bind one UDP port, list the local addresses, and ask a few STUN servers
   (one RFC 8489 binding request each, in parallel) what the public address is. Two
   answers that disagree on the port mean a symmetric NAT.
2. **Pair.** Encode role, NAT class, secret and candidates as a Crockford base32 string
   with a checksum. Paste it to the peer.
3. **Punch.** Send small authenticated probes to every candidate of the peer from the
   same UDP socket, four times a second, for up to two minutes. The first pair of
   addresses that works in both directions wins; the sender tells the receiver which.
4. **Connect.** Hand that UDP socket to [UDT](https://udt.sourceforge.io) in rendezvous
   mode (a pure C# port, vendored in `src/p2pcopy/Udt`). In parallel, TCP tries too:
   listening on the same port number and connecting to the peer from it (simultaneous
   open), which also covers the case where UDP is blocked but a LAN or IPv6 route exists.
5. **Encrypt.** Both sides derive a session key with HKDF from the pairing secret(s) and
   fresh nonces, then speak length-prefixed AES-256-GCM (or ChaCha20-Poly1305) frames.
   No application level acks: the transport does flow control, the receiver verifies a
   SHA-256 per file.

There is no server anywhere in that list, by design. The only outside parties ever
contacted are public STUN servers, and they only ever see a 20 byte question.

## Limits and honest caveats

- Two symmetric NATs cannot be punched. A phone hotspot on one side usually fixes it.
- Resume of an interrupted transfer, UPnP/NAT-PMP port mapping and a stdin/stdout pipe
  mode are not implemented yet.
- UDT is CPU bound in managed code: expect hundreds of megabits, not tens of gigabits.

## Development

```bash
dotnet test
```

```bash
dotnet publish src/p2pcopy -c Release -r <rid> -o out
```

CI builds and tests on Linux, macOS and Windows on every push. Pushing a `v*` tag runs
the release workflow, which publishes Native AOT binaries for seven platforms and
attaches them, with a `SHA256SUMS` file, to a GitHub release.

## License

MIT. The vendored UdtSharp code is MIT as well, copyright PlasticSCM; see
`src/p2pcopy/Udt/LICENSE`.
