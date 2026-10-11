using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>How bad a finding of the self-check is: an error is a part of the feature that does not work, a warning is a part that works less well or whose absence was planned for.</summary>
    public enum SelfCheckLevel
    {
        Warning = 0,
        Error = 1,
    }

    /// <summary>What kind of member of the game a <see cref="GameMemberSpec"/> names.</summary>
    public enum ProbeKind
    {
        /// <summary>A field (the private fields of the windows that the feature reads through reflection).</summary>
        Field,

        /// <summary>A method with the given parameter types.</summary>
        Method,

        /// <summary>An event.</summary>
        Event,
    }

    /// <summary>
    /// A member of the game that the feature reaches by name (a private field, a method, an event) and the self-check looks for (TSK-029). The game side finds the type by its full name
    /// and the member in it, and answers with the first thing that is wrong or nothing; the rules turn the answer into a finding of <see cref="Level"/>.
    /// </summary>
    public sealed class GameMemberSpec
    {
        /// <summary>Full name of the class as reflection spells it (<c>ProcessorTycoon.Menu.MenuGameSetup</c>).</summary>
        public string TypeName;

        public ProbeKind Kind;

        /// <summary>Name of the field, the method or the event.</summary>
        public string Name;

        /// <summary>Full name of the type the member must have (a field), or null when only its existence matters.</summary>
        public string ValueType;

        /// <summary>Full names of the parameters of a method; null or empty for none.</summary>
        public string[] ParameterTypes;

        /// <summary>What a member that is missing is: an error when the feature loses a function, a warning when it only loses a convenience.</summary>
        public SelfCheckLevel Level;

        /// <summary>English: what stops working without the member, for the line of the log.</summary>
        public string Consequence;

        /// <summary><c>MenuGameSetup.companyTypeDropdown</c>: the short name of the class and the member.</summary>
        public string Display
        {
            get
            {
                string type = TypeName ?? string.Empty;
                int dot = type.LastIndexOf('.');
                int plus = type.LastIndexOf('+');
                int cut = dot > plus ? dot : plus;
                return (cut >= 0 ? type.Substring(cut + 1) : type) + "." + Name;
            }
        }
    }

    /// <summary>What the self-check found when it looked for one <see cref="GameMemberSpec"/>.</summary>
    public sealed class MemberFact
    {
        public GameMemberSpec Spec;

        /// <summary>English: what is wrong with the member (the class is not in the game, no such field, the field is of another type); null when it is as expected.</summary>
        public string Problem;
    }

    /// <summary>One patch of the feature as the registry of the core reports it.</summary>
    public sealed class PatchFact
    {
        /// <summary><c>Class.Method</c> of the target, as the registry names it.</summary>
        public string Target;

        /// <summary>The patch is in the game.</summary>
        public bool Applied;

        /// <summary>The feature starts without it when it cannot be applied.</summary>
        public bool Optional;

        /// <summary>The status the registry gives (<c>Applied</c>, <c>Failed</c>, <c>Rejected</c> ...), for the line of the log.</summary>
        public string Status;

        /// <summary>English: why the patch failed or was rejected, as the registry says; may be null.</summary>
        public string Detail;
    }

    /// <summary>How a content file loaded: how many records the catalog holds and how many were turned away.</summary>
    public sealed class ContentFileFact
    {
        /// <summary>File name with the extension (<c>scenarios.json</c>).</summary>
        public string File;

        /// <summary>Records the catalog holds from the file.</summary>
        public int Loaded;

        /// <summary>Records that were rejected, from the built-in file or from the player's override.</summary>
        public int Skipped;

        /// <summary>The player has an override file for it: a rejected record may be the player's own.</summary>
        public bool OverrideFound;

        /// <summary>The first reason a record was rejected (English), or null.</summary>
        public string FirstError;
    }

    /// <summary>A modifier channel the loaded content writes to, and whether the registry of the core declares it.</summary>
    public sealed class ChannelFact
    {
        public string Channel;

        /// <summary>Who writes to it (<c>challenge 'tight-credit'</c>), for the line of the log.</summary>
        public string UsedBy;

        public bool Declared;
    }

    /// <summary>
    /// Everything the self-check at the start looked at, as plain data (TSK-029). The game side (<c>ScenariosSelfCheck</c>) fills it from the registry of the core, reflection, the catalog and the
    /// string tables; <see cref="ScenariosSelfCheckRules.Evaluate"/> judges it. A part that could not be looked at is in <see cref="NotLooked"/> and not guessed.
    /// </summary>
    public sealed class SelfCheckFacts
    {
        /// <summary>The patches this feature declared, as <c>ModHooks.Report</c> lists them.</summary>
        public readonly List<PatchFact> Patches = new List<PatchFact>();

        /// <summary>The members of the game the feature reaches by name, with what was found.</summary>
        public readonly List<MemberFact> Members = new List<MemberFact>();

        /// <summary>The catalog was loaded (an empty catalog after a failed load is not the same as one that has nothing).</summary>
        public bool ContentLoaded;

        public int Scenarios, Challenges, Achievements;

        /// <summary>One entry per content file with records (<c>scenarios.json</c>, <c>challenges.json</c>, <c>achievements.json</c>).</summary>
        public readonly List<ContentFileFact> ContentFiles = new List<ContentFileFact>();

        /// <summary>The modifier channels the records of the catalog write to.</summary>
        public readonly List<ChannelFact> Channels = new List<ChannelFact>();

        /// <summary>The keys the self-check expects (<see cref="ScenarioTextKeys.Required"/>).</summary>
        public readonly List<string> RequiredKeys = new List<string>();

        /// <summary>The keys of the feature (<c>scn.</c>, <c>chl.</c>, <c>ach.</c>) that the Russian table has.</summary>
        public readonly HashSet<string> RussianKeys = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>The keys of the feature that the English table has.</summary>
        public readonly HashSet<string> EnglishKeys = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>Parts the check could not look at (<c>strings: ...</c>), one line each; each is a warning.</summary>
        public readonly List<string> NotLooked = new List<string>();
    }

    /// <summary>One finding of the self-check: which part, how bad, and what is wrong (English; for <c>Player.log</c>).</summary>
    public sealed class SelfCheckFinding
    {
        public SelfCheckLevel Level;

        /// <summary>The part of the feature (<c>patches</c>, <c>game</c>, <c>content</c>, <c>channels</c>, <c>strings</c>).</summary>
        public string Area;

        public string Message;

        public override string ToString()
        {
            return Area + ": " + Message;
        }
    }

    /// <summary>
    /// The result of the self-check: the findings, errors first. <see cref="Summary"/> is the line the log opens with (<c>self-check: OK</c> when there is nothing to report); the findings follow, one
    /// line each. A finding changes nothing in the feature: every missing part already has its way out (a lock that is skipped, a patch that is not applied, a text that falls back to English).
    /// </summary>
    public sealed class SelfCheckVerdict
    {
        private readonly List<SelfCheckFinding> _findings;

        public SelfCheckVerdict(IEnumerable<SelfCheckFinding> findings, string counts)
        {
            _findings = new List<SelfCheckFinding>(findings ?? new SelfCheckFinding[0]);
            Counts = counts ?? string.Empty;
            foreach (SelfCheckFinding finding in _findings)
            {
                if (finding.Level == SelfCheckLevel.Error) Errors++;
                else Warnings++;
            }
        }

        /// <summary>The findings, errors first, each group in the order they were found.</summary>
        public IList<SelfCheckFinding> Findings
        {
            get { return _findings; }
        }

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        /// <summary>Nothing to report.</summary>
        public bool IsOk
        {
            get { return _findings.Count == 0; }
        }

        /// <summary>What was looked at (<c>8 scenario(s), 8 challenge(s) ...</c>), for a line of debug level.</summary>
        public string Counts { get; private set; }

        /// <summary><c>self-check: OK</c>, or <c>self-check: 2 error(s), 1 warning(s)</c>.</summary>
        public string Summary
        {
            get
            {
                if (IsOk) return "self-check: OK";
                return "self-check: " + Errors + " error(s), " + Warnings + " warning(s)";
            }
        }
    }
}
