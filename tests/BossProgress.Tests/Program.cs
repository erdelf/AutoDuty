using AutoDuty.Helpers;
global::AutoDuty.AutoDuty.TestLifecycle();
TestBossProgress();
Check(BossIdentity.Matches("Myrmidon Princess", "蚁后", "myrmidon princess"), "English route resolves localized boss identity");
Check(!BossIdentity.Matches("Myrmidon", "蚁后", "myrmidon princess"), "partial note is rejected");
Check(!BossIdentity.Matches("Myrmidon Princess", "蚁兵", "myrmidon soldier"), "add is not the boss");
Check(!BossIdentity.Matches("", "蚁后", "myrmidon princess"), "empty identity hint is rejected");
static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
static void TestBossProgress()
{
    var progress = new BossProgress();
    var first = new BossProgress.Key(1042, "Chudo-Yudo", new(0, 0, 116));
    var second = new BossProgress.Key(1042, "Koshchei", new(46, 4, -80));
    Check(!progress.Observe(first, false, false, false) && !progress.IsDefeated(first), "missing boss at entry is not victory");
    progress.Observe(first, true, false, false);
    Check(!progress.Observe(first, false, false, false) && !progress.IsDefeated(first), "phase disappearance is not victory");
    Check(progress.Observe(first, false, true, false), "witnessed corpse confirms defeat before Boss step starts");
    progress.Observe(first, false, false, false);
    Check(progress.IsDefeated(first), "despawn and death entrance restart retain confirmed first boss");
    Check(!progress.IsDefeated(second), "wipe at second boss does not complete it");
    Check(!progress.IsDefeated(first with { Territory = 1041 }), "evidence cannot leak to another dungeon");
    Check(!progress.IsDefeated(first with { Position = new(0, 0, -100) }), "same name at another route encounter is isolated");
    Check(progress.Observe(second, false, false, true), "completed game objective handles corpse missed or plugin reload");
    Check(!progress.Observe(second, true, true, true) && !progress.IsDefeated(second), "live respawn/phase overrides corpse and stale completion");
    Check(!BossProgress.ObjectiveComplete("Defeat Chudo-Yudo", 0, 1, new[] { "Chudo-Yudo" }), "named unfinished objective does not prove death");
    Check(!BossProgress.ObjectiveComplete("Defeat Chudo-Yudo", 0, 0, new[] { "Chudo-Yudo" }), "zero target count is not completed objective evidence");
    Check(BossProgress.ObjectiveComplete("Defeat Chudo-Yudo", 1, 1, new[] { "Chudo-Yudo" }), "matching completed objective proves death");
    progress.Reset();
    Check(!progress.IsDefeated(first), "new duty clears prior run evidence even in same dungeon");
    Check(BossProgress.ObjectiveMatches("Defeat Chudo-Yudo", "Chudo-Yudo"), "English boss objective matches full name");
    Check(BossProgress.ObjectiveMatches("击败蚁后", "蚁后"), "localized objective needs no whitespace separator");
    Check(!BossProgress.ObjectiveMatches("Defeat the Kingdom", "King"), "partial English name is not completion proof");
    Check(!BossProgress.ObjectiveMatches("Defeat all enemies", ""), "empty note never matches arbitrary completed objective");
    Check(!BossProgress.ObjectiveMatches("Defeat Koshchei", "Chudo-Yudo"), "another completed objective cannot complete current boss");
    Console.WriteLine("PASS: generic boss defeat evidence, early kills, corpse despawn, wipe/entrance restart, phase/respawn, objective names and fresh duty reset");
}
