using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The OEM adoption curve of a generation (FR-8, formula F7): after <see cref="delayMonths"/> from the launch adoption grows linearly over
    /// <see cref="rampMonths"/> up to <see cref="maxAdoption"/>, and <see cref="ibAccel"/> adds the generation's share of the installed base.
    /// Fields are public and lower-case on purpose: they mirror the JSON of <c>generations.json</c> (design.md, "Контент").
    /// </summary>
    [Serializable]
    public sealed class OemCurveDef
    {
        public int delayMonths;
        public int rampMonths;
        public float maxAdoption;
        public float ibAccel;
    }

    /// <summary>A pair of base package names that count as compatible sockets (FR-6 item 3), no matter which generation lists them.</summary>
    [Serializable]
    public sealed class SocketPair
    {
        public string a;
        public string b;
    }

    /// <summary>
    /// One generation of platforms (FR-1): the record of <c>generations.json</c>. <see cref="sockets"/> are names of the game's <c>Package</c>
    /// (the base package of a CPU's socket, standard or custom), <see cref="memories"/> names of its <c>Memory</c> standards. The technical
    /// <see cref="id"/> never changes; <see cref="name"/> is the real socket name that the player sees (owner decision D13).
    /// Fields mirror the JSON, so they are public and lower-case.
    /// </summary>
    [Serializable]
    public sealed class GenerationDef : IContentRecord
    {
        /// <summary>The members a record may have; <c>disabled</c> is the framework's own switch for an override.</summary>
        private static readonly string[] KnownMembers =
        {
            "id", "order", "name", "description", "announceYear", "obsoleteYear", "sockets", "memories", "earlyWindowMonths", "oem",
            "compatibleSockets", "disabled",
        };

        public string id;

        /// <summary>Position among the generations, oldest first; unique.</summary>
        public int order;

        public LocText name;
        public LocText description;

        /// <summary>The generation is announced on 1 January of this year.</summary>
        public int announceYear;

        /// <summary>The generation is obsolete from 1 January of this year; 0 = it never becomes obsolete.</summary>
        public int obsoleteYear;

        /// <summary>Names of the base packages that belong to the generation.</summary>
        public List<string> sockets = new List<string>();

        /// <summary>Names of the memory standards of the generation (a CPU with one of them is a "full platform", D11).</summary>
        public List<string> memories = new List<string>();

        /// <summary>How many months after the launch the first companies count as early adopters.</summary>
        public int earlyWindowMonths;

        public OemCurveDef oem;

        /// <summary>Pairs of base package names that are compatible although they differ; may be empty.</summary>
        public List<SocketPair> compatibleSockets = new List<SocketPair>();

        /// <summary>The technical id (<see cref="IContentRecord"/>): the same as <see cref="id"/>.</summary>
        public string Id
        {
            get { return id; }
        }

        /// <summary>
        /// Builds a record from its JSON object (the mapper that <c>ModContent.LoadRecords</c> wants). Checks the shape only: types, required
        /// members, unknown members. The value ranges and the existence of packages are <see cref="GenerationCatalog.Validate"/>'s business.
        /// A record with a wrong shape gives null and the reasons in <paramref name="errors"/>; a record that is fine gets only warnings
        /// there (unknown members, which are ignored).
        /// </summary>
        public static GenerationDef FromJson(JsonObject json, List<string> errors)
        {
            if (json == null) return null;

            var problems = new List<string>();
            var def = new GenerationDef();

            JsonFields.String(json, "id", null, true, out def.id, problems);
            JsonFields.Int(json, "order", null, true, out def.order, problems);
            JsonFields.Int(json, "announceYear", null, true, out def.announceYear, problems);
            JsonFields.Int(json, "obsoleteYear", null, false, out def.obsoleteYear, problems);
            JsonFields.Int(json, "earlyWindowMonths", null, true, out def.earlyWindowMonths, problems);

            def.name = ReadText(json, "name", true, problems);
            def.description = ReadText(json, "description", false, problems);

            def.sockets = ReadStrings(json, "sockets", true, problems);
            def.memories = ReadStrings(json, "memories", true, problems);
            def.oem = ReadOem(JsonFields.Object(json, "oem", null, true, problems), problems);
            def.compatibleSockets = ReadPairs(JsonFields.Array(json, "compatibleSockets", null, false, problems), problems);

            if (problems.Count > 0)
            {
                if (errors != null) errors.AddRange(problems);
                return null;
            }

            if (errors != null)
            {
                foreach (string member in JsonFields.Unknown(json, KnownMembers))
                    errors.Add("unknown member '" + member + "' is ignored");
            }
            return def;
        }

        private static LocText ReadText(JsonObject json, string name, bool required, List<string> problems)
        {
            JsonNode node = JsonFields.Get(json, name);
            if (node == null)
            {
                if (required) problems.Add(name + ": is required");
                return null;
            }
            return LocText.FromJson(node, name, problems);
        }

        private static List<string> ReadStrings(JsonObject json, string name, bool required, List<string> problems)
        {
            var result = new List<string>();
            JsonArray array = JsonFields.Array(json, name, null, required, problems);
            if (array == null) return result;

            for (int i = 0; i < array.Count; i++)
            {
                var scalar = array[i] as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.String)
                {
                    problems.Add(name + "[" + i + "]: expected a string, got " + JsonFields.KindName(array[i]));
                    continue;
                }
                result.Add(scalar.StringValue);
            }
            return result;
        }

        private static OemCurveDef ReadOem(JsonObject json, List<string> problems)
        {
            if (json == null) return null;

            var oem = new OemCurveDef();
            double maxAdoption;
            double ibAccel;
            JsonFields.Int(json, "delayMonths", "oem", true, out oem.delayMonths, problems);
            JsonFields.Int(json, "rampMonths", "oem", true, out oem.rampMonths, problems);
            JsonFields.Number(json, "maxAdoption", "oem", true, out maxAdoption, problems);
            JsonFields.Number(json, "ibAccel", "oem", true, out ibAccel, problems);
            oem.maxAdoption = (float)maxAdoption;
            oem.ibAccel = (float)ibAccel;

            return oem;
        }

        private static List<SocketPair> ReadPairs(JsonArray array, List<string> problems)
        {
            var result = new List<SocketPair>();
            if (array == null) return result;

            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i] as JsonObject;
                string path = "compatibleSockets[" + i + "]";
                if (item == null)
                {
                    problems.Add(path + ": expected an object { \"a\": ..., \"b\": ... }, got " + JsonFields.KindName(array[i]));
                    continue;
                }

                var pair = new SocketPair();
                JsonFields.String(item, "a", path, true, out pair.a, problems);
                JsonFields.String(item, "b", path, true, out pair.b, problems);
                result.Add(pair);
            }
            return result;
        }
    }
}
