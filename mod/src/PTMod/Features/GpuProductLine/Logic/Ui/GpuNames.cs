using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The names the windows show: the part a research node opens (an architecture, a memory type or a cooler), the branch, a segment, the status of a card, a feature
    /// of an architecture. The keys of the content (<c>nameKey</c>) are put into the language of the player; an id with no key is shown as it is.
    /// </summary>
    public static class GpuNames
    {
        /// <summary>The text key of a part of the content (architecture, memory or cooler) by its id, or null.</summary>
        public static string PartKey(GpuContentDb content, string id)
        {
            if (content == null || string.IsNullOrEmpty(id)) return null;
            GpuArchitectureDto arch = content.Arch(id);
            if (arch != null) return arch.nameKey;
            GpuMemoryDto mem = content.Mem(id);
            if (mem != null) return mem.nameKey;
            GpuCoolerDto cooler = content.Cooler(id);
            return cooler != null ? cooler.nameKey : null;
        }

        /// <summary>The text of a key, or <paramref name="fallback"/> when no table has the key (content may name a key nobody wrote).</summary>
        public static string Localized(IGpuText text, string key, string fallback)
        {
            return !string.IsNullOrEmpty(key) && text.Has(key) ? text.L(key) : (fallback ?? string.Empty);
        }

        /// <summary>The name of a part by its id (the id itself when the content knows no key).</summary>
        public static string Part(GpuContentDb content, IGpuText text, string id)
        {
            return Localized(text, PartKey(content, id), id);
        }

        /// <summary>The name of a research node: the name of the part it opens.</summary>
        public static string Node(GpuContentDb content, IGpuText text, string nodeId)
        {
            GpuResearchNodeDto node = content != null ? content.Node(nodeId) : null;
            if (node == null) return nodeId ?? string.Empty;
            return Part(content, text, node.unlocks);
        }

        /// <summary>The names of several nodes, joined with commas.</summary>
        public static string Nodes(GpuContentDb content, IGpuText text, IEnumerable<string> nodeIds)
        {
            var names = new List<string>();
            if (nodeIds != null)
            {
                foreach (string id in nodeIds) names.Add(Node(content, text, id));
            }
            return string.Join(", ", names.ToArray());
        }

        /// <summary>The key of the title of a branch of the research tree.</summary>
        public static string BranchKey(string branch)
        {
            switch (branch)
            {
                case GpuContentDb.BranchMemory: return "gpu.research.branch.memory";
                case GpuContentDb.BranchCooling: return "gpu.research.branch.cooling";
                default: return "gpu.research.branch.architecture";
            }
        }

        /// <summary>The name of a segment of the market (its id when it has no key).</summary>
        public static string Segment(GpuContentDb content, IGpuText text, string segmentId)
        {
            GpuSegmentDto segment = content != null ? content.AnySegment(segmentId) : null;
            return Localized(text, segment != null ? segment.nameKey : null, segmentId);
        }

        /// <summary>The key of the text of a status of a card of the player.</summary>
        public static string StatusKey(string status)
        {
            switch (status)
            {
                case GpuState.StatusReady: return "gpu.lineup.status.ready";
                case GpuState.StatusRetired: return "gpu.lineup.status.retired";
                default: return "gpu.lineup.status.selling";
            }
        }

        /// <summary>The name of a feature of an architecture ("3d", "shader"): the key is put together from the id, an id with no text is shown as it is.</summary>
        public static string Feature(IGpuText text, string featureId)
        {
            if (string.IsNullOrEmpty(featureId)) return string.Empty;
            return Localized(text, FeatureKeyPrefix + featureId, featureId);
        }

        /// <summary>The start of the key of the name of a vendor's series; the id of the vendor follows (<c>gpu.series.nvidia</c>).</summary>
        public const string SeriesKeyPrefix = "gpu.series.";

        /// <summary>
        /// The name of the series of a vendor ("GeForce"): the text <c>gpu.series.&lt;vendorId&gt;</c> when the tables have one, otherwise the <c>series</c> of the content. The card of a vendor is
        /// named <c>&lt;series&gt; &lt;n&gt;</c> from the content when it is made, so the two must agree (a test keeps them in step); the text is what a letter says.
        /// </summary>
        public static string Series(IGpuText text, GpuVendorDto vendor)
        {
            if (vendor == null) return string.Empty;
            return Localized(text, SeriesKeyPrefix + vendor.id, vendor.series ?? string.Empty);
        }

        /// <summary>The start of the key of a feature name; the id of the feature follows.</summary>
        public const string FeatureKeyPrefix = "gpu.feat.";

        /// <summary>
        /// What a segment asks for in a year, in words ("Требования года: 3D, шейдеры; видеопамять не меньше 128 МБ"): the tab "Рынок" and the letter of a segment that opened say it the same way.
        /// </summary>
        public static string Requirements(IGpuText text, string[] features, float vramMB)
        {
            string have = features != null && features.Length > 0 ? Features(text, features) : text.L("gpu.market.noFeatures");
            string vram = vramMB > 0f
                ? text.L("gpu.market.vram", text.L("gpu.design.val.mb", GpuUiMath.Plain((float)Math.Round(vramMB, 2))))
                : text.L("gpu.market.noVram");
            return text.L("gpu.market.requirements", have, vram);
        }

        /// <summary>The names of several features, joined with commas.</summary>
        public static string Features(IGpuText text, IEnumerable<string> featureIds)
        {
            var names = new List<string>();
            if (featureIds != null)
            {
                foreach (string id in featureIds) names.Add(Feature(text, id));
            }
            return string.Join(", ", names.ToArray());
        }
    }
}
