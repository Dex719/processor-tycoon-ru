using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// Builders shared by the tests of the pure models of the windows (TSK-015…TSK-017): a formatter that writes the key and its arguments as they are (so a test sees which
    /// text with which numbers the window would show), and companies for the table. Internal, so the runner never discovers it as a test class.
    /// </summary>
    internal static class UiTestData
    {
        /// <summary>A formatter that makes <c>key[arg1|arg2]</c>, money as <c>$1234</c> and a share as <c>25%</c>.</summary>
        public static ViewFormat Format()
        {
            return new ViewFormat
            {
                Text = (key, args) => args == null || args.Length == 0 ? key : key + "[" + string.Join("|", args) + "]",
                Money = value => "$" + Math.Round(value).ToString("F0", CultureInfo.InvariantCulture),
                Percent = share => Math.Round(share * 100.0, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%",
                Number = value => value.ToString("0.##", CultureInfo.InvariantCulture),
            };
        }

        /// <summary>A company of the table: the plain snapshot of <see cref="RivalTestData.Plain"/> with its ids, name and status.</summary>
        public static CompanySnapshot Company(int uniqueId, string name, CompanyStatus status, double enterprise = 400000000.0, double cash = 30000000.0)
        {
            CompanySnapshot t = RivalTestData.Plain(enterprise, cash, RivalTestData.DebtLimit, status);
            t.UniqueId = uniqueId;
            t.SaveId = 400 + uniqueId;
            t.Name = name;
            t.MarketShare = 0.05;
            return t;
        }

        public static List<CompanyRow> Rows(IList<CompanySnapshot> companies, PlayerSnapshot p = null, AcquisitionsState s = null)
        {
            return DealsTable.Build(companies, p ?? RivalTestData.Player(), s ?? new AcquisitionsState(), RivalTestData.Balance(), null);
        }

        public static bool Contains(List<PreviewLine> lines, string text)
        {
            foreach (PreviewLine line in lines)
            {
                if (line.Text != null && line.Text.Contains(text)) return true;
                if (line.Title != null && line.Title.Contains(text)) return true;
            }
            return false;
        }

        public static PreviewSection Section(DealPreviewModel model, string titleKey)
        {
            foreach (PreviewSection section in model.Sections)
            {
                if (section.TitleKey == titleKey) return section;
            }
            return null;
        }

        public static PreviewLine Pair(PreviewSection section, string titleKey)
        {
            foreach (PreviewLine line in section.Lines)
            {
                if (line.Title == titleKey) return line;
            }
            return null;
        }

        public static PreviewLine StartingWith(PreviewSection section, string text)
        {
            foreach (PreviewLine line in section.Lines)
            {
                if (line.Text != null && line.Text.StartsWith(text, StringComparison.Ordinal)) return line;
            }
            return null;
        }
    }
}
