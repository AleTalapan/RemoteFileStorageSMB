using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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

        watcher.Created += (s, e) => HandleFileEvent(e);
        watcher.Changed += (s, e) => HandleFileEvent(e);
        watcher.Deleted += (s, e) => HandleFileEvent(e);
        watcher.Renamed += (s, e) => HandleFileEvent(e);

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

                // If the event is a rename, handle it differently
                if (e is RenamedEventArgs renamedEvent)
                {
                    // Only output renamed events for actual file renames (not temp files)
                    if (!IsTemporaryFile(renamedEvent.OldFullPath) && !IsTemporaryFile(renamedEvent.FullPath))
                    {
                        Console.WriteLine($"Renamed: {renamedEvent.OldFullPath} → {renamedEvent.FullPath}");
                    }
                }
                else if (e is FileSystemEventArgs fileEvent)
                {
                    // Handle regular file changes (e.g., Created, Deleted, Modified)
                    if (!IsTemporaryFile(fileEvent.FullPath))
                    {
                        Console.WriteLine($"{e.ChangeType}: {e.FullPath}");
                    }
                }

                ProcessedEvents++;

                if (ProcessedEvents % 10000 == 0)
                    CleanupOldEntries();
            }

            Thread.Sleep(100);
        }
    }

    // Event handler to process file system events
    static void HandleFileEvent(FileSystemEventArgs e)
    {
        // Skip temporary files (e.g., .goutputstream, .swp, etc.)
        if (IsTemporaryFile(e.FullPath))
        {
            // If it's a temporary file and it's related to a modification, treat it as a modification
            if (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Renamed)
            {
                // Determine the original file name (remove .goutputstream suffix)
                string originalFileName = GetOriginalFileName(e.FullPath);
                Console.WriteLine($"Modified: {originalFileName}");
                return; // Skip further processing of temporary files
            }
            return; // Skip any further actions for temporary files
        }

        // If it's a real file event, just enqueue it for processing
        EventQueue.Enqueue(e);
    }

    // Function to detect temporary files like .swp or .goutputstream
    static bool IsTemporaryFile(string filePath)
    {
        return filePath.Contains(".goutputstream") || filePath.EndsWith(".swp");
    }

    // Function to get the original file name from a temporary file name (e.g., remove the .goutputstream part)
    static string GetOriginalFileName(string tempFilePath)
    {
        // Remove the temporary file extension (e.g., .goutputstream) to get the original file name
        if (tempFilePath.Contains(".goutputstream"))
        {
            return tempFilePath.Replace(".goutputstream", "");
        }
        return tempFilePath; // Return the original path if it's not a temporary file
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
