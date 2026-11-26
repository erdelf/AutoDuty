using ECommons.Logging;
using ECommons.Throttlers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace AutoDuty.Logging;

using System.Linq;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;

public static class ADLogging
{
    private static string GetCallerPrefix(int depth = 3)
    {
        StackFrame  stackFrame = new(depth);
        MethodBase? method     = stackFrame.GetMethod();
        string?     className  = method?.DeclaringType?.Name;
        string?     methodName = method?.Name;

        if (className != null && methodName != null)
            return $"[{className}.{methodName}]";

        if (className != null)
            return $"[{className}]";

        if (methodName != null)
            return $"[{methodName}]";

        return string.Empty;
    }

    private static string FormatMessage(string message, string? prefix)
    {
        string callerPrefix = prefix ?? GetCallerPrefix();
        return $"{callerPrefix} {message}";
    }

    public static void Verbose(string message, string? prefix = null, bool debugOnly = false)
    {
        LogSystem.Log(LogLevel.Verbose, message, prefix);
        if (debugOnly)
        {
        #if DEBUG
            string formattedMessage = FormatMessage(message, prefix);
            PluginLog.Verbose(formattedMessage);
        #endif
        }
        else
        {
            string formattedMessage = FormatMessage(message, prefix);
            PluginLog.Verbose(formattedMessage);
        }
    }

    public static void Debug(string message, string? prefix = null, bool debugOnly = false)
    {
        LogSystem.Log(LogLevel.Debug, message, prefix);
        if (debugOnly)
        {
        #if DEBUG
            string formattedMessage = FormatMessage(message, prefix);
            PluginLog.Debug(formattedMessage);
        #endif
        }
        else
        {
            string formattedMessage = FormatMessage(message, prefix);
            PluginLog.Debug(formattedMessage);
        }
    }

    public static void Info(string message, string? prefix = null, bool debugOnly = false)
    {
        LogSystem.Log(LogLevel.Info, message, prefix);
        if (debugOnly)
        {
        #if DEBUG
            string formattedMessage = FormatMessage(message, prefix);
            PluginLog.Information(formattedMessage);
        #endif
        }
        else
        {
            string formattedMessage = FormatMessage(message, prefix);
            PluginLog.Information(formattedMessage);
        }
    }

