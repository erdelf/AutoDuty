using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using AutoDuty.Logging;

namespace AutoDuty.Helpers
{
    using Data;
    using ECommons.ExcelServices;
    using ECommons.GameFunctions;
    using Lumina.Excel.Sheets;
    using System.Linq;

    internal static class NoviceHelper
    {
        private static readonly uint[] territories = [535, 537, 538, 539, 540, 541, 542, 543, 544, 545, 546, 547, 548, 549, 550, 551, 552, 1127, 1128, 1129];

        private static Tutorial[] Tutorials        => field ??= Svc.Data.GameData.GetExcelSheet<Tutorial>()!.ToArray();
        private static Tutorial[] TutorialsDPS     => field ??= Svc.Data.GameData.GetExcelSheet<TutorialDPS>()!.Select(dps => dps.Objective.Value).ToArray();
        private static Tutorial[] TutorialsTank    => field ??= Svc.Data.GameData.GetExcelSheet<TutorialTank>()!.Select(dps => dps.Objective.Value).ToArray();
        private static Tutorial[] TutorialsHealer  => field ??= Svc.Data.GameData.GetExcelSheet<TutorialHealer>()!.Select(dps => dps.Objective.Value).ToArray();
        private static Tutorial[] TutorialsGimmick => field ??= Svc.Data.GameData.GetExcelSheet<TutorialGimmick>()!.Select(dps => dps.Objective.Value).ToArray();

        private const JobWithRole JobsAllowedDPS = JobWithRole.Monk | JobWithRole.Ninja | JobWithRole.Dragoon | JobWithRole.Bard | JobWithRole.Summoner | JobWithRole.Black_Mage | JobWithRole.Machinist;
        private const JobWithRole JobsAllowedTank = JobWithRole.Paladin | JobWithRole.Warrior | JobWithRole.Dark_Knight;
        private const JobWithRole JobsAllowedHealer = JobWithRole.White_Mage | JobWithRole.Scholar | JobWithRole.Astrologian;

        public static Tutorial? GetTutorialFromTerritory(uint territory)
        {
            int indexOf = territories.IndexOf(territory);
            return indexOf == -1 ? null : Tutorials[indexOf];
        }

        private static uint GetTerritoryOfTutorial(uint tutorial) =>
            territories[tutorial];

        internal static bool CanRunNovice(this Classes.Content content)
        {
            if (!content.DutyModes.HasFlag(DutyMode.NoviceHall))
                return false;

            Tutorial? tutorial = GetTutorialFromTerritory(content.TerritoryType);
            if(!tutorial.HasValue)
                return false;

            bool canRun = false;

            if(TutorialsDPS.Any(t => t.RowId == tutorial.Value.RowId))
            {
                if (JobsAllowedDPS.HasJob(PlayerHelper.GetJob()))
                    canRun = true;
            }
            if (TutorialsTank.Any(t => t.RowId == tutorial.Value.RowId))
            {
                if (JobsAllowedTank.HasJob(PlayerHelper.GetJob()))
                    canRun = true;
            }
            if (TutorialsHealer.Any(t => t.RowId == tutorial.Value.RowId))
            {
                if (JobsAllowedHealer.HasJob(PlayerHelper.GetJob()))
                    canRun = true;
            }

            if(TutorialsGimmick.Any(t => t.RowId == tutorial.Value.RowId))
                if (PlayerHelper.GetJob().GetCombatRole() != CombatRole.NonCombat)
                    canRun = true;

            return canRun;
        }

        public static Playlist CreatePlaylist()
        {
            List<PlaylistEntry> entries = [];
            List<Tutorial> tutorials = [];

            Job job = PlayerHelper.GetJob();
            if (JobsAllowedDPS.HasJob(job))
                tutorials.AddRange(TutorialsDPS);
            if (JobsAllowedTank.HasJob(job))
                tutorials.AddRange(TutorialsTank);
            if (JobsAllowedHealer.HasJob(job))
                tutorials.AddRange(TutorialsHealer);
            if (job.GetCombatRole() != CombatRole.NonCombat && PlayerHelper.GetCurrentLevelFromSheet(job) >= 49)
                tutorials.AddRange(TutorialsGimmick);

            foreach (Tutorial tutorial in tutorials)
            {
                ADLogging.Debug($"Adding NoviceHall: {tutorial.RowId}");
                uint id = GetTerritoryOfTutorial(tutorial.RowId);
                if(ContentPathsManager.DictionaryPaths.ContainsKey(id))
                    entries.Add(new PlaylistEntry
                                {
                                    Id       = id,
                                    DutyMode = DutyMode.NoviceHall,
                                    count    = 1
                                });
            }

            return new Playlist { Name = $"Novice Playlist {job.GetCombatRole()} {job}", Entries = entries };
        }
    }
}
