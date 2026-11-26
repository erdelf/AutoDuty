using AutoDuty.Configurations;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AutoDuty.Logging;

namespace AutoDuty.Managers
{
    using Dalamud.Game.ClientState.Objects.Enums;
    using Dalamud.Game.ClientState.Objects.Types;
    using ECommons.GameFunctions;
    using ECommons.Throttlers;
    using ECommons.UIHelpers.AddonMasterImplementations;
    using ECommons.UIHelpers.AtkReaderImplementations;
    using External;
    using Helpers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using Screens = CrucibleUi.Screens;

    internal sealed unsafe class CrucibleMenus
    {
        private const int   FightPicks = CrucibleTeam.FightSize;
        private const int   RestPicks  = 2;
        private const float RestBelow  = 0.6f;
        private const int   ItemCap    = 10;
        private const int   GearCap    = 10;
        private const float FightLow   = 0.4f;
        private const float BoardLow   = 0.6f;

        private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan Retry         = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PickInterval  = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan CommenceRetry = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShopStep      = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan FeedRetry     = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan FeedTimeout   = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ItemGap       = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ItemMenuWait  = TimeSpan.FromMilliseconds(1500);

        private DateTime confirmFrom  = DateTime.MinValue;
        private int      yesNoCounter = 0;

        private DateTime fightNext;

        private List<int>? restPicks;
        private int        restStep;

        private readonly HashSet<uint>                         shopTried = [];
        private          DateTime                              shopNext;
        private          DateTime                              feedFrom = DateTime.MinValue;
        private          List<ReaderXBMPetParty.MonsterEntry>? feedOrder;
        private          int                                   feedTry;
        private          bool                                  fedThisVisit;
        private          bool                                  closedThisVisit;

        private DateTime itemNext;
        private DateTime itemLastUse = DateTime.MinValue;
        private DateTime itemMenuFrom = DateTime.MinValue;

        public string Status { get; private set; } = "";

        private static ConfigurationProfileV2.MetaConfig.CrucibleConfig Config => AutoDuty.Configuration.Meta.Crucible;

        public void Reset()
        {
            this.confirmFrom  = DateTime.MinValue;
            this.restPicks    = null;
            this.feedFrom     = DateTime.MinValue;
            this.itemMenuFrom = DateTime.MinValue;
            this.ResetShopVisit();
            this.Status = "";
        }

        public void Update()
        {
            DateTime now = DateTime.UtcNow;

            if (!EzThrottler.Throttle("CrucibleMenus", 250))
                return;

            this.UpdateItems(now);

            if (now - this.confirmFrom <= ConfirmWindow && !this.FeedPending(now))
            {
                if (CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* confirm))
                {
                    if(this.yesNoCounter < 5)
                        new AddonMaster.SelectYesno(confirm).Yes();
                    else
                        new AddonMaster.SelectYesno(confirm).No();

                    this.yesNoCounter++;
                    return;
                }

                this.confirmFrom  = DateTime.MinValue;
                this.yesNoCounter = 0;
            }

            if (this.StartFight(now))
                return;

            this.UpdateShop(now);



            if (!this.FeedPending(now) && !CrucibleUi.IsOpen(CrucibleUi.ShopWindow) && !CrucibleUi.IsOpen(CrucibleUi.BoardLayout) &&
                CrucibleUi.TryReady(CrucibleUi.TeamWindow, out AtkUnitBase* party))
            {
                this.Rest(party, now);
                return;
            }

            this.restPicks = null;

            if (CrucibleUi.TryReady(CrucibleUi.TreasureWindow, out AtkUnitBase* treasure))
            {
                this.PickTreasure(treasure, now);
                return;
            }

