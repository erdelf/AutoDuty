namespace AutoDuty.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using Configurations;
    using Dalamud.Interface.Utility.Raii;
    using ECommons.ExcelServices;
    using Dalamud.Bindings.ImGui;

    public static class Enums
    {
        [Flags]
        public enum ActionTag
        {
            None     = 0,
            Synced   = 1 << 0,
            Unsynced = 1 << 1,
            Comment  = 1 << 2,
            Revival  = 1 << 3,
            Treasure = 1 << 4,
            W2W      = 1 << 5
        }

        public enum RotationPlugin
        {
            BossMod              = 0,
            RotationSolverReborn = 1,
            WrathCombo           = 2,
            All                  = 3
        }

        [Flags]
        public enum JobWithRole
        {
            None        = 0,
            Paladin     = 1 << 0,
            Warrior     = 1 << 1,
            Dark_Knight = 1 << 2,
            Gunbreaker  = 1 << 3,
            Tanks       = Paladin | Warrior | Dark_Knight | Gunbreaker,
            White_Mage  = 1 << 4,
            Scholar     = 1 << 5,
            Astrologian = 1 << 6,
            Sage        = 1 << 7,
            Healers     = White_Mage | Scholar | Astrologian | Sage,
            Monk        = 1 << 8,
            Dragoon     = 1 << 9,
            Ninja       = 1 << 10,
            Samurai     = 1 << 11,
            Reaper      = 1 << 12,
            Viper       = 1 << 13,
            Beastmaster = 1 << 22,
            Striking    = Monk     | Samurai | Beastmaster ,
            Maiming     = Dragoon  | Reaper,
            Scouting    = Ninja    | Viper,
            Melee       = Striking | Maiming | Scouting,
            Bard        = 1 << 14,
            Machinist   = 1 << 15,
            Dancer      = 1 << 16,
            Aiming      = Bard | Machinist | Dancer,
            Black_Mage  = 1 << 17,
            Summoner    = 1 << 18,
            Red_Mage    = 1 << 19,
            Pictomancer = 1 << 20,
            Blue_Mage   = 1 << 21,
            Casters     = Black_Mage | Summoner | Red_Mage | Pictomancer | Blue_Mage,
            DPS         = Melee      | Aiming   | Casters,
            All         = Tanks      | Healers  | DPS
        }

        public enum JobRole
        {
            None = 0,
            Tank = 1,
            Melee = 2,
            Ranged = 3,
            Healer = 4,
            Ranged_Physical = 5,
            Ranged_Magical = 6,
            Disciple_Of_The_Land = 7,
            Disciple_Of_The_Hand = 8,
        }

        public enum LootMethod : int
        {
            AutoDuty = 0,
            //[Obsolete("Removed by RSR", false)] RotationSolver = 1,
            Pandora = 2,
            All     = 3
        }

        public enum GearsetUpdateSource : int
        {
            Vanilla,
            Gearsetter,
            Stylist
        }

        public enum Housing : int
        {
            Apartment = 1,
            Personal_Home = 2,
            FC_Estate = 3,
        }
        public enum RetireLocation : int
        {
            Inn = 0,
            Apartment = 1,
            Personal_Home = 2,
            FC_Estate = 3,
            GC_Barracks = 4,
            Lifestream_Auto = 5
        }
        public enum TerminationMode : int
        {
            Do_Nothing = 0,
            Logout = 1,
            Start_AR_Multi_Mode = 2,
            Kill_Client = 3,
            Kill_PC = 4,
            Start_AR_Night_Mode = 5
        }
        public enum Role : int
        {
            Tank = 0,
            Healer = 1,
            Ranged_DPS = 2,
            Melee_DPS = 3
        }
        public enum Positional : int
        {
            Any = 0,
            Flank = 1,
            Rear = 2,
            Front = 3
        }
        public enum SummoningBellLocations : uint
        {
            Inn = 0,
            Apartment = 1,
            Personal_Home = 2,
            FC_Estate = 3,
            Limsa_Lominsa_Lower_Decks = 129,
            Old_Gridania = 133,
            Uldah_Steps_of_Thal = 131,
            The_Pillars = 419,
            Rhalgrs_Reach = 635,
            Kugane = 628,
            The_Doman_Enclave = 759,
            The_Crystarium = 819,
            Eulmore = 820,
            Old_Sharlayan = 962,
            Radz_at_Han = 963,
            Tuliyollal = 1185,
            Nexus_Arcade = 1186
        }

        public enum DutyState : int
        {
            None = 0,
            DutyStarted = 1,
            DutyWiped = 2,
            DutyRecommenced = 3,
            DutyComplete = 4,
        }

        public enum Stage : int
        {
            Stopped = 0,
            Reading_Path = 1,
            Action = 2,
            Looping = 3,
            Condition = 4,
            Moving = 5,
            Waiting_For_Combat = 6,
            Paused = 7,
            Dead = 8,
            Revived = 9,
            Interactable = 10,
            Idle = 11
        }

        public enum ActionState : int
        {
            None = 0,
            Running = 1
        }

        public enum AutoDutyMode : byte
        {
            Looping = 0,
            Playlist = 1
        }

        [Flags]
        public enum DutyMode : int
        {
            None       = 0 << 0,
            Support    = 1 << 0,
            Trust      = 1 << 1,
            Squadron   = 1 << 2,
            Regular    = 1 << 3,
            Trial      = 1 << 4,
            Raid       = 1 << 5,
            Variant    = 1 << 6,
            NoviceHall = 1 << 7,
            Crucible   = 1 << 8
        }

        public enum CrucibleTeamMode : int
        {
            Recommended = 0,
            Leveling    = 1,
            Custom      = 2
        }

        public enum CrucibleLevelingMode : int
        {
            Full = 0,
            Minus_3 = 1,
            Only_3 = 2,
            Carry_Minus_2 = 3
        }


        public enum LevelingMode : int
        {
            None = 0,
            Support = 1,
            Trust_Group = 2,
            Trust_Solo = 3
        }

        [Flags]
        public enum PluginState : int
        {
            None = 0,
            Looping = 1,
            Navigating = 2,
            Paused = 4,
            Other = 8
        }

        [Flags]
        public enum SettingsActive : int
        {
            None = 0,
            Vnav_Align_Camera_Off = 1,
            Pandora_Interact_Objects = 2,
            YesAlready = 4,
            PreLoop_Enabled = 8,
            BetweenLoop_Enabled = 16,
            TerminationActions_Enabled = 32,
            BareMode_Active = 64
        }

        public enum Sounds : byte
        {
            None = 0x00,
            Unknown = 0x01,
            Sound01 = 0x25,
            Sound02 = 0x26,
            Sound03 = 0x27,
            Sound04 = 0x28,
            Sound05 = 0x29,
            Sound06 = 0x2A,
            Sound07 = 0x2B,
            Sound08 = 0x2C,
            Sound09 = 0x2D,
            Sound10 = 0x2E,
            Sound11 = 0x2F,
            Sound12 = 0x30,
            Sound13 = 0x31,
            Sound14 = 0x32,
            Sound15 = 0x33,
            Sound16 = 0x34,
        }

        public enum TrustMemberName : byte
        {
            Alphinaud = 1,
            Alisaie = 2,
            Thancred = 3,
            Urianger = 5,
            Yshtola = 6,
            Ryne = 7,
            Estinien = 12,
            Graha = 10,
            Zero = 41,
            Krile = 60
        }

        public enum TrustRole : byte
        {
            DPS = 0,
            Healer = 1,
            Tank = 2,
            AllRounder = 3
        }

        public enum PlayerLifeState
        {
            Alive = 0,
            Dead = 1,
            Revived = 2
        }

        //AutoRetainer
        public enum FCHousingMarker : uint
        {
            SmallYellow = 60761,
            MediumYellow = 60762,
            LargeYellow = 60763,
            SmallBlue = 60764,
            MediumBlue = 60765,
            LargeBlue = 60766,
        }

        //AutoRetainer
        public enum ApartmentHousingMarker : uint
        {
            PartiallyFilled = 60790,
            Full = 60792,
        }

        //AutoRetainer
        public enum PrivateHousingMarker : uint
        {
            SmallYellow = 60776,
            MediumYellow = 60777,
            LargeYellow = 60778,
            SmallBlue = 60779,
            MediumBlue = 60780,
            LargeBlue = 60781,
        }

        [Flags]
        public enum PathActionFlags : int
        {
            None = 0,
            NoPartyCoherency = 1 << 0
        }

        [Flags]
        public enum ExternalPlugin
        {
            None                 = 0,
            vnav                 = 1 << 0,
            BossMod              = 1 << 1,
            Avarice              = 1 << 2,
            RotationSolverReborn = 1 << 3,
            WrathCombo           = 1 << 4,
            AutoRetainer         = 1 << 5,
            Gearsetter           = 1 << 6,
            Stylist              = 1 << 7,
            Lifestream           = 1 << 8,
            AntiAFK              = 1 << 9,
            Pandora              = 1 << 10,
            GlamourLog           = 1 << 11
        }

        public enum ConditionType
        {
            None,
            Distance,
            ItemCount,
            ObjectData,
            Job,
            ActionStatus,
            VariantPath,
            ConditionFlag,
            Collision,
            ToDo,
            Not,
            Or,
            And
        }

        public enum ObjectDataProperty
        {
            EventState,
            IsTargetable
        }

        public enum DistanceLocationTypes
        {
            Player,
            Object,
            Location
        }


        extension<T>(T instance) where T : Enum
        {
            public bool HasAnyFlag(params T[] parameter) => 
                parameter.Any(enu => instance.HasFlag(enu));

            public T[] GetFlags() =>
                [.. Enum.GetValues(typeof(T)).Cast<T>().Where(t => instance.HasFlag(t) && (int)(object)t != 0)];
        }
    }

    public static class JobWithRoleHelper
    {
        private static readonly List<JobWithRole> enumVals = [..Enum.GetValues<JobWithRole>().Skip(1)];

        private static readonly Dictionary<JobWithRole, IEnumerable<JobWithRole>> CATEGORIES = enumVals.Select(jwr => (jwr, enumVals.Where(jwrr => jwr != jwrr && jwr.HasFlag(jwrr)))).Where(j => j.Item2.Any())
                                                                                                       .ToDictionary(j => j.jwr, j => j.Item2);

        private static readonly Dictionary<JobWithRole, IEnumerable<JobWithRole>> VALUES = enumVals.Select(jwr => (jwr, enumVals.Where(jwrr => jwr != jwrr && jwrr.HasFlag(jwr)))).Where(j => j.Item2.Any())
                                                                                                   .ToDictionary(j => j.jwr, j => j.Item2);

        extension(JobWithRole value)
        {
            public bool HasJobFlagFast(JobWithRole flag) =>
                (value & flag) == flag;

            public bool HasJob(Job job)
            {
                JobWithRole jw = job.JobToJobWithRole();
                return value.HasJobFlagFast(jw);
            }

            public IEnumerable<Job> ContainedJobs() =>
                Enum.GetValuesAsUnderlyingType<Job>().Cast<Job>().Where(job => value.HasJob(job));
        }

        public static JobWithRole JobToJobWithRole(this Job job) =>
            job switch
            {
                Job.GLA or Job.PLD => JobWithRole.Paladin,
                Job.MRD or Job.WAR => JobWithRole.Warrior,
                Job.DRK => JobWithRole.Dark_Knight,
                Job.GNB => JobWithRole.Gunbreaker,
                Job.CNJ or Job.WHM => JobWithRole.White_Mage,
                Job.SCH => JobWithRole.Scholar,
                Job.SGE => JobWithRole.Sage,
                Job.AST => JobWithRole.Astrologian,
                Job.PGL or Job.MNK => JobWithRole.Monk,
                Job.LNC or Job.DRG => JobWithRole.Dragoon,
                Job.ROG or Job.NIN => JobWithRole.Ninja,
                Job.SAM => JobWithRole.Samurai,
                Job.RPR => JobWithRole.Reaper,
                Job.VPR => JobWithRole.Viper,
                Job.ARC or Job.BRD => JobWithRole.Bard,
                Job.MCH => JobWithRole.Machinist,
                Job.DNC => JobWithRole.Dancer,
                Job.THM or Job.BLM => JobWithRole.Black_Mage,
                Job.ACN or Job.SMN => JobWithRole.Summoner,
                Job.RDM => JobWithRole.Red_Mage,
                Job.PCT => JobWithRole.Pictomancer,
                Job.BLU => JobWithRole.Blue_Mage,
                _ => JobWithRole.None
            };

        public static bool DrawSelectable(JobWithRole jwr, ref JobWithRole config, bool allowRemoval = true)
        {
            int flag = (int)config;
            
            using(ImRaii.Disabled(!allowRemoval && config.HasFlag(jwr)))
            {
                if (ImGui.CheckboxFlags(jwr.ToLocalizedString(), ref flag, (int)jwr))
                {
                    config = (JobWithRole)flag;
                    ConfigurationProfileV2.Save();
                    return true;
                }
            }

            return false;
        }

        public static bool DrawCategory(JobWithRole category, ref JobWithRole config, bool allowRemoval = true)
        {
            ImGui.PushStyleColor(ImGuiCol.Header,        Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.2f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive,  new Vector4(0.3f));
            bool collapse = ImGui.CollapsingHeader("##" + category, ImGuiTreeNodeFlags.AllowItemOverlap);
            ImGui.PopStyleColor(3);
            ImGui.SameLine();
            bool changed = DrawSelectable(category, ref config, allowRemoval);
            if (collapse)
            {
                ImGui.Indent();
                foreach (JobWithRole jobW in CATEGORIES[category])
                    if (VALUES[jobW].MinBy(jwr => CATEGORIES[jwr].Count()) == category)
                        if (CATEGORIES.ContainsKey(jobW))
                        {
                            changed |= DrawCategory(jobW, ref config, allowRemoval);
                        }
                        else
                        {
                            ImGui.Indent();
                            changed |= DrawSelectable(jobW, ref config, allowRemoval);
                            ImGui.Unindent();
                        }

                ImGui.Unindent();
            }
            return changed;
        }
    }
}
