using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The fingerprints of CPU projects and the binding of CPUs (FR-5, D4; TSK-008): a project's fingerprint still matches its CPU after the side file
    /// went through JSON (the IPS within 1e-4), two projects with the same fingerprint come out in the order they started (FIFO), the lazy default of
    /// a CPU without a binding is bound once, and its default family follows the owner and the year (the seed policy, TSK-014).
    /// </summary>
    [Covers(typeof(CpuFingerprint), typeof(ArchRegistry))]
    public class CpuFingerprintTests
    {
        private static ArchRegistry NewRegistry()
        {
            return new ArchRegistry(ArchTestKit.BuiltInCatalog());
        }

        /// <summary>The registry after <c>ModJson.From</c> → text → parse → <c>ModJson.Bind</c> → <c>Load</c>, as the side file does it.</summary>
        private static ArchRegistry ThroughJson(ArchRegistry registry)
        {
            JsonNode tree = ModJson.From(registry.ToSave());
            string text = ModJson.Write(tree, true);

            JsonNode parsed;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out parsed, out error), "the section must be valid JSON: " + error);

            var errors = new List<string>();
            ArchSaveData data = ModJson.Bind<ArchSaveData>(parsed, errors);
            Assert.Equal(0, errors.Count, string.Join("; ", errors));

            var warnings = new List<string>();
            ArchRegistry loaded = NewRegistry();
            loaded.Load(data, warnings);
            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            return loaded;
        }

        // ---- matching ----

        [Test]
        public void Matches_SameProject_AndOnlyIt()
        {
            var fp = new CpuFingerprint(3, "PT-386", 1987, 5.6e6f);

            Assert.True(fp.Matches(new CpuFingerprint(3, "PT-386", 1987, 5.6e6f)));
            Assert.False(fp.Matches(new CpuFingerprint(4, "PT-386", 1987, 5.6e6f)), "another company");
            Assert.False(fp.Matches(new CpuFingerprint(3, "PT-486", 1987, 5.6e6f)), "another name");
            Assert.False(fp.Matches(new CpuFingerprint(3, "pt-386", 1987, 5.6e6f)), "names are compared exactly");
            Assert.False(fp.Matches(new CpuFingerprint(3, "PT-386", 1988, 5.6e6f)), "another year");
            Assert.False(fp.Matches(new CpuFingerprint(3, "PT-386", 1987, 5.7e6f)), "another design");
            Assert.False(fp.Matches(new CpuFingerprint(3, "PT-386", 1987, float.NaN)));
            Assert.False(fp.Matches((PendingSave)null));
        }

        [Test]
        public void Ips_IsComparedWithARelativeTolerance()
        {
            // 1e-4 of the larger value: the same rule for 50 IPS and for 5·10^9 IPS.
            Assert.True(CpuFingerprint.IpsClose(5e9f, 5e9f * (1f + 5e-5f)));
            Assert.False(CpuFingerprint.IpsClose(5e9f, 5e9f * (1f + 5e-4f)));
            Assert.True(CpuFingerprint.IpsClose(50f, 50.004f));
            Assert.False(CpuFingerprint.IpsClose(50f, 50.02f));
            Assert.True(CpuFingerprint.IpsClose(0f, 0f));
            Assert.False(CpuFingerprint.IpsClose(0f, 1e-9f), "relative to the larger value: nothing is close to 0 but 0");
            Assert.False(CpuFingerprint.IpsClose(float.NaN, float.NaN));
            Assert.False(CpuFingerprint.IpsClose(float.PositiveInfinity, 1f));
            Assert.True(CpuFingerprint.IpsClose(50f, 50.02f, 1e-3f), "a wider tolerance on request");
        }

        [Test]
        public void JsonRoundTrip_TheProjectFindsItsCpu()
        {
            // An IPS with many significant digits, as the designer computes it; the game's save writes it as text too.
            float ips = 5600000f * 1.05f / 3f;
            var project = new CpuFingerprint(3, "PT-386", 1987, ips);

            ArchRegistry registry = NewRegistry();
            Assert.True(registry.AddPending(project, "x86"));
            ArchRegistry loaded = ThroughJson(registry);
            Assert.Equal(1, loaded.Pending.Count);

            // The CPU of the finished project, its IPS read back from the game's save (double text) - and a little noise on top.
            float fromGameSave = (float)double.Parse(((double)ips).ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var cpu = new CpuFingerprint(3, "PT-386", 1987, fromGameSave * (1f + 3e-5f));
            Assert.Equal("x86", loaded.TakePending(cpu), "AC-5.1: saved in the middle of the project, loaded, finished - the right family");
            Assert.Equal(0, loaded.Pending.Count, "taken out of the queue");
            Assert.Null(loaded.TakePending(cpu), "only once");
        }

        [Test]
        public void SameFingerprint_ComesOutFirstInFirstOut()
        {
            var fp = new CpuFingerprint(3, "PT-1", 1990, 1e7f);
            ArchRegistry registry = NewRegistry();
            Assert.True(registry.AddPending(fp, "x86"));
            Assert.True(registry.AddPending(new CpuFingerprint(3, "PT-2", 1990, 1e7f), "risc"));
            Assert.True(registry.AddPending(fp, "arm"));

            ArchRegistry loaded = ThroughJson(registry);
            Assert.Equal("x86", loaded.TakePending(fp), "the project started first");
            Assert.Equal("arm", loaded.TakePending(fp), "then the second one");
            Assert.Null(loaded.TakePending(fp));
            Assert.Equal(1, loaded.Pending.Count, "the other project waits on");
            Assert.Equal("risc", loaded.TakePending(new CpuFingerprint(3, "PT-2", 1990, 1e7f)));
        }

        [Test]
        public void UnusableFingerprints_DoNotWait()
        {
            ArchRegistry registry = NewRegistry();
            Assert.False(registry.AddPending(new CpuFingerprint(-1, "X", 1990, 1f), "x86"), "no company");
            Assert.False(registry.AddPending(new CpuFingerprint(3, null, 1990, 1f), "x86"), "no name");
            Assert.False(registry.AddPending(new CpuFingerprint(3, "X", 1990, float.NaN), "x86"), "no IPS");
            Assert.False(registry.AddPending(new CpuFingerprint(3, "X", 1990, 1f), "X86!"), "not a family id");
            Assert.True(registry.AddPending(new CpuFingerprint(3, "", 1990, 1f), "x86"), "an empty name is a name");
            Assert.Equal(1, registry.Pending.Count);
        }

        [Test]
        public void PendingSave_RoundTrip_AndEquality()
        {
            var fp = new CpuFingerprint(7, "Z80 Turbo", 1979, 2.5e5f);
            PendingSave pending = fp.ToPending("proprietary");
            Assert.Equal(7, pending.company);
            Assert.Equal("Z80 Turbo", pending.name);
            Assert.Equal(1979, pending.year);
            Assert.Equal("proprietary", pending.arch);
            Assert.True(fp.Equals(CpuFingerprint.FromPending(pending)));
            Assert.True(fp.Matches(pending));
            Assert.Equal(fp.GetHashCode(), CpuFingerprint.FromPending(pending).GetHashCode());
            Assert.False(fp.Equals(new CpuFingerprint(7, "Z80 Turbo", 1979, 2.5e5f * (1f + 1e-6f))), "Equals is exact, Matches is tolerant");
            Assert.True(fp.Equals((object)new CpuFingerprint(7, "Z80 Turbo", 1979, 2.5e5f)));
            Assert.False(fp.Equals("not a fingerprint"));
            Assert.True(fp.ToString().Contains("Z80 Turbo"));
            Assert.True(fp.IsUsable);
            Assert.Throws<System.ArgumentNullException>(() => CpuFingerprint.FromPending(null));
        }

        // ---- the lazy default (FR-5 item 3) ----

        [Test]
        public void ResolveOrBind_BeforeTheSeed_KeepsNothing()
        {
            // An old save before its migration: the owner's default is not known yet, the migration binds the CPU later (FR-8 item 3).
            ArchRegistry registry = NewRegistry();
            Assert.True(registry.MigrationNeeded);
            Assert.Equal("proprietary", registry.ResolveOrBind(1043, "proprietary"));
            Assert.Null(registry.GetCpuArch(1043), "not kept before the seed");

            registry.Bind(1043, "x86");
            Assert.Equal("x86", registry.ResolveOrBind(1043, "proprietary"), "an explicit binding is used before the seed too");
        }

        [Test]
        public void ResolveOrBind_BindsTheFallbackOnce()
        {
            ArchRegistry registry = NewRegistry();
            registry.MarkSeeded();
            Assert.Equal("x86", registry.ResolveOrBind(1043, "x86"));
            Assert.Equal("x86", registry.GetCpuArch(1043), "bound: computed once");
            Assert.Equal("x86", registry.ResolveOrBind(1043, "arm"), "the binding wins over a later fallback");

            Assert.Equal("arm", registry.ResolveOrBind(0, "arm"), "a CPU without an id gets the family...");
            Assert.Null(registry.GetCpuArch(0), "...but no binding");
            Assert.False(ArchRegistry.IsBindableCpuId(0));
            Assert.True(ArchRegistry.IsBindableCpuId(1));
            Assert.Null(registry.ResolveOrBind(2000, null));
        }

        [Test]
        public void DefaultFamilyFor_FollowsTheSeedPolicy_NotTheStateOfTheCompany()
        {
            // The stub of chunk 2 followed the active family of the owner; since TSK-014 the default is the policy's: the owner (player or the record of
            // the AI company) and the year of the CPU. The state of the company does not matter, so a CPU gets the same family whenever it is asked.
            ArchRegistry registry = ArchTestKit.BuiltInRegistry();
            Assert.Equal("x86", registry.DefaultFamilyFor(true, -1, 2001), "a CPU of the player from 2001");
            Assert.Equal("x86_64", registry.DefaultFamilyFor(false, 0, 2008), "Intel in 2008");

            CompanyArchState intel = registry.GetOrCreate(17, false);
            intel.Active = "arm";
            Assert.Equal("x86_64", registry.DefaultFamilyFor(false, 0, 2008), "the active family of a company changes nothing");
        }
    }
}
