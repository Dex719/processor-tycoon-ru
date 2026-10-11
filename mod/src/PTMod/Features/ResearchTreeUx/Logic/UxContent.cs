using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Everything the feature takes from <c>Content/research-tree-ux/ui.json</c>, read in one place (design: "Контент", decision 0006, NFR-2): the
    /// tabs, the numbers of the view part, the compare table, the reminders and the range of the side panel. The core hands over the merged object (the built-in file with the
    /// player's override laid over it); here each section is read by its own loader, which checks every value.
    /// <list type="bullet">
    /// <item>A <b>broken record</b> (a tab without an id, a number out of range, a row with an unknown direction) is skipped or keeps its default, and a
    /// line goes to <see cref="Problems"/>; the rest of the file stands.</item>
    /// <item>A <b>section of the wrong shape</b> gives the defaults of that section, with a line; the other sections stand.</item>
    /// <item>A file that cannot be used at all (<c>null</c>, empty, or none of the sections usable) gives <see cref="Defaults"/>, the same values the
    /// built-in file holds, so the feature runs exactly as shipped.</item>
    /// <item>A loader that throws (it should not) costs only its own section.</item>
    /// <item>A member this version does not know is named in <see cref="Problems"/> and ignored: a typo in an override would otherwise change nothing
    /// without a word.</item>
    /// </list>
    /// </summary>
    public sealed class UxContent
    {
        /// <summary>The top-level members of the file this version knows.</summary>
        public static readonly string[] KnownMembers =
        {
            "schema", "tabs", "timeRescale", "tooltipFundings", "hoverSeenSeconds", "notifications", "sidebar", "compare",
        };

        private readonly List<string> _problems = new List<string>();

        private UxContent()
        {
        }

        /// <summary>The built-in tabs, in the order of the strip. Never null, never empty, always holds the tab "all".</summary>
        public IReadOnlyList<TabDefinition> Tabs { get; private set; }

        /// <summary>The numbers of the tooltip and of the "seen" timer. Never null.</summary>
        public UiTuning Tuning { get; private set; }

        /// <summary>The directions, the positions and the highlight threshold of the compare table. Never null.</summary>
        public CompareConfig Compare { get; private set; }

        /// <summary>The numbers of the reminders. Never null.</summary>
        public NotifyConfig Notify { get; private set; }

        /// <summary>The range of the width factor of the side panel (FR-10). Never null.</summary>
        public SidebarConfig Sidebar { get; private set; }

        /// <summary>One line per record or value that was skipped or replaced; empty when the file was read without a word to say.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return _problems; }
        }

        /// <summary>The values the built-in file holds, compiled in: what the feature runs on when the file is gone or unusable.</summary>
        public static UxContent Defaults()
        {
            return new UxContent
            {
                Tabs = TabCatalog.Defaults(),
                Tuning = UiTuning.Defaults(),
                Compare = CompareConfig.Defaults(),
                Notify = NotifyConfig.Defaults(),
                Sidebar = SidebarConfig.Defaults(),
            };
        }

        /// <summary>Reads the merged <c>ui.json</c>. A null object gives <see cref="Defaults"/>. Never throws.</summary>
        public static UxContent FromConfig(JsonObject config)
        {
            UxContent content = Defaults();
            if (config == null) return content;

            var problems = content._problems;
            foreach (KeyValuePair<string, JsonNode> member in config)
            {
                if (Array.IndexOf(KnownMembers, member.Key) < 0) problems.Add("'" + member.Key + "': not a member this version of the feature knows, it is ignored");
            }

            content.Tabs = Section("tabs", problems, content.Tabs, p => TabCatalog.FromConfig(config, p));
            content.Tuning = Section("view numbers", problems, content.Tuning, p => UiTuning.FromConfig(config, p));
            content.Compare = Section("compare", problems, content.Compare, p => CompareConfig.FromConfig(config, p));
            content.Notify = Section("notifications", problems, content.Notify, p => NotifyConfig.FromConfig(config, p));
            content.Sidebar = Section("sidebar", problems, content.Sidebar, p => SidebarConfig.FromConfig(config, p));
            return content;
        }

        // One section: its loader's lines go to the shared list; a loader that throws leaves the defaults and says so. Internal for the tests.
        internal static T Section<T>(string name, List<string> problems, T fallback, Func<List<string>, T> read) where T : class
        {
            try
            {
                T value = read(problems);
                return value ?? fallback;
            }
            catch (Exception e)
            {
                problems.Add(name + ": the section could not be read (" + e.GetType().Name + "), the defaults are used");
                return fallback;
            }
        }
    }
}
