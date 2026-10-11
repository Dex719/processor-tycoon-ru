using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// Builders for the tests of the texts and the views (TSK-013...TSK-018): a <see cref="ViewText"/> over the shipped string tables of the feature in
    /// ru or en, and a small world to look at - the shipped generations and balance, a chronology, an installed base. Internal, so the runner never discovers it.
    /// </summary>
    internal static class ViewTestData
    {
        public const string Player = "player";

        public static readonly Dictionary<string, string> Companies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "player", "MyChips" },
            { "ai:1", "Intelligent" },
            { "ai:2", "Advanced Micro" },
        };

        public static readonly Dictionary<string, string> CustomSockets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "custom:5120", "MySocket" },
        };

        /// <summary>The string tables of the feature as the game loads them, in the language asked for ("ru" or "en").</summary>
        public static LocTextResolver Resolver(string language)
        {
            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson("ru", "ru", PlatformTestData.ReadStrings("ru"), problems);
            tables.AddJson("en", "en", PlatformTestData.ReadStrings("en"), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            var resolver = new LocTextResolver(tables);
            resolver.Language = language;
            return resolver;
        }

        /// <summary>A <see cref="ViewText"/> that speaks the language, names the companies of <see cref="Companies"/> and the custom sockets of <see cref="CustomSockets"/>.</summary>
        public static ViewText Text(string language)
        {
            LocTextResolver resolver = Resolver(language);
            return new ViewText
            {
                Text = resolver.L,
                GenerationName = gen => resolver.Resolve(gen.name),
                CompanyName = key =>
                {
                    string name;
                    return Companies.TryGetValue(key, out name) ? name : null;
                },
                SocketName = socket =>
                {
                    string name;
                    return CustomSockets.TryGetValue(socket.Key, out name) ? name : null;
                },
            };
        }

        public static GenerationCatalog Catalog()
        {
            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return catalog;
        }

        public static PlatformBalance Balance()
        {
            var problems = new List<string>();
            PlatformBalance balance = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return balance;
        }

        public static GameDate D(int year, int month, int day = 1)
        {
            return new GameDate(year, month, day);
        }

        public static SocketId Std(string package)
        {
            return SocketIdentity.Standard(package);
        }

        /// <summary>A ledger with the units sold in month 0 on each socket by each company (one CPU per entry); the base is the units themselves (month 0 is the last full month).</summary>
        public static InstalledBaseLedger Ledger(PlatformBalance balance, GenerationCatalog catalog, params LedgerEntry[] entries)
        {
            var views = new List<CpuLedgerView>();
            for (int i = 0; i < entries.Length; i++)
            {
                LedgerEntry entry = entries[i];
                GenerationDef gen = catalog.BySocket(entry.Socket);
                views.Add(LedgerTestData.Cpu(100 + i, entry.Company, entry.Socket, gen == null ? null : gen.id, new[] { entry.Units, 0 }));
            }

            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(views, 0, balance);
            return ledger;
        }

        public struct LedgerEntry
        {
            public string Company;
            public SocketId Socket;
            public int Units;

            public LedgerEntry(string company, SocketId socket, int units)
            {
                Company = company;
                Socket = socket;
                Units = units;
            }
        }

        /// <summary>The state of the AC-6.1 situation on 1 January 2000: the player has 25 % of the base on PGA 1990 and has supported it for 12 months.</summary>
        public static PlatformSnapshot Ac61()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var snapshot = new PlatformSnapshot
            {
                Catalog = catalog,
                Balance = balance,
                Today = D(2000, 1, 1),
                InstalledBase = Ledger(balance, catalog,
                    new LedgerEntry(Player, Std("PGA 1990"), 25000),
                    new LedgerEntry("ai:1", Std("LGA 2000"), 75000)),
            };
            snapshot.Timeline.Announce("gen-1990-pga", D(1990, 1, 1));
            snapshot.Timeline.SetLaunched("gen-1990-pga", D(1990, 2, 1), "ai:1");
            snapshot.Timeline.Announce("gen-1995-pga", D(1995, 1, 1));
            snapshot.Timeline.SetLaunched("gen-1995-pga", D(1995, 3, 1), "ai:1");
            snapshot.Timeline.AddSocketIntro(Player, "pkg:PGA 1990", D(1999, 1, 1), null, 0, 0f);
            return snapshot;
        }

        /// <summary>The text of a notice in the language, with its arguments.</summary>
        public static string Say(ViewText text, Notice notice)
        {
            return text.L(notice.Key, notice.Args);
        }
    }
}
