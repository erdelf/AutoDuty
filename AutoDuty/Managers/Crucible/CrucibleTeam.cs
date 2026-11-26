using AutoDuty.Configurations;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets.Experimental;
using Newtonsoft.Json;
#pragma warning disable PendingExcelSchema

namespace AutoDuty.Managers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ECommons;
using ECommons.Throttlers;
using ECommons.UIHelpers.AtkReaderImplementations;
using Logging;
using Screens = CrucibleUi.Screens;

[JsonObject(MemberSerialization.OptOut)]
public record CrucibleFamiliar
{
    [JsonProperty]
    private readonly uint number;

    public CrucibleFamiliar() { }
    public CrucibleFamiliar(uint number) => this.number = number;

    [JsonIgnore] public CrucibleFamiliarSheetData SheetData => this.number > 0 ? CrucibleTeam.FamiliarSheetDatas[this.number] : CrucibleFamiliarSheetData.invalid;

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

    public int Score() => BeastTierList.ActiveTierList.IndexOf(x => x.ID == this.number);
}

public record CrucibleFamiliarSheetData
{
    public required     uint   Number             { get; init; }
    public required     string Name               { get; init; }
    public required     string Element            { get; set; }
    public required     byte   Classification     { get; init;}
    [JsonIgnore] public string ClassificationName => CrucibleTeam.ClassificationSheetNames[this.Classification];

    public static readonly CrucibleFamiliarSheetData invalid = new()
                                                               {
                                                                   Number         = 0,
                                                                   Name           = null,
                                                                   Element        = null,
                                                                   Classification = 0
                                                               };
}

[JsonObject(MemberSerialization.OptOut)]
public class CrucibleCharacterData
{
    public SortedDictionary<uint, CrucibleFamiliar> Familiars { get; set; } = [];
    public List<uint>                               Team      { get; set; } = [];

    public CrucibleCharacterData()
    {
    }

    public CrucibleCharacterData(bool createFamiliars)
    {
        if(createFamiliars)
            this.FillBasicFamiliars();
    }

    public void FillBasicFamiliars()
    {
        this.Familiars.Clear();
        foreach (uint key in CrucibleTeam.FamiliarSheetDatas.Keys)
            this.Familiars[key] = new CrucibleFamiliar(key);
    }
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

