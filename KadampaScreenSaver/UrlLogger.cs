using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KadampaScreenSaver
{
    public class UrlLogger
    {
        private readonly string _logPath;

        public UrlLogger(string logPath)
        {
            _logPath = logPath;
            if (!File.Exists(_logPath)) File.WriteAllText(_logPath, string.Empty);
        }

        public void LogUrl(string url)
        {
            File.AppendAllLines(_logPath, new[] { $"{url}|{DateTime.UtcNow:O}" });
        }

        public bool AlreadyVisited(string url)
        {
            return File.ReadAllLines(_logPath)
                       .Any(line => line.Split('|')[0].Equals(url, StringComparison.OrdinalIgnoreCase));
        }

        public void Cleanup(int retentionDays)
        {
            if (!File.Exists(_logPath)) return;

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var entries = File.ReadAllLines(_logPath)
                              .Select(line => line.Split('|'))
                              .Where(parts => parts.Length == 2)
                              .Select(parts => (url: parts[0], stamp: DateTime.Parse(parts[1])))
                              .Where(e => e.stamp >= cutoff)
                              .Select(e => $"{e.url}|{e.stamp:O}");
            File.WriteAllLines(_logPath, entries);
        }
    }
}
