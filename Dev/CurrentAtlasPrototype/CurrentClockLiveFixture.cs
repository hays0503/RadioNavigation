using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UBOAT.Game.Core;
using UBOAT.Game.Core.Time;

namespace UBOAT.Mods.RadioNavigation
{
    // Test-only timing witness. Optional split caps the native chunk at one
    // known test listener; it never writes positions or drops requested time.
    public static class CurrentClockLiveFixture
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance;
        private static readonly Harmony Patches = new Harmony("radionav.fixture.current-clock");
        private static long updateBefore;
        private static long updateAfter;
        private static long skipBefore;
        private static long skipAfter;
        private static long callbackTick;
        private static long callbackCandidate;
        private static long observerTick;
        private static long observerElapsed;
        private static int skipWrites;
        private static long splitAt;
        private static long firstSkipBefore;
        private static long firstSkipAfter;
        private static int observerCalls;

        public static bool Install()
        {
            Type type = typeof(GameTime);
            MethodInfo update = type.GetMethod("Update", All, null, Type.EmptyTypes, null);
            MethodInfo skip = type.GetMethod("SimulateTicks", All, null,
                new[] { typeof(long), typeof(GameTime.TimeSimulationPrecision) }, null);
            if (update == null || skip == null)
                return false;
            try
            {
                Patches.Patch(update, transpiler: Handler(nameof(UpdateClock)));
                Patches.Patch(skip, transpiler: Handler(nameof(SkipClock)));
                return true;
            }
            catch (Exception)
            {
                Patches.Unpatch(update, HarmonyPatchType.Transpiler, Patches.Id);
                Patches.Unpatch(skip, HarmonyPatchType.Transpiler, Patches.Id);
                throw;
            }
        }

        public static bool Arm(int seconds, bool split = false)
        {
            var injection = InjectionFramework.Instance;
            var time = injection?.GetInstance<GameTime>();
            var queue = typeof(GameTime).GetField("executionQueue", All)?.GetValue(null) as ExecutionQueue;
            if (time == null || queue == null || seconds < 1 || seconds > 60)
                return false;
            callbackTick = callbackCandidate = observerTick = observerElapsed = 0;
            skipBefore = skipAfter = skipWrites = 0;
            firstSkipBefore = firstSkipAfter = observerCalls = 0;
            DateTime date = time.CurrentDateTime.AddSeconds(seconds);
            splitAt = split ? date.Ticks : 0;
            queue.AddDateListener(() =>
            {
                callbackTick = time.StoryTicks;
                callbackCandidate = skipAfter;
            }, date);
            Action<long> observer = null;
            observer = elapsed =>
            {
                observerCalls++;
                if (observerCalls == 1)
                    observerTick = time.StoryTicks;
                observerElapsed += elapsed;
                if (!split || observerCalls == 2)
                    time.TicksSimulated -= observer;
            };
            time.TicksSimulated += observer;
            return true;
        }

        public static string Snapshot() => string.Join(",", new[]
        {
            updateBefore, updateAfter, skipBefore, skipAfter, callbackTick, callbackCandidate,
            observerTick, observerElapsed, skipWrites, firstSkipBefore, firstSkipAfter,
            observerCalls, splitAt
        });

        private static HarmonyMethod Handler(string name) =>
            new HarmonyMethod(typeof(CurrentClockLiveFixture).GetMethod(name, All));

        private static IEnumerable<CodeInstruction> UpdateClock(IEnumerable<CodeInstruction> source) =>
            InjectProbe(source, nameof(ObserveUpdate));

        private static IEnumerable<CodeInstruction> SkipClock(IEnumerable<CodeInstruction> source)
        {
            var code = InjectProbe(source, nameof(ObserveSkip)).ToList();
            var ticks = typeof(GameTime).GetField("ticksBeingSimulated", All);
            int index = code.FindIndex(c => c.opcode == OpCodes.Stfld && Equals(c.operand, ticks));
            if (ticks == null || index < 2 || code[index - 2].opcode != OpCodes.Ldarg_0
                || code[index - 1].opcode != OpCodes.Ldloc_0)
                throw new InvalidOperationException("Native clock limit point changed");
            var loadClock = new CodeInstruction(OpCodes.Ldarg_0);
            var labels = typeof(CodeInstruction).GetField("labels");
            if (labels == null)
                throw new InvalidOperationException("Harmony labels unavailable");
            labels.SetValue(loadClock, labels.GetValue(code[index - 2]));
            labels.SetValue(code[index - 2], labels.GetValue(new CodeInstruction(OpCodes.Nop)));
            code.InsertRange(index - 2, new[]
            {
                loadClock, new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Call, typeof(CurrentClockLiveFixture).GetMethod(nameof(LimitChunk), All)),
                new CodeInstruction(OpCodes.Stloc_0)
            });
            return code;
        }

        public static long LimitChunk(GameTime clock, long native)
        {
            long remaining = splitAt - clock.StoryTicks;
            return remaining > 0 && remaining < native ? remaining : native;
        }

        private static IEnumerable<CodeInstruction> InjectProbe(IEnumerable<CodeInstruction> source, string name)
        {
            var code = source.ToList();
            FieldInfo ticks = typeof(GameTime).GetField("storyTicks", All);
            FieldInfo labels = typeof(CodeInstruction).GetField("labels");
            var stores = code.Where(c => c.opcode == OpCodes.Stfld && Equals(c.operand, ticks)).ToArray();
            if (ticks == null || labels == null || stores.Length != 1)
                throw new InvalidOperationException("Story clock store was not unique");
            var loadClock = new CodeInstruction(OpCodes.Ldarg_0);
            labels.SetValue(loadClock, labels.GetValue(stores[0]));
            labels.SetValue(stores[0], labels.GetValue(new CodeInstruction(OpCodes.Nop)));
            code.InsertRange(code.IndexOf(stores[0]), new[]
            {
                loadClock, new CodeInstruction(OpCodes.Call, typeof(CurrentClockLiveFixture).GetMethod(name, All))
            });
            return code;
        }

        public static long ObserveUpdate(long next, GameTime clock)
        {
            updateBefore = clock.StoryTicks;
            updateAfter = next;
            return next;
        }

        public static long ObserveSkip(long next, GameTime clock)
        {
            skipBefore = clock.StoryTicks;
            skipAfter = next;
            if (skipWrites == 0)
            {
                firstSkipBefore = skipBefore;
                firstSkipAfter = skipAfter;
            }
            skipWrites++;
            return next;
        }
    }
}
