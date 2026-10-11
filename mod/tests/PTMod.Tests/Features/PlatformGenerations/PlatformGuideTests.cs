using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-023, FR-9, FR-13: the description of the feature for the player. The blocks of <see cref="PlatformGuide"/> (the installed base, the early
    /// transition, the change of a socket, the OEM customers, the full platform) carry the numbers of the balance in force and agree with the models
    /// that do the work - the example "30 % of the base gives +7.5 %" is what <see cref="RatingEffectModel"/> computes - and the section "Поколения
    /// платформ" of the README (<c>Features/PlatformGenerations/README.section.md</c>, for <c>mod/README.md</c>) is exactly the guide built with the
    /// default <c>balance.json</c> and the shipped <c>generations.json</c>, with the table of the generations and the real names of their sockets.
    /// </summary>
    [Covers(typeof(PlatformGuide), typeof(GuideSection), typeof(GuideGeneration))]
    public class PlatformGuideTests
    {
        private const string ReadmeFile = "README.section.md";

        private static string FeatureDirectory()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations");
        }

        private static GuideSection Section(List<GuideSection> sections, string id)
        {
            foreach (GuideSection section in sections)
            {
                if (section.Id == id) return section;
            }
            Assert.Fail("no section " + id);
            return null;
        }

        private static List<GuideSection> Ru(PlatformBalance balance = null)
        {
            return PlatformGuide.Build(balance ?? ViewTestData.Balance(), ViewTestData.Catalog(), ViewTestData.Text("ru"));
        }

        private static List<GuideSection> En(PlatformBalance balance = null)
        {
            return PlatformGuide.Build(balance ?? ViewTestData.Balance(), ViewTestData.Catalog(), ViewTestData.Text("en"));
        }

        // ---- the blocks ----

        [Test]
        public void TheGuide_HasTheFiveBlocksOfTheDesign_InOrder_WithTitlesAndTexts()
        {
            foreach (List<GuideSection> sections in new[] { Ru(), En() })
            {
                Assert.Equal(5, sections.Count);
                Assert.Equal(PlatformGuide.BaseId, sections[0].Id);
                Assert.Equal(PlatformGuide.EarlyId, sections[1].Id);
                Assert.Equal(PlatformGuide.ChurnId, sections[2].Id);
                Assert.Equal(PlatformGuide.OemId, sections[3].Id);
                Assert.Equal(PlatformGuide.FullId, sections[4].Id);
                foreach (GuideSection section in sections)
                {
                    Assert.False(string.IsNullOrWhiteSpace(section.Title), section.Id + " has a title");
                    Assert.True(section.Body.Length > 80, section.Id + " says something: " + section.Body);
                    Assert.False(section.Body.Contains("{") || section.Body.Contains("}"), section.Id + " has an argument left: " + section.Body);
                    Assert.False(section.Body.Contains("pg.hub") || section.Title.Contains("pg.hub"), section.Id + " shows a key instead of a text");
                }
            }
        }

        [Test]
        public void TheTexts_AreInTheirLanguages()
        {
            var cyrillic = new Regex("[Ѐ-ӿ]");
            foreach (GuideSection section in Ru()) Assert.True(cyrillic.IsMatch(section.Body) && cyrillic.IsMatch(section.Title), "ru " + section.Id);
            foreach (GuideSection section in En()) Assert.False(cyrillic.IsMatch(section.Body) || cyrillic.IsMatch(section.Title), "en " + section.Id + ": " + section.Body);
        }

        // ---- the numbers are the numbers of the balance ----

        [Test]
        public void TheNumbersOfTheText_AreTheNumbersOfTheBalance()
        {
            PlatformBalance b = ViewTestData.Balance();
            List<GuideSection> sections = Ru(b);

            string baseText = Section(sections, PlatformGuide.BaseId).Body;
            Assert.True(baseText.Contains("за последние 60 мес."), baseText);
            Assert.True(baseText.Contains("весом 50 %"), "the weight of contract sales: " + baseText);
            Assert.True(baseText.Contains("до +10 % при доле от 40 %"), "the ceiling and the saturation: " + baseText);
            Assert.True(baseText.Contains("добавляется 25 % базы других компаний"), "the shared part of a standard socket: " + baseText);
            Assert.True(baseText.Contains("действуют на 50 % от обычного"), "Industries: " + baseText);

            string early = Section(sections, PlatformGuide.EarlyId).Body;
            Assert.True(early.Contains("Мест для ранних последователей — 3"), early);
            Assert.True(early.Contains("+6 % к рейтингу"), early);
            Assert.True(early.Contains("затухает за 60 мес."), early);
            Assert.True(early.Contains("обычно 24 мес."), "the window of the generations: " + early);

            string churn = Section(sections, PlatformGuide.ChurnId).Body;
            Assert.True(churn.Contains("меньше 36 мес."), churn);
            Assert.True(churn.Contains("не меньше 2 % рынка"), churn);
            Assert.True(churn.Contains("штраф до −10 %"), churn);
            Assert.True(churn.Contains("затухает за 24 мес."), churn);

            string oem = Section(sections, PlatformGuide.OemId).Body;
            Assert.True(oem.Contains("повышает шанс подписать контракт на 20 %"), oem);
            Assert.True(oem.Contains("на одно поколение снижает его на 25 %"), oem);
            Assert.True(oem.Contains("на два и больше — на 50 %"), oem);
            Assert.True(oem.Contains("до +10 % при доле от 40 %"), "the loyalty: " + oem);

            string full = Section(sections, PlatformGuide.FullId).Body;
            Assert.True(full.Contains("ещё на 5 %"), full);
        }

        [Test]
        public void ThePageFollowsTheBalance_WhenTheBalanceChanges()
        {
            PlatformBalance b = ViewTestData.Balance();
            b.Rating.IbMaxBonus = 0.20f;
            b.Rating.EarlyBonus = 0.09f;
            b.Rating.MinSocketLifeMonths = 48;
            b.Oem.AdoptBonus = 0.30f;
            b.Oem.FullPlatformBonus = 0.075f;

            List<GuideSection> sections = Ru(b);
            Assert.True(Section(sections, PlatformGuide.BaseId).Body.Contains("до +20 % при доле"));
            Assert.False(Section(sections, PlatformGuide.BaseId).Body.Contains("до +10 % при доле"));
            Assert.True(Section(sections, PlatformGuide.EarlyId).Body.Contains("+9 % к рейтингу"));
            Assert.True(Section(sections, PlatformGuide.ChurnId).Body.Contains("меньше 48 мес."));
            Assert.True(Section(sections, PlatformGuide.OemId).Body.Contains("повышает шанс подписать контракт на 30 %"));
            Assert.True(Section(sections, PlatformGuide.FullId).Body.Contains("ещё на 7.5 %"));
        }

        // ---- the examples are what the models compute ----

        [Test]
        public void TheExample_ThirtyPercentOfTheBase_GivesPlusSevenPointFive_AsTheModelSays()
        {
            PlatformBalance b = ViewTestData.Balance();
            string ru = Section(Ru(b), PlatformGuide.BaseId).Body;
            string en = Section(En(b), PlatformGuide.BaseId).Body;

            Assert.True(ru.Contains("Пример: ваша доля в базе рынка — 30 %, бонус к рейтингу = +10 % × 30 / 40 = +7.5 %."), ru);
            Assert.True(en.Contains("Example: your share of the base of the market is 30%, the bonus to the rating = +10% x 30 / 40 = +7.5%."), en);

            // The model: 30 units of 100 on a closed socket.
            RatingBreakdown rating = RatingEffectModel.Compute(new RatingInput { OwnIb = 30, SocketIb = 30, TotalIb = 100, IsStandard = false }, b);
            Assert.Equal("7.5", PlatformNumbers.Percent(rating.Bib), "B_ib of the model, written the way the text writes it");
            Assert.Near(1.075, rating.ConsumerMult, 1e-6);
        }

        [Test]
        public void TheExample_OfTheChange_IsTheModelsPenaltyForAMainSocketThatLivedAThirdOfItsTerm()
        {
            PlatformBalance b = ViewTestData.Balance();
            string ru = Section(Ru(b), PlatformGuide.ChurnId).Body;
            Assert.True(ru.Contains("основной сокет прожил 12 из 36 мес.: −6.7 %"), ru);

            float severity = ChurnDetector.Severity(true, 12, 0.10, false, b);
            RatingBreakdown rating = RatingEffectModel.Compute(new RatingInput { ChurnSeverity = severity, MonthsSinceIntro = 0, TotalIb = 0 }, b);
            Assert.Equal("6.7", PlatformNumbers.Percent(rating.Pchurn), "P_churn of the model (AC-6.1)");
        }

        [Test]
        public void TheOemNumbers_AreTheOnesOfTheModel()
        {
            PlatformBalance b = ViewTestData.Balance();
            string reason;
            float prefers = OemAdoptionModel.PlatformMultiplier(5, 5, false, 1, b, out reason);
            float lag1 = OemAdoptionModel.PlatformMultiplier(4, 5, false, 1, b, out reason);
            float lag2 = OemAdoptionModel.PlatformMultiplier(3, 5, false, 1, b, out reason);
            float full = OemAdoptionModel.PlatformMultiplier(5, 5, true, 1, b, out reason);
            float calculator = OemAdoptionModel.PlatformMultiplier(3, 5, false, 0, b, out reason);

            string oem = Section(Ru(b), PlatformGuide.OemId).Body;
            Assert.True(oem.Contains("на " + PlatformNumbers.Percent(prefers - 1f) + " %"), "the bonus of the preferred platform: " + oem);
            Assert.True(oem.Contains("на " + PlatformNumbers.Percent(1f - lag1) + " %"), "one generation behind: " + oem);
            Assert.True(oem.Contains("на " + PlatformNumbers.Percent(1f - lag2) + " %"), "two behind: " + oem);
            Assert.True(Section(Ru(b), PlatformGuide.FullId).Body.Contains("на " + PlatformNumbers.Percent(full - prefers) + " %"), "the full platform");
            Assert.Equal(1f, calculator, "and the calculator is left alone, as the text says");
            Assert.True(oem.Contains("калькуляторы не затрагиваются"), oem);
        }

        // ---- the generations ----

        [Test]
        public void TheEarlyWindow_IsTheOneMostGenerationsHave()
        {
            Assert.Equal(24, PlatformGuide.UsualEarlyWindow(ViewTestData.Catalog()));
            Assert.Equal(24, PlatformGuide.UsualEarlyWindow(null), "no generations: the usual one");
            Assert.Equal(24, PlatformGuide.UsualEarlyWindow(GenerationCatalog.Empty));
            Assert.Equal(PlatformGuide.DefaultEarlyWindowMonths, 24);

            GenerationDef a = PlatformTestData.Gen("a", 0, "PGA 1990");
            GenerationDef b = PlatformTestData.Gen("b", 1, "PGA 1995");
            GenerationDef c = PlatformTestData.Gen("c", 2, "LGA 2000");
            a.earlyWindowMonths = 12;
            b.earlyWindowMonths = 36;
            c.earlyWindowMonths = 36;
            GenerationCatalog catalog = GenerationCatalog.Build(new List<GenerationDef> { a, b, c }, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, new List<string>());
            Assert.Equal(36, PlatformGuide.UsualEarlyWindow(catalog), "the most common");

            GenerationCatalog tie = GenerationCatalog.Build(new List<GenerationDef> { a, b }, PlatformTestData.PackageExists, PlatformTestData.MemoryExists, new List<string>());
            Assert.Equal(12, PlatformGuide.UsualEarlyWindow(tie), "12 once, 36 once: the smaller");
        }

        [Test]
        public void TheFullPlatformExample_IsAGenerationWithTwoMemories()
        {
            GenerationDef example = PlatformGuide.FullPlatformExample(ViewTestData.Catalog());
            Assert.NotNull(example);
            Assert.Equal("gen-2000", example.id, "the earliest of the generations with the most memory standards");
            Assert.Equal(2, example.memories.Count);
            Assert.Null(PlatformGuide.FullPlatformExample(null));
            Assert.Null(PlatformGuide.FullPlatformExample(GenerationCatalog.Empty));

            string ru = Section(Ru(), PlatformGuide.FullId).Body;
            Assert.True(ru.Contains("Например, для поколения LGA 775 / Socket 939 это память DDR, DDR2."), ru);
            string noExample = Section(PlatformGuide.Build(ViewTestData.Balance(), GenerationCatalog.Empty, ViewTestData.Text("ru")), PlatformGuide.FullId).Body;
            Assert.False(noExample.Contains("Например"), "no generations, no example: " + noExample);
        }

        [Test]
        public void TheList_HasEveryGeneration_WithTheRealNamesOfItsSockets()
        {
            List<GuideGeneration> list = PlatformGuide.Generations(ViewTestData.Catalog(), ViewTestData.Text("ru"));
            Assert.Equal(10, list.Count);

            Assert.Equal("DIP-40", list[0].Name);
            Assert.Equal("DIP 1970, DIP 1975", list[0].Sockets);
            Assert.Equal("DIP SRAM", list[0].Memories);
            Assert.Equal(1971, list[0].AnnounceYear);
            Assert.Equal(1987, list[0].ObsoleteYear);
            Assert.Equal("DIP-40: сокеты DIP 1970, DIP 1975; память DIP SRAM; анонс 1971, устаревает с 1987", list[0].Line);

            GuideGeneration last = list[9];
            Assert.Equal("LGA 2030 A/B", last.Name);
            Assert.Equal(0, last.ObsoleteYear);
            Assert.Equal("LGA 2030 A/B: сокеты LGA 2030 A, LGA 2030 B; память DDR6; анонс 2030", last.Line, "a generation that never becomes obsolete has no such clause");

            Assert.Equal(0, PlatformGuide.Generations(null, ViewTestData.Text("ru")).Count);
            Assert.Equal(0, PlatformGuide.Generations(GenerationCatalog.Empty, null).Count);
        }

        [Test]
        public void TheList_InEnglish_UsesTheEnglishNames()
        {
            List<GuideGeneration> list = PlatformGuide.Generations(ViewTestData.Catalog(), ViewTestData.Text("en"));
            Assert.Equal("DIP-40: sockets DIP 1970, DIP 1975; memory DIP SRAM; announced 1971, obsolete from 1987", list[0].Line);
        }

        // ---- the section of the README ----

        /// <summary>The section "Поколения платформ" the way the README has it: the guide of the default balance in Russian, and the table of the generations.</summary>
        private static string RenderReadmeSection()
        {
            PlatformBalance balance = ViewTestData.Balance();
            GenerationCatalog catalog = ViewTestData.Catalog();
            ViewText text = ViewTestData.Text("ru");

            var md = new StringBuilder();
            md.Append("## Поколения платформ\n\n");
            md.Append("Фича `platform-generations` даёт платформам историю: сокеты и память собраны в поколения с анонсом, запуском и сроком жизни. Мод считает установленную базу каждой компании на каждом сокете, награждает ранний переход, наказывает слишком частую смену сокета и учитывает интерес OEM-заказчиков к новым платформам. Для игрока и для ИИ правила одинаковые.\n\n");
            md.Append("Окно «Платформы» открывается на странице фичи в окне «Моды». В нём три вкладки: «Поколения», «Установленная база» и «Мои CPU». В настройках фичи три переключателя: эффекты на рейтинг, эффекты на контракты (OEM), уведомления и письма. Когда эффекты выключены, игра не меняется, а хронология платформ ведётся дальше.\n\n");

            foreach (GuideSection section in PlatformGuide.Build(balance, catalog, text))
            {
                md.Append("### ").Append(section.Title).Append("\n\n");
                md.Append(section.Body).Append("\n\n");
            }

            md.Append("### ").Append(text.L("pg.hub.guide.gens.title")).Append("\n\n");
            md.Append("| Поколение | Сокеты игры | Память | Анонс | Устаревает |\n");
            md.Append("|---|---|---|---|---|\n");
            foreach (GuideGeneration gen in PlatformGuide.Generations(catalog, text))
            {
                md.Append("| ").Append(gen.Name).Append(" | ").Append(gen.Sockets).Append(" | ").Append(gen.Memories).Append(" | ").Append(gen.AnnounceYear)
                    .Append(" | ").Append(gen.ObsoleteYear > 0 ? gen.ObsoleteYear.ToString() : "—").Append(" |\n");
            }
            md.Append("\n");
            md.Append("Числа в тексте — значения по умолчанию из `balance.json`. Свои значения и свои поколения кладутся в папку `PTMod\\content\\platform-generations\\` рядом с игрой (см. «Свои правки контента и текстов» в README репозитория).\n");
            return md.ToString();
        }

        private static string ReadReadmeSection()
        {
            string path = Path.Combine(FeatureDirectory(), ReadmeFile);
            Assert.True(File.Exists(path), "the section of the README is missing: " + path);
            return SourceScan.ReadText(path).Replace("\r\n", "\n");
        }

        [Test]
        public void TheReadmeSection_IsTheGuideOfTheDefaultBalance_WithTheTableOfTheGenerations()
        {
            string expected = RenderReadmeSection();

            string outPath = Environment.GetEnvironmentVariable("PTMOD_README_OUT");
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, expected, new UTF8Encoding(false));

            string actual = ReadReadmeSection();
            Assert.True(actual == expected,
                "The section of the README (Features/PlatformGenerations/" + ReadmeFile + ") is not the guide of the default balance.json and generations.json. "
                + "Set PTMOD_README_OUT to a file name and run this test to get the text it expects; the line that differs first is " + FirstDifference(expected, actual));
        }

        private static string FirstDifference(string expected, string actual)
        {
            string[] a = expected.Split('\n');
            string[] b = actual.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string left = i < a.Length ? a[i] : "(end)";
                string right = i < b.Length ? b[i] : "(end)";
                if (left != right) return (i + 1) + ":\n  expected: " + left + "\n  found:    " + right;
            }
            return "none";
        }

        [Test]
        public void TheReadmeSection_HasTheExampleOfTheDesign_AndTheRealNamesOfTheSockets()
        {
            string readme = ReadReadmeSection();
            Assert.True(readme.StartsWith("## Поколения платформ\n", StringComparison.Ordinal), "the heading of the section");
            Assert.True(readme.Contains("+10 % × 30 / 40 = +7.5 %"), "the example: 30 % of the base gives +7.5 %");

            foreach (string heading in new[] { "### Установленная база", "### Ранний переход", "### Смена сокета", "### Заказчики OEM", "### Полная платформа", "### Поколения" })
            {
                Assert.True(readme.Contains(heading + "\n"), "no heading " + heading);
            }

            // The names of the sockets as they are in the game, from the shipped content.
            foreach (string name in new[] { "DIP-40", "PLCC-68", "PGA-132", "Socket 1–3", "Socket 5/7", "LGA 775 / Socket 939", "LGA 1156 / AM3", "LGA 1151 / AM4", "LGA 1700 / AM5", "LGA 2030 A/B" })
            {
                Assert.True(readme.Contains("| " + name + " |"), "the table lacks " + name);
            }
            foreach (string package in PlatformTestData.GamePackages) Assert.True(readme.Contains(package), "the table lacks the socket " + package);
            foreach (string memory in PlatformTestData.GameMemories) Assert.True(readme.Contains(memory), "the table lacks the memory " + memory);
        }

        // ---- the hub page ----

        [Test]
        public void TheHubPage_ShowsTheGuideAndTheSelfCheck_WithTheNumbersOfTheBalanceInForce()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDirectory(), "PlatformGenerationsFeature.cs")), false);
            string squeezed = Regex.Replace(feature, @"\s+", " ");
            Assert.True(squeezed.Contains("foreach (GuideSection section in PlatformGuide.Build(Balance, Catalog, text))"), "the guide of the balance in force");
            Assert.True(squeezed.Contains("foreach (GuideGeneration generation in PlatformGuide.Generations(Catalog, text)) ModUI.Text(page, generation.Line);"));
            Assert.True(squeezed.Contains("ModHooks.Guard(FeatureId, \"hub/guide\", () => AddGuide(page));"), "a failure in the text stays in the text");
            Assert.True(squeezed.Contains("ModHooks.Guard(FeatureId, \"hub/selfCheck\", () => AddSelfCheckLine(page));"));
            Assert.True(squeezed.Contains("ModUI.Text(page, section.Title, true); ModUI.Text(page, section.Body);"), "a heading and a paragraph per block");
        }

        [Test]
        public void TheEnglishGuide_HasTheSameExample_AndNoRussianLetters()
        {
            string text = string.Join("\n", En().ConvertAll(s => s.Title + "\n" + s.Body).ToArray());
            Assert.True(text.Contains("+7.5%"));
            Assert.True(text.Contains("-6.7%"));
            Assert.False(new Regex("[Ѐ-ӿ]").IsMatch(text));
        }
    }
}
