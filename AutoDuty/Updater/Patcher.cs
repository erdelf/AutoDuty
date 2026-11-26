using ECommons.DalamudServices;
using AutoDuty.Logging;

namespace AutoDuty.Updater
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Configurations;

    public class Patcher
    {
        internal static ActionState PatcherState => PatcherTask is { IsCompleted: false, IsCanceled: false, IsFaulted: false } ? ActionState.Running : ActionState.None;
            
        internal static Task<bool>? PatcherTask = null;

        internal static void Patch(bool skipMD5 = false, bool startup = false)
        {
            if (PatcherTask == null && (!startup || ConfigurationMain.Instance.UpdatePathsOnStartup))
            {
                PatcherTask = Task.Run(() => PatchTask(skipMD5));
                PatcherTask.ContinueWith(t => {
                    OnPatcherTaskCompleted(t.IsCompletedSuccessfully);
                });
            }
        }

        private static void OnPatcherTaskCompleted(bool success)
        {
            if (PatcherTask != null && success && PatcherTask.Result)
                ADLogging.Info("Patching Complete");
            PatcherTask = null;
        }

        public static async Task<bool> PatchTask(bool skipMD5)
        {
            ADLogging.Info("Patching Started");
            try
            {
                IEnumerable<FileInfo> localFileInfos = Plugin.pathsDirectory.EnumerateFiles("*.json", SearchOption.AllDirectories);
                Dictionary<string, string> localFilesDictionary = skipMD5 ? [] : localFileInfos.ToDictionary(
                                                                                              fileInfo => fileInfo.Name,
                                                                                              fileInfo => Convert.ToHexString(FileHelper.CalculateMD5(fileInfo.FullName))
                                                                                             );

                (Dictionary<string, string>? md5, Dictionary<string, string>? del) = await GitHubHelper.GetPathUpdateInfoAsync();

                HashSet<string> doNotUpdatePathFiles = ConfigurationMain.Instance.DoNotUpdatePathFiles;
                if (md5 != null)
                {
                    IEnumerable<KeyValuePair<string, string>> downloadList =
                        md5.Where(kvp => !doNotUpdatePathFiles.Contains(kvp.Key) && (!localFilesDictionary.ContainsKey(kvp.Key) || !localFilesDictionary[kvp.Key].Equals(kvp.Value, StringComparison.OrdinalIgnoreCase)));

                    foreach (KeyValuePair<string, string> file in downloadList)
                    {
                        bool    result = await GitHubHelper.DownloadFileAsync($"https://raw.githubusercontent.com/erdelf/AutoDuty/refs/heads/master/AutoDuty/Paths/{file.Key}", $"{Plugin.pathsDirectory.FullName}/{file.Key}");
                        ADLogging.Info(result ? $"Successfully downloaded: {file.Key}" : $"Failed to download: {file.Key}");
                    }
                }

                if (del != null)
                {
                    IEnumerable<KeyValuePair<string, string>>? deleteList =
                        del.Where(kvp => !doNotUpdatePathFiles.Contains(kvp.Key) && localFilesDictionary.ContainsKey(kvp.Key) && localFilesDictionary[kvp.Key].Equals(kvp.Value, StringComparison.OrdinalIgnoreCase));

                    foreach (KeyValuePair<string, string> file in deleteList)
                    {
                        File.Delete($"{Plugin.pathsDirectory.FullName}/{file.Key}");
                        ADLogging.Info("Deleted " + file.Key);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ADLogging.Error($"error patching path files: {ex}");
                return false;
            }
        }
    }
}
