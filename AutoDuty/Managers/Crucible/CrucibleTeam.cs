using AutoDuty.Configurations;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;

namespace AutoDuty.Managers
{
    using ECommons.Throttlers;
    using ECommons.UIHelpers.AtkReaderImplementations;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using ECommons;
    using Screens = CrucibleUi.Screens;

    [JsonObject(MemberSerialization.OptOut)]
    public record CrucibleFamiliar
    {
        public uint   Number             { get; set; }
        public string Name               { get; set; } = string.Empty;

        public int Rank
        {
            get;
            set
            {
                if (value > field)
                    field = value;
            }
        }

        public int    Hp                 { get; set; }
        public int    Strength           { get; set; }
        public int    PhysicalResistance { get; set; }
        public int    Constitution       { get; set; }
        public int    Intelligence       { get; set; }
        public int    MagicResistance    { get; set; }
        public string Exp                { get; set; } = string.Empty;
        public string Classification     { get; set; } = string.Empty;
        public string Element            { get; set; } = string.Empty;

        public int Score() => this.Hp + this.Strength + this.PhysicalResistance + this.Constitution + this.Intelligence + this.MagicResistance;
    }

    [JsonObject(MemberSerialization.OptOut)]
    public class CrucibleCharacterData
    {
        public Dictionary<uint, CrucibleFamiliar> Familiars { get; set; } = [];
        public List<uint>                         Team      { get; set; } = [];
    }

    internal static unsafe class CrucibleTeam
    {
        public static int TeamSize(uint? id = null, CrucibleLevelingMode? levelingMode = null)
        {
            int size = (id ?? Plugin.CurrentTerritoryContent?.TerritoryType) switch
            {
                1339u => 10,
                1340u => 12,
                1341u => 14,
                1342u => 12,
                1343u => 15,
                _ => 10
            };


            ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = ConfigurationMain.Instance.GetCurrentConfig.Meta.Crucible;

            if (crucible.TeamMode != CrucibleTeamMode.Leveling)
                return size;

            return (levelingMode ?? crucible.LevelingMode) switch
            {
                CrucibleLevelingMode.Minus_3 => size - 3,
                CrucibleLevelingMode.Only_3 => 3,
                CrucibleLevelingMode.Full => size,
                CrucibleLevelingMode.Carry_Minus_2 => size - 2,
                _ => throw new ArgumentOutOfRangeException(nameof(levelingMode))
            };
        }


        public const int FightSize = 3;

        private static readonly string[]  DetailWindows = ["XBMMonsterBookDetail", "XBMPetActionDetail"];
        private static readonly TimeSpan CacheInterval = TimeSpan.FromMilliseconds(750);

        private static SortedDictionary<uint, string> SheetNames
        {
            get
            {
                if (field != null)
                    return field;

                SortedDictionary<uint, string> names = [];
                try
                {
                    ExcelSheet<Pet> pets = Svc.Data.GetExcelSheet<Pet>();
                    foreach (RawRow familiar in Svc.Data.GetExcelSheet<RawRow>(name: "XBMPet"))
                        if (familiar.RowId > 0 && pets.TryGetRow((uint)familiar.ReadInt32Column(0), out Pet pet) && pet.Name.ExtractText() is { Length: > 0 } name)
                            names[familiar.RowId] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
                }
                catch (Exception ex)
                {
                    Svc.Log.Error(ex, "[Crucible] Couldn't read the XBMPet sheet; familiar names are unknown");
                    names.Clear();
                }

                return field = names;
            }
        }

        private static DateTime nextCacheRead;

        private static CrucibleCharacterData? Mine(bool create)
        {
            if (!Player.Available)
                return null;

            Dictionary<ulong, CrucibleCharacterData> all = ConfigurationMain.Instance.crucibleByCID;
            if (!all.TryGetValue(Player.CID, out CrucibleCharacterData? mine) && create)
                all[Player.CID] = mine = new CrucibleCharacterData();
            return mine;
        }

        public static void ClearSaved()
        {
            if (!Player.Available || !ConfigurationMain.Instance.crucibleByCID.Remove(Player.CID))
                return;

            Svc.Log.Info("[Crucible] Cleared the saved familiar ranks for this character");
            ConfigurationMain.Save();
        }

        public static IReadOnlyDictionary<uint, CrucibleFamiliar> Familiars =>
            Mine(false)?.Familiars ?? new Dictionary<uint, CrucibleFamiliar>();

