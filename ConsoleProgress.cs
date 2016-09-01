using System;

namespace p2pcopy
{
    static class ConsoleProgress
    {
        static internal void Draw(
            int i,
            long transferred,
            long total,
            int transferStarted,
            int width)
        {
            Console.Write("\r");

            char[] progress = new char[] { '-', '\\', '|', '/' };

            Console.Write(progress[i % 4]);

            int fillPos = (int)((float)transferred / (float)total * width);
            string filled = new string('#', fillPos);
            string empty = new string('-', width - fillPos);
            Console.Write("[" + filled + empty + "] ");

            Console.Write("{0, 22}. ",
                SizeConverter.ConvertToSizeString(transferred) + " / " +
                SizeConverter.ConvertToSizeString(total));

            int seconds = (Environment.TickCount - transferStarted) / 1000;

            if (seconds == 0)
            {
                return;
            }

            Console.Write("{0, 10}/s",
                SizeConverter.ConvertToSizeString(transferred / seconds));
        }
    }
}