            if (CrucibleUi.TryReady(CrucibleUi.LootWindow, out AtkUnitBase* loot))
            {
                if (!Config.Loot)
                {
                    Screens.Booty.Close(loot);
                    this.confirmFrom = now;
                    return;
                }

                ReaderXBMContentsBooty booty = new(loot);

                if (booty is { LootCoinsTaken: false, LootCoins: > 0 })
                {
                    Screens.Booty.TakeCoins(loot);
                    this.confirmFrom = now;
                    return;
                }


                IEnumerable<ReaderXBMContentsBooty.LootChoice> choices = booty.LootChoices.Where(lc => !lc.Taken);

                IEnumerable<ReaderXBMContentsItemShop.GearEntry> gearEntries = booty.OwnedEntriesOwned.ToList();
                IEnumerable<ReaderXBMContentsItemShop.ItemEntry> itemEntries = booty.ItemEntriesValid.ToList();

                if (gearEntries.Count() < GearCap || itemEntries.Count() < ItemCap)
                    foreach (ReaderXBMContentsBooty.LootChoice gearChoice in choices)
                        if (CrucibleItemData.ShopGear.Contains(gearChoice.Item))
                        {
                            if (gearEntries.All(ge => ge.Id != gearChoice.Item) && gearEntries.Count() < GearCap)
                            {
                                Screens.Booty.Take(loot, gearChoice.lootIndex);
                                this.confirmFrom = now;
                                return;
                            }
                        }
                        else if(itemEntries.Count() < ItemCap)
                        {
                            Screens.Booty.Take(loot, gearChoice.lootIndex);
                            this.confirmFrom = now;
                            return;
                        }

                Screens.Booty.Close(loot);
                this.confirmFrom = now;
                return;
            }

            if (CrucibleUi.TryReady(CrucibleUi.ResultWindow, out AtkUnitBase* result))
            {
                ReaderXBMResult xbmResult = new(result);

                ADLogging.Info($"Crucible Results: Total Score: {xbmResult.TotalScore}, Rank: {xbmResult.Rank}" +
                             "\nBeasts:\n\t" + 
                             string.Join("\n\t", xbmResult.BeastEntries.Select(entry => $"{CrucibleTeam.FamiliarSheetDatas[entry.Number].Name} ({entry.Number}) - Rank: {entry.PrevRank} -> {entry.NewRank}, XP: {entry.PrevXP} -> {entry.NewXP}")) +
                             $"\nScores: Performance: {xbmResult.Performance} | MovesMade: {xbmResult.MovesMadeScore} | Enemy: {xbmResult.EnemyScore} | Elite: {xbmResult.EliteScore} | Boss: {xbmResult.BossScore} | HP: {xbmResult.RemainingHP} -> {xbmResult.RemainingHPScore}\n"+
                             "Bonus Boost: " + xbmResult.BonusBoostScore + "\n\t" + 
                             string.Join("\n\t", xbmResult.BonusBoostEntries.Select(entry => $"{entry.Name}: {entry.Score}")) +
                             "\n Loot:\n\t" + 
                             string.Join("\n\t", xbmResult.LootEntries.Select(entry => $"{entry.Name} ({entry.ItemId}) - Count: {entry.Count}"))
                             );

                foreach (ReaderXBMResult.BeastEntry entry in xbmResult.BeastEntries)
                    CrucibleTeam.UpdateFamiliar(entry.Number, entry.NewRank, entry.NewXP);

                if (ConfigurationMain.Instance.GetCurrentConfig.DutyConfig.AutoExitDuty || Plugin.currentLoop < ConfigurationMain.Instance.GetCurrentConfig.Meta.LoopTimes)
                {
                    this.Status = "Finishing the board";
                    Screens.Result.Continue(result);
                } else
                {
                    Plugin.Stage = Stage.Stopped;
                }
            }
        }

