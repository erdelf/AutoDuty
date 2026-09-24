using AutoDuty.Configurations;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoDuty.Managers
{
    using ECommons.UIHelpers.AtkReaderImplementations;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ECommons.Throttlers;
    using ECommons.UIHelpers.AddonMasterImplementations;
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
        private static readonly TimeSpan FeedTimeout   = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ItemGap       = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ItemMenuWait  = TimeSpan.FromMilliseconds(1500);

        private DateTime confirmFrom = DateTime.MinValue;

        private int      fightStep = -1;
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
            this.fightStep    = -1;
            this.restPicks    = null;
            this.feedFrom     = DateTime.MinValue;
            this.itemMenuFrom = DateTime.MinValue;
            this.ResetShopVisit();
            this.Status = "";
        }

        public void Update()
        {
            DateTime now = DateTime.UtcNow;

            this.UpdateItems(now);

            if (now - this.confirmFrom <= ConfirmWindow && !this.FeedPending(now) && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* confirm))
            {
                Screens.Prompt.Yes(confirm);
                this.confirmFrom = DateTime.MinValue;
                return;
            }

            if (this.StartFight(now))
                return;

            this.UpdateShop(now);

            if (!EzThrottler.Throttle("CrucibleMenus", 250) || CrucibleUi.IsOpen(CrucibleUi.YesNo))
                return;

            if (Config.Rest && !this.FeedPending(now) && !CrucibleUi.IsOpen(CrucibleUi.ShopWindow) && !CrucibleUi.IsOpen(CrucibleUi.BoardLayout) &&
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

                if (!booty.LootCoinsTaken)
                {
                    Screens.Booty.TakeCoins(loot);
                    this.confirmFrom = now;
                    return;
                }


                IEnumerable<ReaderXBMContentsBooty.LootChoice> choices = booty.LootChoices.Where(lc => !lc.Taken);

                IEnumerable<ReaderXBMContentsItemShop.ItemEntry> itemEntries = booty.ItemEntriesValid.ToList();
                IEnumerable<ReaderXBMContentsItemShop.GearEntry> gearEntries = booty.OwnedEntriesOwned.ToList();


                if (gearEntries.Count() < GearCap)
                    foreach (ReaderXBMContentsBooty.LootChoice gearChoice in choices)
                    {
                        if (CrucibleItemData.ShopGear.Contains(gearChoice.Item))
                            if (gearEntries.All(ge => ge.Id != gearChoice.Item))
                            {
                                Screens.Booty.Take(loot, gearChoice.lootIndex);
                                this.confirmFrom = now;
                                return;
                            }

                        if (itemEntries.All(ge => ge.Id != gearChoice.Item))
                        {
                            Screens.Booty.Take(loot, gearChoice.lootIndex);
                            this.confirmFrom = now;
                            return;
                        }
                    }

                Screens.Booty.Close(loot);
                this.confirmFrom = now;
                return;
            }

            if (CrucibleUi.TryReady(CrucibleUi.ResultWindow, out AtkUnitBase* result))
            {
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
            {
                this.fightStep = -1;
                return false;
            }

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

            if (this.fightStep < 0)
                this.fightStep = 0;

            if (this.fightStep < alive.Count)
            {
                int row = alive[this.fightStep].index;
                Screens.PetParty.Pick(party, row);
                this.fightStep++;
                this.fightNext = now + PickInterval;
                this.Status    = $"Picking familiar {this.fightStep} of {alive.Count} ({team[row].Name})";
                return true;
            }

            Screens.StageDetail.Confirm(layout);
            this.fightNext = now + CommenceRetry;
            this.Status    = "Commencing the battle";
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

            HashSet<uint> items = xbmTreasure.ItemEntriesValid.Select(ie => ie.Id).ToHashSet();
            HashSet<uint> gear  = xbmTreasure.OwnedEntriesOwned.Select(ie => ie.Id).ToHashSet();

            List<ReaderXBMContentsTreasure.TreasureChoice> treasureChoices = xbmTreasure.TreasureChoices;

            if (treasureChoices.Count == 0)
                return;

            List<ReaderXBMContentsTreasure.TreasureChoice> choices = treasureChoices.Where(tc => !tc.Bought                                                                                             && 
                                                                                                            (!CrucibleItemData.ShopGear.Contains(tc.Item)    || (gear.Count < GearCap && !gear.Contains(tc.Item))) &&
                                                                                                            (!CrucibleItemData.ShopHealing.Contains(tc.Item) || (items.Count < ItemCap && !items.Contains(tc.Item)))).ToList();
            if (choices.Count == 0)
            {
                Screens.Treasure.Close(treasure);
                this.confirmFrom = now;
            }

            ReaderXBMContentsTreasure.TreasureChoice best = choices.OrderBy(x => CrucibleItemData.TreasureRank(x.Item)).ThenBy(x => x.treasureIndex).First();

            Svc.Log.Info($"[Crucible] Treasure: taking {Describe(best)} from {string.Join(" / ", choices.Select(Describe))}");

            Screens.Treasure.Take(treasure, (uint)best.treasureIndex);

            this.confirmFrom = now;
            this.Status      = $"Taking {(best.Item != 0 ? CrucibleItemData.NameOf(best.Item) : best.treasureIndex)}";

            return;

            static string Describe(ReaderXBMContentsTreasure.TreasureChoice choice) =>
                $"{CrucibleItemData.NameOf(choice.Item)} ({choice.treasureIndex})";
        }

        private void Rest(AtkUnitBase* party, DateTime now)
        {
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
                Svc.Log.Info($"[Crucible] Campsite: resting with {(names.Length > 0 ? names : "no familiars (90% self heal)")}");
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

            AtkUnitBase* shop = CrucibleUi.Ready(CrucibleUi.ShopWindow);
            if (shop == null)
            {
                this.ResetShopVisit();
                return;
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

            int                                       coins      = CrucibleUi.ShopCoins(shop);
            List<ReaderXBMContentsItemShop.StockEntry> affordable = CrucibleUi.ShopStock(shop).Where(x => !x.Bought && x.Price <= coins && !this.shopTried.Contains(x.Item)).ToList();

            if (this.ChooseBuy(affordable, CrucibleUi.ShopHeldItems(shop), CrucibleUi.ShopOwnedGear(shop)) is not { } buy)
            {
                if (!this.closedThisVisit)
                {
                    this.closedThisVisit = true;
                    this.Status          = $"Shop done, {coins} coins left";
                    Svc.Log.Info($"[Crucible] Shop: done with {coins} coins left");

                    if (Screens.ItemShop.Close(shop))
                        this.confirmFrom = now;
                }

                return;
            }

            this.Status = $"Buying {CrucibleItemData.NameOf(buy.Item)} for {buy.Price}";
            Svc.Log.Info($"[Crucible] Shop: buying {CrucibleItemData.NameOf(buy.Item)} for {buy.Price} of {coins} coins");

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

        private ReaderXBMContentsItemShop.StockEntry? ChooseBuy(List<ReaderXBMContentsItemShop.StockEntry> stock, HashSet<uint> held, HashSet<uint> ownedGear)
        {
            if (held.Count < ItemCap && FirstInStock(stock, CrucibleItemData.ShopHealing, held) is { } healing)
                return healing;

            if (ownedGear.Count < GearCap && FirstInStock(stock.Where(x => !ownedGear.Contains(x.Item)), CrucibleItemData.ShopGear, held) is { } gear)
                return gear;

            return this.fedThisVisit ? null : FirstInStock(stock, CrucibleItemData.ShopFeed, held);
        }

        private static ReaderXBMContentsItemShop.StockEntry? FirstInStock(IEnumerable<ReaderXBMContentsItemShop.StockEntry> stock, uint[] priority, HashSet<uint> owned)
        {
            Dictionary<uint, ReaderXBMContentsItemShop.StockEntry> byRow = stock.GroupBy(x => x.Item).ToDictionary(g => g.Key, g => g.First());
            foreach (uint row in priority)
                if (!owned.Contains(row) && byRow.TryGetValue(row, out ReaderXBMContentsItemShop.StockEntry? entry))
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

                team = team.Where(x => !x.Disabled).ToList();
                this.feedOrder = CrucibleTeam.FightOrder(team);
            }

            if (this.feedTry >= this.feedOrder.Count)
            {
                Svc.Log.Info("[Crucible] Shop: no familiar would take the feed");
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

            if (now - this.itemMenuFrom <= ItemMenuWait)
            {
                if (CrucibleUi.TryReady(CrucibleUi.ContextMenu, out AtkUnitBase* menu))
                {
                    Screens.Menu.ChooseFirst(menu);
                    this.itemMenuFrom = DateTime.MinValue;
                    this.itemLastUse  = now;
                }

                return;
            }

            if (!Config.Items || now - this.itemLastUse < ItemGap || Svc.Objects.LocalPlayer is not { IsDead: false, MaxHp: > 0 } me)
                return;

            AtkUnitBase* hud = CrucibleUi.Ready(CrucibleUi.MainHud);
            if (hud == null)
                return;

            bool  fighting = Svc.Condition[ConditionFlag.InCombat];
            float hp       = (float)me.CurrentHp / me.MaxHp;
            if (hp >= (fighting ? FightLow : BoardLow))
                return;

            List<CrucibleUi.ItemSlot> items = CrucibleUi.HudItems(hud);
            CrucibleUi.ItemSlot pick = (fighting ? CrucibleItemData.FightItems : CrucibleItemData.BoardItems).Select(row => items.FirstOrDefault(x => x.Row == row))
                                                                          .FirstOrDefault(x => x.Row != 0);
            if (pick.Row == 0)
                return;

            Screens.MainHud.OpenItemMenu(hud, pick.Slot);
            this.itemMenuFrom = now;
            this.Status       = $"Using {pick.Name} at {hp:P0}";
            Svc.Log.Info($"[Crucible] Items: using {pick.Name} (slot {pick.Slot}) at {hp:P0} {(fighting ? "in a fight" : "on the board")}");
        }
    }
}