        public static IReadOnlyList<uint> CurrentTeam =>
            Mine(false)?.Team ?? [];

        public static string NameOf(uint number) =>
            Familiars.TryGetValue(number, out CrucibleFamiliar? seen) && seen.Name.Length > 0 ? seen.Name :
            SheetNames.TryGetValue(number, out string? name) ? name : $"No. {number}";

        public static uint NumberFor(string name)
        {
            if (name.Length == 0)
                return 0;

            CrucibleFamiliar? seen = Familiars.Values.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (seen is { Number: > 0 })
                return seen.Number;

            return SheetNames.FirstOrDefault(x => string.Equals(x.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
        }

        public static IEnumerable<uint> Owned()
        {
            XBMManager* manager = XBMManager.Instance();
            bool        ready   = manager != null && manager->State == XBMManager.DataState.Received;
            IReadOnlyDictionary<uint, CrucibleFamiliar> cached = Familiars;

            return SheetNames.Keys.Where(x => ready ? manager->IsPetUnlocked(x) : cached.ContainsKey(x));
        }

        public static bool OwnershipKnown
        {
            get
            {
                XBMManager* manager = XBMManager.Instance();
                return manager != null && manager->State == XBMManager.DataState.Received;
            }
        }

        public static List<uint> For(CrucibleTeamMode mode, bool sizeCap = true) => mode switch
        {
            CrucibleTeamMode.Leveling    => Leveling(sizeCap),
            CrucibleTeamMode.Recommended => Recommended(sizeCap),
            _                            => Custom()
        };

        public static List<uint> Custom()
        {
            HashSet<uint> owned = Owned().ToHashSet();
            return AutoDuty.Configuration.Meta.Crucible.CustomTeam.Distinct().Where(owned.Contains).Take(TeamSize()).ToList();
        }

        public static bool SetCustomPick(uint number, bool pick)
        {
            List<uint> team = Custom();
            if (pick)
            {
                if (team.Contains(number) || team.Count >= TeamSize() || !Owned().Contains(number))
                    return false;
                team.Add(number);
            }
            else if (!team.Remove(number))
            {
                return false;
            }

            SaveCustom(team);
            return true;
        }

        private static void SaveCustom(List<uint> team)
        {
            if (!OwnershipKnown)
                team = team.Concat(AutoDuty.Configuration.Meta.Crucible.CustomTeam.Where(x => !team.Contains(x))).Distinct().Take(TeamSize()).ToList();

            AutoDuty.Configuration.Meta.Crucible.CustomTeam = team;
            ConfigurationProfileV2.Save();
        }

        public static bool DependsOnCache(CrucibleTeamMode mode) =>
            mode is CrucibleTeamMode.Leveling or CrucibleTeamMode.Recommended;

        public static List<uint> Recommended(bool sizeCap)
        {
            IReadOnlyDictionary<uint, CrucibleFamiliar> cached = Familiars;
            return Owned().OrderByDescending(x => cached.TryGetValue(x, out CrucibleFamiliar? f) ? f.Rank : -1)
                          .ThenByDescending(x => cached.TryGetValue(x, out CrucibleFamiliar? f) ? f.Score() : -1)
                          .ThenBy(x => x)
                          .Take(sizeCap ? TeamSize() : int.MaxValue)
                          .ToList();
        }

        public static List<uint> Leveling(bool sizeCap)
        {
            CrucibleLevelingMode levelingMode = ConfigurationMain.Instance.GetCurrentConfig.Meta.Crucible.LevelingMode;

            IOrderedEnumerable<uint> levelingFamiliars = Owned().OrderBy(x => LevelingKey(x)).ThenBy(x => x);

            return levelingMode switch
            {
                CrucibleLevelingMode.Carry_Minus_2 => Recommended(sizeCap).Take(3).Concat(levelingFamiliars.Take(sizeCap ? TeamSize() - 3 : int.MaxValue)).Distinct().ToList(),
                _ => levelingFamiliars.Take(sizeCap ? TeamSize() : int.MaxValue).ToList()
            };
        }

        public static (int Rank, float Exp) LevelingKey(uint number, int liveRank = 0)
        {
            Familiars.TryGetValue(number, out CrucibleFamiliar? familiar);
            return (liveRank > 0 ? liveRank : familiar?.Rank ?? 0, CrucibleUi.ExpShare(familiar?.Exp ?? ""));
        }

        public static List<ReaderXBMPetParty.MonsterEntry> FightOrder(List<ReaderXBMPetParty.MonsterEntry> team)
        {
            /*
            if (AutoDuty.Configuration.Meta.Crucible.TeamMode == CrucibleTeamMode.Leveling)
                alive = alive.OrderBy(row => LevelingKey(NumberFor(team[row].Name), team[row].Rank)).ThenBy(row => row);
            */

            return team.Where(me => me.MaxHP == 0 || me.HP > 0).OrderByDescending(me => (me.Number, me.Rank)).ThenBy(row => row).ToList();
        }

        public static void UpdateCache()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextCacheRead)
                return;
            nextCacheRead = now + CacheInterval;

            if (!Player.Available)
                return;

            bool changed = false;
            foreach (string window in DetailWindows)
                if (CrucibleUi.FamiliarDetail(window) is { } seen)
                    changed |= RememberFamiliar(seen);

            if (!Scanning && CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* notebook) && CrucibleUi.BestiarySelected(notebook) is { } selected)
                changed |= RememberFamiliar(selected);