        private bool StartFight(DateTime now)
        {
            if (!Config.FightPicks)
                return false;

            AtkUnitBase* layout = CrucibleUi.Ready(CrucibleUi.BoardLayout);
            AtkUnitBase* party  = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            if (layout == null || party == null)
                return false;

            if (CrucibleUi.IsOpen(CrucibleUi.YesNo) || now < this.fightNext)
                return true;

            List<ReaderXBMPetParty.MonsterEntry>? team = CrucibleUi.Team();
            if (team == null || team.Count == 0)
                return true;

            List<ReaderXBMPetParty.MonsterEntry> alive = CrucibleTeam.FightOrder(team).Take(FightPicks).ToList();
            if (alive.Count == 0)
            {
                this.Status = "Every familiar is knocked out";
                return true;
            }

            List<ReaderXBMPetParty.MonsterEntry> picked = team.OrderBy(me => me.SelectionIndex).Where(me => me.SelectionIndex < 3).Take(FightPicks).ToList();

            if (picked.Count > 0)
            {
                for (int i = picked.Count - 1; i >= 0; i--)
                {
                    ReaderXBMPetParty.MonsterEntry pickedEntry = picked[i];
                    if (pickedEntry.Number != alive[i].Number)
                    {
                        Screens.PetParty.Pick(party, pickedEntry.index);
                        ADLogging.Debug($"Crucible: Unpicking familiar ({pickedEntry.Name})");
                        this.fightNext = now + PickInterval;
                        return true;
                    }
                }
            }



            for (int i = picked.Count; i < Math.Min(FightPicks, alive.Count); i++)
            {
                ReaderXBMPetParty.MonsterEntry entry = alive[i];

                Screens.PetParty.Pick(party, entry.index);
                this.fightNext = now + PickInterval;
                ADLogging.Debug($"Crucible: Picking familiar ({entry.Name})");
                return true;
            }

            Screens.StageDetail.Confirm(layout);
            this.fightNext   = now + CommenceRetry;
            this.confirmFrom = now;
            ADLogging.Debug("Crucible: Commencing the battle");
            this.Status = "Commencing the battle";
            return true;
        }

        private void PickTreasure(AtkUnitBase* treasure, DateTime now)
        {
            ReaderXBMContentsTreasure xbmTreasure = new(treasure);

            if (!Config.Treasure)
            {
                Screens.Treasure.Close(treasure);
                this.confirmFrom = now;
                return;
            }
            
            uint[] items = xbmTreasure.ItemEntriesValid.Select(ie => ie.Id).ToArray();
            HashSet<uint> gear  = xbmTreasure.OwnedEntriesOwned.Select(ie => ie.Id).ToHashSet();

            List<ReaderXBMContentsTreasure.TreasureChoice> treasureChoices = xbmTreasure.TreasureChoices;

            if (treasureChoices.Count == 0)
                return;

            List<ReaderXBMContentsTreasure.TreasureChoice> choices = [];

            foreach (ReaderXBMContentsTreasure.TreasureChoice choice in treasureChoices)
            {
                if (choice.Bought)
                    continue;

                if(CrucibleItemData.ShopGear.Contains(choice.Item))
                {
                    if (gear.Count < GearCap && !gear.Contains(choice.Item) && !CrucibleItemData.BlockedGear(choice.Item, gear))
                        choices.Add(choice);

                    continue;
                }

                if(items.Length < ItemCap)
                    choices.Add(choice);
            }

            if (choices.Count == 0)
            {
                Screens.Treasure.Close(treasure);
                this.confirmFrom = now;
            }

            ReaderXBMContentsTreasure.TreasureChoice best = choices.OrderBy(x => CrucibleItemData.TreasureRank(x.Item)).ThenBy(x => x.treasureIndex).First();

            ADLogging.Info($"[Crucible] Treasure: taking {Describe(best)} from {string.Join(" / ", choices.Select(Describe))}");

            Screens.Treasure.Take(treasure, (uint)best.treasureIndex);

            this.confirmFrom = now;
            this.Status      = $"Taking {(best.Item != 0 ? CrucibleItemData.NameOf(best.Item) : best.treasureIndex)}";

            return;

            static string Describe(ReaderXBMContentsTreasure.TreasureChoice choice) =>
                $"{CrucibleItemData.NameOf(choice.Item)} ({choice.treasureIndex})";
        }

