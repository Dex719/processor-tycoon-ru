using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The game side of <see cref="IModContentSource"/>: <c>ModContent</c> reads the built-in resource <c>content/gpu-product-line/&lt;file&gt;.json</c>, merges
    /// the player's file <c>&lt;game folder&gt;/PTMod/content/gpu-product-line/&lt;file&gt;.json</c> into it by <c>id</c> (replace, <c>"disabled": true</c> removes)
    /// and logs what it skipped, under the id of this feature.
    /// </summary>
    internal sealed class GpuModContentSource : IModContentSource
    {
        public IReadOnlyList<T> LoadRecords<T>(string file, Func<JsonObject, List<string>, T> map, Func<T, List<string>, bool> validate)
            where T : class, IContentRecord
        {
            return ModContent.LoadRecords(GpuFeature.FeatureId, file, map, validate, GpuContentDb.ContentSchema);
        }

        public JsonObject LoadConfig(string file)
        {
            return ModContent.LoadConfig(GpuFeature.FeatureId, file, GpuContentDb.ContentSchema);
        }
    }
}
