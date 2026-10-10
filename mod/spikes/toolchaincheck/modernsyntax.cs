// Toolchain probe (spike 0003, TSK-003). Not part of PTMod.dll; built only on request:
//
//   python mod/build.py --game-dir "<game dir>" --src mod/spikes/toolchaincheck --out spikes/ModernSyntax.dll
//   powershell -File mod/spikes/toolchaincheck/check_reflection_load.ps1 -Dll mod/build/spikes/ModernSyntax.dll
//
// Purpose: prove that Roslyn csc (-langversion:latest -nostdlib) compiles modern C# against the Unity 6 mscorlib /
// netstandard / UnityEngine*, the English Assembly-CSharp.dll and Harmony 2.3.6, and that the result references
// Assembly-CSharp and opens with Assembly.ReflectionOnlyLoadFrom. Every construct below is one the mod code may use
// in shared code (Core/Logic); constructs that need a missing runtime type are listed at the end and NOT used here.
// The code is never executed: the spike only checks that it compiles, links and loads for reflection.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ProcessorTycoon;
using ProcessorTycoon.TimeSystem;
using UnityEngine;

namespace PTMod.Spikes.ToolchainCheck
{
    /// <summary>readonly struct + in parameter + expression-bodied members + tuple deconstruction.</summary>
    public readonly struct Money : IEquatable<Money>
    {
        public readonly long Cents;

        public Money(long cents) { Cents = cents; }

        public Money Plus(in Money other) => new Money(Cents + other.Cents);

        public bool Equals(Money other) => Cents == other.Cents;

        public override bool Equals(object obj) => obj is Money m && Equals(m);

        public override int GetHashCode() => Cents.GetHashCode();   // no System.HashCode: absent in mscorlib 4.8

        public override string ToString() => $"{Cents / 100}.{Cents % 100:D2}";

        public void Deconstruct(out long units, out long cents) => (units, cents) = (Cents / 100, Cents % 100);
    }

    public enum Kind { Unknown, Cpu, Gpu }

    /// <summary>Plain C# object used to show the ?. / ?? operators on non-Unity types.</summary>
    public sealed class Holder
    {
        public string Name { get; set; }
        public List<int> Values { get; set; }
    }

    public static class ModernSyntax
    {
        // ?. ?? ??= on plain C# objects. Never use them on UnityEngine.Object: they skip Unity's overloaded null check.
        public static int CountOrZero(Holder holder) => holder?.Values?.Count ?? 0;

        public static string NameOrDefault(Holder holder)
        {
            holder.Name ??= "anonymous";
            return holder?.Name ?? "none";
        }

        // String interpolation, alignment and format specifiers (falls back to string.Format: Unity has no
        // DefaultInterpolatedStringHandler, and the compiler copes with that).
        public static string Format(string who, double value) => $"[{who,-8}] {value:F2} ({value / 100:P1})";

        // Local functions, static local functions, out var, switch expression, property and relational patterns.
        public static string Classify(IEnumerable<int> source, Kind kind)
        {
            var total = Sum(source);
            var label = kind switch
            {
                Kind.Cpu => "cpu",
                Kind.Gpu => "gpu",
                _ => "unknown",
            };
            var bucket = total switch
            {
                < 0 => "negative",
                0 => "zero",
                > 0 and < 100 => "small",
                _ => "large",
            };
            return Join(label, bucket);

            int Sum(IEnumerable<int> items)
            {
                var acc = 0;
                foreach (var item in items) acc += item;
                return acc;
            }

            static string Join(string a, string b) => a + ":" + b;
        }

        public static bool TryParseCents(string text, out Money money)
        {
            if (long.TryParse(text, out var cents) && cents is >= 0 and < 1_000_000_000)   // digit separators
            {
                money = new Money(cents);
                return true;
            }
            money = default;
            return false;
        }

        // Tuples, deconstruction, target-typed new, Func with static lambda.
        public static (int Min, int Max) Range(IReadOnlyList<int> values)
        {
            var (min, max) = (int.MaxValue, int.MinValue);
            foreach (var v in values)
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            return (min, max);
        }

        public static Dictionary<string, Func<int, int>> Table() => new()
        {
            ["double"] = static x => x * 2,
            ["negate"] = static x => -x,
        };

        // using declaration (needs only IDisposable), is not null, nameof.
        public static string Describe(IDisposable resource, object value)
        {
            using var _ = resource;
            return value is not null ? nameof(value) + "=" + value : nameof(value) + " is null";
        }

        // UnityEngine: Debug, Mathf, Vector2 compile against UnityEngine.CoreModule.
        public static float Clamp01(float x) => Mathf.Clamp01(x);

        public static void LogLine(string text) => Debug.Log($"[PTMod] {text}");

        public static Vector2 Move(Vector2 from, Vector2 to, float t) => Vector2.Lerp(from, to, Mathf.Clamp01(t));

        // Game types from Assembly-CSharp.dll (English original): this is what puts the AssemblyRef on the DLL.
        public static Type[] GameTypes() => new[]
        {
            typeof(GameManager),
            typeof(DateController),
        };

        public static bool GameManagerExists() => GameManager.Instance != null;   // UnityEngine.Object comparison, on purpose

        // Harmony 2.3.6 against the Unity mscorlib: attributes, prefix with __instance, transpiler signature.
        public static void CreateHarmony()
        {
            var harmony = new Harmony("ptmod.spike.toolchaincheck");
            harmony.CreateClassProcessor(typeof(ProbePatches)).Patch();
            foreach (var method in harmony.GetPatchedMethods()) LogLine(method.Name);
        }
    }

    /// <summary>Harmony patch shapes the framework uses (design.md, spike 0002). Never applied by this spike.</summary>
    [HarmonyPatch(typeof(GameManager), "Awake")]
    public static class ProbePatches
    {
        [HarmonyPrefix]
        public static bool Prefix(GameManager __instance, out int __state)
        {
            __state = 1;
            return __instance != null;
        }

        [HarmonyPostfix]
        public static void Postfix(GameManager __instance, int __state) => Debug.Log(__instance.name + __state);

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception) => null;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret) instruction.labels.Clear();
                yield return instruction;
            }
        }
    }

    // Not used here, because they need types that Unity's mscorlib (or the net48 test runtime) lacks:
    //   init / record          -> System.Runtime.CompilerServices.IsExternalInit      (polyfill: Core/Compat/IsExternalInit.cs)
    //   required members       -> RequiredMemberAttribute, CompilerFeatureRequiredAttribute (not used)
    //   ^ and .. (Index/Range) -> exist in Unity, absent in net48: a polyfill would clash in Unity (not used)
    //   Span<T>, System.HashCode, default interface members -> absent in net48 (Core/Logic is compiled by both)
    // Full table with the compiler output: .kiro/decisions/0003-build-toolchain.md, section Evidence.
}
