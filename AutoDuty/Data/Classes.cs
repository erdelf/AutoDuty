using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using Serilog.Events;
using System.Numerics;
using System.Text.Json.Serialization;

namespace AutoDuty.Data
{
    using ECommons.ExcelServices;
    using Helpers;
    using Lumina.Excel.Sheets;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Configurations;
    using ECommons.DalamudServices;

    public class Classes
    {
        public class LogMessage
        {
            public string        Message       { get; set; } = string.Empty;
            public LogEventLevel LogEventLevel { get; set; }
        }

        public class Message
        {
            public string           Sender { get; set; } = string.Empty;
            public List<PathAction> Action { get; set; } = [];
        }

        public class Content
        {
            public uint              RowId                  { get; init; }
            public uint              Id                     { get; init; }
            public string?           Name                   { get; init; }
            public string?           EnglishName            { get; init; }
            public uint              TerritoryType          { get; init; }
            public uint              ExVersion              { get; init; }
            public byte              ClassJobLevelRequired  { get; init; }
            public uint              ItemLevelRequired      { get; init; }
            public uint              DawnRowId              { get; init; }
            public int               DawnIndex              { get; init; } = -1;
            public ushort            DawnIndicator          { get; set; }
            public uint              ContentFinderCondition { get; init; }
            public uint              ContentType            { get; set; }
            public uint              ContentMemberType      { get; set; }
            public int               TrustIndex             { get; init; } = -1;
            public bool              VariantContent         { get; set; }  = false;
            public int               VVDIndex               { get; init; } = -1;
            public int               GCArmyIndex            { get; init; } = -1;
            public bool              GCArmyContent          => this.GCArmyIndex >= 0;
            public List<TrustMember> TrustMembers           { get; }      = [];
            public DutyMode          DutyModes              { get; set; } = DutyMode.None;
            public uint              UnlockQuest            { get; init; }
        }

        public class TrustMember
        {
            public byte            Index      { get; set; }
            public byte[]          MemberIds  { get; set; } = [];
            public TrustRole       Role       { get; set; }         // 0 = DPS, 1 = Healer, 2 = Tank, 3 = G'raha All Rounder
            public ClassJob?       Job        { get; set; } = null; //closest actual job that applies. G'raha gets Blackmage
            public string          Name       { get; set; } = string.Empty;
            public TrustMemberName MemberName { get; set; }

            public uint Level       { get; set; }
            public uint LevelCap    { get; set; }
            public uint LevelInit   { get; set; }
            public bool LevelIsSet  { get; set; }
            public uint UnlockQuest { get; init; }

            public bool Available => this.UnlockQuest <= 0 || QuestManager.IsQuestComplete(this.UnlockQuest);

            public void ResetLevel()
            {
                this.Level      = this.LevelInit;
                this.LevelIsSet = this.LevelInit == this.LevelCap;
            }

            public void SetLevel(uint level)
            {
                if (level >= this.LevelInit - 1)
                {
                    this.LevelIsSet = true;
                    this.Level      = level;
                }
            }
        }

    #region PathFile

        public class PathFile
        {
            public List<PathAction> Actions { get; set; } = [];

            public PathFileMetaData Meta { get; set; } = new()
                                                         {
                                                             CreatedAt = Plugin.Version,
                                                             Changelog = [],
                                                             Notes     = []
                                                         };
        }

        public class PathAction
        {
            public ActionTag Tag { get; set; } = ActionTag.None;

            public string Name { get; set; } = string.Empty;

            public Vector3 Position { get; set; } = Vector3.Zero;

            public List<string> Arguments { get; set; } = [];

            /// <summary>
            /// Will skip the action if this is not fulfilled on initial check
            /// </summary>
            public List<PathActionCondition> Conditions { get; set; } = [];

            /// <summary>
            /// Will abort action if this is not fulfilled
            /// </summary>
            public List<PathActionCondition> ConditionsWhile { get; set; } = [];

            /// <summary>
            /// Won't leave action till this is fulfilled.
            /// </summary>
            public List<PathActionCondition> ConditionsUntil { get; set; } = [];

            public string Note { get; set; } = string.Empty;
            public PathActionFlags Flags { get; set; } = PathActionFlags.None;
        }

        public class PathFileMetaData
        {
            public int CreatedAt { get; set; }

            public List<PathFileChangelogEntry> Changelog { get; set; } = [];

            [JsonIgnore]
            public int LastUpdatedVersion => this.Changelog.Count > 0 ? this.Changelog.Last().Version : this.CreatedAt;

            public List<string> Notes { get; set; } = [];
        }

        public class PathFileChangelogEntry
        {
            public int Version { get; set; }