        private void Rest(AtkUnitBase* party, DateTime now)
        {
            if (!Config.Rest)
            {
                Screens.PetParty.RestReturn(party);
                this.confirmFrom = now;
                return;
            }

            if (this.restPicks == null)
            {
                if (CrucibleUi.Team() is not { Count: > 0 } rows)
                    return;

                // Picking nobody gives a 90% heal
                this.restPicks = rows.Where(x => x is { MaxHP: > 0, HP: > 0 } && (float)x.HP / x.HP < RestBelow)
                                     .OrderBy(x => (float)x.HP / x.MaxHP)
                                     .Take(RestPicks)
                                     .Select(x => x.index)
                                     .ToList();
                this.restStep = 0;

                string names = string.Join(", ", this.restPicks.Select(i => $"{rows[i].Name} {rows[i].HP}/{rows[i].MaxHP}"));
                ADLogging.Info($"[Crucible] Campsite: resting with {(names.Length > 0 ? names : "no familiars (90% self heal)")}");
            }

            if (this.restStep < this.restPicks.Count)
            {
                Screens.PetParty.Pick(party, this.restPicks[this.restStep]);
                this.restStep++;
                EzThrottler.Throttle("CrucibleMenus", 400);
                return;
            }

            if (Screens.PetParty.Rest(party))
            {
                this.confirmFrom = now;
                this.Status      = "Resting at the campsite";
            }
        }

        private bool FeedPending(DateTime now) =>
            now - this.feedFrom <= FeedTimeout;

        private void UpdateShop(DateTime now)
        {
            if (now < this.shopNext)
                return;

            this.shopNext = now + TimeSpan.FromMilliseconds(250);

            if (!Config.Shop)
            {
                this.ResetShopVisit();
                AtkUnitBase* shopWindow = CrucibleUi.Ready(CrucibleUi.ShopWindow);
                if (shopWindow != null)
                {
                    if (now - this.confirmFrom <= ConfirmWindow && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* yesQuit))
                    {
                        new AddonMaster.SelectYesno(yesQuit).Yes();
                        this.confirmFrom = DateTime.MinValue;
                        this.shopNext    = now + ShopStep;
                        return;
                    }

                    if (Screens.ItemShop.Close(shopWindow))
                        this.confirmFrom = now;
                }

                return;
            }

            AtkUnitBase* shop = CrucibleUi.Ready(CrucibleUi.ShopWindow);
            if (shop == null)
            {
                this.ResetShopVisit();
                return;
            }


            if (this.FeedPending(now))
            {
                if (now - this.confirmFrom <= ConfirmWindow && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* feedYes))
                {
                    Screens.Prompt.Yes(feedYes);
                    this.confirmFrom = DateTime.MinValue;
                    this.feedFrom    = DateTime.MinValue;
                    this.shopNext    = now + ShopStep;
                    return;
                }

