using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What <see cref="PackCatalog"/>, <see cref="ContentValidator"/> and <see cref="StandardEffectRules"/> have to ask the running mod while content is loaded:
    /// how a condition is parsed, which effect types are registered, which modifier channels exist, which names the game's money enums have. In the game <c>CoreContentEnvironment</c> answers
    /// from <c>ModConditions</c>, <c>EventEffects</c>, <c>ModModifiers</c> and <c>ProcessorTycoon.ExpenseOrigin</c> / <c>RevenueOrigin</c>; the tests give a fake. Main thread only.
    /// </summary>
    public interface IContentEnvironment
    {
        /// <summary>
        /// Parses a condition node of a record (<c>conditions</c>, <c>targets.filter</c>, <c>requires</c>): <c>ModConditions.Parse</c>. Errors are added to
        /// <paramref name="errors"/> as <c>path: message</c>; the result is null when there is any.
        /// </summary>
        ICondition ParseCondition(JsonNode node, List<string> errors, string path);

        /// <summary>A handler is registered for the effect type (<c>EventEffects.Register</c>).</summary>
        bool IsEffectTypeRegistered(string type);

        /// <summary>
        /// Runs <c>IEventEffectHandler.Validate</c> of the effect's type. False - the effect is rejected; the reasons are in <paramref name="errors"/>
        /// (an implementation that has none to give leaves the list empty, the validator then writes a general one). An exception of the handler is the
        /// implementation's to catch: it counts it against the handler's feature and answers false.
        /// </summary>
        bool ValidateWithHandler(EventEffect effect, IList<string> errors);

        /// <summary>The channel is known to <c>ModModifiers</c>: a standard one of <c>core.json</c> or one a feature declared (<c>ModModifiers.IsDeclared</c>).</summary>
        bool IsChannelDeclared(string channel);

        /// <summary>The name is a member of <c>ExpenseOrigin</c> (<paramref name="spend"/>) or <c>RevenueOrigin</c> of the game.</summary>
        bool IsOriginName(bool spend, string name);
    }

    /// <summary>
    /// Where <see cref="PackCatalog"/> gets the files of a pack (decision 0006): in the game a thin wrapper over <c>ModContent</c> (the built-in resource
    /// with the player's override from <c>PTMod/content/&lt;folder&gt;/</c> merged in), in the tests the same merge over fixture text
    /// (<c>ContentMerger</c>). An implementation never throws: a file it cannot use gives an empty list, and says why in its own log.
    /// </summary>
    public interface IPackContentSource
    {
        /// <summary>
        /// The object of a config file of the pack after the override was merged in (the caller's own copy), or null when the pack has no such
        /// file - a pack without <c>config.json</c> is normal and must not leave a line in the log.
        /// </summary>
        JsonObject LoadConfig(string folder, string file, int schema);

        /// <summary>
        /// The records of a record file after the override was merged in (<c>ModContent.LoadRecords</c>). <paramref name="map"/> builds a record from its
        /// JSON and <paramref name="validate"/> checks it; a rejected record is skipped and the others load.
        /// </summary>
        IReadOnlyList<T> LoadRecords<T>(string folder, string file, Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate, int schema) where T : class, IContentRecord;
    }
}
