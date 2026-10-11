using System;
using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>Builders of synthetic CPUs, sales and dates for the tests of the models. Internal, so the runner never discovers it.</summary>
    internal static class LedgerTestData
    {
        public const string Player = "player";

        /// <summary>A list of <paramref name="length"/> months of zero sales with the given months set: <c>Sales(24, 0, 100000)</c>, <c>Sales(24, 0, 10, 5, 20)</c> (month, units, month, units...).</summary>
        public static int[] Sales(int length, params int[] monthUnitPairs)
        {
            var sales = new int[length];
            for (int i = 0; i + 1 < monthUnitPairs.Length; i += 2) sales[monthUnitPairs[i]] = monthUnitPairs[i + 1];
            return sales;
        }

        public static CpuLedgerView Cpu(int id, string company, SocketId socket, string genId, IReadOnlyList<int> sold, IReadOnlyList<int> byContracts = null)
        {
            return new CpuLedgerView
            {
                CpuId = id,
                CompanyKey = company,
                SocketKey = socket.Key,
                BasePackageName = socket.BasePackageName,
                GenId = genId,
                Sold = sold,
                SoldByContracts = byContracts,
            };
        }

        public static SocketId Std(string packageName)
        {
            return SocketIdentity.Standard(packageName);
        }

        public static SocketId Custom(int id, string basePackageName)
        {
            return SocketIdentity.Custom(id, basePackageName);
        }

        public static GameDate D(int year, int month, int day = 1)
        {
            return new GameDate(year, month, day);
        }

        /// <summary>The first month of the game is January 1971; the ledger month index of a date.</summary>
        public static int LedgerMonth(GameDate date)
        {
            return (date.Year - 1971) * 12 + (date.Month - 1);
        }

        /// <summary>A small deterministic generator (the tests must not depend on System.Random's algorithm).</summary>
        public sealed class Lcg
        {
            private uint _state;

            public Lcg(uint seed)
            {
                _state = seed;
            }

            public uint Next()
            {
                _state = _state * 1664525u + 1013904223u;
                return _state >> 8;
            }

            public int Next(int maxExclusive)
            {
                return (int)(Next() % (uint)maxExclusive);
            }
        }

        /// <summary>
        /// A synthetic market: <paramref name="count"/> CPUs on a handful of sockets of a few companies, each selling for a stretch of months
        /// (some through contracts) inside <paramref name="months"/>; deterministic for a seed.
        /// </summary>
        public static List<CpuLedgerView> Market(int count, int months, uint seed)
        {
            var rng = new Lcg(seed);
            string[] companies = { Player, "ai:1", "ai:2", "ai:3", "ai:4" };
            string[] packages = { "PGA 1985", "PGA 1990", "PGA 1995", "LGA 2000", "PGA 2000" };

            var cpus = new List<CpuLedgerView>(count);
            for (int i = 0; i < count; i++)
            {
                int start = rng.Next(months);
                int length = 1 + rng.Next(Math.Min(120, months - start));
                var sold = new int[months];
                var byContracts = new int[months];
                for (int m = start; m < start + length; m++)
                {
                    sold[m] = rng.Next(5000);
                    byContracts[m] = rng.Next(2) == 0 ? 0 : rng.Next(3000);
                }

                string package = packages[rng.Next(packages.Length)];
                SocketId socket = rng.Next(4) == 0 ? Custom(100 + rng.Next(5), package) : Std(package);
                cpus.Add(Cpu(i + 1, companies[rng.Next(companies.Length)], socket, "gen-" + package, sold, byContracts));
            }
            return cpus;
        }
    }
}
