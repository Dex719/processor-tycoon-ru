using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.FirstYearOnboarding;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The API of the tutorial for the other features (TSK-014, FR-14), as far as it can be run without the game: the rules of the pending mode of the next
    /// new game (<c>PendingModeBook</c>, which <c>OnboardingService</c> delegates to), the final decision of a new game, and the facts about the service
    /// that are read from its source (events through <c>ModHooks.Raise</c>, the neutral answers while the feature is off, the twin enums). The service
    /// itself needs the core and is not part of the test build.
    /// <para>
    /// <c>PendingMode</c> and <c>PendingModeBook</c> are the real files of <c>Api/Dto</c>: the test builds compile that folder together with <c>Logic/**</c>.
    /// </para>
    /// </summary>
    [Covers(typeof(PendingMode), typeof(PendingModeBook))]
    public class OnboardingServiceTests
    {
        private static string ServiceCode()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding", "Api", "OnboardingService.cs");
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "no " + path);
            return SourceScan.Strip(text, false);
        }

        // ---- the pending mode ----

        [Test]
        public void ANewBook_HasNoRequest()
        {
            var book = new PendingModeBook();

            Assert.Equal(PendingMode.Allowed, book.Mode);
            Assert.Equal(string.Empty, book.Source);
        }

        [Test]
        public void Set_RemembersTheModeAndWhoAsked_AndSaysItChanged()
        {
            var book = new PendingModeBook();

            Assert.True(book.Set(PendingMode.ForcedOn, "scenario:garage-1975"));

            Assert.Equal(PendingMode.ForcedOn, book.Mode);
            Assert.Equal("scenario:garage-1975", book.Source);
        }

        [Test]
        public void Set_TheSameRequestAgain_IsNotAChange()
        {
            var book = new PendingModeBook();
            book.Set(PendingMode.ForcedOff, "scenario:a");

            Assert.False(book.Set(PendingMode.ForcedOff, "scenario:a"), "no event for the same request");
            Assert.True(book.Set(PendingMode.ForcedOff, "scenario:b"), "another source is a change");
            Assert.True(book.Set(PendingMode.ForcedOn, "scenario:b"), "another mode is a change");
        }

        [Test]
        public void Set_ANullSource_IsTheEmptyOne()
        {
            var book = new PendingModeBook();

            Assert.True(book.Set(PendingMode.ForcedOn, null));
            Assert.Equal(string.Empty, book.Source);
            Assert.False(book.Set(PendingMode.ForcedOn, string.Empty), "null and empty are the same source");
        }

        [Test]
        public void Set_ANumberThatIsNoMode_ChangesNothing()
        {
            var book = new PendingModeBook();
            book.Set(PendingMode.ForcedOn, "scenario:a");

            Assert.False(book.Set((PendingMode)42, "scenario:b"));

            Assert.Equal(PendingMode.ForcedOn, book.Mode);
            Assert.Equal("scenario:a", book.Source);
            Assert.False(PendingModeBook.IsValid((PendingMode)42));
            Assert.True(PendingModeBook.IsValid(PendingMode.Allowed));
        }

        [Test]
        public void Set_Allowed_HandsTheChoiceBack()
        {
            var book = new PendingModeBook();
            book.Set(PendingMode.ForcedOn, "scenario:a");

            Assert.True(book.Set(PendingMode.Allowed, "scenario:a"));

            Assert.Equal(PendingMode.Allowed, book.Mode);
        }

        [Test]
        public void Take_GivesTheRequestOnce_AndFallsBackToAllowed()
        {
            var book = new PendingModeBook();
            book.Set(PendingMode.ForcedOn, "scenario:garage-1975");

            string source;
            bool changed;
            PendingMode mode = book.Take(out source, out changed);

            Assert.Equal(PendingMode.ForcedOn, mode);
            Assert.Equal("scenario:garage-1975", source);
            Assert.True(changed, "FR-14 item 1: the event is raised for the fall back");
            Assert.Equal(PendingMode.Allowed, book.Mode, "the nearest new game only");
            Assert.Equal(string.Empty, book.Source);

            PendingMode again = book.Take(out source, out changed);
            Assert.Equal(PendingMode.Allowed, again);
            Assert.Equal(string.Empty, source);
            Assert.False(changed, "nothing to announce the second time");
        }

        [Test]
        public void Reset_IsAChangeOnlyWhenThereWasARequest()
        {
            var book = new PendingModeBook();
            Assert.False(book.Reset());

            book.Set(PendingMode.ForcedOff, "scenario:a");
            Assert.True(book.Reset());
            Assert.False(book.Reset());

            book.Set(PendingMode.Allowed, "scenario:a");
            Assert.True(book.Reset(), "a source alone is a request to forget");
            Assert.Equal(string.Empty, book.Source);
        }

        // ---- the final mode of a new game ----

        [Test]
        public void AForcedMode_WinsOverTheToggle()
        {
            foreach (bool hasToggle in new[] { false, true })
            {
                foreach (bool toggleOn in new[] { false, true })
                {
                    Assert.True(PendingModeBook.ResolveTutorial(PendingMode.ForcedOn, hasToggle, toggleOn), "ForcedOn, toggle " + hasToggle + "/" + toggleOn);
                    Assert.False(PendingModeBook.ResolveTutorial(PendingMode.ForcedOff, hasToggle, toggleOn), "ForcedOff, toggle " + hasToggle + "/" + toggleOn);
                }
            }
        }

        [Test]
        public void AllowedMode_FollowsTheToggle_AndNoToggleMeansNoTutorial()
        {
            Assert.True(PendingModeBook.ResolveTutorial(PendingMode.Allowed, true, true), "AC-1.1: the toggle is on");
            Assert.False(PendingModeBook.ResolveTutorial(PendingMode.Allowed, true, false), "AC-1.2: the toggle is off");
            Assert.False(PendingModeBook.ResolveTutorial(PendingMode.Allowed, false, true), "FR-2 item 5: no recorded choice, no tutorial");
            Assert.False(PendingModeBook.ResolveTutorial(PendingMode.Allowed, false, false));
        }

        [Test]
        public void ForcedOn_StartsTheTutorialEvenWhenTheStartWasNotThroughTheFeaturesToggle()
        {
            Assert.True(PendingModeBook.ResolveTutorial(PendingMode.ForcedOn, false, false), "AC-14.1: a scenario starts it without a toggle");
        }

        // ---- the copies of the files ----

        // ---- facts about the service ----

        [Test]
        public void TheEvents_AreRaisedThroughModHooksRaise_SoASubscriberCannotBreakTheOthers()
        {
            string code = ServiceCode();

            Assert.True(Regex.IsMatch(code, @"ModHooks\s*\.\s*Raise\s*\(\s*CompletedEvent\s*,\s*Completed\s*,\s*result\s*\)"), "Completed goes through ModHooks.Raise (FR-14 item 3)");
            Assert.True(Regex.Matches(code, @"ModHooks\s*\.\s*Raise\s*\(\s*PendingModeChangedEvent\s*,\s*PendingModeChanged\s*\)").Count >= 2,
                "PendingModeChanged goes through ModHooks.Raise from SetPendingMode and from the reset");
            Assert.False(Regex.IsMatch(code, @"\bCompleted\s*\?\s*\.\s*Invoke|\bCompleted\s*\(\s*result|\bPendingModeChanged\s*\?\s*\.\s*Invoke|\bPendingModeChanged\s*\(\s*\)"),
                "the events are never invoked directly");
        }

        [Test]
        public void TheEventIds_FollowThePrefixOfTheFeature()
        {
            string code = ServiceCode();

            Assert.True(code.Contains("\"onb.completed\""));
            Assert.True(code.Contains("\"onb.pendingModeChanged\""));
        }

        [Test]
        public void EveryCommandAndQuery_GoesThroughTheGuard_AndIsNeutralWhileTheFeatureIsOff()
        {
            string code = ServiceCode();

            Assert.True(code.Contains("IsAvailable"), "the service knows whether the feature is active");
            Assert.True(Regex.IsMatch(code, @"ActiveBackend\s*\(\s*\)\s*\{[^}]*if\s*\(\s*!IsAvailable\s*\)\s*return\s+null"), "no backend while the feature is off: the null object answers");
            Assert.True(Regex.IsMatch(code, @"GetStatus\s*\(\s*\)[^}]*OnboardingStatus\.Off"), "GetStatus is Off while the feature is off");
            Assert.True(Regex.IsMatch(code, @"ModHooks\s*\.\s*Guard\s*\(\s*FeatureId\s*,\s*SitePrefix\s*\+\s*command"), "the commands run in ModHooks.Guard");
            Assert.True(Regex.IsMatch(code, @"ModHooks\s*\.\s*Guard\s*\(\s*FeatureId\s*,\s*SitePrefix\s*\+\s*""GetStatus"""), "GetStatus runs in ModHooks.Guard");
            Assert.True(Regex.IsMatch(code, @"IsAvailable\s*\?\s*Pending\.Mode\s*:\s*PendingMode\.Allowed"), "GetPendingMode is Allowed while the feature is off");
        }

        [Test]
        public void TheApiHasTheFiveCommandsAndTheQueriesOfTheDesign()
        {
            string code = ServiceCode();

            foreach (string signature in new[]
            {
                "static void SetPendingMode(PendingMode mode, string source)",
                "static PendingMode GetPendingMode()",
                "static OnboardingStatus GetStatus()",
                "static void SkipAll(string reason)",
                "static void SkipCurrentStep()",
                "static void ResendCurrent()",
                "static void Restart()",
                "static event Action PendingModeChanged",
                "static event Action<OnboardingResult> Completed"
            })
            {
                Assert.True(Regex.IsMatch(code, @"public\s+" + Regex.Escape(signature).Replace("\\ ", @"\s+")), "design.md, OnboardingService: public " + signature);
            }
        }

        [Test]
        public void TheApi_DoesNotReachTheLogicOfTheFeature_SoADependentFeatureBuildsWithTheApiFolderOnly()
        {
            string api = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding", "Api");
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(api, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"\bFirstYearOnboarding\s*\.\s*Logic\b|\bnamespace\s+PTMod\.Features\.FirstYearOnboarding\.Logic\b"))
                    offenders.Add(SourceScan.Relative(api, path));
            }

            Assert.True(offenders.Count == 0, "build_feature.py gives a dependent feature the Api folder only; these files use the Logic namespace: " + string.Join(", ", offenders));
        }

        // ---- the twin types ----

        [Test]
        public void TheTwinEnums_OfTheMachineAndOfTheApi_HaveTheSameNamesAndNumbers()
        {
            string dto = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding", "Api", "Dto", "OnboardingMode.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(dto), false);
            Assert.True(code.Length > 0, "no " + dto);

            var api = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(code, @"\b([A-Z][A-Za-z]*)\s*=\s*(\d+)"))
            {
                api[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
            }

            var logic = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (PTMod.Features.FirstYearOnboarding.Logic.OnboardingMode mode in Enum.GetValues(typeof(PTMod.Features.FirstYearOnboarding.Logic.OnboardingMode)))
            {
                logic[mode.ToString()] = (int)mode;
            }

            Assert.Equal(logic.OrderBy(p => p.Value).Select(p => p.Key + "=" + p.Value).ToArray(), api.OrderBy(p => p.Value).Select(p => p.Key + "=" + p.Value).ToArray(),
                "OnboardingApiBridge.ToApi copies the mode by its number; the enums must stay twins");
        }

        [Test]
        public void TheTwinResults_HaveTheSameFields()
        {
            string dto = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding", "Api", "Dto", "OnboardingResult.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(dto), false);
            Assert.True(code.Length > 0, "no " + dto);

            var api = new List<string>();
            foreach (Match match in Regex.Matches(code, @"public\s+(?:string|int|bool)\s+([A-Z][A-Za-z]*)\s*;"))
            {
                api.Add(match.Groups[1].Value);
            }

            string[] logic = typeof(PTMod.Features.FirstYearOnboarding.Logic.OnboardingResult).GetFields().Select(f => f.Name).ToArray();

            Assert.Equal(logic.OrderBy(n => n).ToArray(), api.OrderBy(n => n).ToArray(), "OnboardingApiBridge copies the result field by field");
        }
    }

    /// <summary>
    /// <c>Api/Dto</c> is compiled by the test builds together with <c>Logic</c>: its files must stay pure (no core, Unity, game or Harmony types).
    /// </summary>
    public class ApiDtoPurityTests
    {
        [Test]
        public void TheDtoFiles_AreNotUsingTheCoreOrTheGame()
        {
            string dir = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding", "Api", "Dto");
            string[] files = SourceScan.Files(dir, ".cs").ToArray();
            Assert.True(files.Length >= 5, "the scan found the files of Api/Dto: " + files.Length);
            foreach (string file in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                Assert.False(Regex.IsMatch(code, @"\busing\s+(?:PTMod\.Core|UnityEngine|ProcessorTycoon|HarmonyLib)\b"), Path.GetFileName(file) + " must stay a pure file, the tests compile it");
            }
        }
    }
}
