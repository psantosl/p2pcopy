using System;

namespace p2pcopy
{
    public class PLog
    {
        private static bool debug = false;
        private static bool verbose = false;

        public static bool Debug
        {
            get
            {
                return debug;
            }

            internal set
            {
                debug = value;
            }
        }
            
        public static bool Verbose
        {
            get
            {
                return verbose;
            }

            internal set
            {
                verbose = value;
            }
        }

        public static void DebugWriteLine(string fmt, params object[] args)
        {
            if (debug)
            {
                Console.WriteLine (string.Format (fmt, args));
            }
        }

        public static void VerboseWriteLine(string fmt, params object[] args)
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
    