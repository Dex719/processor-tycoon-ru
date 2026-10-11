using System;
using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-003, FR-6 item 3 and FR-1 item 5: the keys <c>pkg:</c> / <c>custom:</c>, and the compatibility of two sockets - the same socket, the same
    /// base package (AC-6.2), a pair of <c>compatibleSockets</c> - against the shipped catalog and balance.
    /// </summary>
    [Covers(typeof(SocketIdentity), typeof(SocketId))]
    public class SocketIdentityTests
    {
        private static GenerationCatalog Catalog()
        {
            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return catalog;
        }

        // ---- keys ----

        [Test]
        public void Standard_HasAPkgKey_AndIsItsOwnBase()
        {
            SocketId id = SocketIdentity.Standard("PGA 1995");
            Assert.Equal("pkg:PGA 1995", id.Key);
            Assert.Equal("PGA 1995", id.BasePackageName);
            Assert.True(id.IsStandard);
            Assert.True(id.IsValid);
            Assert.Equal("pkg:PGA 1995", id.ToString());
        }

        [Test]
        public void Custom_HasACustomKey_AndTheBaseOfItsPackage()
        {
            SocketId id = SocketIdentity.Custom(5120, "PGA 1995");
            Assert.Equal("custom:5120", id.Key);
            Assert.Equal("PGA 1995", id.BasePackageName);
            Assert.False(id.IsStandard);
            Assert.True(id.IsValid);
        }

        [Test]
        public void Custom_WithAnUnreadableBase_KeepsTheKey_AndHasNoBase()
        {
            Assert.Null(SocketIdentity.Custom(7, null).BasePackageName);
            Assert.Null(SocketIdentity.Custom(7, string.Empty).BasePackageName);
            Assert.Equal("custom:7", SocketIdentity.Custom(7, null).Key);
        }

        [Test]
        public void Custom_KeysFollowTheHardwareId_WithTheInvariantCulture()
        {
            Assert.Equal("custom:0", SocketIdentity.Custom(0, "PGA 1995").Key);
            Assert.Equal("custom:1234567", SocketIdentity.Custom(1234567, "PGA 1995").Key);
            Assert.NotEqual(SocketIdentity.Custom(1, "PGA 1995").Key, SocketIdentity.Custom(2, "PGA 1995").Key);
        }

        [Test]
        public void Standard_WithoutAName_IsNotValid()
        {
            Assert.False(SocketIdentity.Standard(null).IsValid);
            Assert.False(SocketIdentity.Standard(string.Empty).IsValid);
            Assert.False(default(SocketId).IsValid);
            Assert.Equal(string.Empty, default(SocketId).ToString());
        }

        // ---- compatibility ----

        [Test]
        public void Compatible_TheSameSocket_IsCompatible()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            SocketId a = SocketIdentity.Standard("PGA 1995");
            Assert.True(SocketIdentity.Compatible(a, SocketIdentity.Standard("PGA 1995"), catalog, balance));
            Assert.True(SocketIdentity.Compatible(SocketIdentity.Custom(9, "PGA 1995"), SocketIdentity.Custom(9, "PGA 1995"), catalog, balance));
        }

        [Test]
        public void Compatible_ACustomSocketOnAStandardBase_IsCompatibleWithTheStandard_AC62()
        {
            // AC-6.2: the AI builds its own socket on PGA 1995 while its main socket is the standard PGA 1995: no penalty.
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            SocketId standard = SocketIdentity.Standard("PGA 1995");
            SocketId custom = SocketIdentity.Custom(5120, "PGA 1995");
            Assert.True(SocketIdentity.Compatible(standard, custom, catalog, balance));
            Assert.True(SocketIdentity.Compatible(custom, standard, catalog, balance));
        }

        [Test]
        public void Compatible_TwoCustomSocketsOnOneBase_AreCompatible()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            Assert.True(SocketIdentity.Compatible(SocketIdentity.Custom(1, "LGA 2000"), SocketIdentity.Custom(2, "LGA 2000"), catalog, balance));
        }

        [Test]
        public void Compatible_WhenTheSwitchIsOff_OnlyTheSameSocketIsCompatibleOnOneBase()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            balance.Rating.CustomOnSameBaseIsCompatible = false;

            SocketId standard = SocketIdentity.Standard("PGA 1995");
            SocketId custom = SocketIdentity.Custom(5120, "PGA 1995");
            Assert.False(SocketIdentity.Compatible(standard, custom, catalog, balance));
            Assert.False(SocketIdentity.Compatible(custom, standard, catalog, balance));
            Assert.True(SocketIdentity.Compatible(custom, SocketIdentity.Custom(5120, "PGA 1995"), catalog, balance), "the same socket always is");
        }

        [Test]
        public void Compatible_DifferentBases_AreNotCompatible()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            Assert.False(SocketIdentity.Compatible(SocketIdentity.Standard("PGA 1990"), SocketIdentity.Standard("PGA 1995"), catalog, balance));
            Assert.False(SocketIdentity.Compatible(SocketIdentity.Custom(1, "PGA 1990"), SocketIdentity.Custom(2, "PGA 1995"), catalog, balance));
        }

        [Test]
        public void Compatible_TwoBranchesOfOneGeneration_AreNotCompatible_UnlessDeclared()
        {
            // gen-2000 has LGA 2000 and PGA 2000 and declares no pair: moving between the branches is a socket change.
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            Assert.False(SocketIdentity.Compatible(SocketIdentity.Standard("LGA 2000"), SocketIdentity.Standard("PGA 2000"), catalog, balance));
        }

        [Test]
        public void Compatible_APairOfCompatibleSockets_IsCompatible_InBothDirections_AndForCustomSockets()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            SocketId dip70 = SocketIdentity.Standard("DIP 1970");
            SocketId dip75 = SocketIdentity.Standard("DIP 1975");
            Assert.True(SocketIdentity.Compatible(dip70, dip75, catalog, balance));
            Assert.True(SocketIdentity.Compatible(dip75, dip70, catalog, balance));
            Assert.True(SocketIdentity.Compatible(SocketIdentity.Custom(3, "DIP 1975"), dip70, catalog, balance), "a custom socket counts by its base");
        }

        [Test]
        public void Compatible_APairOfAnOverrideGeneration_IsFound()
        {
            GenerationDef g = PlatformTestData.Gen("g", 0, "PGA 1990", "PGA 1995");
            g.compatibleSockets.Add(new SocketPair { a = "PGA 1995", b = "PGA 1990" });
            GenerationCatalog catalog = GenerationCatalog.Build(new[] { g }, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, null);
            PlatformBalance balance = PlatformBalance.Defaults();
            Assert.True(SocketIdentity.Compatible(SocketIdentity.Standard("PGA 1990"), SocketIdentity.Standard("PGA 1995"), catalog, balance));
            Assert.False(SocketIdentity.Compatible(SocketIdentity.Standard("PGA 1990"), SocketIdentity.Standard("LGA 2000"), catalog, balance));
        }

        [Test]
        public void Compatible_AnUnknownBase_IsCompatibleOnlyWithItself()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            SocketId orphan = SocketIdentity.Custom(11, null);
            Assert.True(SocketIdentity.Compatible(orphan, SocketIdentity.Custom(11, null), catalog, balance));
            Assert.False(SocketIdentity.Compatible(orphan, SocketIdentity.Custom(12, null), catalog, balance), "two unknown bases are not one base");
            Assert.False(SocketIdentity.Compatible(orphan, SocketIdentity.Standard("PGA 1995"), catalog, balance));
            Assert.False(SocketIdentity.Compatible(SocketIdentity.Standard("PGA 1995"), orphan, catalog, balance));
        }

        [Test]
        public void Compatible_ASocketWithoutAKey_IsCompatibleWithNothing()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            SocketId none = default(SocketId);
            Assert.False(SocketIdentity.Compatible(none, none, catalog, balance));
            Assert.False(SocketIdentity.Compatible(none, SocketIdentity.Standard("PGA 1995"), catalog, balance));
            Assert.False(SocketIdentity.Compatible(SocketIdentity.Standard("PGA 1995"), none, catalog, balance));
        }

        [Test]
        public void Compatible_WithoutCatalogOrBalance_UsesTheBaseRuleOnly()
        {
            SocketId standard = SocketIdentity.Standard("DIP 1970");
            Assert.True(SocketIdentity.Compatible(standard, SocketIdentity.Custom(1, "DIP 1970"), null, null));
            Assert.False(SocketIdentity.Compatible(standard, SocketIdentity.Standard("DIP 1975"), null, null), "no catalog, no declared pairs");
        }

        [Test]
        public void Compatible_AcrossAllSixteenPackages_OnlyTheSamePackage_AndTheDipPair_AreCompatible()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = PlatformBalance.Defaults();
            foreach (string x in PlatformTestData.GamePackages)
            {
                foreach (string y in PlatformTestData.GamePackages)
                {
                    bool expected = x == y || (x.StartsWith("DIP ", StringComparison.Ordinal) && y.StartsWith("DIP ", StringComparison.Ordinal));
                    bool forward = SocketIdentity.Compatible(SocketIdentity.Standard(x), SocketIdentity.Standard(y), catalog, balance);
                    bool backward = SocketIdentity.Compatible(SocketIdentity.Standard(y), SocketIdentity.Standard(x), catalog, balance);
                    Assert.Equal(expected, forward, x + " vs " + y);
                    Assert.Equal(forward, backward, "symmetry of " + x + " and " + y);
                }
            }
        }
    }
}