    public static void ChatInfo(string s, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Info, s, prefix);
        if (EzThrottler.Throttle($"Throttling chat message: {s}", 1000))
        {
            string formattedMessage = FormatMessage(s, prefix);
            Svc.Chat.Print(formattedMessage);
            PluginLog.Information(formattedMessage);
        }
    }

    public static void ChatError(string s, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Error, s, prefix);
        if (EzThrottler.Throttle($"Throttling chat message: {s}", 60000))
        {
            string formattedMessage = FormatMessage(s, prefix);
            ECommons.ChatMethods.ChatPrinter.Red(formattedMessage);
            PluginLog.Error(formattedMessage);
        }
    }

    public static void Warning(string message, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Warning, message, prefix);
        string formattedMessage = FormatMessage(message, prefix);
        PluginLog.Warning(formattedMessage);
    }

    public static void Warning(Exception? ex, string message, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Warning, message, prefix);
        string formattedMessage = FormatMessage(message, prefix);
        Svc.Log.Warning(ex, formattedMessage);
    }

    public static void Error(string message, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Error, message, prefix);
        string formattedMessage = FormatMessage(message, prefix);
        PluginLog.Error(formattedMessage);
    }

    public static void Error(Exception? ex, string message, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Error, message, prefix);
        string formattedMessage = FormatMessage(message, prefix);
        Svc.Log.Error(ex, formattedMessage);
    }

    public static void Fatal(string message, string? prefix = null)
    {
        LogSystem.Log(LogLevel.Fatal, message, prefix);
        string formattedMessage = FormatMessage(message, prefix);
        PluginLog.Fatal(formattedMessage);
    }

    [Flags]
    public enum LogLevel
    {
        Verbose = 1 << 0,
        Debug   = 1 << 1,
        Info    = 1 << 2,
        Warning = 1 << 3,
        Error   = 1 << 4,
        Fatal   = 1 << 5
    }

    public class LogEntry(LogLevel level, string message, string? category = null)
    {
        public DateTime Timestamp      { get; set; } = DateTime.Now;
        public DateTime LastOccurrence { get; set; } = DateTime.Now;
        public LogLevel Level          { get; set; } = level;
        public string   Message        { get; set; } = message;
        public string?  Category       { get; set; } = category;
        public int      Count          { get; set; } = 1;

        public bool IsRepeating => this.Count > 1;

        public string GetKey() => $"{this.Level}|{this.Category ?? ""}|{this.Message}";
    }

    public static class LogSystem
    {
        private static readonly List<LogEntry>               logs                = [];
        private static readonly Dictionary<string, LogEntry> recentLogs          = []; // Track recent logs for time-window matching
        private const           int                          MaxLogCount         = 1000;
        private static readonly TimeSpan                     consolidationWindow = TimeSpan.FromMilliseconds(500); // Merge duplicates

        public static IReadOnlyList<LogEntry> Logs => logs.AsReadOnly();

        public static void Log(LogLevel level, string message, string? category = null)
        {
            category ??= GetCallerPrefix(3);
            string   key = $"{level}|{category ?? ""}|{message}";
            DateTime now = DateTime.Now;

            // Check if we have this exact log recently (within consolidation window)
            if (recentLogs.TryGetValue(key, out LogEntry? recentLog))
            {
                TimeSpan timeSinceLastOccurrence = now - recentLog.LastOccurrence;

                // If it happened recently, just increment the counter
                if (timeSinceLastOccurrence <= consolidationWindow)
                {
                    recentLog.Count++;
                    recentLog.LastOccurrence = now;
                    return; // Don't add a new entry
                }
                else
                {
                    // Too old, this is a new burst
                    recentLogs.Remove(key);
                }
            }

            // Check if the LAST log entry is identical (for consecutive duplicates)
            if (logs.Count > 0)
            {
                LogEntry lastLog = logs[^1];

                if (lastLog.GetKey() == key)
                {
                    lastLog.Count++;
                    lastLog.LastOccurrence = now;
                    recentLogs[key]        = lastLog; // Update recent tracker
                    return;
                }
            }

            // New unique entry
            LogEntry newLog = new(level, message, category);
            logs.Add(newLog);
            recentLogs[key] = newLog;

            // Cleanup old entries from recent logs dictionary (older than window)
            CleanupRecentLogs(now);

            // Prune old logs
            if (logs.Count > MaxLogCount)
                logs.RemoveAt(0);
        }

        private static void CleanupRecentLogs(DateTime now)
        {
            // Remove entries older than the consolidation window
            List<string> keysToRemove =
            [
                .. recentLogs
                  .Where(kvp => now - kvp.Value.LastOccurrence > consolidationWindow)
                  .Select(kvp => kvp.Key)
            ];

            foreach (string key in keysToRemove)
                recentLogs.Remove(key);
        }

        public static void Clear()
        {
            logs.Clear();
            recentLogs.Clear();
        }

        public static void CopyToClipboard()
        {
            StringBuilder sb = new();
            foreach (LogEntry log in logs)
            {
                string countSuffix = log.Count > 1 ? $" (x{log.Count})" : "";
                string timeRange   = log.Count > 1 ? $"{log.Timestamp:HH:mm:ss} - {log.LastOccurrence:HH:mm:ss}" : $"{log.Timestamp:HH:mm:ss}";

                sb.AppendLine($"[{timeRange}] [{log.Level}] {(log.Category != null ? $"[{log.Category}] " : "")}{log.Message}{countSuffix}");
            }

            ImGui.SetClipboardText(sb.ToString());
        }
    }
}