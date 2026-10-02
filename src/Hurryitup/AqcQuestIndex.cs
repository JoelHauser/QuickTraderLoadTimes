using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using UnityEngine;

namespace Hurryitup
{
    /// <summary>
    /// Fix candidate 5 (off unless AqcQuestIndex is on): AllQuestsCheckmarks 1.4.0 compatibility.
    ///
    /// Measured 2026-10-01 (0.5.0): after AqcCompat and QuestPanelOnce, a new cell still spends
    /// ~0.57 ms (42% of its cost) in AllQuestsCheckmarks, whose QuestsHelper.GetActiveQuestsWithItem
    /// walks every started quest's every condition looking for the item's template, once per cell.
    ///
    /// This builds, once per burst of cells, an index from template id to the quest matches that
    /// walk would find, in the same order and by the same rules (first condition group with a match
    /// wins per quest; within a group the last matching condition wins unless a handover condition
    /// matched first), then answers the method from the index and finishes exactly as the method
    /// does (quest item, found-in-raid, only-found-in-raid flag). Weapons always go to the mod's own
    /// method, since their answer also checks the weapon's build, and so does everything in raid. The index is rebuilt on the same
    /// rules as AqcCompat's stash count (any inventory event, 2 idle frames, 1 s). The first 50
    /// answers also run the mod's own method and compare.
    /// </summary>
    internal static class AqcQuestIndex
    {
        private const int VerifyCalls = 50;

        private sealed class Match
        {
            public QuestTemplate Template;
            public ConditionItem Condition;
            public bool Completed;
        }

        private static ConstructorInfo _currentQuestCtor;
        private static MethodInfo _original;
        private static Dictionary<string, List<Match>> _index;
        private static List<QuestDataClass> _indexedFor;
        private static int _builtVersion = -1;
        private static int _lastFrame = -100;
        private static float _builtAt;
        private static int _verifyLeft = VerifyCalls;
        private static int _verifyMismatches;
        private static bool _inVerify;

        public static int Builds;
        public static int Served;
        public static string Status = "not patched";

        public static void Apply(Harmony harmony)
        {
            Type helper = AccessTools.TypeByName("AllQuestsCheckmarks.Helpers.QuestsHelper");
            if (helper == null)
            {
                Status = "AllQuestsCheckmarks not installed";
                return;
            }
            Type currentQuest = AccessTools.Inner(helper, "CurrentQuest");
            _currentQuestCtor = currentQuest?.GetConstructor(new[] { typeof(QuestTemplate), typeof(ConditionItem) });
            _original = AccessTools.Method(helper, "GetActiveQuestsWithItem");
            if (_currentQuestCtor == null || _original == null)
            {
                throw new MissingMemberException("AllQuestsCheckmarks QuestsHelper.GetActiveQuestsWithItem / CurrentQuest has changed");
            }
            MethodInfo prefix = AccessTools.Method(typeof(AqcQuestIndex), nameof(Prefix)).MakeGenericMethod(currentQuest);
            harmony.Patch(_original, prefix: new HarmonyMethod(prefix));
            Status = "patched";
        }

        /// <summary>
        /// Stands in for QuestsHelper.GetActiveQuestsWithItem(Profile, Item, out Dictionary&lt;MongoID,
        /// CurrentQuest&gt; activeQuests, out ...fulfilled). CurrentQuest is the mod's internal class, so
        /// this is closed over it at runtime (MakeGenericMethod) and the out parameters match exactly.
        /// Anything unexpected falls back to the mod's own method.
        /// </summary>
        public static bool Prefix<TQuest>(Profile profile, Item item, ref Dictionary<MongoID, TQuest> activeQuests,
            ref Dictionary<MongoID, TQuest> fulfilled, ref bool __result)
        {
            if (_inVerify || !HurryitupPlugin.AqcQuestIndex.Value) return true;
            // In raid (solo or a Fika co-op raid) quest progress can change as items are picked up,
            // and few cells are built there anyway: the mod's own lookup runs, like AqcCompat's.
            if (Comfort.Common.Singleton<AbstractGame>.Instance?.InRaid ?? false) return true;
            try
            {
                if (profile == null || item == null || item is Weapon) return true;

                Dictionary<string, List<Match>> index = Current(profile);
                if (index == null) return true;

                Dictionary<MongoID, TQuest> active = new Dictionary<MongoID, TQuest>();
                Dictionary<MongoID, TQuest> done = new Dictionary<MongoID, TQuest>();
                bool anyNonFir = false;
                if (index.TryGetValue(item.StringTemplateId, out List<Match> matches))
                {
                    foreach (Match m in matches)
                    {
                        TQuest cq = (TQuest)_currentQuestCtor.Invoke(new object[] { m.Template, m.Condition });
                        MongoID key = m.Template.Id;
                        if (m.Completed)
                        {
                            done.Add(key, cq);
                        }
                        else
                        {
                            active.Add(key, cq);
                            if (!m.Condition.onlyFoundInRaid) anyNonFir = true;
                        }
                    }
                }

                bool result;
                if (active.Count == 0) result = false;
                else if (item.QuestItem) result = true;
                else result = anyNonFir || item.MarkedAsSpawnedInSession;

                if (_verifyLeft > 0) Verify(profile, item, active, done, result);

                activeQuests = active;
                fulfilled = done;
                __result = result;
                Served++;
                return false;
            }
            catch (Exception e)
            {
                HurryitupPlugin.Log.LogWarning("AQC quest index lookup failed, using the mod's own: " + e.Message);
                return true;
            }
        }

