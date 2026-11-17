using AutoDuty.Helpers;
Check(InteractionCompletion.HasReward(1041, 1004346, id => id == 2000521 ? 1 : 0), "received key finishes persistent NPC, including a repeated route step");
Check(!InteractionCompletion.HasReward(1041, 1004346, _ => 0), "no key requires interaction");
Check(!InteractionCompletion.HasReward(1041, 2001462, _ => 1), "key does not complete gate interaction");
Check(!InteractionCompletion.HasReward(1042, 1004346, _ => 1), "reward scoped to correct duty");
var dialogue = new InteractionDialogueProgress();
Check(!dialogue.Observe(false, false), "missing dialog is not success");
Check(!dialogue.Observe(true, false), "visible dialog waits for close");
Check(!dialogue.Observe(false, true), "casting delays completion after close");
Check(dialogue.Observe(false, false), "closed seen dialog completes");
Check(dialogue.Observe(false, false), "throttled repeated polls do not consume completion twice");
Check(!new InteractionDialogueProgress().Observe(false, false), "another NPC cannot reuse prior dialog state");
static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
