using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// Where <see cref="GpuContentDb"/> gets the text of its files (design.md, "GpuContentDb"): in the game a thin wrapper over
    /// <c>ModContent.LoadRecords</c> / <c>ModContent.LoadConfig</c> (the built-in resource with the player's override merged in by <c>id</c>),
    /// in the tests the same merge over fixture text. File names are plain: <c>architectures</c>, <c>balance</c> (the extension is optional).
    /// An implementation never throws: a file it cannot use gives an empty list or an empty object, and says why in its own log or report.
    /// </summary>
    public interface IModContentSource
    {
        /// <summary>
        /// The records of a record file (<c>{ "schema": 1, "records": [ ... ] }</c>) after the override was merged in. <paramref name="map"/> builds
        /// a record from its JSON (null = unusable, with the reasons added to the list), <paramref name="validate"/> may still reject it.
        /// A rejected record is skipped, the others load.
        /// </summary>
        IReadOnlyList<T> LoadRecords<T>(string file, Func<JsonObject, List<string>, T> map, Func<T, List<string>, bool> validate)
            where T : class, IContentRecord;

        /// <summary>The object of a config file (<c>balance</c>, <c>timeline</c>) after the merge; the caller's own copy, empty when the file cannot be used.</summary>
        JsonObject LoadConfig(string file);
    }
}
