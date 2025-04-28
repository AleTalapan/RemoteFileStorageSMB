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
        var path = @"\\172.20.10.6\shared";

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

        watcher.Created += (s, e) => HandleCreatedEvent(e);
        watcher.Changed += (s, e) => HandleChangedEvent(e);  // Handle changed events explicitly
        watcher.Deleted += (s, e) => EventQueue.Enqueue(e);
        watcher.Renamed += (s, e) => HandleRenamedEvent(e);

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

                // Print event details
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

    // Handle the Created event
    static void HandleCreatedEvent(FileSystemEventArgs e)
    {
        // Ignore temporary files created by editors (e.g., .swp, .goutputstream)
        if (IsTemporaryFile(e.FullPath))
        {
            return; // Ignore these files
        }

        // Log file creation
        Console.WriteLine($"Created: {e.FullPath}");

        // Enqueue the event to the queue for further processing (or just process it right away if needed)
        EventQueue.Enqueue(e);
    }

    // Handle the Changed event (for modifications)
    static void HandleChangedEvent(FileSystemEventArgs e)
    {
        // Ignore temporary files created by editors (e.g., .swp, .goutputstream)
        if (IsTemporaryFile(e.FullPath))
        {
            return; // Ignore these files
        }

        // Log file modification
        Console.WriteLine($"Modified: {e.FullPath}");

        // Enqueue the event to the queue for further processing
        EventQueue.Enqueue(e);
    }

    // Handle the Renamed event
    static void HandleRenamedEvent(RenamedEventArgs e)
    {
        // Ignore temporary files created by editors (e.g., .swp, .goutputstream)
        if (IsTemporaryFile(e.FullPath) || IsTemporaryFile(e.OldFullPath))
        {
            return; // Ignore these files
        }

        // Log file renaming
        Console.WriteLine($"Renamed: {e.OldFullPath} → {e.FullPath}");

        // Enqueue the event to the queue for further processing
        EventQueue.Enqueue(e);
    }

    // Function to detect temporary files like .swp or .goutputstream
    static bool IsTemporaryFile(string filePath)
    {
        return filePath.EndsWith(".swp") || filePath.Contains(".goutputstream");
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
