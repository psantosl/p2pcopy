using System;

namespace p2pcopy
{
    public class PLog
    {
        private static bool debug = false;
        private static bool verbose = false;

        public static void SetDebug()
        {
            debug = true;
        }

        public static void SetVerbose()
        {
            verbose = true;
        }

        public static void Debug(string fmt, params object[] args)
        {
            if (debug)
            {
                Console.WriteLine (string.Format (fmt, args));
            }
        }

        public static void Verbose(string fmt, params object[] args)
        {
            if (verbose || debug)
            {
                Console.WriteLine(string.Format(fmt, args));
            }
        }

        public static void VerboseWrite(string fmt, params object[] args)
        {
            if (verbose || debug)
            {
                Console.Write(string.Format(fmt, args));
            }
        }
    }
}
    