                this.Feed(now);
                return;
            }
            else
            {
                AtkUnitBase* party = CrucibleUi.Ready(CrucibleUi.TeamWindow);
                if (party != null)
                {
                    Screens.PetParty.Return(party);
                    return;
                }
            }


            if (now - this.confirmFrom <= ConfirmWindow && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* yes))
            {
                Screens.Prompt.Yes(yes);
                this.confirmFrom = DateTime.MinValue;
                this.shopNext    = now + ShopStep;
                return;
            }

            if (CrucibleUi.IsOpen(CrucibleUi.YesNo) || CrucibleUi.IsOpen(CrucibleUi.TeamWindow))
                return;

            int coins = CrucibleUi.ShopCoins(shop);

            List<ReaderXBMContentsItemShop.StockEntry> affordable = CrucibleUi.ShopStock(shop).Where(x => !x.Bought && x.Price <= coins && !this.shopTried.Contains(x.Item)).ToList();

            if (this.ChooseBuy(affordable, CrucibleUi.ShopHeldItems(shop), CrucibleUi.ShopOwnedGear(shop)) is not { } buy)
            {
                if (!this.closedThisVisit)
                {
                    this.closedThisVisit = true;
                    this.Status          = $"Shop done, {coins} coins left";
                    ADLogging.Info($"[Crucible] Shop: done with {coins} coins left");

                    if (Screens.ItemShop.Close(shop))
                        this.confirmFrom = now;
                }

                return;
            }

            this.Status = $"Buying {CrucibleItemData.NameOf(buy.Item)} for {buy.Price}";
            ADLogging.Info($"[Crucible] Shop: buying {CrucibleItemData.NameOf(buy.Item)} for {buy.Price} of {coins} coins");

            this.shopTried.Add(buy.Item);
            Screens.ItemShop.Buy(shop, buy.purchaseIndex);
            this.confirmFrom = now;
            this.shopNext    = now + ShopStep;

            if (CrucibleItemData.ShopFeed.Contains(buy.Item))
            {
                this.fedThisVisit = true;
                this.feedFrom     = now;
                this.feedOrder    = null;
                this.feedTry      = 0;
            }
        }

        private ReaderXBMContentsItemShop.StockEntry? ChooseBuy(List<ReaderXBMContentsItemShop.StockEntry> stock, uint[] held, HashSet<uint> ownedGear)
        {
            bool itemRoom = held.Length    < ItemCap;
            bool gearRoom = ownedGear.Count < GearCap;

            IEnumerable<ReaderXBMContentsItemShop.StockEntry> wanted = stock.Where(x =>
                                                                                   {
                                                                                       CrucibleItemData.CrucibleItemCategory category = CrucibleItemData.GetCategoryOf(x.Item);
                                                                                       return category switch
                                                                                       { 
                                                                                           CrucibleItemData.CrucibleItemCategory.Gear => gearRoom && !ownedGear.Contains(x.Item) && !CrucibleItemData.BlockedGear(x.Item, ownedGear),
                                                                                           CrucibleItemData.CrucibleItemCategory.Feed => !this.fedThisVisit,
                                                                                           _ when CrucibleItemData.CrucibleItemCategory.Item.HasFlag(category) => itemRoom,
                                                                                           _ => false
                                                                                       };
                                                                                   });

            return FirstInStock(wanted, CrucibleItemData.ShopOrderActive, held);
        }

        private static ReaderXBMContentsItemShop.StockEntry? FirstInStock(IEnumerable<ReaderXBMContentsItemShop.StockEntry> stock, List<ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry> priority, uint[] owned)
        {
            Dictionary<uint, ReaderXBMContentsItemShop.StockEntry> byRow = stock.GroupBy(x => x.Item).ToDictionary(g => g.Key, g => g.First());
            foreach (ConfigurationProfileV2.MetaConfig.CrucibleShopList.Entry row in priority)
                if (!owned.Contains(row.ID) && byRow.TryGetValue(row.ID, out ReaderXBMContentsItemShop.StockEntry? entry))
                    return entry;
            return null;
        }

        private void Feed(DateTime now)
        {
            AtkUnitBase* party = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            if (party == null || CrucibleUi.IsOpen(CrucibleUi.YesNo))
                return;

            ReaderXBMPetParty petParty = new(party);

            if (this.feedOrder == null)
            {
                if (CrucibleUi.Team(petParty) is not { Count: > 0 } team)
                    return;

                team = team.Where(x => !x.Disabled && x.FedCurrent < x.FedMax && !x.FedItems.Contains(petParty.FeedItem)).ToList();
                this.feedOrder = CrucibleTeam.FightOrder(team);
            }

            if (this.feedTry >= this.feedOrder.Count)
            {
                ADLogging.Info("[Crucible] Shop: no familiar would take the feed");
                Screens.PetParty.Return(party);
                this.feedFrom = DateTime.MinValue;
                return;
            }

            Screens.PetParty.Pick(party, this.feedOrder[this.feedTry].index);
            this.feedTry++;
            this.confirmFrom = now;
            this.shopNext    = now + FeedRetry;
        }

        private void ResetShopVisit()
        {
            this.shopTried.Clear();
            this.fedThisVisit    = false;
            this.closedThisVisit = false;
        }

        private void UpdateItems(DateTime now)
        {
            if (now < this.itemNext)
                return;

            this.itemNext = now + TimeSpan.FromMilliseconds(250);

            if (!Config.Items || now - this.itemLastUse < ItemGap || Svc.Objects.LocalPlayer is not { IsDead: false, MaxHp: > 0 } me)
                return;

            AtkUnitBase* hud = CrucibleUi.Ready(CrucibleUi.MainHud);
            if (hud == null)
                return;

            ReaderXBMContentsMainHUD reader = new(hud);

            List<ReaderXBMContentsItemShop.ItemEntry> items  = reader.ItemEntries;

            CrucibleBoard?     board = CrucibleBoard.Current();
            CrucibleBoardStop? currentStop   = board?.StopPlayerIsOn();

            bool  InArena  = currentStop == null;
            bool  fighting = Svc.Condition[ConditionFlag.InCombat];
            float hp       = (float)me.CurrentHp / me.MaxHp;

            IEnumerable<CrucibleBoardStop> boardStops = [];
            if (hp <= (InArena ? FightLow : BoardLow) && (InArena && fighting || !InArena && !board!.HasCampBeforeNextFight(currentStop!, ref boardStops)))
                if (Pick(PickFromItems(InArena ? CrucibleItemData.FightHealingItems : CrucibleItemData.BoardHealingItems)))
                    return;

            foreach (uint[] statusItem in CrucibleItemData.StatusItems)
                if (!PlayerHelper.HasStatus(statusItem[1]) && (statusItem[0] is not (100 or 101) || fighting))
                    if (Pick(items.FirstOrDefault(x => x.Id == statusItem[0])))
                        return;

            if (!InArena)
                return;

            ReaderXBMContentsItemShop.ItemEntry? dmgItem = PickFromItems(CrucibleItemData.CombatDamageItems);
            if (dmgItem == null)
                return;

            List<IBattleNpc> enemies = Svc.Objects.Where(igo => igo is { ObjectKind: ObjectKind.BattleNpc, IsTargetable: true } && igo.IsHostile() && ObjectHelper.BelowDistanceToPlayer(igo.Position, 40f, 20f / 2f)).Cast<IBattleNpc>().Where(ibn => ibn.Health > 0).ToList();
            if (enemies.Count < 4)
                return;

            IBattleNpc   bossObject = enemies.MaxBy(igo => igo.MaxHp)!;
            IBattleNpc[] battleNpcs = enemies.Except([bossObject]).ToArray();

            IBattleNpc? target = battleNpcs.FirstOrDefault(ibn => battleNpcs.Count(ibe => Vector2.DistanceSquared(ibn.Position2, ibe.Position2) < 121f) > 2);

            if (target != null)
            {
                Svc.Targets.Target = target;
                Pick(dmgItem);
            }

            return;

            ReaderXBMContentsItemShop.ItemEntry? PickFromItems(IEnumerable<uint> ids)
            {
                foreach (uint id in ids)
                {
                    foreach (ReaderXBMContentsItemShop.ItemEntry entry in items)
                        if (entry.Id == id && entry.Available)
                            return entry;
                }
                return null;
            }

            bool Pick(ReaderXBMContentsItemShop.ItemEntry? pick)
            {
                if (pick == null)
                    return false;
                if (pick.Id == 0)
                    return false;
                if (!pick.Available)
                    return false;

                int slot = reader.GetItemIndex(pick);

                InstanceContentCrucible.UseItem((uint) slot, 0);

                this.itemLastUse = now;

                this.Status       = $"Using {pick.Name} at {hp:P0}";
                ADLogging.Info($"[Crucible] Items: using {pick.Name} (slot {slot}) at {hp:P0} {(InArena ? "in a fight" : "on the board")}");

                return true;
            }
        }
    }
}
