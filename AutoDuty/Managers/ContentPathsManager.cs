using AutoDuty.Helpers;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using System.Text;
using System.Text.RegularExpressions;
using AutoDuty.Logging;

namespace AutoDuty.Managers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Configurations;
    using Data;
    using Newtonsoft.Json;
    using static Data.Classes;

    internal static class ContentPathsManager
    {
        internal static Dictionary<uint, ContentPathContainer> DictionaryPaths = [];

        public class ContentPathContainer
        {
            public ContentPathContainer(Content content)
            {
                this.Content = content;
                this.ID         = content.TerritoryType;

                this.ColoredNameString = $"({ImGuiHelper.idColor}{this.ID}</>) {ImGuiHelper.dutyColor}{this.Content!.Name}</>";
                this.ColoredNameRegex     = RegexHelper.ColoredTextRegex().Match(this.ColoredNameString);
            }

            public uint ID { get; }

            public Content Content { get; }

            public List<DutyPath> Paths { get; } = [];

            public string ColoredNameString { get; }

            public Match ColoredNameRegex { get; private set; }

            public DutyPath? SelectPath(out int pathIndex, Job? job = null)
            {
                job ??= PlayerHelper.GetJob();

                DutyPath defaultPath = this.Paths[0];

                if (job == null)
                {
                    pathIndex = 0;
                    return defaultPath;
                }

                if (this.Paths.Count > 1)
                {
                    if (AutoDuty.Configuration.Meta.PathSelectionsByPath.TryGetValue(this.Content.TerritoryType, out Dictionary<string, JobWithRole>? jobConfig))
                        if(jobConfig != null)
                            foreach ((string? pathName, JobWithRole pathJobs) in jobConfig)
                                if (pathJobs.HasJob((Job)job))
                                {
                                    int pInx = this.Paths.IndexOf(dp => dp.FileName.Equals(pathName));

                                    if (pInx >= 0 && pInx < this.Paths.Count)
                                    {
                                        pathIndex = pInx;

                                        ADLogging.Debug($"Selecting path {pathIndex} from {this.Paths.Count}");

                                        return this.Paths[pathIndex];
                                    }
                                }

                    //temporary while w2w gets integrated
                    if (!defaultPath.W2WFound && AutoDuty.Configuration.DutyConfig.W2WJobs.HasJob(job.Value))
                        for (int index = 0; index < this.Paths.Count; index++)
                        {
                            string curPath = this.Paths[index].Name;
                            if (curPath.Contains(PathIdentifiers.W2W))
                            {
                                pathIndex = index;
                                return this.Paths[index];
                            }
                        }
                }

                pathIndex = 0;
                return defaultPath;
            }

            public void AddPath(string name) => 
                this.Paths.Add(new DutyPath(name, this));
        }

        public class DutyPath
        {
            public DutyPath(string filePath, ContentPathContainer container)
            {
                this.container = container;

                this.FilePath  = filePath;
                this.FileName  = Path.GetFileName(filePath);
                this.Name      = this.FileName.Replace(".json", string.Empty);
            }

            public void UpdateColoredNames()
            {
                Match pathMatch = RegexHelper.PathFileRegex().Match(this.FileName);

                string pathFileColor = ConfigurationMain.Instance.DoNotUpdatePathFiles.Contains(this.FileName) ? ImGuiHelper.pathFileColorNoUpdate : ImGuiHelper.pathFileColor;
                this.ColoredNameString = pathMatch.Success ?
                                             $"<0.8,0.8,1>{pathMatch.Groups[4]}</>{pathFileColor}{pathMatch.Groups[5]}</>" :
                                             this.FileName;
                this.ColoredNameRegex = RegexHelper.ColoredTextRegex().Match(this.ColoredNameString);
            }

            public readonly ContentPathContainer container;

            public string Name     { get; }
            public string FileName { get; }
            public string FilePath { get; }

            public string ColoredNameString
            {
                get
                {
                    if(field == null)
                        this.UpdateColoredNames();
                    return field;
                }
                private set;
            } = null!;

            public Match ColoredNameRegex
            {
                get
                {
                    if (field == null)
                        this.UpdateColoredNames();
                    return field;
                }
                private set;
            } = null!;

            public PathFile PathFile
            {
                get
                {
                    if (field == null)
                        try
                        {
                            this.RevivalFound = false;
                            this.W2WFound     = false;

                            string json;

                            using (StreamReader streamReader = new(this.FilePath, Encoding.UTF8))
                                json = streamReader.ReadToEnd();


                            field = JsonConvert.DeserializeObject<PathFile>(json, ConfigurationMain.JsonSerializerSettings);

                            this.RevivalFound = this.PathFile.Actions.Any(x => x.Tag.HasFlag(ActionTag.Revival));
                            this.W2WFound     = this.PathFile.Actions.Any(x => x.Tag.HasFlag(ActionTag.W2W));

                            
                            if (field.Meta.LastUpdatedVersion < 304)
                            {

                                field.Meta.Changelog.Add(new PathFileChangelogEntry
                                                         {
                                                             Version = 304,
                                                             Change  = "Version update"
                                                         });

                                json = JsonConvert.SerializeObject(field, ConfigurationMain.JsonSerializerSettings);
                                File.WriteAllText(FilePath, json);
                            }
                        }
                        catch (Exception ex)
                        {
                            ADLogging.Info($"{this.FilePath} is not a valid duty path: {ex}");
                            this.container.Paths.Remove(this);
                        }

                    return field!;
                }
            } = null;

            public List<PathAction> Actions      => this.PathFile.Actions;
            public bool             RevivalFound { get; private set; }
            public bool             W2WFound     { get; private set; }
        }
    }

    internal static class ContentPathContainerExtensions
    {
        public static bool IsFirstPath(this ContentPathsManager.ContentPathContainer container, ContentPathsManager.DutyPath dp) => 
            container.Paths[0] == dp;
    }
}
