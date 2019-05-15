using System;
using System.Collections.Generic;

namespace p2pcopy
{
    public class Config
    {
        // Attempt connection every 15s, max 5s for NAT traversal, max 5s for TCP handshake
        public static List<int> CONNECTION_SYNC_TIMES = new List<int>() {20, 40, 60};
        public static int NAT_TRAVERSAL_TRIES = 2;
        public static int MAX_TRAVERSAL_TIME = 10000;
        public static int PUNCH_PKTS_PER_TRY = 10;
        public static int MAX_TCP_HANDSHAKE_TIME = 5000;
    }
}
