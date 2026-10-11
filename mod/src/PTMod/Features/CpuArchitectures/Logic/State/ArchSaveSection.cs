using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The section <c>cpu-architectures</c> of the side save file as the core's logic sees it (<see cref="ISideFileParticipant"/>): reads the
    /// section into the registry and writes the registry out (NFR-3). The runtime holder <c>ArchSaveParticipant</c> (<c>IModSaveParticipant</c>) wraps
    /// it, so that the section is under the unit tests without the game.
    /// <list type="bullet">
    /// <item>The DTO has no version field: the core writes <c>_schema</c> (the participant reports <see cref="SchemaVersion"/> = 1) and reads it back.</item>
    /// <item><see cref="ResetToDefaults"/> (a new game, the menu, a save without the section or with a newer one) empties the registry, which then
    /// reports <see cref="ArchRegistry.MigrationNeeded"/>.</item>
    /// <item><see cref="Read"/> never throws for bad data: unreadable members and records are dropped and every note goes to the warning callback.</item>
    /// </list>
    /// </summary>
    public sealed class ArchSaveSection : ISideFileParticipant
    {
        /// <summary>The section id: the id of the feature.</summary>
        public const string Section = "cpu-architectures";

        /// <summary>The version of the section. Stage 2 reads the same version: its fields are in the DTO from stage 1.</summary>
        public const int Version = 1;

        private readonly ArchRegistry _registry;
        private readonly Action<string> _warn;

        /// <param name="registry">The state the section fills and writes.</param>
        /// <param name="warn">Receives one English line per problem met while reading; null to ignore them.</param>
        public ArchSaveSection(ArchRegistry registry, Action<string> warn = null)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            _registry = registry;
            _warn = warn;
        }

        public string SectionId
        {
            get { return Section; }
        }

        public int SchemaVersion
        {
            get { return Version; }
        }

        public ArchRegistry Registry
        {
            get { return _registry; }
        }

        public void ResetToDefaults()
        {
            _registry.Clear();
        }

        public void Read(JsonObject section, int schema)
        {
            if (section == null)
            {
                _registry.Clear();
                return;
            }

            var problems = new List<string>();
            ArchSaveData data = ModJson.Bind<ArchSaveData>(section, problems);
            foreach (string problem in problems) Warn("section" + (problem.StartsWith("$", StringComparison.Ordinal) ? problem.Substring(1) : ": " + problem));

            var warnings = new List<string>();
            _registry.Load(data, warnings);
            foreach (string warning in warnings) Warn(warning);
        }

        public JsonObject Write()
        {
            var problems = new List<string>();
            JsonNode node = ModJson.From(_registry.ToSave(), problems);
            foreach (string problem in problems) Warn("write: " + problem);
            return node as JsonObject;
        }

        private void Warn(string line)
        {
            if (_warn == null) return;
            try
            {
                _warn(line);
            }
            catch (Exception)
            {
                // A logging callback must not break the save.
            }
        }
    }
}
