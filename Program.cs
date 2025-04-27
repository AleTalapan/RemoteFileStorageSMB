using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static readonly ConcurrentQueue<FileSystemEventArgs> EventQueue = new();
    private static readonly Dictionary<string, DateTime> LastEventTimes = new();
    private const int DebounceMilliseconds = 500;
    private static bool Running = true;
    private static int ProcessedEvents = 0;
    private static readonly TimeSpan CleanupAge = TimeSpan.FromMinutes(2);

    static void Main()
    {
        var path = @"Z:\";

        if (!Directory.Exists(path))
        {
            Console.WriteLine("Path not found.");
            return;
        }

        using var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Attributes 
        };

        watcher.Created += (s, e) => EventQueue.Enqueue(e);
        watcher.Changed += (s, e) => EventQueue.Enqueue(e);
        watcher.Deleted += (s, e) => EventQueue.Enqueue(e);
        watcher.Renamed += (s, e) => EventQueue.Enqueue(e);

        Task.Run(() => ProcessEvents());

        Console.WriteLine("Press [Enter] to exit.");
        Console.ReadLine();
        Running = false;
    }

    static void ProcessEvents()
    {
        while (Running)
        {
            while (EventQueue.TryDequeue(out var e))
            {
                if (IsDebounced(e.FullPath))
                    continue;

                if (e is RenamedEventArgs renamedEvent)
                {
                    Console.WriteLine($"Renamed: {renamedEvent.OldFullPath} → {renamedEvent.FullPath}");
                }
                else
                {
                    Console.WriteLine($"{e.ChangeType}: {e.FullPath}");
                }

                ProcessedEvents++;

                if (ProcessedEvents % 10000 == 0)
                    CleanupOldEntries();
            }

            Thread.Sleep(100);
        }
    }


    private static bool IsDebounced(string path)
    {
        var now = DateTime.Now;

        if (LastEventTimes.TryGetValue(path, out var lastTime))
        {
            if ((now - lastTime).TotalMilliseconds < DebounceMilliseconds)
                return true;
        }

        LastEventTimes[path] = now;
        return false;
    }

    private static void CleanupOldEntries()
    {
        var cutoff = DateTime.Now - CleanupAge;
        var keysToRemove = new List<string>();

        foreach (var entry in LastEventTimes)
        {
            if (entry.Value < cutoff)
                keysToRemove.Add(entry.Key);
        }

        foreach (var key in keysToRemove)
        {
            LastEventTimes.Remove(key);
        }

        Console.WriteLine($"[Cleanup] Removed {keysToRemove.Count} old entries from debounce cache.");
    }
}