            if (CrucibleUi.Team() is { } rows)
                changed |= RememberTeam(rows);

            if (changed)
                ConfigurationMain.Save();
        }

        public static bool RememberTeam(List<ReaderXBMPetParty.MonsterEntry> rows)
        {
            Svc.Log.Debug("Crucible Team - Remember Team");

            bool       changed = false;
            List<uint> team    = new(rows.Count);
            foreach (ReaderXBMPetParty.MonsterEntry row in rows)
            {
                uint number = row.Number;
                if (number == 0)
                    continue;

                team.Add(number);
                changed |= RememberFamiliarFromTeamRow(number, row);
            }

            CrucibleCharacterData mine = Mine(true)!;
            if (!mine.Team.SequenceEqual(team))
            {
                mine.Team = team;
                changed   = true;
            }

            return changed;
        }

        public static List<uint> MissingRanks()
        {
            IReadOnlyDictionary<uint, CrucibleFamiliar> cached = Familiars;
            IReadOnlyList<uint>                         team   = CurrentTeam;
            return Owned().Where(x => !team.Contains(x) && (!cached.TryGetValue(x, out CrucibleFamiliar? f) || f.Rank <= 0 || f.Exp.Length == 0)).ToList();
        }

        private static DateTime scanningUntil;

        public static bool Scanning
        {
            get => DateTime.UtcNow < scanningUntil;
            set => scanningUntil = value ? DateTime.UtcNow.AddSeconds(2) : DateTime.MinValue;
        }

        public static bool RememberFamiliarUnsaved(CrucibleFamiliar seen) =>
            RememberFamiliar(seen);

        private static bool RememberFamiliar(CrucibleFamiliar seen)
        {
            if (seen.Number == 0)
                seen.Number = NumberFor(seen.Name);
            if (seen.Number == 0)
                return false;

            Dictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;
            familiars.TryGetValue(seen.Number, out CrucibleFamiliar? before);

            if (seen != before)
            {
                if(before != null)
                    if (seen.Rank < before.Rank)
                        return false;

                familiars[seen.Number] = seen;
                return true;
            }

            return false;
        }

