using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// Shared helpers of the market-shocks tests (TSK-008): JSON written with single quotes, a sink that records what an effect does to its channel,
    /// a list of CPUs for the predicates, a book of warehouses for <c>InventoryLoss</c>. Not a test and not a logic type: exempt from the [Covers] check.
    /// </summary>
    internal static class ShockTestData
    {
        /// <summary>The <c>params</c> of an effect from JSON text; single quotes stand for double ones.</summary>
        public static JsonObject Params(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            Assert.True(node is JsonObject, "test JSON is not an object: " + json);
            return (JsonObject)node;
        }

        /// <summary>Every channel is known.</summary>
        public static bool AnyChannel(string channel)
        {
            return true;
        }

        /// <summary>Only the channels a test lists are known.</summary>
        public static Func<string, bool> Channels(params string[] known)
        {
            var set = new HashSet<string>(known, StringComparer.Ordinal);
            return set.Contains;
        }

        /// <summary>The ramp of the design example (AC-7.1): peak 1.6, 30 days up, 90 at the top, 90 down.</summary>
        public const string DramSpike = "{'channel':'production.memoryCost','peak':1.6,'rampIn':30,'hold':90,'rampOut':90,'scope':'Global','scalable':true}";

        public static RampSpec RampOf(string json)
        {
            var errors = new List<string>();
            RampSpec spec;
            Assert.True(RampSpec.TryRead(Params(json), AnyChannel, errors, out spec), "ramp spec rejected: " + string.Join("; ", errors));
            return spec;
        }

        public static WaveSpec WaveOf(string json)
        {
            var errors = new List<string>();
            WaveSpec spec;
            Assert.True(WaveSpec.TryRead(Params(json), AnyChannel, errors, out spec), "wave spec rejected: " + string.Join("; ", errors));
            return spec;
        }

        public static LossSpec LossOf(string json)
        {
            var errors = new List<string>();
            LossSpec spec;
            Assert.True(LossSpec.TryRead(Params(json), errors, out spec), "loss spec rejected: " + string.Join("; ", errors));
            return spec;
        }

        /// <summary>Records the calls of an effect to the aggregator: the last multiplier, how many times it was set and cleared.</summary>
        public sealed class RecordingSink : IContributionSink
        {
            public float Last = float.NaN;
            public int Sets;
            public int Clears;

            public void Set(float multiplier)
            {
                Last = multiplier;
                Sets++;
            }

            public void Clear()
            {
                Last = float.NaN;
                Clears++;
            }
        }

        /// <summary>The CPUs of the companies for the predicates, with the questions the predicate asked.</summary>
        public sealed class FakeCpuSource : IShockCpuSource
        {
            private readonly Dictionary<string, List<CpuSalesFacts>> _cpus = new Dictionary<string, List<CpuSalesFacts>>(StringComparer.Ordinal);

            public bool Unavailable;
            public int Calls;
            public bool LastNeedUnitCost;
            public string LastCompanyKey;

            public FakeCpuSource Add(string companyKey, CpuSalesFacts cpu)
            {
                List<CpuSalesFacts> list;
                if (!_cpus.TryGetValue(companyKey, out list)) _cpus[companyKey] = list = new List<CpuSalesFacts>();
                list.Add(cpu);
                return this;
            }

            public bool TryGetCpus(string companyKey, bool needUnitCost, List<CpuSalesFacts> into)
            {
                Calls++;
                LastNeedUnitCost = needUnitCost;
                LastCompanyKey = companyKey;
                into.Clear();
                if (Unavailable) return false;

                List<CpuSalesFacts> list;
                if (_cpus.TryGetValue(companyKey, out list)) into.AddRange(list);
                return true;
            }
        }

        /// <summary>A CPU on sale that sold something yesterday: the usual shape, with the price and the real cost a test cares about.</summary>
        public static CpuSalesFacts OnSale(float price, float realUnitCost, int year = 1990, int sold = 100)
        {
            return new CpuSalesFacts(true, true, false, sold, price, realUnitCost, year);
        }

        /// <summary>Warehouses by save id.</summary>
        public sealed class FakeStockBook : IStockBook
        {
            public readonly Dictionary<int, int> Stock = new Dictionary<int, int>();
            public readonly List<int> Writes = new List<int>();

            public FakeStockBook With(int saveId, int stock)
            {
                Stock[saveId] = stock;
                return this;
            }

            public bool TryGetStock(int saveId, out int stock)
            {
                return Stock.TryGetValue(saveId, out stock);
            }

            public void SetStock(int saveId, int stock)
            {
                Writes.Add(saveId);
                Stock[saveId] = stock;
            }
        }
    }
}
