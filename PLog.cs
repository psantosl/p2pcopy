using System;

namespace p2pcopy
{
    public class PLog
    {
        public static bool debug = false;
        public static bool verbose = false;

        public static void DEBUG(string fmt, params object[] args)
        {
            if (debug)
            {
                Console.WriteLine (string.Format (fmt, args));
            }
        }

        public static void VERBOSE(string fmt, params object[] args)
        {
            if (verbose || debug)
            {
                Console.WriteLine(string.Format(fmt, args));
            }
        }

        public static void VERBOSE_WRITE(string fmt, params object[] args)
        {
            if (verbose || debug)
            {
                Console.Write(string.Format(fmt, args));
            }
        }
    }
}
    