        private static bool RememberFamiliarFromTeamRow(uint number, ReaderXBMPetParty.MonsterEntry row)
        {
            if (row.MaxHP == 0)
                return false;

            Dictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;
            familiars.TryGetValue(number, out CrucibleFamiliar? before);

            CrucibleFamiliar familiar = (before ?? new CrucibleFamiliar { Number = number }) with
                                        {
                                            Name               = row.Name.GetText(),
                                            Rank               = (int) row.Rank,
                                            Hp                 = (int)row.MaxHP,
                                            Strength           = row.Strength,
                                            PhysicalResistance = row.PhysResistance,
                                            Constitution       = row.Constitution,
                                            Intelligence       = row.Intelligence,
                                            MagicResistance    = row.MagicResistance
                                        };

            familiars[number] = familiar;
            return familiar != before;
        }
    }

    internal sealed unsafe class CrucibleTeamSetup
    {
        private const int MaxActions = 40;

        private static readonly TimeSpan ScanPatience = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan ScanRetry    = TimeSpan.FromMilliseconds(500);

        private static readonly TimeSpan StepPatience = TimeSpan.FromSeconds(3);

        private static readonly TimeSpan Resend = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(250);

        private enum Step
        {
            Plan,
            AwaitRemoveAllMenu,
            AwaitRemoveAllConfirm,
            AwaitMenu,
            AwaitRemoved,
            AwaitBestiary,
            AddBatch,
            Settle
        }

        private CrucibleTeamMode mode;
        private List<uint>       goal = [];
        private Step             step;
        private DateTime         stepSince;
        private int              actions;
        private int              countBefore;
        private string           signature = "";
        private DateTime         signatureSince;
        private bool             triedRemoveAll;
        private bool             rebuilding;
        private DateTime         requestedAt;

        private readonly List<uint> batch = [];
        private int                 batchTotal;

        private bool        scanDone;
        private List<uint>? scanQueue;
        private int         scanTotal;
        private uint        scanning;
        private DateTime    scanStarted;
        private DateTime    scanFrom;
        private CrucibleFamiliar? scanRead;
        private bool        scanChanged;
        private bool        scanRetried;
        private bool        scanClearing;
        //private int         scanCap = CrucibleTeam.TeamSizeMax;
        private uint        scanRequeued;
        private int         clearTries;

        public string? Error  { get; private set; }
        public string  Status { get; private set; } = "";

        public void Start(CrucibleTeamMode teamMode)
        {
            this.mode           = teamMode;
            this.goal           = CrucibleTeam.For(teamMode);
            this.SetStep(Step.Plan, DateTime.UtcNow);
            this.actions        = 0;
            this.signature      = "";
            this.triedRemoveAll = false;
            this.rebuilding     = true;
            this.requestedAt    = DateTime.MinValue;
            this.batch.Clear();
            this.scanDone       = false;
            this.scanQueue      = null;
            this.scanning       = 0;
            this.scanClearing   = false;
            this.scanRequeued   = 0;
            this.clearTries     = 0;
            CrucibleTeam.Scanning = false;
            this.Error          = null;
            this.Status         = "Setting up the team";
        }

        public bool Update()
        {
            if (this.Error != null)
                return true;

            if (!EzThrottler.Throttle("CrucibleTeamRegistration", 100))
                return false;

            DateTime now = DateTime.UtcNow;

            AtkUnitBase*                          party = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            List<ReaderXBMPetParty.MonsterEntry>? rows  = CrucibleUi.Team();
            if (party == null || rows == null || party->AtkValuesCount <= 0 || !party->AtkValues[0].Bool)
                return false;

            if (!this.scanDone)
            {
                if (this.scanQueue == null && CrucibleTeam.RememberTeam(rows))
                    ConfigurationMain.Save();
                return this.Scan(party, rows.Count, now);
            }

            CrucibleTeam.UpdateCache();

            if (this.step == Step.AddBatch)
                return this.AddBatch(now);

            List<uint> team = new(rows.Count);
            foreach (ReaderXBMPetParty.MonsterEntry row in rows)
            {
                uint number = row.Number;
                if (number == 0)
                    return this.Fail($"Can't tell which familiar \"{row.Name}\" is. Open its details in the bestiary once so AutoDuty learns the name.");
                team.Add(number);
            }

            string read = string.Join(",", team);
            if (read != this.signature)
            {
                this.signature      = read;
                this.signatureSince = now;
                return false;
            }

            bool waitedTooLong = now - this.stepSince > StepPatience;

            Svc.Log.Debug("Crucible Team - Executing step: " + this.step);
            switch (this.step)
            {
                case Step.Plan:
                    return this.Plan(party, team, now);

                case Step.AwaitRemoveAllMenu:
                    if (CrucibleUi.TryReady(CrucibleUi.ContextMenu, out AtkUnitBase* allMenu))
                    {
                        if (CrucibleUi.ContextMenuOptionCount(allMenu) >= 3)
                        {
                            Screens.Menu.ChooseRemoveAll(allMenu);
                            this.SetStep(Step.AwaitRemoveAllConfirm, now);
                        }
                        else
                        {
                            Svc.Log.Warning("[Crucible] No \"Remove All\" option; removing familiars one at a time");
                            Screens.Menu.Close(allMenu);
                            this.SetStep(Step.Plan, now);
                        }
                    }
                    else if (waitedTooLong)
                    {
                        this.SetStep(Step.Plan, now);
                    }

                    return false;

                case Step.AwaitRemoveAllConfirm:
                    if (CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* confirm))
                    {
                        Screens.Prompt.Yes(confirm);
                        this.SetStep(Step.AwaitRemoved, now);
                    }
                    else if (waitedTooLong)
                    {
                        this.SetStep(Step.Plan, now);
                    }

                    return false;

                case Step.AwaitMenu:
                    if (CrucibleUi.TryReady(CrucibleUi.ContextMenu, out AtkUnitBase* menu))
                    {
                        Screens.Menu.ChooseFirst(menu);
                        this.SetStep(Step.AwaitRemoved, now);
                    }
                    else if (waitedTooLong)
                    {
                        this.SetStep(Step.Plan, now);
                    }

                    return false;

                case Step.AwaitRemoved:
                    if (team.Count < this.countBefore || waitedTooLong)
                        this.SetStep(Step.Plan, now);
                    return false;

                case Step.AwaitBestiary:
                    if (CrucibleUi.IsOpen(CrucibleUi.BestiaryWindow) || waitedTooLong)
                        this.SetStep(Step.Plan, now);
                    else if (now - this.requestedAt > Resend)
                        this.RequestBestiary(party, now);
                    return false;

                case Step.Settle:
                    if (this.goal.All(team.Contains) || now - this.signatureSince > SettleTime || waitedTooLong)
                        this.SetStep(Step.Plan, now);
                    return false;
                case Step.AddBatch:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return false;
        }

        private bool Scan(AtkUnitBase* party, int teamCount, DateTime now)
        {
            if (this.scanClearing)
                return this.ClearForScan(party, teamCount, now);

            if (!CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* notebook))
            {
                if (this.scanQueue == null && CrucibleTeam.Owned().Any() && CrucibleTeam.MissingRanks().Count == 0)
                    return this.FinishScan(now);

                if (now - this.requestedAt > Resend)
                    this.RequestBestiary(party, now);
                return false;
            }

            CrucibleTeam.Scanning = true;

            if (this.scanQueue == null)
            {
                IEnumerable<uint> showing = CrucibleUi.BestiaryShowing(notebook);

                this.scanQueue   = CrucibleTeam.MissingRanks().OrderByDescending(showing.Contains).ThenBy(x => x).ToList();
                this.scanTotal   = this.scanQueue.Count;
                this.scanFrom    = now;
                this.scanChanged = false;
                if (this.scanTotal > 0)
                    Svc.Log.Info($"[Crucible] Reading ranks for {this.scanTotal} familiars from the bestiary: {Names(this.scanQueue)}");
            }

            if (this.scanning != 0)
            {
                if (CrucibleUi.BestiarySelected(notebook) is { } selected && selected.Number == this.scanning)
                {
                    if (selected != this.scanRead)
                    {
                        this.scanRead = selected;
                        return false;
                    }

                    this.scanChanged |= CrucibleTeam.RememberFamiliarUnsaved(selected);
                    this.scanQueue.Remove(this.scanning);
                    this.scanning = 0;
                }
                else if (now - this.scanStarted > ScanPatience)
                {
                    if (teamCount > 0 && this.scanRequeued != this.scanning)
                    {
                        Svc.Log.Info($"[Crucible] The board won't take another familiar at {teamCount}; clearing it and re-reading No. {this.scanning}");
                        this.scanRequeued = this.scanning;
                        this.scanning     = 0;
                        this.StartScanClear(party, teamCount, now);
                        return false;
                    }

                    Svc.Log.Warning($"[Crucible] No. {this.scanning} never showed in the bestiary's detail pane; skipping it");
                    this.scanQueue.Remove(this.scanning);
                    this.scanning = 0;
                }
                else if (!this.scanRetried && now - this.scanStarted > ScanRetry)
                {
                    this.scanRetried = true;
                    Screens.Notebook.PickEntry(notebook, SlotOf(notebook, this.scanning));
                }

                return false;
            }

            if (this.scanQueue.Count == 0)
                return this.FinishScan(now);

            if (teamCount >= Screens.PetParty.GetTeamSize(party))
            {
                Svc.Log.Info($"[Crucible] Board is holding {teamCount}; clearing it before reading more ranks");
                this.StartScanClear(party, teamCount, now);
                return false;
            }

            uint next = this.scanQueue[0];
            if (!this.ShowPageOf(notebook, next, now))
                return false;

            this.Status = $"Reading familiar ranks ({this.scanTotal - this.scanQueue.Count + 1}/{this.scanTotal})";
            if (!Screens.Notebook.PickEntry(notebook, SlotOf(notebook, next)))
            {
                Svc.Log.Warning($"[Crucible] Couldn't click No. {next} in the bestiary; skipping it");
                this.scanQueue.Remove(next);
                return false;
            }

            this.scanning    = next;
            this.scanStarted = now;
            this.scanRead    = null;
            this.scanRetried = false;
            return false;
        }

        private uint SlotOf(AtkUnitBase* notebook, uint number)
        {
            ReaderXBMMonsterNotebook x = new(notebook);

            for (int index = 0; index < x.CurrentPageEntries.Count; index++)
            {
                ReaderXBMMonsterNotebook.MonsterEntry entry = x.CurrentPageEntries[index];
                if (entry.Number == number)
                    return (uint) index;
            }

            return 0;
        }

        private void StartScanClear(AtkUnitBase* party, int teamCount, DateTime now)
        {
            this.scanClearing = true;
            this.countBefore  = teamCount;
            this.Status       = "Clearing the board to read more ranks";
            Screens.PetParty.OpenRowMenu(party, 0);
            this.SetStep(Step.AwaitRemoveAllMenu, now);
        }

        private bool ClearForScan(AtkUnitBase* party, int teamCount, DateTime now)
        {
            if (teamCount == 0)
            {
                this.scanClearing = false;
                this.clearTries   = 0;
                this.requestedAt  = DateTime.MinValue;
                return false;
            }

            bool waitedTooLong = now - this.stepSince > StepPatience;

            switch (this.step)
            {
                case Step.AwaitRemoveAllMenu:
                    if (CrucibleUi.TryReady(CrucibleUi.ContextMenu, out AtkUnitBase* menu))
                    {
                        this.clearTries = 0;
                        if (CrucibleUi.ContextMenuOptionCount(menu) >= 3)
                        {
                            Screens.Menu.ChooseRemoveAll(menu);
                            this.SetStep(Step.AwaitRemoveAllConfirm, now);
                        }
                        else
                        {
                            Screens.Menu.ChooseFirst(menu);
                            this.SetStep(Step.AwaitRemoved, now);
                        }
                    }
                    else if (waitedTooLong)
                    {
                        return this.StalledClear(party, teamCount, now);
                    }

                    return false;

                case Step.AwaitRemoveAllConfirm:
                    if (CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* confirm))
                    {
                        this.clearTries = 0;
                        Screens.Prompt.Yes(confirm);
                        this.SetStep(Step.AwaitRemoved, now);
                    }
                    else if (waitedTooLong)
                    {
                        return this.StalledClear(party, teamCount, now);
                    }

                    return false;

                default:
                    if (teamCount < this.countBefore)
                    {
                        this.clearTries = 0;
                        this.StartScanClear(party, teamCount, now);
                    }
                    else if (waitedTooLong)
                    {
                        return this.StalledClear(party, teamCount, now);
                    }

                    return false;
            }
        }

        private bool StalledClear(AtkUnitBase* party, int teamCount, DateTime now)
        {
            if (++this.clearTries <= 3)
            {
                this.StartScanClear(party, teamCount, now);
                return false;
            }

            Svc.Log.Warning("[Crucible] Couldn't clear the board to keep reading ranks; going with the ranks already known");
            this.scanQueue?.Clear();
            this.scanClearing = false;
            this.clearTries   = 0;
            return false;
        }

        private bool FinishScan(DateTime now)
        {
            if (this.scanQueue is { } && this.scanTotal > 0)
                Svc.Log.Info($"[Crucible] Read {this.scanTotal} familiars from the bestiary in {(now - this.scanFrom).TotalSeconds:0.0}s");
            if (this.scanChanged)
                ConfigurationMain.Save();
            CrucibleTeam.Scanning = false;

            List<uint> refreshed = CrucibleTeam.For(this.mode);
            if (!refreshed.SequenceEqual(this.goal))
                Svc.Log.Info($"[Crucible] {this.mode} team re-read with every rank known: {Names(this.goal)} -> {Names(refreshed)}");

            this.scanDone       = true;
            this.goal           = refreshed;
            this.triedRemoveAll = false;
            this.rebuilding     = true;
            this.signature      = "";
            this.Status         = "Setting up the team";
            this.SetStep(Step.Plan, now);
            return false;
        }

        private bool Plan(AtkUnitBase* party, List<uint> team, DateTime now)
        {
            if (this.goal.Count == 0)
                return this.Fail("No familiars to take: pick at least one for a Custom team.");

            if (++this.actions > MaxActions)
                return this.Fail("Team setup kept going without settling; stopped.");

            if (this.rebuilding && team.Count > 0)
            {
                if (!this.triedRemoveAll)
                {
                    this.triedRemoveAll = true;
                    this.countBefore    = team.Count;
                    this.Status         = "Removing every familiar";
                    Screens.PetParty.OpenRowMenu(party, 0);
                    this.SetStep(Step.AwaitRemoveAllMenu, now);
                    return false;
                }

                this.countBefore = team.Count;
                this.Status      = $"Removing {CrucibleTeam.NameOf(team[0])}";
                Screens.PetParty.OpenRowMenu(party, 0);
                this.SetStep(Step.AwaitMenu, now);
                return false;
            }

            this.rebuilding = false;

            int extra = team.FindIndex(x => !this.goal.Contains(x));
            if (extra >= 0)
            {
                this.countBefore = team.Count;
                this.Status      = $"Removing {CrucibleTeam.NameOf(team[extra])}";
                Screens.PetParty.OpenRowMenu(party, extra);
                this.SetStep(Step.AwaitMenu, now);
                return false;
            }

            List<uint> missing = this.goal.Where(x => !team.Contains(x)).ToList();
            if (missing.Count == 0)
            {
                this.Status = "Team ready";
                Svc.Log.Info($"[Crucible] Team ready: {Names(team)}");
                return true;
            }

            if (!CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* bestiary))
            {
                this.RequestBestiary(party, now);
                this.SetStep(Step.AwaitBestiary, now);
                return false;
            }


            this.batch.Clear();

            IEnumerable<uint> showing = CrucibleUi.BestiaryShowing(bestiary);

            this.batch.AddRange(missing.OrderBy(x => showing.Contains(x) ? 0 : 1).ThenBy(x => x));
            this.batchTotal = this.batch.Count;
            this.SetStep(Step.AddBatch, now);
            return false;
        }

        private bool AddBatch(DateTime now)
        {
            if (this.batch.Count == 0)
            {
                this.signature = "";
                this.SetStep(Step.Settle, now);
                return false;
            }

            if (!CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* bestiary))
            {
                this.SetStep(Step.Plan, now);
                return false;
            }

            uint next = this.batch[0];
            if (!this.ShowPageOf(bestiary, next, now))
            {
                if (now - this.stepSince > StepPatience)
                    this.SetStep(Step.Plan, now);
                return false;
            }

            this.Status = $"Adding {CrucibleTeam.NameOf(next)} ({this.batchTotal - this.batch.Count + 1}/{this.batchTotal})";
            if (!Screens.Notebook.PickEntry(bestiary, SlotOf(bestiary, next)))
                return this.Fail($"Couldn't pick No. {next} in the bestiary.");

            this.batch.RemoveAt(0);
            this.stepSince = now;
            return false;
        }

        private bool ShowPageOf(AtkUnitBase* notebook, uint number, DateTime now)
        {
            if (CrucibleUi.BestiaryShows(notebook, number))
                return true;

            if (now - this.requestedAt > Resend)
            {
                Svc.Log.Debug($"Crucible Team - Switch to Page for {number}");
                ReaderXBMMonsterNotebook monsterNotebook = new(notebook);

                Screens.Notebook.ShowPage(notebook, (monsterNotebook.CurrentPage + 1) % monsterNotebook.PageCount);
                this.requestedAt = now;
            }

            return false;
        }

        private void RequestBestiary(AtkUnitBase* party, DateTime now)
        {
            Screens.PetParty.OpenBestiary(party);
            this.requestedAt = now;
        }

        private void SetStep(Step next, DateTime now)
        {
            this.step        = next;
            this.stepSince   = now;
            this.requestedAt = DateTime.MinValue;
        }

        private bool Fail(string message)
        {
            this.Error  = message;
            this.Status = message;
            Svc.Log.Error($"[Crucible] {message}");
            return true;
        }

        private static string Names(IEnumerable<uint> team) =>
            string.Join(", ", team.Select(CrucibleTeam.NameOf));
    }
}
