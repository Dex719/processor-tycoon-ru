using System.Linq;
using System.Collections.Generic;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-012 (FR-1): the three lines of requirements of the breakdown, with the examples AC-1.1 (IPS 0.80 against 1.20 MIPS, tolerance 0.75), AC-1.2 (70 W against 60 W) and AC-1.3 (a PGA CPU for a
    /// DIP/PLCC contract), and the note when the mod and the game disagree (FR-12 item 1, AC-12.1: tolerance 0.7).
    /// </summary>
    [Covers(typeof(RequirementLines), typeof(RequirementPanelInput))]
    public class RequirementLinesTests
    {
        private static RequirementPanelInput Input(float cpuIps, float minIps, float tolerance, float cpuPower, float maxPower, int packageType, bool dip, bool plcc, bool pga)
        {
            var input = new RequirementPanelInput
            {
                Ips = RequirementCheck.Ips(cpuIps, minIps, tolerance),
                Power = RequirementCheck.Power(cpuPower, maxPower),
                Package = RequirementCheck.Package(packageType, dip, plcc, pga),
                Tolerance = tolerance,
                RequiredPackageText = packageType == 1 ? "DIP/PLCC" : packageType == 2 ? "PGA/LGA" : "any",
                CpuPackageText = pga ? "PGA" : dip ? "DIP" : plcc ? "PLCC" : "BGA",
            };
            input.FailsByGame = !(input.Ips.Passed && input.Power.Passed && input.Package.Passed);
            return input;
        }

        private static List<string> Keys(List<PanelLine> lines)
        {
            return lines.Select(l => l.Key).ToList();
        }

        [Test]
        public void Ac11_TheIpsLineIsRed_WithTheShortfallInMipsAndPercent_AndThePowerAndPackageAreGreen()
        {
            RequirementPanelInput input = Input(0.8f, 1.2f, 0.75f, 45f, 60f, 2, false, false, true);
            List<PanelLine> lines = RequirementLines.Build(input);

            Assert.Equal(new[] { "ct.req.ips", "ct.req.ips.fail", "ct.req.power", "ct.req.package", "ct.req.rejected" }, Keys(lines).ToArray());

            PanelLine ips = lines[0];
            Assert.Equal(LineTone.Bad, ips.Tone);
            Assert.Near(0.8, ips.Args[0].Number, 1e-6);
            Assert.Near(0.9, ips.Args[1].Number, 1e-6, "the lowest the contract takes: 75 % of 1.20");
            Assert.Near(75.0, ips.Args[2].Number, 1e-6);
            Assert.Near(1.2, ips.Args[3].Number, 1e-6);

            PanelLine detail = lines[1];
            Assert.Equal(LineTone.Bad, detail.Tone);
            Assert.Equal(1, detail.Indent);
            Assert.Near(0.1, detail.Args[0].Number, 1e-6, "short by 0.10 MIPS");
            Assert.Near(11.1, detail.Args[1].Number, 0.05, "11 % of the lowest accepted value");

            Assert.Equal(LineTone.Good, lines[2].Tone);
            Assert.Equal(LineTone.Good, lines[3].Tone);
            Assert.Equal(LineTone.Bad, lines[4].Tone, "the offer will be turned down silently, the chance is 0 %");
            Assert.True(lines[4].Bold);
        }

        [Test]
        public void Ac12_APowerOfSeventyForSixtyWatts_IsRed_WithTheExcessInWatts()
        {
            RequirementPanelInput input = Input(2.0f, 1.2f, 0.75f, 70f, 60f, 2, false, false, true);
            List<PanelLine> lines = RequirementLines.Build(input);

            Assert.Equal(new[] { "ct.req.ips", "ct.req.power", "ct.req.power.fail", "ct.req.package", "ct.req.rejected" }, Keys(lines).ToArray());
            Assert.Equal(LineTone.Good, lines[0].Tone);
            Assert.Equal(LineTone.Bad, lines[1].Tone);
            Assert.Equal(70.0, lines[1].Args[0].Number);
            Assert.Equal(60.0, lines[1].Args[1].Number);
            Assert.Equal(10.0, lines[2].Args[0].Number, "over by 10 W");
        }

        [Test]
        public void Ac13_APgaCpuForADipContract_NamesBothPackages()
        {
            RequirementPanelInput input = Input(2.0f, 1.2f, 0.75f, 45f, 60f, 1, false, false, true);
            List<PanelLine> lines = RequirementLines.Build(input);

            Assert.Equal(new[] { "ct.req.ips", "ct.req.power", "ct.req.package", "ct.req.package.fail", "ct.req.rejected" }, Keys(lines).ToArray());
            Assert.Equal(LineTone.Bad, lines[2].Tone);
            PanelLine detail = lines[3];
            Assert.Equal("DIP/PLCC", detail.Args[0].Text, "what the contract asks for");
            Assert.Equal("PGA", detail.Args[1].Text, "what the CPU has");
        }

        [Test]
        public void ACpuThatPassesEverything_GetsThreeGreenLinesAndTheVerdictOk()
        {
            List<PanelLine> lines = RequirementLines.Build(Input(2.0f, 1.2f, 0.75f, 45f, 60f, 0, false, false, true));

            Assert.Equal(new[] { "ct.req.ips", "ct.req.power", "ct.req.package", "ct.req.allOk" }, Keys(lines).ToArray());
            Assert.True(lines.All(l => l.Tone == LineTone.Good));
        }

        [Test]
        public void TheToleranceOfTheContract_IsShownAsRead_AndTheBorderPasses()
        {
            // AC-12.1: another mod changed the tolerance to 0.7: the limit is 0.7 x MinIps and the line says 70 %.
            float lowestAccepted = (float)(1.2f * 0.7f);
            RequirementPanelInput input = Input(lowestAccepted, 1.2f, 0.7f, 45f, 60f, 0, false, false, true);
            List<PanelLine> lines = RequirementLines.Build(input);

            Assert.Near(70.0, lines[0].Args[2].Number, 1e-6);
            Assert.Near(0.84, lines[0].Args[1].Number, 1e-5);
            Assert.Equal(LineTone.Good, lines[0].Tone, "IPS equal to the lowest accepted passes");
        }

        [Test]
        public void TheVerdictIsTheGames_AndADisagreementIsMarked()
        {
            RequirementPanelInput input = Input(2.0f, 1.2f, 0.75f, 45f, 60f, 0, false, false, true);
            input.FailsByGame = true;        // the mod says it passes, the game says it fails
            input.MismatchWithGame = true;

            List<PanelLine> lines = RequirementLines.Build(input);

            Assert.Equal("ct.req.rejected", lines[lines.Count - 2].Key, "the verdict of the game is shown");
            Assert.Equal("ct.req.mismatch", lines[lines.Count - 1].Key);
            Assert.Equal(LineTone.Warn, lines[lines.Count - 1].Tone);
        }
    }
}
