using AutoDuty.Helpers;
using System.Security.Cryptography;
using ECommons;
using Serilog.Events;
using AutoDuty.Windows;

namespace AutoDuty.Updater
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using static Data.Classes;
    internal static class FileHelper
    {
        internal static readonly FileSystemWatcher FILESYSTEM_WATCHER = new(Plugin.pathsDirectory.FullName)
        {
            NotifyFilter = NotifyFilters.Attributes
                                                                                        | NotifyFilters.CreationTime
                                                                                        | NotifyFilters.DirectoryName
                                                                                        | NotifyFilters.FileName
                                                                                        | NotifyFilters.LastAccess
                                                                                        | NotifyFilters.LastWrite
                                                                                        | NotifyFilters.Security
                                                                                        | NotifyFilters.Size,

            Filter = "*.json",
            IncludeSubdirectories = true
        };

        internal static readonly FileSystemWatcher fileWatcher = new();

        private static readonly Lock updateLock = new();


        public static byte[] CalculateMD5(string filename)
        {
            using MD5?        md5    = MD5.Create();
            using FileStream? stream = File.OpenRead(filename);
            return md5.ComputeHash(stream);
        }

        internal static void Init()
        {
            FILESYSTEM_WATCHER.Changed += OnChanged;
            FILESYSTEM_WATCHER.Created += OnCreated;
            FILESYSTEM_WATCHER.Deleted += OnDeleted;
            FILESYSTEM_WATCHER.Renamed += OnRenamed;
            FILESYSTEM_WATCHER.EnableRaisingEvents = true;
            Update();
        }

        private static void Update()
        {
            lock (updateLock)
            {
                ContentPathsManager.DictionaryPaths = [];

                MainTab.PathsUpdated();
                PathsTab.PathsUpdated();

                foreach ((_, Content content) in ContentHelper.DictionaryContent)
                {
                    IEnumerable<FileInfo> files = Plugin.pathsDirectory.EnumerateFiles($"({content.TerritoryType})*.json", SearchOption.AllDirectories);

                    foreach (FileInfo file in files)
                    {
                        if (!ContentPathsManager.DictionaryPaths.ContainsKey(content.TerritoryType))
                            ContentPathsManager.DictionaryPaths.Add(content.TerritoryType, new ContentPathsManager.ContentPathContainer(content));

                        ContentPathsManager.DictionaryPaths[content.TerritoryType].AddPath(file.FullName);
                    }
                }
            }
        }

        private static void OnChanged(object sender, FileSystemEventArgs e) => Update();

        private static void OnCreated(object sender, FileSystemEventArgs e) => Update();

        private static void OnDeleted(object sender, FileSystemEventArgs e) => Update();

        private static void OnRenamed(object sender, RenamedEventArgs e) => Update();
    }
}