            public string Change { get; set; } = string.Empty;
        }

    #endregion

    #region github

        public class PollResponseClass
        {
            [JsonPropertyName("interval")]
            public int Interval { get; set; } = -1;

            [JsonPropertyName("error")]
            public string Error { get; set; } = string.Empty;

            [JsonPropertyName("error_description")]
            public string Error_Description { get; set; } = string.Empty;

            [JsonPropertyName("error_uri")]
            public string Error_Uri { get; set; } = string.Empty;

            [JsonPropertyName("access_token")]
            public string Access_Token = string.Empty;

            [JsonPropertyName("expires_in")]
            public int Expires_In { get; set; } = 0;

            [JsonPropertyName("refresh_token")]
            public string Refresh_Token = string.Empty;

            [JsonPropertyName("refresh_token_expires_in")]
            public int Refresh_Token_Expires_In = 0;

            [JsonPropertyName("token_type")]
            public string Token_Type = string.Empty;

            [JsonPropertyName("scope")]
            public string Scope = string.Empty;
        }

        public class UserCode
        {
            [JsonPropertyName("device_code")]
            public string Device_Code { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int Expires_In { get; set; } = 0;

            [JsonPropertyName("user_code")]
            public string User_Code { get; set; } = string.Empty;

            [JsonPropertyName("verification_uri")]
            public string Verification_Uri { get; set; } = string.Empty;

            [JsonPropertyName("interval")]
            public int Interval { get; set; } = 500;
        }

        public class GitHubIssue
        {
            [JsonPropertyName("title")]
            public string Title { get; set; } = "[Bug] ";

            [JsonPropertyName("body")]
            public string Body { get; set; } = string.Empty;

            [JsonPropertyName("labels")]
            public List<string> Labels = ["bug", "unconfirmed"];

            public static string Version => $"{Plugin.Version}";

            public static string LogFile => Plugin.dalamudLogEntries.SelectMulti(x => x.Message).ToList().ToCustomString("\n");

            public static string InstalledPlugins => PluginInterface.InstalledPlugins.Select(x => $"{x.InternalName}, Version= {x.Version}").ToList().ToCustomString("\n");

            public static string ConfigFile => ReadConfigFile().ToCustomString("\n");

            private static List<string> ReadConfigFile()
            {
                using FileStream   fs = new(Plugin.configFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader sr = new(fs);
                string?            x;
                List<string>       strings = [];
                while ((x = sr.ReadLine()) != null) strings.Add(x);
                return strings;
            }
        }

        #endregion

        [JsonObject(MemberSerialization.OptOut)]
        public class Playlist
        {
            public string Name { get; set; } = string.Empty;
            public List<PlaylistEntry> Entries { get; set; } = [];

            public void AdjustToCurrentChar()
            {
                foreach (PlaylistEntry entry in this.Entries)
                    entry.AdjustToCurrentChar();
            }
        }

        [JsonObject(MemberSerialization.OptOut)]
        public class PlaylistEntry
        {
            private uint id;

            public uint Id
            {
                get => this.id;
                set
                {
                    if (value != this.id)
                    {
                        Svc.Log.Debug(ContentPathsManager.DictionaryPaths.Count.ToString());
                        this.Path    = null;
                        this.content = null;
                    }

                    this.id = value;
                }
            }

            [JsonIgnore]
            private Content? content;

            [JsonIgnore]
            public Content? Content =>
                this.content ??= this.id == 0 ? null : ContentHelper.DictionaryContent[this.id];

            public DutyMode DutyMode
            {
                get;
                set
                {
                    if (value != field && !(this.Content?.DutyModes.HasFlag(value) ?? false))
                        this.Id = ContentPathsManager.DictionaryPaths.Keys.FirstOrDefault(key => ContentHelper.DictionaryContent[key].DutyModes.HasFlag(value));
                    field = value;
                }
            }

            public byte variantPathIndex = 0;

            public string Path
            {
                get => field ??= ContentPathsManager.DictionaryPaths[this.id].SelectPath(out _)!.FileName;
                set;
            }


            [JsonIgnore] public bool terminationConfigOpen = false;

            public              ConfigurationProfileV2.LoopConfig.TerminationConfig TerminationConfig { get; set; } = new(null);

            public int count    = 1;
            public int curCount = 0;

            public byte? gearset;

            public bool unsynced;

            public void AdjustToCurrentChar()
            {

            }
        }

        public readonly record struct DutyDataRecord(DateTime CompletionTime, TimeSpan Duration, uint TerritoryId, ulong CID, int ilvl, Job Job, int? Deaths);
    }
}