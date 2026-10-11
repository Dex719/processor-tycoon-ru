using System;
using System.IO;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>The channels of the built-in <c>core.json</c> declared in a fresh engine of the core (<see cref="ChannelRegistry"/>), the way <c>ModModifiers</c> declares them at start.</summary>
    internal static class GpuCoreRegistry
    {
        public static ChannelRegistry Create(DateTime today)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found");
            string path = Path.Combine(TestEnv.SrcDir, "Content", "mod-framework", "core.json");
            JsonObject root = GpuFiles.ParseObject(File.ReadAllText(path, new UTF8Encoding(false)), "core.json");
            var registry = new ChannelRegistry(null, today);
            foreach (JsonNode node in root.GetArray("channels"))
            {
                var entry = (JsonObject)node;
                var spec = new ChannelSpec
                {
                    Name = entry.GetString("name"),
                    Min = (float)entry.GetDouble("min"),
                    Max = (float)entry.GetDouble("max"),
                    BalanceMin = (float)entry.GetDouble("balanceMin"),
                    BalanceMax = (float)entry.GetDouble("balanceMax"),
                    Global = entry.GetBool("global"),
                    PerCompany = entry.GetBool("perCompany"),
                    PerCpu = entry.GetBool("perCpu"),
                    PerSegment = entry.GetBool("perSegment"),
                    OwnerFeature = entry.GetString("owner"),
                };
                Assert.True(registry.Declare(spec), "core.json declares " + spec.Name);
            }
            return registry;
        }
    }
}