        return crucible.TeamMode switch
        {
            CrucibleTeamMode.Recommended => Math.Min(size, crucible.RecommendedTeamCap),
            CrucibleTeamMode.Leveling => (levelingMode ?? crucible.LevelingMode) switch
            {
                CrucibleLevelingMode.Minus_3 => size - 3,
                CrucibleLevelingMode.Only_3 => 3,
                CrucibleLevelingMode.Full => size,
                CrucibleLevelingMode.Carry_Minus_2 => size - 2,
                _ => throw new ArgumentOutOfRangeException(nameof(levelingMode))
            },
            _ => size
        };
    }


    public const int FightSize = 3;

    private static readonly string[] DetailWindows = ["XBMMonsterBookDetail", "XBMPetActionDetail"];
    private static readonly TimeSpan CacheInterval = TimeSpan.FromMilliseconds(750);

    public static SortedDictionary<uint, CrucibleFamiliar> FamiliarsFallback => field ??= new SortedDictionary<uint, CrucibleFamiliar>(FamiliarSheetDatas.Keys.ToDictionary(key => key, key => new CrucibleFamiliar(key)));

    public static SortedDictionary<uint, CrucibleFamiliarSheetData> FamiliarSheetDatas
    {
        get
        {
            if (field != null)
                return field;

            SortedDictionary<uint, CrucibleFamiliarSheetData> familiars = [];

            foreach (XBMPet familiar in Svc.Data.GetExcelSheet<XBMPet>())
                if (familiar.RowId > 0 && familiar.Pet.ValueNullable?.Name.ExtractText() is { Length: > 0 } name)
                    familiars[familiar.RowId] = new CrucibleFamiliarSheetData
                                                {
                                                    Number         = familiar.RowId,
                                                    Name           = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name),
                                                    Element        = "", //familiar.Element.Value?.Name.ExtractText() ?? string.Empty,
                                                    Classification = familiar.Classification
                                                };

            return field = familiars;
        }
    }


    public static string[] ClassificationSheetNames
    {
        get
        {
            if (field != null)
                return field;

            try
            {
                byte     maxIds = Svc.Data.GetExcelSheet<RawRow>(name: "XBMPet").Max(p => p.ReadUInt8(24));
                string[] names  = new string[maxIds+1];

                names[0] = string.Empty;
                for (uint i = 1; i <= maxIds; i++)
                    names[i] = Svc.Data.GetExcelSheet<Addon>().GetRow(17740 + i).Text.GetText();

                return field = names;
            }
            catch (Exception ex)
            {
                ADLogging.Error(ex, "[Crucible] Couldn't read the XBMPet sheet; familiar names are unknown");
            }

            return [];
        }
    }

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
                ADLogging.Error(ex, "[Crucible] Couldn't read the XBMPet sheet; familiar names are unknown");
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

        Dictionary<ulong, CrucibleCharacterData> all = ConfigurationMain.Instance.crucibleConfigByCID;
        if (!all.TryGetValue(Player.CID, out CrucibleCharacterData? mine))
        {
            if (create)
                all[Player.CID] = mine = new CrucibleCharacterData(true);
        }
        else if(mine!.Familiars.Count < FamiliarSheetDatas.Count)
        {
            mine.FillBasicFamiliars();
        }

        return mine;
    }

    public static void ClearSaved()
    {
        if (!Player.Available || !ConfigurationMain.Instance.crucibleConfigByCID.Remove(Player.CID))
            return;

        ADLogging.Info("[Crucible] Cleared the saved familiar ranks for this character");
        ConfigurationMain.Save();
    }

    public static IReadOnlyDictionary<uint, CrucibleFamiliar> Familiars =>
        Mine(false)?.Familiars ?? FamiliarsFallback;

    public static IReadOnlyList<uint> CurrentTeam =>
        Mine(false)?.Team ?? [];

    public static string NameOf(uint number) => Familiars.TryGetValue(number, out CrucibleFamiliar? seen) && seen.SheetData.Name.Length > 0 ? 
                                                    seen.SheetData.Name : 
                                                    SheetNames.TryGetValue(number, out string? name) ? name : $"No. {number}";

    public static uint NumberFor(string name)
    {
        if (name.Length == 0)
            return 0;

        CrucibleFamiliar? seen = Familiars.Values.FirstOrDefault(x => string.Equals(x.SheetData.Name, name, StringComparison.OrdinalIgnoreCase));
            
        return seen is { SheetData.Number: > 0 } ? seen.SheetData.Number : SheetNames.FirstOrDefault(x => string.Equals(x.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
    }

    public static IEnumerable<uint> Owned()
    {
        XBMManager*                                 manager = XBMManager.Instance();
        bool                                        ready   = manager != null && manager->State == XBMManager.DataState.Received;
        IReadOnlyDictionary<uint, CrucibleFamiliar> cached  = Familiars;
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
        ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = ConfigurationMain.Instance.GetCurrentConfig.Meta.Crucible;

        HashSet<uint> owned = Owned().ToHashSet();
        crucible.CustomTeam = crucible.CustomTeam.Where(x => crucible.ClassificationLock == 0 || FamiliarSheetDatas[x].Classification == crucible.ClassificationLock).ToList();

        return crucible.CustomTeam.Distinct().Where(owned.Contains).Take(TeamSize()).ToList();
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

    public static List<uint> Recommended(bool sizeCap)
    {
        ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = ConfigurationMain.Instance.GetCurrentConfig.Meta.Crucible;
        IReadOnlyDictionary<uint, CrucibleFamiliar>      cached   = Familiars;
        return Owned().Where(x => crucible.ClassificationLock == 0 || FamiliarSheetDatas[x].Classification == crucible.ClassificationLock)
                      .Where(x => !(BeastTierList.ActiveTierList.FirstOrDefault(en => en.ID == x)?.PickBlock ?? false))
                      .OrderByDescending(x => cached.TryGetValue(x, out CrucibleFamiliar? f) ? f.Rank : -1)
                      .ThenBy(x => cached.TryGetValue(x,  out CrucibleFamiliar? f) ? f.Score() : int.MaxValue)
                      .ThenBy(x => x)
                      .Take(sizeCap ? TeamSize() : int.MaxValue)
                      .ToList();
    }

    public static List<uint> Leveling(bool sizeCap)
    {
        ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible     = ConfigurationMain.Instance.GetCurrentConfig.Meta.Crucible;
        CrucibleLevelingMode                             levelingMode = crucible.LevelingMode;

        IOrderedEnumerable<uint> levelingFamiliars = Owned().Where(x => crucible.ClassificationLock == 0 || FamiliarSheetDatas[x].Classification == crucible.ClassificationLock).OrderBy(x => LevelingKey(x)).ThenBy(x => x);

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

        List<ReaderXBMPetParty.MonsterEntry> monsterEntries = team.Where(me => me.MaxHP == 0 || me.HP > 0).OrderByDescending(me => Familiars[me.Number].Rank).ThenByDescending(row => Familiars[row.Number].Score()).ToList();

        List<ReaderXBMPetParty.MonsterEntry> unblocked = monsterEntries.Where(me => !(BeastTierList.ActiveTierList.FirstOrDefault(x => x.ID == me.Number)?.BattleBlock ?? false)).ToList();

        return unblocked.Count >= 3 ? monsterEntries.Concat(unblocked).Distinct().ToList() : monsterEntries;
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
        ADLogging.Debug("Crucible Team - Remember Team");

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

    public static void UpdateFamiliar(uint number, uint rank, uint xp)
    {
        SortedDictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;
        if (familiars.TryGetValue(number, out CrucibleFamiliar? familiar))
        {
            familiar.Rank = (int)rank;
            familiar.Exp  = xp.ToString();
        }
    }

    public static bool RememberFamiliarUnsaved(CrucibleFamiliar seen) =>
        RememberFamiliar(seen);

    private static bool RememberFamiliar(CrucibleFamiliar seen)
    {
        if (seen.SheetData.Number == 0)
            return false;

        SortedDictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;
        familiars.TryGetValue(seen.SheetData.Number, out CrucibleFamiliar? before);

        if (seen != before)
        {
            if(before != null)
                if (seen.Rank < before.Rank)
                    return false;

            familiars[seen.SheetData.Number] = seen;
            return true;
        }

        return false;
    }

    private static bool RememberFamiliarFromTeamRow(uint number, ReaderXBMPetParty.MonsterEntry row)
    {
        if (row.MaxHP == 0)
            return false;

        SortedDictionary<uint, CrucibleFamiliar> familiars = Mine(true)!.Familiars;

        CrucibleFamiliar familiar = familiars[number];

        if (row.Rank <= familiar.Rank)
            return false;

        familiar.Rank               = row.Rank;
        familiar.Hp                 = (int)row.MaxHP;
        familiar.Strength           = row.Strength;
        familiar.PhysicalResistance = row.PhysResistance;
        familiar.Constitution       = row.Constitution;
        familiar.Intelligence       = row.Intelligence;
        familiar.MagicResistance    = row.MagicResistance;

        familiars[number] = familiar;
        return true;
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
    private          int        batchTotal;

    private bool              scanDone;
    private List<uint>?       scanQueue;
    private int               scanTotal;
    private uint              scanning;
    private DateTime          scanStarted;
    private DateTime          scanFrom;
    private CrucibleFamiliar? scanRead;
    private bool              scanChanged;
    private bool              scanRetried;
    private bool              scanClearing;
    //private int         scanCap = CrucibleTeam.TeamSizeMax;
    private uint scanRequeued;
    private int  clearTries;

    public string? Error  { get; private set; }
    public string  Status { get; private set; } = "";

    public void Start(CrucibleTeamMode teamMode)
    {
        this.mode = teamMode;
        this.goal = CrucibleTeam.For(teamMode);
        this.SetStep(Step.Plan, DateTime.UtcNow);
        this.actions        = 0;
        this.signature      = "";
        this.triedRemoveAll = false;
        this.rebuilding     = true;
        this.requestedAt    = DateTime.MinValue;
        this.batch.Clear();
        this.scanDone         = false;
        this.scanQueue        = null;
        this.scanning         = 0;
        this.scanClearing     = false;
        this.scanRequeued     = 0;
        this.clearTries       = 0;
        CrucibleTeam.Scanning = false;
        this.Error            = null;
        this.Status           = "Setting up the team";
    }

    public bool Update()
    {
        if (this.Error != null)
            return true;

        if (!EzThrottler.Throttle("CrucibleTeamRegistration", 100))
            return false;

        DateTime now = DateTime.UtcNow;

        AtkUnitBase* party = CrucibleUi.Ready(CrucibleUi.TeamWindow);

        List<ReaderXBMPetParty.MonsterEntry>? rows  = CrucibleUi.Team();
        if (party == null || rows == null || party->AtkValuesCount <= 0 || !party->AtkValues[0].Bool)
            return false;

        if (!this.scanDone)
        {
            if (this.scanQueue == null && CrucibleTeam.RememberTeam(rows))
                ConfigurationMain.Save();

            this.Scan(party, now);
            return false;
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

        ADLogging.Debug("Crucible Team - Executing step: " + this.step);
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
                        ADLogging.Warning("[Crucible] No \"Remove All\" option; removing familiars one at a time");
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

    public bool Scan(AtkUnitBase* notebook, DateTime now)
    {
        if (!ResetFilter(notebook))
            return false;

        CrucibleTeam.Scanning = true;

        if (this.scanQueue == null)
        {
            this.scanQueue   = ScanQueue(notebook);
            this.scanTotal   = this.scanQueue.Count;
            this.scanFrom    = now;
            this.scanChanged = false;
            if (this.scanTotal > 0)
                ADLogging.Info($"[Crucible] Reading ranks for {this.scanTotal} familiars from the bestiary: {Names(this.scanQueue)}");
        }

        if (this.scanning != 0)
        {
            ADLogging.Debug("Scanning");
            if (CrucibleUi.BestiarySelected(notebook) is { } selected && selected.SheetData.Number == this.scanning)
            {
                this.scanChanged |= CrucibleTeam.RememberFamiliarUnsaved(selected);
                this.scanQueue.Remove(this.scanning);
                this.scanning = 0;
            }
            else if (now - this.scanStarted > ScanPatience)
            {
                ADLogging.Warning($"[Crucible] No. {this.scanning} never showed in the bestiary's detail pane; skipping it");
                this.scanQueue.Remove(this.scanning);
                this.scanning = 0;
            }
            else if (!this.scanRetried && now - this.scanStarted > ScanRetry)
            {
                ADLogging.Debug("[Crucible] Scan retry");
                this.scanRetried = true;
                Screens.Notebook.PickEntry(notebook, SlotOf(notebook, this.scanning));
            }

            return false;
        }

        if (this.scanQueue.Count == 0)
        {
            this.FinishScan(now);
            return true;
        }

        uint next = this.scanQueue[0];
        if (!this.ShowPageOf(notebook, next, now))
            return false;

        this.Status = $"Reading familiar ranks ({this.scanTotal - this.scanQueue.Count + 1}/{this.scanTotal})";
        ADLogging.Debug(this.Status);
        if (!Screens.Notebook.PickEntry(notebook, SlotOf(notebook, next)))
        {
            ADLogging.Warning($"[Crucible] Couldn't click No. {next} in the bestiary; skipping it");
            this.scanQueue.Remove(next);
            return false;
        }

        this.scanning    = next;
        this.scanStarted = now;
        this.scanRetried = false;
        return false;
    }

    private static List<uint> ScanQueue(AtkUnitBase* notebook)
    {
        IEnumerable<uint> showing = CrucibleUi.BestiaryShowing(notebook);
        return CrucibleTeam.MissingRanks().OrderByDescending(showing.Contains).ThenBy(x => x).ToList();
    }

    public static bool ScanRequired() => 
        CrucibleTeam.MissingRanks().Count != 0;

    private static uint SlotOf(AtkUnitBase* notebook, uint number)
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

        ADLogging.Warning("[Crucible] Couldn't clear the board to keep reading ranks; going with the ranks already known");
        this.scanQueue?.Clear();
        this.scanClearing = false;
        this.clearTries   = 0;
        return false;
    }

    private void FinishScan(DateTime now)
    {
        if (this.scanQueue is not null && this.scanTotal > 0)
            ADLogging.Info($"[Crucible] Read {this.scanTotal} familiars from the bestiary in {(now - this.scanFrom).TotalSeconds:0.0}s");
        if (this.scanChanged)
            ConfigurationMain.Save();
        CrucibleTeam.Scanning = false;

        List<uint> refreshed = CrucibleTeam.For(this.mode);
        if (!refreshed.SequenceEqual(this.goal))
            ADLogging.Info($"[Crucible] {this.mode} team re-read with every rank known: {Names(this.goal)} -> {Names(refreshed)}");

        this.scanDone       = true;
        this.goal           = refreshed;
        this.triedRemoveAll = false;
        this.rebuilding     = true;
        this.signature      = "";
        this.Status         = "Setting up the team";
        this.SetStep(Step.Plan, now);
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
            ADLogging.Info($"[Crucible] Team ready: {Names(team)}");
            return true;
        }

        if (!CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out AtkUnitBase* bestiary))
        {
            this.RequestBestiary(party, now);
            this.SetStep(Step.AwaitBestiary, now);
            return false;
        }

        if (!ResetFilter(bestiary))
            return false;

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
            ADLogging.Debug($"Crucible Team - Switch to Page for {number}");
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

    private static bool ResetFilter(AtkUnitBase* notebook)
    {
        ReaderXBMMonsterNotebook readerNotebook = new(notebook);
        if (!readerNotebook.Filtered)
            return true;
            
        ADLogging.Debug("[Crucible] Bestiary Filter active");

        AtkUnitBase* bestiaryFilter = CrucibleUi.Ready(CrucibleUi.BestiaryWindowFilter);
        if (bestiaryFilter == null)
            Screens.Notebook.OpenFilter(notebook);
        else
            Screens.NotebookFilter.ResetFilters(bestiaryFilter);

        return false;
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
        ADLogging.Error($"[Crucible] {message}");
        return true;
    }

    private static string Names(IEnumerable<uint> team) =>
        string.Join(", ", team.Select(CrucibleTeam.NameOf));
}

[JsonObject(MemberSerialization.OptOut)]
public class BeastTierList : List<BeastTierList.Entry>, ConfigurationProfileV2.MetaConfig.ICrucibleList
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Entry
    {
        public uint ID          { get; set; }
        public bool PickBlock   { get; set; }
        public bool BattleBlock { get; set; }

        public static implicit operator Entry(long id) => (uint)id;
        public static implicit operator Entry(uint id) => new() { ID = id };
    }

    public static readonly uint[] beastTierListDefault =
        new uint[]
        {
            13, // Geshunpest
            20, // Flying Trap
            27, // Uragnite

            19, // Bat
            7,  // Coblyn
            44, // Damselfly
            2,  // Squirrel
            17, // Slime

            15, // Crab
            39, // Morbol
            10, // Wespe
            47, // Ice Golem
            37, // Antling
            6,  // Dodo
            35, // Drake
            8,  // Diremite
            11, // Vulture

            18, // Dullahan
            50, // Behemoth
            36, // Treant
            46, // Zu
            4,  // Pugil
            16, // Mantis
            23, // Golem
            41, // Salamander
            34, // Raptor
            21, // Ziz
            29, // Spriggan

            38, // Chimera
            3,  // Lamb
            28, // Worm
            48, // Karlabos
            33, // Coeurl
            31, // Gigantoad
            43, // Hydra
            5,  // Opo-opo
            14, // Puk
            12, // Mandragora
            45, // Rotting Goobbue

            26, // Buffalo
            49, // Rafflesia
            1,  // Cu Sith
            40, // Ghost
            9,  // Megalocrab
            42, // Cobra
            22, // Sabotender
            25, // Adamantoise
            32, // Colibri
            24, // Apkallu
            //30, // Goobbue
        }.Concat(CrucibleTeam.FamiliarSheetDatas.Select(f => f.Key)).Distinct().ToArray();

    public static BeastTierList ActiveTierList
    {
        get
        {
            ConfigurationProfileV2.MetaConfig.CrucibleConfig crucible = Configuration.Meta.Crucible;
            if (crucible.BeastTierLists.Count == 0)
                crucible.BeastTierLists.Add(Default());

            crucible.BeastTierListIndex = Math.Clamp(crucible.BeastTierListIndex, 0, crucible.BeastTierLists.Count - 1);
            return crucible.BeastTierLists[crucible.BeastTierListIndex];
        }
    }

    public static BeastTierList Default()
    {
        BeastTierList list = new() { Name = "Default" };
        list.AddRange(beastTierListDefault.Select(id => new Entry { ID = id }));
        return list;
    }

    [JsonIgnore]
    public bool IsCompleted => beastTierListDefault.All(id => this.Any(e => e.ID == id));

    [JsonIgnore]
    public bool OrderChanged => beastTierListDefault.Length != this.Count || beastTierListDefault.Where((id, index) => id != this[index].ID).Any();

    public void Complete()
    {
        foreach (uint id in beastTierListDefault)
            if(this.All(e => e.ID != id))
                this.Add(new Entry { ID = id });
    }

    public required string Name { get; set; }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<Entry> Entries
    {
        get => this.ToList();
        set
        {
            this.Clear();
            this.AddRange(value);
        }
    }
}