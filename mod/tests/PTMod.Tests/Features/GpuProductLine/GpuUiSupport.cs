using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PTMod.Core.Logic.L10n;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// <see cref="IGpuText"/> for the tests of the windows: the words are the real tables of the feature (<c>L10n/ru|en/gpu-product-line.json</c>) read by the same
    /// resolver the game uses, so a test sees the text the player sees - placeholders, plural forms and all; the numbers are written the plain invariant way (the
    /// formatter of the game is not in the test build).
    /// </summary>
    internal sealed class TestGpuText : IGpuText
    {
        private static L10nTables _tables;

        public readonly LocTextResolver Resolver;
        public readonly List<string> Warnings = new List<string>();

        public TestGpuText(string language = "ru")
        {
            Resolver = new LocTextResolver(Tables(), Warnings.Add);
            Resolver.Language = language;
        }

        /// <summary>The two tables of the feature, read once for the run.</summary>
        public static L10nTables Tables()
        {
            if (_tables != null) return _tables;

            var tables = new L10nTables();
            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                string path = Path.Combine(TestEnv.SrcDir, "L10n", language, "gpu-product-line.json");
                tables.AddJson(language, "l10n/" + language + "/gpu-product-line.json", GpuFiles.Read(path), problems);
            }
            Assert.Equal(0, problems.Count, string.Join("\n", problems.ToArray()));
            _tables = tables;
            return tables;
        }

        public bool IsRussian
        {
            get { return Resolver.Language == "ru"; }
        }

        public string L(string key, params object[] args)
        {
            return Resolver.L(key, args);
        }

        public string Plural(string key, long n, params object[] args)
        {
            return Resolver.Plural(key, n, args);
        }

        public bool Has(string key)
        {
            return Resolver.Has(key);
        }

        public string Money(float amount)
        {
            return "$" + amount.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public string Percent(float fraction)
        {
            return (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        public string Frequency(float megahertz)
        {
            return megahertz.ToString("0.##", CultureInfo.InvariantCulture) + " MHz";
        }

        public string Number(float value, string unit)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture) + (unit ?? string.Empty);
        }

        public string Compact(double value)
        {
            return value.ToString("0.##e+0", CultureInfo.InvariantCulture);
        }

        public string Integer(double value)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        public string Date(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public string MonthYear(int year, int month)
        {
            return year.ToString(CultureInfo.InvariantCulture) + "-" + month.ToString("00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A world of the services with the pieces the models of the windows are built from.</summary>
    internal static class UiWorlds
    {
        /// <summary>The year 2005 with the market of the shipped content seeded; the division is founded and the parts of the AC-3.1 design are researched.</summary>
        public static GpuWorld Ac31(DateTime? date = null)
        {
            var world = new GpuWorld(date ?? new DateTime(2005, 3, 10)).Founded("arch.shader", "vram.gddr3", "cool.fan");
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 3400000f;
            return world;
        }

        public static GpuDesignerModel Designer(GpuWorld world, TestGpuText text)
        {
            return new GpuDesignerModel(world.Content, world.Research, world.Designer, world.Projects, world.Market, world.Game, text);
        }

        /// <summary>Puts the model on the design of AC-3.1: arch.shader, 90 nm, 300 mm2, 510 MHz (the highest), GDDR3 256 bit, 256 MB, fan.</summary>
        public static void SetAc31(GpuDesignerModel model)
        {
            model.SelectArch(model.ArchLabels().Count - 1);
            model.SelectNode(IndexOf(model.NodeLabels(), "90nm"));
            model.SelectMemory(model.MemoryLabels().Count - 1);
            model.SelectCooler(model.CoolerLabels().Count - 1);
            model.SelectBus(BusIndex(model, 256));
            model.SetDie(300f);
            model.SetClockPercent(100);
            model.SetVramIndex(VramIndex(model, 256f));
        }

        public static int BusIndex(GpuDesignerModel model, int bits)
        {
            for (int i = 0; i < model.Buses.Count; i++)
            {
                if (model.Buses[i] == bits) return i;
            }
            Assert.Fail("no bus of " + bits + " bits among the buses of the memory");
            return -1;
        }

        public static int VramIndex(GpuDesignerModel model, float megabytes)
        {
            for (int i = 0; i < model.VramValues.Count; i++)
            {
                if (model.VramValues[i] == megabytes) return i;
            }
            Assert.Fail("no video memory of " + megabytes + " MB among the values");
            return -1;
        }

        public static int IndexOf(List<string> list, string value)
        {
            int index = list.IndexOf(value);
            Assert.True(index >= 0, "not in the list: " + value + " (" + string.Join(", ", list.ToArray()) + ")");
            return index;
        }
    }
}