        private static Dictionary<string, List<Match>> Current(Profile profile)
        {
            int frame = Time.frameCount;
            float now = Time.realtimeSinceStartup;
            bool valid = _index != null
                && ReferenceEquals(_indexedFor, profile.QuestsData)
                && _builtVersion == InventoryEvents.Version
                && frame - _lastFrame <= 2
                && now - _builtAt < 1f;
            _lastFrame = frame;
            if (valid) return _index;

            try
            {
                _index = Build(profile.QuestsData);
            }
            catch (Exception e)
            {
                HurryitupPlugin.Log.LogWarning("AQC quest index could not be built, using the mod's own lookup: " + e.Message);
                _index = null;
                return null;
            }
            _indexedFor = profile.QuestsData;
            _builtVersion = InventoryEvents.Version;
            _builtAt = now;
            Builds++;
            return _index;
        }

        /// <summary>GetActiveQuestsWithItem's walk, done once for every template at the same time.</summary>
        private static Dictionary<string, List<Match>> Build(IEnumerable<QuestDataClass> quests)
        {
            Dictionary<string, List<Match>> index = new Dictionary<string, List<Match>>();
            Dictionary<string, Match> questMatches = new Dictionary<string, Match>();
            Dictionary<string, Match> groupMatches = new Dictionary<string, Match>();
            HashSet<string> frozen = new HashSet<string>();

            foreach (QuestDataClass quest in quests)
            {
                if (quest.Template == null || (quest.Status != EQuestStatus.Started && quest.Status != EQuestStatus.AvailableForFinish))
                {
                    continue;
                }
                questMatches.Clear();
                foreach (KeyValuePair<EQuestStatus, ConditionCollection> group in quest.Template.Conditions)
                {
                    groupMatches.Clear();
                    frozen.Clear();
                    foreach (Condition condition in group.Value)
                    {
                        if (!(condition is ConditionItem conditionItem) || conditionItem.target == null) continue;
                        bool completed = quest.CompletedConditions.Contains(condition.id);
                        bool handover = conditionItem is ConditionHandoverItem;
                        foreach (string template in conditionItem.target.Distinct())
                        {
                            if (template == null || frozen.Contains(template)) continue;
                            groupMatches[template] = new Match { Template = quest.Template, Condition = conditionItem, Completed = completed };
                            if (handover) frozen.Add(template);
                        }
                    }
                    // Per quest, the first group with a match for a template is the one that counts.
                    foreach (KeyValuePair<string, Match> kv in groupMatches)
                    {
                        if (!questMatches.ContainsKey(kv.Key)) questMatches[kv.Key] = kv.Value;
                    }
                }
                foreach (KeyValuePair<string, Match> kv in questMatches)
                {
                    if (!index.TryGetValue(kv.Key, out List<Match> list)) index[kv.Key] = list = new List<Match>();
                    list.Add(kv.Value);
                }
            }
            return index;
        }

        /// <summary>Runs the mod's own method for the same item and compares.</summary>
        private static void Verify(Profile profile, Item item, IDictionary active, IDictionary done, bool result)
        {
            object[] args = { profile, item, null, null };
            bool expected;
            _inVerify = true;
            try
            {
                expected = (bool)_original.Invoke(null, args);
            }
            finally
            {
                _inVerify = false;
            }
            bool same = expected == result
                && SameEntries((IDictionary)args[2], active)
                && SameEntries((IDictionary)args[3], done);
            if (!same)
            {
                _verifyMismatches++;
                HurryitupPlugin.Log.LogWarning($"AQC quest index MISMATCH for {item.TemplateId}: index {result} ({Keys(active)} / {Keys(done)}), " +
                    $"mod's own {expected} ({Keys((IDictionary)args[2])} / {Keys((IDictionary)args[3])})");
            }
            if (--_verifyLeft == 0)
            {
                HurryitupPlugin.Log.LogInfo($"AQC quest index check: {VerifyCalls - _verifyMismatches} of {VerifyCalls} answers matched the mod's own lookup");
            }
        }

        /// <summary>Same keys in the same order, each with the same condition object.</summary>
        private static bool SameEntries(IDictionary a, IDictionary b)
        {
            if (a.Count != b.Count) return false;
            List<object> ka = a.Keys.Cast<object>().ToList(), kb = b.Keys.Cast<object>().ToList();
            for (int i = 0; i < ka.Count; i++)
            {
                if (!Equals(ka[i], kb[i])) return false;
                object ca = Traverse.Create(a[ka[i]]).Field("Condition").GetValue();
                object cb = Traverse.Create(b[kb[i]]).Field("Condition").GetValue();
                if (!ReferenceEquals(ca, cb)) return false;
            }
            return true;
        }

        private static string Keys(IDictionary d) => d == null ? "null" : d.Count == 0 ? "none" : string.Join(",", d.Keys.Cast<object>());
    }
}
