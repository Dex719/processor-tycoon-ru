# Публичный API ядра PTMod

Сгенерировано `mod/tools/core_api_digest.py` из `mod/src/PTMod/Core`. Только публичные типы и члены;
за деталями поведения смотри исходник (путь в заголовке) и `.kiro/specs/mod-framework/design.md`.

## `src/PTMod/Core/Bootstrap/CoreInitAttribute.cs` — `PTMod.Core`

```csharp
    CoreInitAttribute(CoreInitStage stage, int order = 0)
    CoreInitStage Stage
    int Order   // Position inside the stage, smaller first; equal orders go by type and method name.
    bool Optional   // A failure of this method is logged and the start continues (default: the start fails, the mod stays off).
```

## `src/PTMod/Core/Bootstrap/FeatureContext.cs` — `PTMod.Core`

```csharp
class FeatureContext   // What a feature uses in <see cref="IModFeature.Register"/> to declare its needs (FR-3). One object per feature for the whole session:
    void Patch(Type patchClass)   // Declares the patches of the feature: the <see cref="ModPatchAttribute"/> methods of <paramref name="patchClass"/>, a static class in
    void Param(ParamSpec spec)   // Declares a parameter of the feature (a hub slider, toggle or number; FR-6). <see cref="ParamSpec.FeatureId"/> is always this
    IDisposable OnTick(TickPhase phase, Action handler, int priority = 0, TickOptions options = TickOptions.None)   // Subscribes a handler of the feature to a phase of the game day (FR-4): <c>ctx.OnTick(TickPhase.DayPassed, OnDay)</c>, not a
```

## `src/PTMod/Core/Bootstrap/FeatureInfo.cs` — `PTMod.Core`

```csharp
class FeatureInfo   // One discovered feature as the hub and the core services see it (<see cref="ModFeatures.All"/>): what the feature declared, read once
    string Id
    string NameKey   // <see cref="ModL10n"/> key of the name; null for a feature that could not be read (show <see cref="Id"/> then).
    string DescriptionKey
    IReadOnlyList<string> DependsOn   // Declared hard dependencies (a copy taken at start).
    bool EnabledByDefault
    bool SupportsHotToggle
    Type FeatureType   // The class marked <c>[ModFeature]</c>.
    bool IsRegistered   // <see cref="IModFeature.Register"/> ran and returned (it runs once per session).
```

## `src/PTMod/Core/Bootstrap/IModFeature.cs` — `PTMod.Core`

```csharp
interface IModFeature   // The entry point of a feature (FR-3): a <c>[ModFeature] sealed class … : IModFeature</c> with a public parameterless constructor in the
class ModFeatureAttribute : Attribute   // Marks the entry class of a feature; the bootstrap finds the features by this attribute (FR-3 item 1).
```

## `src/PTMod/Core/Bootstrap/ModBootstrap.cs` — `PTMod.Core`

```csharp
class ModBootstrap   // The mod's entry point (FR-1, decision 0001). Unity calls <see cref="Init"/> before the first scene loads: the installer lists it in
    const string ModVersion = BuildInfo.Version   // Mod version. Single source: mod/VERSION, injected by build.py through BuildInfo.g.cs.
    static bool IsInitialized   // True after a successful start.
    static void Init()
    CoreInitEntry(CoreInitStage stage, int order, bool optional, string name, string sortKey, Action run)
    CoreInitStage Stage
    int Order
    bool Optional
    string Name   // <c>Type.Method</c> for the log.
    string SortKey
    Action Run
    static int Compare(CoreInitEntry a, CoreInitEntry b)
```

## `src/PTMod/Core/Bootstrap/ModFeatures.cs` — `PTMod.Core`

```csharp
class ModFeatures   // The feature registry (FR-3): the features found by <c>[ModFeature]</c>, their dependency order and their state. A thin runtime layer
    static event Action<string, FeatureState> StateChanged   // The state of a feature changed: (feature id, new state). Raised after the feature was switched (after <c>OnEnabled</c> or
    static FeatureInfo Find(string featureId)   // The registry entry of a feature, or null.
    static FeatureState StateOf(string featureId)   // The state of a feature. The framework id (<c>mod-framework</c>) is Enabled; an unknown id is Unavailable; after a failed start
    static bool IsActive(string featureId)   // The feature is Enabled: its patches, handlers, ticks and providers run (not Disabled, Faulted or Unavailable).
    static FeatureStatus StatusOf(string featureId)   // The structured state: <see cref="FeatureStatus.ReasonKey"/> and <see cref="FeatureStatus.ReasonArg"/> for the hub, English <see cref="FeatureStatus.Reason"/> for logs.
    static string ReasonOf(string featureId)   // Why the feature is Unavailable or Faulted, as text for the player (<c>core.feature.reason.*</c> in the current language, AC-3.1:
    static bool WantsEnabled(string featureId)   // The player's choice for the feature (the position of the hub toggle), including a choice waiting for the scene to change.
    static bool HasPendingChange(string featureId)   // A switch of a feature without hot toggle waits for the scene to change ("takes effect after loading a save", FR-3 item 5).
    static string FeatureOfType(Type type)   // The feature that owns code of this type, by namespace: <c>PTMod.Features.&lt;Name&gt;[.…]</c> gives the slug of &lt;Name&gt;
    static void SetEnabled(string featureId, bool enabled)   // The player switches a feature (the hub; no confirmation here - the hub asks first). The choice is saved in <c>ptmod_settings.json</c>
```

## `src/PTMod/Core/Bootstrap/PTModHost.cs` — `PTMod.Core`

```csharp
class PTModHost : MonoBehaviour   // The mod's one persistent object (FR-1 item 2): a <c>DontDestroyOnLoad</c> GameObject created by <see cref="ModBootstrap.Init"/>. It
    const int MainMenuScene = 0   // Build index of the main menu scene (<c>level0</c>).
    const int GameScene = 1   // Build index of the game scene (<c>level1</c>); loading a save reloads it.
    static event Action<int> SceneReady   // The main menu (0) or the game scene (1) is loaded and its objects have started: (re)subscribe to the scene's singletons, build UI.
    static event Action SceneLeaving   // The scene is about to go (<c>SceneTransitionHandler.OnTransitionsStarted</c>): close the mod's windows, drop references to the scene's objects.
    Coroutine Run(IEnumerator routine)   // Starts a coroutine on the host (it survives scene loads). The owner is the feature of the routine's type (its namespace): an
    void Stop(Coroutine coroutine)   // Stops a coroutine started by <see cref="Run"/>; null is ignored.
```

## `src/PTMod/Core/Content/ModContent.cs` — `PTMod.Core`

```csharp
class ModContent   // Loads the JSON content of the features (FR-10, decision 0006): the built-in file embedded in <c>PTMod.dll</c> as the resource
    const string CoreFeatureId = ModLog.CoreFeatureId   // The framework's own content folder and file: <c>content/mod-framework/core.json</c>.
    const string CoreFile = "core.json"
    const int CoreSchema = 1   // The <c>schema</c> of <c>core.json</c> this version reads.
    static IReadOnlyList<T> LoadRecords<T>(   // Loads a record file (<c>{ "schema": N, "records": [ { "id": "..." } ] }</c>): the built-in records in file order, with the
    static JsonObject LoadConfig(string featureId, string file, int supportedSchema)   // Loads a config file (<c>{ "schema": N, ... }</c>): the override is merged into the built-in object field by field (nested objects
    static ContentReport ReportOf(string featureId, string file)   // What happened when the file was last loaded, or null when it was not loaded (yet). The key is the content folder and the file, with or without ".json".
```

## `src/PTMod/Core/Events/ModEvents.cs` — `PTMod.Core`

```csharp
class ModEvents   // The events of the game that more than one feature needs (FR-23): a CPU is released, a CPU moves to another company. The core raises the
    static event Action<ICompany, Cpu, bool, bool> CpuReleased   // A CPU was released (FR-23 item 1): <c>owner</c> is the company that owns it, <c>isVariant</c> is <c>cpu.Base != null</c> (a variant
    static event Action<ICompany, ICompany, Cpu> CpuTransferred   // A CPU moved from one company to another (FR-23 item 4): <c>from</c>, <c>to</c>, <c>cpu</c>. <see cref="CpuReleased"/> does not
    static void RaiseCpuTransferred(string featureId, ICompany from, ICompany to, Cpu cpu)   // Announces a transfer of <paramref name="cpu"/> from <paramref name="from"/> to <paramref name="to"/> to the subscribers of
```

## `src/PTMod/Core/Hooks/Guard.cs` — `PTMod.Core`

```csharp
class ModHooks   // Fault isolation (FR-2 items 3, 4, 8): the wrappers for patch bodies and handlers, the fault counter and the circuit breaker.
    static bool Guard(string featureId, string siteId, Action body)   // Runs <paramref name="body"/> in try/catch. True when it ran to the end; false when it threw (the exception is counted against
    static T Guard<T>(string featureId, string siteId, Func<T> body, T fallback)   // Runs <paramref name="body"/> in try/catch and returns its value; <paramref name="fallback"/> when it threw (counted against the
    static bool CanRun(string featureId, string siteId)   // May code of this feature run at this site: false when the feature is Faulted or Unavailable (an unknown id too, and every id after a
    static void ReportFault(string featureId, string siteId, Exception e)   // Counts a failure of feature code caught at the boundary of a hook (FR-2 items 3, 4): the first failure of the site goes to the log
    static int FaultsOf(string featureId)   // Failures counted against the feature in this session (the core: all its units together).
    static void Raise(string eventName, Delegate handlers, params object[] args)   // Raises an event handler by handler (FR-2 item 8): handlers of features that are not Enabled are skipped, an exception is counted
    static void Raise(string eventName, Action handlers)   // Raises an event without arguments; see <see cref="Raise(string, Delegate, object[])"/>.
    static void Raise<T1>(string eventName, Action<T1> handlers, T1 arg1)   // Raises an event with one argument; see <see cref="Raise(string, Delegate, object[])"/>.
    static void Raise<T1, T2>(string eventName, Action<T1, T2> handlers, T1 arg1, T2 arg2)   // Raises an event with two arguments; see <see cref="Raise(string, Delegate, object[])"/>.
    static void Raise<T1, T2, T3>(string eventName, Action<T1, T2, T3> handlers, T1 arg1, T2 arg2, T3 arg3)   // Raises an event with three arguments; see <see cref="Raise(string, Delegate, object[])"/>.
    static void Raise<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> handlers, T1 arg1, T2 arg2, T3 arg3, T4 arg4)   // Raises an event with four arguments (<c>ModEvents.CpuReleased</c>); see <see cref="Raise(string, Delegate, object[])"/>.
    SiteFaults(string featureId, string siteId)
    string FeatureId
    string SiteId
    int Count
    int Unreported
    string LastError
```

## `src/PTMod/Core/Hooks/HarmonyBackend.cs` — `PTMod.Core`

```csharp
    bool Supports(PatchKind kind)
    bool TryApply(PatchSpec spec, out string error)
    void Remove(PatchSpec spec)
    void RemoveFeature(string featureId)
```

## `src/PTMod/Core/Hooks/IPatchBackend.cs` — `PTMod.Core`

```csharp
interface IPatchBackend   // What actually changes the game's methods (D2): Harmony (<see cref="HarmonyBackend"/>) or a fallback if spike 0002 fails (TSK-008). The
    bool Supports(PatchKind kind)
    bool TryApply(PatchSpec spec, out string error)
    void Remove(PatchSpec spec)
    void RemoveFeature(string featureId)
```

## `src/PTMod/Core/Hooks/ModHooks.cs` — `PTMod.Core`

```csharp
class ModHooks   // The patch registry (FR-2, decision 0002): what every feature and the core declared with <see cref="ModPatchAttribute"/>, applied
    const string CoreId = ModLog.CoreFeatureId   // The framework's feature id; also the owner of the core's patches.
    const string HarmonyIdPrefix = "ptmod."   // Harmony owner ids are <c>ptmod.&lt;feature-id&gt;</c> (<c>ptmod.mod-framework</c> for the core).
    static FeatureState ApplyFeature(string featureId)   // Applies the declared patches of a feature, all or nothing for the required ones (FR-2 item 2), and returns the state it can have:
    static void RemoveFeature(string featureId)   // Removes every patch of the feature now (only that feature's; for the core: all core patches). Never throws.
    static bool IsReserved(MethodBase target, out ReservedTarget info)   // The method is reserved (FR-2 item 6): <paramref name="info"/> tells by whom, for which kinds of patch and what to use instead.
    static bool IsReservedByChannel(MethodBase target, out string channel)   // The method's formula is read by a channel of <c>ModModifiers</c> (a <see cref="ReservedKind.Channel"/> reservation); <paramref name="channel"/> names it.
    static bool IsApplied(Type patchClass)   // All patches of a declared patch class are in the game (a core service asks before relying on its patch: <c>ModCredit</c> and the
    Problem(string detail, string reason)
    string Detail   // English, for the log.
    string Reason   // For the player (<c>core.feature.reason.unavailable</c>, {0}).
    string FeatureId
    string Id
    Type PatchClass
    string TargetName
    string Signature
    string HandlerName
    PatchKind Kind
    int Priority
    bool Optional
    MethodBase Target
    MethodInfo Handler
    PatchSpec Spec   // Null when the declaration cannot be applied (missing target, reserved, bad handler, unsupported kind).
    PatchStatus Status = PatchStatus.Declared
    string Detail
    string Reason
    ReservedTarget Reserved
    string ConflictWith
    void Fail(PatchStatus status, string detail, string reason)
    PatchReport ToReport()
```

## `src/PTMod/Core/Hooks/ModPatchAttribute.cs` — `PTMod.Core`

```csharp
enum PatchKind   // The kind of a patch method, as Harmony knows them.
class ModPatchAttribute : Attribute   // Declares a patch method of the mod (FR-2 item 1): <c>[ModPatch(typeof(Market), "CalculateRatings", PatchKind.Postfix)]</c> on a static
    const int NormalPriority = 400   // Harmony's <c>Priority.Normal</c>.
    ModPatchAttribute(Type target, string method, PatchKind kind)
    Type Target   // The type that declares the target (or that the nested <see cref="InnerType"/> lives in).
    string Method   // The name of the target method.
    PatchKind Kind
    Type[] ArgTypes   // Exact parameter types of the target (<c>typeof(float).MakeByRefType()</c> for <c>ref float</c>); null - the name is unique.
    string InnerType   // A nested type of <see cref="Target"/> that declares the method: <c>"CpuOffer"</c> of <c>Contract</c>; <c>"A.B"</c> for deeper nesting.
    int Priority   // Harmony priority: higher runs earlier among the prefixes and postfixes of one method (<c>Priority.Last</c> = 0, <c>First</c> = 800).
    bool Optional   // A failure of this patch (missing target, apply error, transpiler conflict) is a warning and the feature still starts without it; a
```

## `src/PTMod/Core/Hooks/PatchSpec.cs` — `PTMod.Core`

```csharp
class PatchSpec   // One resolved patch: what a backend applies. Built by <c>ModHooks</c> from a <see cref="ModPatchAttribute"/> once the target and the
    string FeatureId   // The feature that declared the patch (<c>mod-framework</c> for the core); its Harmony id is <c>ptmod.&lt;feature-id&gt;</c>.
    string Id   // <c>&lt;feature&gt;/&lt;Class.Method&gt;/&lt;kind&gt;</c>, for example <c>mod-framework/Market.CalculateRatings/postfix</c>.
    MethodBase Target   // The game method that is patched.
    string TargetName   // <c>Class.Method</c> of the target (nested types joined with dots: <c>Contract.CpuOffer.DefineChance</c>).
    MethodInfo Handler   // The static patch method.
    PatchKind Kind
    int Priority
    bool Optional
enum PatchStatus   // Where a declared patch stands (the Diagnostics tab and the self-check read it from <c>ModHooks.Report</c>).
class PatchReport   // A snapshot of one declared patch for the Diagnostics tab and the self-check: what was declared, where it stands and why.
    string FeatureId
    string Id   // The patch id (<see cref="PatchSpec.Id"/>), also for patches whose target could not be resolved.
    string Target   // <c>Class.Method</c> of the target as declared.
    string Signature   // Parameter types of the resolved target, <c>(Cpu, Boolean, Boolean, Int32, Single)</c>; the declared <c>ArgTypes</c> or null when unresolved.
    string Handler   // <c>Class.Method</c> of the patch method.
    PatchKind Kind
    int Priority
    bool Optional
    PatchStatus Status
    string Detail   // English: why the patch failed or was rejected, or what happened to it last; null when there is nothing to say.
    ReservedTarget Reserved   // The reservation that rejected the patch (AC-2.3, AC-2.4), or null.
    string ConflictWith   // The feature whose transpiler holds the method when this one was refused as the second transpiler (FR-2 item 5), or null.
```

## `src/PTMod/Core/Hooks/ReservedTargets.cs` — `PTMod.Core`

```csharp
enum ReservedKind   // Who holds a reserved game method (the "Резерв" column of the patch table in design.md).
class ReservedTarget   // One reserved game method (FR-2 item 6): a feature's patch on it is rejected with a hint at the API to use instead. Immutable; the table
    string Patch   // The row of the patch table: <c>P4</c>, <c>P20</c>, <c>F1</c>.
    string TypeName   // Full name of the game type as reflection spells it (<c>ProcessorTycoon.ContractSystem.Contract+CpuOffer</c>).
    string Method
    ReservedKind Kind
    string Name   // The channel (<c>market.rating.cpu</c>), the service (<c>ModEvents</c>) or, for a feature formula, the owner's channel.
    string OwnerFeature   // <c>mod-framework</c>, or the feature that owns the formula (<c>market-shocks</c>).
    string Api   // The API to use instead, as code: <c>ModEvents.CpuReleased</c>, <c>ModModifiers</c>; null for a feature formula.
    IReadOnlyList<PatchKind> Kinds   // The kinds of patch the reservation covers; the others stay open to every feature (P7: postfixes on <c>EarnMoney</c> for accounting,
    bool Covers(PatchKind kind)   // The reservation covers this kind of patch.
    bool Rejects(string featureId, PatchKind kind)   // The patch is refused: the kind is covered and the feature is neither the core nor, for a feature formula, the owner. The owner's
    string Hint()   // The hint for the player in the current language (<see cref="UseInstead"/> with <see cref="Name"/> and <see cref="Api"/>):
    string Describe()   // One English line for the log: why the target is closed and what to use.
```

## `src/PTMod/Core/L10n/ModL10n.cs` — `PTMod.Core`

```csharp
class ModL10n   // The mod's texts (FR-8): <c>L("core.hub.title")</c>, <c>Plural("core.mail.daysLeft", 22)</c>, <c>Resolve(locText)</c>. A thin adapter over
    static string L(string key, params object[] args)   // The text of the key with <c>{0}..{n}</c> filled in; <c>[key]</c> (and one warning) when no language has it.
    static string Plural(string key, long n, params object[] args)   // The form of the key that fits <paramref name="n"/>: ru <c>key.one/.few/.many</c>, en <c>key.one/.other</c>, or one text
    static string Resolve(LocText text, params object[] args)   // A <see cref="LocText"/> from content (<c>{ "key": ... }</c> or <c>{ "ru": ..., "en": ... }</c>) by the same chain as <see cref="L"/>.
    static bool Has(string key)   // True when the key (or a plural group with that base) exists in the current language, <c>en</c> or <c>ru</c>.
    readonly string Language
    readonly string Feature; // null for a language file
    readonly string Resource; // resource name, or the full path of an override file
    readonly string Source; // label for messages and SourceOf
    TableFile(string language, string feature, string resource, string source)
```

## `src/PTMod/Core/Log/ModLog.cs` — `PTMod.Core`

```csharp
class ModLog   // The mod's log (FR-7): <c>[PTMod] [&lt;feature-id&gt;] LEVEL message</c> through <c>Debug.Log</c> / <c>LogWarning</c> / <c>LogError</c>
    const string CoreFeatureId = LogFormatter.CoreFeatureId   // Feature id of the framework's own records (the same text <see cref="LogFormatter"/> uses for a blank id).
    static void Debug(string featureId, string message)   // Verbose output, off unless <c>ModSettings.VerboseLog</c>. Records of this level carry the label DEBUG and go to <c>Debug.Log</c>.
    static void Info(string featureId, string message)
    static void Warn(string featureId, string message)
    static void Error(string featureId, string message, Exception e = null)   // An error; with an exception, its type, message and inner exceptions follow the text and the stack trace goes on the next lines.
```

## `src/PTMod/Core/Logic/Companies/CompanyKey.cs` — `PTMod.Core.Logic.Companies`

```csharp
class CompanyKey   // Stable string keys of companies (FR-13): <c>player</c> for the player and <c>ai:&lt;UniqueID&gt;</c> for an AI
    const string Player = "player"   // Key of the player's company.
    const string AiPrefix = "ai:"   // Prefix of an AI company key.
    const string AnyAi = "anyAI"   // Selector used in conditions (not a key): any AI company. Resolved by the code that evaluates a condition.
    static bool IsValid(string key)   // True for <c>player</c> and for <c>ai:</c> followed by one or more ASCII digits (any number of them, as the
    static bool IsPlayer(string key)
    static bool IsAi(string key)   // True for <c>ai:&lt;digits&gt;</c>.
    static string ForAi(int uniqueId)   // The key of the AI company with this <c>UniqueID</c>; null for a negative id (an unset id).
    static bool TryGetAiId(string key, out int uniqueId)   // Reads the <c>UniqueID</c> out of an AI key. False for the player key, an invalid key and an id that does
    static bool IsKeyOrSelector(string text)   // A key or a selector as it can be written in content: a valid key or <see cref="AnyAi"/>.
```

## `src/PTMod/Core/Logic/Conditions/ConditionNodes.cs` — `PTMod.Core.Logic.Conditions`

```csharp
class AllCondition : ICondition   // Conjunction: true when every child is true. An empty list is true (the neutral element), so
    AllCondition(IEnumerable<ICondition> children)
    bool Evaluate(ConditionContext ctx)
    string Describe()
class AnyCondition : ICondition   // Disjunction: true when at least one child is true. An empty list is false (nothing can be true), so
    AnyCondition(IEnumerable<ICondition> children)
    bool Evaluate(ConditionContext ctx)
    string Describe()
class NotCondition : ICondition   // Negation. Conditions are two-valued: a predicate whose data source is unavailable is false, so
    NotCondition(ICondition inner)
    ICondition Inner
    bool Evaluate(ConditionContext ctx)
    string Describe()
class DelegateCondition : ICondition   // A condition backed by a function: the building block of every core predicate, and the easy way for a feature
    DelegateCondition(string description, Func<ConditionContext, bool> evaluate)
    bool Evaluate(ConditionContext ctx)
    string Describe()
class ConditionEvaluator   // The only entry point that guarantees the "no exception" contract of FR-11 for a whole tree: a condition
    static bool Evaluate(ICondition condition, ConditionContext ctx, Action<string> onError = null)
    static ICondition[] CopyChildren(IEnumerable<ICondition> children, string owner)
    static string DescribeGroup(string name, ICondition[] children)
```

## `src/PTMod/Core/Logic/Conditions/ConditionParser.cs` — `PTMod.Core.Logic.Conditions`

```csharp
enum CompanyScope   // Which values the <c>company</c> parameter of a predicate accepts.
enum CompanySelectorKind   // What a <see cref="CompanySelector"/> points at.
class CompanySelector   // The parsed <c>company</c> parameter of a predicate: the context subject (no parameter), one company by key,
    static readonly CompanySelector Subject = new CompanySelector(CompanySelectorKind.Context, null)   // The subject of the context: the player unless the caller says otherwise.
    static readonly CompanySelector AnyAi = new CompanySelector(CompanySelectorKind.AnyAi, null)   // Any AI company.
    CompanySelectorKind Kind
    string Key   // The company key for <see cref="CompanySelectorKind.Key"/>, otherwise null.
    static CompanySelector ForKey(string key)   // A selector for one company; throws for a string that is not a valid <see cref="CompanyKey"/>.
    bool Test(ConditionContext ctx, Func<IGameState, string, bool?> test, bool includeBankrupt = false)   // Runs <paramref name="test"/> for the selected company and returns whether it came back true. A null result of the
class ConditionArgs   // Typed reader of the parameters of one predicate node, used by the factories of core predicates and available to features.
    ConditionArgs(JsonObject node, List<string> errors, string path)
    string Path   // Path of the node, for example <c>$.all[1]</c>.
    bool Has(string name)   // True when the node has the parameter (useful for parameters whose absence has a meaning of its own).
    void Fail(string name, string message)   // Adds an error for the parameter <paramref name="name"/> (<c>path.name: message</c>); use it for checks across parameters.
    int RequireInt(string name, int min, int max)   // A required integer in [min, max].
    int OptionalInt(string name, int defaultValue, int min, int max)   // An optional integer in [min, max]; <paramref name="defaultValue"/> when the parameter is absent.
    double RequireNumber(string name, double min, double max)   // A required finite number in [min, max].
    double OptionalNumber(string name, double defaultValue, double min, double max)   // An optional finite number in [min, max]; <paramref name="defaultValue"/> when absent.
    bool OptionalBool(string name, bool defaultValue)   // An optional boolean.
    string RequireString(string name)   // A required non-empty string.
    string OptionalString(string name)   // An optional non-empty string; null when absent.
    string RequireChoice(string name, params string[] allowed)   // A required string out of a fixed set (compared case-insensitively); returns the spelling from <paramref name="allowed"/>.
    string OptionalChoice(string name, string defaultValue, params string[] allowed)   // An optional string out of a fixed set; <paramref name="defaultValue"/> (one of the allowed spellings) when absent.
    CompanySelector Company(CompanyScope scope)   // Reads the <c>company</c> parameter. Absent: the context subject for <see cref="CompanyScope.PlayerOrAi"/>,
    bool Finish()   // Reports every parameter that no accessor asked for (a typo would otherwise be ignored silently) and returns
    string Describe(string typeName)   // The text for <see cref="ICondition.Describe"/>: <c>name(a=1, b=true)</c> with the parameters that were given, or just the name when there are none.
class ConditionParser   // Turns the JSON of a condition into a tree of <see cref="ICondition"/> (FR-11 item 2). A node is an object with exactly one of:
    const int MaxDepth = 64   // Deepest nesting of condition nodes (a hand-built cyclic tree cannot hang the parser).
    ConditionParser(PredicateRegistry registry)
    PredicateRegistry Registry
    ICondition Parse(JsonNode node, List<string> errors, string path = "$")
    ICondition Parse(string json, List<string> errors)   // Parses JSON text first (a syntax error is reported as <c>$: invalid JSON: ... line L, column C</c>).
```

## `src/PTMod/Core/Logic/Conditions/IGameState.cs` — `PTMod.Core`

```csharp
enum MarketWindow   // The period a market share predicate looks at.
interface IGameState   // The narrow read-only view of the game that conditions need (FR-11, table "ModConditions: predicates and data sources"
interface ICondition   // A node of a parsed condition tree. Built by the condition parser, evaluated against a <see cref="ConditionContext"/>.
class ConditionContext   // What a condition is evaluated against: the subject company, today's date and the game view.
    ConditionContext(IGameState state, DateTime today, string subjectKey = null)
    string SubjectKey   // The company that predicates without an explicit <c>company</c> parameter look at; the player by default.
    DateTime Today   // The current game date. <c>default(DateTime)</c> means "unknown" (date predicates are then false).
    IGameState State   // The view of the game. Null makes every predicate that needs it false.
```

## `src/PTMod/Core/Logic/Conditions/PredicateRegistry.cs` — `PTMod.Core.Logic.Conditions`

```csharp
class RejectedPredicate   // A refused <see cref="PredicateRegistry.Register"/> call, kept for the self-check report (AC-19.3).
    RejectedPredicate(string type, string ownerFeature, string reason)
    string Type   // The predicate name that was refused.
    string OwnerFeature   // The feature that tried to register it.
    string Reason   // English explanation, also the text of the log warning.
class PredicateRegistry   // The table of predicates that a JSON condition can name in <c>type</c> (FR-11 items 4 and 6): name, owning feature and
    const string CoreOwner = FeatureGraph.CoreFeatureId   // The owner id of built-in predicates: the framework itself.
    const int MaxNameLength = 64
    string Owner
    Func<JsonObject, List<string>, string, ICondition> Factory
    PredicateRegistry(Func<string, string, bool> namePolicy = null)   // Optional extra check <c>(ownerFeature, predicateName) =&gt; allowed</c>, applied after the structural rules;
    Func<string, string, bool> NamePolicy   // See the constructor. Can be set once the prefix table is loaded; affects later registrations only.
    bool IsKnown(string type)
    string OwnerOf(string type)   // The feature that registered the predicate; null for an unknown name.
    bool Register(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory)   // Registers a predicate. <paramref name="factory"/> gets the JSON object of the node (it contains <c>type</c> and the parameters),
    bool TryRegister(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory, out string error)   // Same as <see cref="Register"/> and tells why a registration was refused.
    ICondition Create(string type, JsonObject node, List<string> errors, string path)   // Builds the condition for a node with the given <c>type</c>. Returns null and adds at least one error (to the path
```

## `src/PTMod/Core/Logic/Conditions/Predicates.cs` — `PTMod.Core.Logic.Conditions`

```csharp
class Predicates   // The predicates of the framework (design.md, "ModConditions: predicates and data sources"): 32 names without a dot, each reading
    static readonly IReadOnlyList<string> SegmentNames = new[]   // Segment names accepted by <c>segmentShareAtLeast</c>, the suffixes of the <c>market.demand.*</c> channels.
    static readonly IReadOnlyList<string> CompanyTypes = new[]   // Company types accepted by <c>companyType</c>.
    static int RegisterCore(PredicateRegistry registry)   // Registers every core predicate under the owner <see cref="PredicateRegistry.CoreOwner"/>. Returns how many were accepted;
    static PredicateRegistry CreateRegistry(Func<string, string, bool> namePolicy = null)   // A new registry that already holds the core predicates.
```

## `src/PTMod/Core/Logic/Content/ContentMerger.cs` — `PTMod.Core.Logic.Content`

```csharp
class ContentLoad<T> where T : class, IContentRecord   // The records of a content file after the merge, and the report of how it went. Immutable.
    IReadOnlyList<T> Records   // Built-in records in file order (a replaced record keeps its place), then the ids the override added; removed and rejected ones are absent.
    ContentReport Report
class ConfigLoad   // The object of a config file after the merge, and the report. Immutable reference; the object belongs to the caller.
    JsonObject Config   // The merged object (never null; empty when the built-in file could not be read, so features fall back to their defaults).
    ContentReport Report
class ContentMerger   // The merge half of <c>ModContent</c> (FR-10, decision 0006) on plain text: the adapter reads the built-in resource and the player's
    static string NormalizeFile(string file)   // The file name with its extension: "events" and "events.json" both give "events.json". Blank gives an empty string.
    static bool IsSafeName(string name)   // True for a name that is safe as one path segment: letters, digits, '-', '_' and '.', at most 100 characters, not starting with a dot,
    static string BuiltinResourceName(string featureId, string file)   // Logical name of the built-in resource, <c>content/&lt;feature&gt;/&lt;file&gt;.json</c>; null when a name is unsafe.
    static string OverrideRelativePath(string featureId, string file)   // Path of the override file relative to <c>&lt;game&gt;/PTMod/content</c>, with '/' separators: <c>&lt;feature&gt;/&lt;file&gt;.json</c>; null when a name is unsafe.
    static string ReportKey(string featureId, string file)   // The key under which the report of a file is kept: <c>&lt;feature&gt;/&lt;file&gt;.json</c>.
    static ContentLoad<T> MergeRecords<T>(string featureId, string file, string builtinText, string overrideText,   // Merges the override into the built-in record file and maps the result (see the class summary).
    static ConfigLoad MergeConfig(string featureId, string file, string builtinText, string overrideText, int supportedSchema)   // Merges the override into the built-in config object (see the class summary). The result is never null: when the built-in file
```

## `src/PTMod/Core/Logic/Content/ContentReport.cs` — `PTMod.Core`

```csharp
interface IContentRecord   // A record of a content file (<c>{ "schema": 1, "records": [ ... ] }</c>) as <c>ModContent</c> sees it: only the identity.
class ContentReport   // What happened while a content file was loaded (FR-10; <c>ModContent.ReportOf</c>, the self-check and the Diagnostics tab read it).
    int Builtin, Replaced, Added, Removed
    List<string> Errors = new List<string>()   // Problems that need the author's or the player's attention (a skipped record, an ignored override). Empty when all is well.
    string FeatureId = string.Empty   // Feature id (the content folder), as passed to the loader.
    string File = string.Empty   // File name with the extension (<c>samples.json</c>).
    int Skipped   // Records that were rejected, from either file. Each one has an entry in <see cref="Errors"/>.
    bool OverrideFound   // An override file was present (its text was given), whether or not it could be used.
    bool OverrideApplied   // The override file was read and merged; false when absent or when it was ignored as a whole.
    List<string> Warnings = new List<string>()   // Notes that are not errors: a record disabled in the override that matches nothing, a disabled built-in record, soft messages of a validation that accepted the record.
    List<string> ReplacedIds = new List<string>()   // Ids of the built-in records replaced by the override, in override order. For a config file: the top-level fields that changed.
    List<string> AddedIds = new List<string>()   // Ids that the override added. For a config file: the new top-level fields.
    List<string> RemovedIds = new List<string>()   // Ids of the built-in records the override removed.
    ContentReport()
    ContentReport(string featureId, string file)
```

## `src/PTMod/Core/Logic/Events/ReleaseDeduper.cs` — `PTMod.Core.Logic.Events`

```csharp
enum ReleaseVerdict   // What <see cref="ReleaseDeduper.Admit"/> decided about one call of the game's <c>AddCpu</c>.
class ReleaseDeduper   // The rule behind <c>ModEvents.CpuReleased</c> (FR-23 items 2-3, P20): which calls of <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c> are a
    int Admitted   // Calls that were admitted (<see cref="ReleaseVerdict.Raise"/>).
    int SkippedLoading   // Calls turned away because a save was loading.
    int SkippedDuplicates   // Calls turned away because the object was seen before.
    int SkippedNoCpu   // Calls with a null CPU.
    int Clears   // How many times the set was cleared (scene changes).
    static bool IsSaveLoading(bool isLoading, bool duringFastForward)   // A save is being read: the loading screen is up and the new-game fast-forward does not run. Not a release, and not a transfer either.
    ReleaseVerdict Admit(object cpu, bool isLoading, bool duringFastForward)   // Judges one call of <c>AddCpu</c> and remembers the object (except a null). <see cref="ReleaseVerdict.Raise"/> comes at most once
    bool Contains(object cpu)   // The object was seen (admitted, or met while a save loaded) since the last <see cref="Clear"/>.
    void Clear()   // Forgets every object (the scene changed); the counters stay.
    static readonly ReferenceComparer Instance = new ReferenceComparer()
    new bool Equals(object x, object y)
    int GetHashCode(object obj)
```

## `src/PTMod/Core/Logic/Features/FeatureGraph.cs` — `PTMod.Core.Logic.Features`

```csharp
class FeatureNode   // A feature as the graph sees it: its id and the ids of the features it hard-depends on (<c>IModFeature.DependsOn</c>).
    FeatureNode(string id, IEnumerable<string> dependsOn = null)
    string Id
    IReadOnlyList<string> DependsOn
class FeatureGraph   // Dependency graph of the features (FR-3): a deterministic topological order (dependencies first),
    const string CoreFeatureId = "mod-framework"   // Id of the framework itself; the owner of every type outside <c>PTMod.Features.&lt;Name&gt;</c>.
    FeatureGraph(IEnumerable<FeatureNode> features)
    bool Contains(string id)
    int IndexOf(string id)   // Position in <see cref="Order"/>, or -1 for an unknown id.
    IReadOnlyList<string> DependenciesOf(string id)   // Declared dependencies of a feature, in declaration order (including unknown ones and the framework id). Empty for an unknown id.
    IReadOnlyList<string> DependentsOf(string id)   // Features that declare a direct dependency on <paramref name="id"/>, in ordinal order.
    IReadOnlyList<string> MissingDependenciesOf(string id)   // Declared dependencies of a feature that are not registered features, in declaration order.
    IReadOnlyList<string> CycleOf(string id)   // All members of the dependency cycle the feature belongs to (ordinal order), or an empty list.
    bool IsInCycle(string id)
    static string FeatureOfNamespace(string ns)   // Feature that owns code in the given namespace: <c>PTMod.Features.&lt;Name&gt;[.…]</c> gives the slug of
    static string PascalToSlug(string name)   // Folder/namespace name to feature slug: a hyphen goes before an upper-case letter that follows a
```

## `src/PTMod/Core/Logic/Features/FeatureStateMachine.cs` — `PTMod.Core.Logic.Features`

```csharp
enum FeatureState   // State of a feature as the hub and the services see it. <see cref="Enabled"/> is the only state in
enum FeatureReasonKind   // Why a feature is <see cref="FeatureState.Unavailable"/> or <see cref="FeatureState.Faulted"/>.
class FeatureStatus   // Immutable snapshot of one feature. <see cref="ReasonKey"/> and <see cref="ReasonArg"/> go to ModL10n
    string Id
    FeatureState State
    FeatureReasonKind ReasonKind
    IReadOnlyList<string> ReasonFeatures   // The features the reason is about: inactive or missing dependencies, or the members of the cycle; otherwise empty.
    string Detail   // Free text given to <c>MarkUnavailable</c> or <c>Fault</c> (for example "no method X (another game version?)"), or null.
    bool WantsEnabled   // What the player asked for (the hub toggle); can differ from <see cref="State"/>, for example for a dependent of a disabled feature.
    string Reason   // English reason for logs, or null when there is no problem.
class FeatureTransition   // A change of the effective state of one feature caused by a call of <see cref="FeatureStateMachine"/>.
    FeatureStatus Before
    FeatureStatus After
class FeatureStateMachine   // State model of the feature registry (FR-3). Per feature it keeps what the player asked for
    FeatureStateMachine(FeatureGraph graph, Func<string, bool> initiallyEnabled = null)   // What the player asked for at start (settings over <c>EnabledByDefault</c>); null enables everything.
    FeatureStatus StatusOf(string id)   // Status of a feature; the framework id is Enabled, an unknown id is Unavailable (NotRegistered).
    FeatureState StateOf(string id)   // State of a feature without allocating (called on hot paths such as event dispatch).
    bool IsActive(string id)   // True when the feature is Enabled (not Disabled, Faulted or Unavailable).
    string ReasonOf(string id)   // English reason for Unavailable and Faulted, otherwise null.
    IReadOnlyList<FeatureTransition> SetEnabled(string id, bool enabled)   // The player turns the feature on or off. Remembered even when the feature cannot run now (a faulted or
    IReadOnlyList<FeatureTransition> Fault(string id, string detail)   // Circuit breaker tripped: the feature is Faulted until the game restarts (no way back, <see cref="SetEnabled"/>
    IReadOnlyList<FeatureTransition> MarkUnavailable(string id, string detail)   // The feature cannot run for good (a required patch target is missing, a patch is rejected): Unavailable
```

## `src/PTMod/Core/Logic/Json/JsonArray.cs` — `PTMod.Core.Logic.Json`

```csharp
class JsonArray : JsonNode, IEnumerable<JsonNode>   // A JSON array: an ordered list of nodes. Elements are never C# null: null becomes <see cref="JsonValue.Null"/>.
    JsonArray()
    JsonArray(int capacity)
    override JsonKind Kind
    int Count
    bool TryGetValue(int index, out JsonNode value)
    JsonArray Add(JsonNode value)   // Appends an element. Returns this array for chaining (and works in a collection initializer).
    JsonArray AddRange(IEnumerable<JsonNode> values)
    JsonArray Insert(int index, JsonNode value)
    void RemoveAt(int index)
    void Clear()
    IEnumerator<JsonNode> GetEnumerator()
```

## `src/PTMod/Core/Logic/Json/JsonBinder.cs` — `PTMod.Core.Logic.Json`

```csharp
    Context(List<string> errors)
    readonly StringBuilder Path = new StringBuilder("$")
    void Error(string message)
    string Name
    Type Type
    FieldInfo Field
    PropertyInfo Property
    object Get(object instance)
    void Set(object instance, object value)
    readonly List<Member> Members = new List<Member>()
    readonly Dictionary<string, Member> ByName = new Dictionary<string, Member>(StringComparer.Ordinal)
    PlanKind Kind
    Type Concrete; // what is instantiated (List<T> for IList<T>, ...)
    Type Element; // sequence element or dictionary value type
    MethodInfo Add; // ICollection<T>.Add for sets; null when the non-generic IList is used
    string Problem
```

## `src/PTMod/Core/Logic/Json/JsonError.cs` — `PTMod.Core.Logic.Json`

```csharp
class JsonError   // Why <see cref="ModJson.TryParse"/> rejected a text, with the place of the problem. Line and column are 1-based;
    JsonError(string message, int line, int column, int offset)
    string Message
    int Line
    int Column
    int Offset
```

## `src/PTMod/Core/Logic/Json/JsonNode.cs` — `PTMod.Core.Logic.Json`

```csharp
enum JsonKind   // The kind of a JSON node: the four scalar kinds (all of them are <see cref="JsonValue"/>) plus object and array.
class JsonNode   // Base of the JSON tree built by <see cref="ModJson"/>. The hierarchy is closed: <see cref="JsonObject"/>,
    abstract JsonKind Kind
    bool IsNull
    bool IsObject
    bool IsArray
    bool IsScalar   // Scalar: null, bool, number or string.
    JsonNode DeepClone()   // Independent copy of the whole subtree (scalars are immutable and shared).
    override int GetHashCode()
    static implicit operator JsonNode(string value)
    static implicit operator JsonNode(bool value)
    static implicit operator JsonNode(ushort value)
    static implicit operator JsonNode(int value)
    static implicit operator JsonNode(uint value)
    static implicit operator JsonNode(long value)
    static implicit operator JsonNode(ulong value)
    static implicit operator JsonNode(float value)   // A float keeps its short text: 0.1f becomes 0.1, not 0.10000000149011612.
    static implicit operator JsonNode(double value)
```

## `src/PTMod/Core/Logic/Json/JsonObject.cs` — `PTMod.Core.Logic.Json`

```csharp
class JsonObject : JsonNode, IEnumerable<KeyValuePair<string, JsonNode>>   // A JSON object: string keys (ordinal, case-sensitive) to nodes, in insertion order. Parsing keeps the order of
    JsonObject()
    override JsonKind Kind
    int Count
    bool ContainsKey(string key)
    bool TryGetValue(string key, out JsonNode value)
    JsonObject Set(string key, JsonNode value)   // Sets a member: replaces the value of an existing key in place, appends a new key. Returns this object for chaining.
    JsonObject Add(string key, JsonNode value)   // Like <see cref="Set"/> but throws <see cref="ArgumentException"/> when the key exists (collection initializer: new JsonObject { { "a", 1 } }).
    bool Remove(string key)
    void Clear()
    IEnumerator<KeyValuePair<string, JsonNode>> GetEnumerator()
    bool TryGetString(string key, out string value)
    string GetString(string key, string defaultValue = null)
    bool TryGetBool(string key, out bool value)
    bool GetBool(string key, bool defaultValue = false)
    bool TryGetDouble(string key, out double value)
    double GetDouble(string key, double defaultValue = 0)
    bool TryGetInt32(string key, out int value)   // A number without fractional part inside the Int32 range; anything else (including 1.5) is "not an int".
    int GetInt32(string key, int defaultValue = 0)
    bool TryGetInt64(string key, out long value)
    long GetInt64(string key, long defaultValue = 0)
    JsonObject GetObject(string key)   // The member as an object, or null when it is missing or not an object.
    JsonArray GetArray(string key)   // The member as an array, or null when it is missing or not an array.
```

## `src/PTMod/Core/Logic/Json/JsonValue.cs` — `PTMod.Core.Logic.Json`

```csharp
class JsonValue : JsonNode   // A JSON scalar: null, bool, number or string. Immutable; <see cref="Null"/>, <see cref="True"/> and
    static readonly JsonValue Null = new JsonValue(JsonKind.Null, false, 0, null, null)
    static readonly JsonValue True = new JsonValue(JsonKind.Bool, true, 0, null, null)
    static readonly JsonValue False = new JsonValue(JsonKind.Bool, false, 0, null, null)
    override JsonKind Kind
    bool BoolValue   // The bool of a <see cref="JsonKind.Bool"/> value; false for every other kind.
    double NumberValue   // The number of a <see cref="JsonKind.Number"/> value; 0 for every other kind.
    string StringValue   // The text of a <see cref="JsonKind.String"/> value; null for every other kind.
    static JsonValue Of(bool value)
    static JsonValue Of(string value)   // A null string becomes JSON null.
    static JsonValue Of(double value)   // NaN and infinities cannot be written in JSON: they become JSON null.
    static JsonValue Of(float value)   // A float is written in its own shortest text: 0.1f becomes 0.1 (not 0.10000000149011612), so the value reads back
    static JsonValue Of(long value)
    static JsonValue Of(ulong value)
    bool TryGetInt64(out long value)   // True when this is a number with no fractional part that fits in an Int64 (exactly, for long literals).
    bool TryGetUInt64(out ulong value)
    bool TryGetInt32(out int value)
```

## `src/PTMod/Core/Logic/Json/ModJson.cs` — `PTMod.Core.Logic.Json`

```csharp
class ModJson   // The mod's own JSON: a strict RFC 8259 parser into a <see cref="JsonNode"/> tree, a writer, and a DTO binder
    const int MaxBytes = 8 * 1024 * 1024   // Largest accepted input, in bytes of UTF-8 text.
    const int MaxDepth = 64   // Deepest accepted nesting of arrays and objects (the document root counts as level 1).
    static bool TryParse(string text, out JsonNode node, out JsonError error)   // Parses a JSON document (any value, not only an object). On success returns true with the tree and a null
    static string Write(JsonNode node, bool pretty)   // JSON text of the tree: compact (<c>{"a":1}</c>) or indented by two spaces with "\n" line breaks.
    static void Write(JsonNode node, TextWriter writer, bool pretty)   // Streaming form of <see cref="Write(JsonNode, bool)"/>: no intermediate string for big documents.
    static T Bind<T>(JsonNode node, List<string> errors) where T : new()   // Builds a <typeparamref name="T"/> from the tree: public instance fields and properties with a public setter
    static object Bind(Type type, JsonNode node, List<string> errors)   // Non-generic <see cref="Bind{T}"/>; returns null when the type cannot be created at all (the error says why).
    static JsonNode From(object dto)   // Converts a DTO (or list, dictionary, scalar) to a tree: the inverse of <see cref="Bind{T}"/>, so
    static JsonNode From(object dto, List<string> errors)   // Like <see cref="From(object)"/>, with a list that receives a description of everything that could not be converted.
    Parser(string text)
    JsonError Error
    JsonNode ParseDocument()
    Writer(TextWriter writer, bool pretty)
    void WriteNode(JsonNode node, int depth)
```

## `src/PTMod/Core/Logic/L10n/L10nTables.cs` — `PTMod.Core.Logic.L10n`

```csharp
class L10nMismatchReport   // Result of <see cref="L10nTables.Compare"/>: which keys one language has and the other lacks (FR-8 item 6),
    string LanguageA
    string LanguageB
    IReadOnlyList<string> MissingInA   // Logical keys that <see cref="LanguageB"/> has and <see cref="LanguageA"/> lacks, sorted.
    IReadOnlyList<string> MissingInB   // Logical keys that <see cref="LanguageA"/> has and <see cref="LanguageB"/> lacks, sorted.
    IReadOnlyList<string> IncompletePlurals   // Plural groups that lack a form their language needs, as "language: key.form": the key with the missing suffix
    IReadOnlyList<string> ToLines()   // One English line per finding, for the log and the Diagnostics tab: missing keys first, then incomplete plurals.
class L10nTables   // The string tables of <c>ModL10n</c> (FR-8): language -> key -> text, loaded in layers. Later layers win, so the
    string Text
    string Source
    readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal)
    int Count(string language)   // Number of keys of the language; 0 for an unknown one.
    bool Contains(string language, string key)
    bool TryGet(string language, string key, out string text)   // The raw text (with placeholders, plural pipes, nothing resolved); false when the language has no such key.
    string SourceOf(string language, string key)   // The label of the file the key came from (the winning layer), or null when the key is absent. For diagnostics and prefix checks.
    IReadOnlyList<string> Keys(string language)   // Snapshot of the keys of the language, sorted ordinally; empty for an unknown language.
    bool Set(string language, string key, string text, string source = null)   // Adds (or replaces) one key. Returns false for a blank key or language, or a null text.
    int AddTable(string language, string source, JsonNode table, List<string> errors)   // Adds the entries of a parsed table file on top of what is there (same key: the new text wins). The node must be an object
    int AddJson(string language, string source, string jsonText, List<string> errors)   // Parses the text of a table file and adds it like <see cref="AddTable(string, string, JsonNode, List{string})"/>; a syntax error is reported with its line and column and adds nothing.
    static bool TryParseTablePath(string path, out string language, out string featureId)   // Reads the language and the optional feature out of a table file path, as a built-in resource name or a path relative
    L10nMismatchReport Compare(string languageA, string languageB)   // Keys that exist in one language and not in the other (FR-8 item 6; <c>ModSelfCheck</c> prints
    static bool TrySplitPluralKey(string key, out string baseKey, out PluralForm form)   // Splits <c>core.mail.daysLeft.few</c> into the base <c>core.mail.daysLeft</c> and the form. False when the key does not end
```

## `src/PTMod/Core/Logic/L10n/LocTextResolver.cs` — `PTMod.Core.Logic.L10n`

```csharp
class LocText   // A user-visible text of the mod: a table key, inline Russian and English text, or both. In JSON content it is written as
    string Key   // Key of the string tables (for example <c>es.event.title</c>); null or empty when the text is inline only.
    string Ru   // Inline Russian text; null or empty when there is none.
    string En   // Inline English text; null or empty when there is none.
    LocText()
    LocText(string key, string ru, string en)
    static LocText FromKey(string key)   // A text that lives in the string tables.
    static LocText Of(string ru, string en)   // An inline text in both languages.
    string InlineFor(string language)   // The inline text of a language ("ru" or "en", case-insensitive); null for any other language or when it is empty.
    static LocText FromJson(JsonNode node, string path, List<string> errors)   // Reads a text from JSON content: an object with any of <c>key</c>, <c>ru</c>, <c>en</c> (strings; unknown members are ignored,
    JsonObject ToJson()   // The JSON form read by <see cref="FromJson"/>: only the parts that are set, in the order key, ru, en.
class LocTextResolver   // The text lookup of <c>ModL10n</c> (FR-8) over loaded <see cref="L10nTables"/>, without Unity.
    const string DefaultLanguage = "ru"   // The mod's own default language, independent of the game's setting (FR-8 item 7).
    const string FallbackLanguage = "en"   // The first fallback language.
    LocTextResolver(L10nTables tables, Action<string> warn = null)
    event Action LanguageChanged   // Raised after <see cref="Language"/> changed. A throwing subscriber is reported and does not stop the others.
    string L(string key, params object[] args)   // The text of the key with <c>{0}..{n}</c> filled in; <c>[key]</c> (and one warning) when no language has it.
    string Plural(string key, long n, params object[] args)   // The form of the key that fits <paramref name="n"/> (AC-8.1: <c>Plural("core.mail.daysLeft", 22)</c> gives "22 дня").
    string Resolve(LocText text, params object[] args)   // A <see cref="LocText"/> as a string, by the same chain as <see cref="L"/>: per language the table entry of <see cref="LocText.Key"/>,
    bool Has(string key)   // True when the key (or a plural group with that base) exists in any language of the chain.
```

## `src/PTMod/Core/Logic/L10n/PluralRules.cs` — `PTMod.Core.Logic.L10n`

```csharp
enum PluralForm   // The plural categories the mod uses (a subset of CLDR): ru has One/Few/Many, en (and everything else) One/Other.
class PluralRules   // Plural rules of <c>ModL10n.Plural</c> (FR-8): which form a whole number takes in a language, how a form is spelled
    static PluralForm Select(string language, long n)   // The form that <paramref name="n"/> takes in <paramref name="language"/> (see the class summary).
    static IReadOnlyList<PluralForm> FormsOf(string language)   // The forms a complete table of the language provides: ru One/Few/Many, otherwise One/Other. Shared array: do not modify.
    static IReadOnlyList<PluralForm> Candidates(PluralForm form)   // Forms to try, in order, for a requested form when the table of the language lacks some of them:
    static string Suffix(PluralForm form)   // Spelling of a form in a key suffix: "one", "few", "many", "other".
    static bool TryParseSuffix(string suffix, out PluralForm form)   // Parses a key suffix (case-sensitive, lower case only, as keys are); false for anything but one/few/many/other.
    static int PartIndex(PluralForm form, int partCount)   // Position of a form in a pipe-separated text (<c>{0} день|{0} дня|{0} дней</c>) with <paramref name="partCount"/> parts:
    static string NormalizeLanguage(string language)   // The language as tables and settings spell it: trimmed and lower case ("RU" -> "ru"). A blank value gives an empty string.
    static bool IsSlavic(string language)   // True for the languages with the one/few/many rule (ru, uk, be, with or without a region: "ru-RU").
```

## `src/PTMod/Core/Logic/Log/LogFormatter.cs` — `PTMod.Core.Logic.Log`

```csharp
enum LogLevel   // Severity of a ModLog record. <see cref="Debug"/> records are written only when verbose logging
class LogFormatter   // Pure text rules of ModLog: the line format <c>[PTMod] [&lt;feature-id&gt;] LEVEL message</c>,
    const string Prefix = "[PTMod]"   // Fixed prefix of every line the mod writes.
    const string CoreFeatureId = "mod-framework"   // Feature id used for records of the framework itself and for records without a feature.
    static string LevelName(LogLevel level)   // Upper-case level name as it appears in the line.
    static string NormalizeFeatureId(string featureId)   // Feature id as written in the line and used in repeat keys: trimmed; null or blank becomes the
    static string Format(string featureId, LogLevel level, string message)   // <c>[PTMod] [id] LEVEL message</c>; a null or empty message leaves just the header.
    static string Format(string featureId, LogLevel level, string message, Exception exception, bool includeStackTrace)   // Line with an exception: <c>[PTMod] [id] ERROR message: Type: text &lt;- InnerType: text</c>.
    static string DescribeException(Exception exception)   // "Type: message" of the exception followed by up to three inner exceptions, joined with " &lt;- ".
    static string CollapseDigits(string message)   // Message text with every run of digits replaced by one <c>#</c>, so "tick 5 failed" and
    static string RepeatKey(string featureId, string message)   // Key under which the repeat limiter counts a message: normalized feature id + text without numbers.
    static string RepeatedPhrase(int count)   // The wording of every repeat summary: "повторено 597 раз", "повторено 4 раза" (AC-2.2, AC-7.1). Russian plural of "раз":
    static string FormatSummary(string featureId, LogLevel level, string message, int suppressed)   // Summary line written after a repeat window: the message with "(повторено N раз)" appended; N = suppressed repeats.
    static string FormatSummary(RepeatSummary summary)   // Summary line for a closed repeat window of <see cref="RepeatLimiter"/>.
```

## `src/PTMod/Core/Logic/Log/ProblemBuffer.cs` — `PTMod.Core.Logic.Log`

```csharp
class LogEntry   // One record of ModLog as kept for the "Diagnostics" tab (<c>ModLog.RecentProblems</c>). Immutable.
    LogEntry(LogLevel level, string featureId, string message, double timeSeconds, string details = null, int suppressedCount = 0)
    LogLevel Level
    string FeatureId   // Normalized feature id (never null).
    string Message   // Message text without the <c>[PTMod] [id] LEVEL</c> header.
    double TimeSeconds
    string Details   // Exception description for the tab, or null.
    int SuppressedCount
class ProblemBuffer   // Ring buffer of the latest warnings and errors (default 50, <c>log.recentProblems</c>). When full, the
    const int DefaultCapacity = 50
    const int MaxCapacity = 1000
    ProblemBuffer(int capacity = DefaultCapacity)
    bool Add(LogEntry entry)   // Stores a warning or an error; returns false for null, Debug and Info entries.
    IReadOnlyList<LogEntry> Snapshot()   // Copy of the stored entries, oldest first. Later changes of the buffer do not affect it.
    void Clear()
    void Resize(int capacity)   // Applies a new capacity (<c>log.recentProblems</c>), clamped to 1..<see cref="MaxCapacity"/>; never throws.
```

## `src/PTMod/Core/Logic/Log/RepeatLimiter.cs` — `PTMod.Core.Logic.Log`

```csharp
class RepeatSummary   // Closed (or force-closed) repeat window of one message: how many repeats were NOT written.
    RepeatSummary(string featureId, LogLevel level, string message, int suppressed, double windowStartSeconds, double windowSeconds)
    string FeatureId
    LogLevel Level   // Level of the first record of the window; the summary line is written at the same level.
    string Message   // Text of the first record of the window (the one that was written).
    int Suppressed   // Number of suppressed repeats ("повторено N раз").
    double WindowStartSeconds   // Time of the first record of the window, in the caller's clock.
    double WindowSeconds
struct RepeatDecision   // Answer of <see cref="RepeatLimiter.Evaluate"/>: write the record or not, and a summary of the
    RepeatDecision(bool write, int occurrence, RepeatSummary summaryBefore)
    bool Write   // True: write the record. False: it is a suppressed repeat, drop it.
    int Occurrence   // 1-based number of this record within its window.
    RepeatSummary SummaryBefore   // Summary of the window that just ended for this message, or null. Write it before the record.
class RepeatLimiter   // Repeat suppression of ModLog (FR-7): the same message (feature + text with numbers collapsed, see
    const int DefaultLimit = 3
    const double DefaultWindowSeconds = 60.0
    const int MaxTrackedKeys = 512   // Bound on tracked messages. Past it, expired windows are dropped first and then the oldest quarter;
    string FeatureId
    LogLevel Level
    string Message
    double Start
    int Seen
    int Suppressed
    RepeatLimiter(int limit = DefaultLimit, double windowSeconds = DefaultWindowSeconds)
    void Configure(int newLimit, double newWindowSeconds)   // Applies settings (<c>log.repeatLimit</c>, <c>log.repeatWindowSeconds</c>); never throws: a limit below 1
    RepeatDecision Evaluate(string featureId, LogLevel level, string message, double nowSeconds)   // Registers one record and decides whether it is written.
    IReadOnlyList<RepeatSummary> Flush(double nowSeconds, bool force = false)   // Summaries of windows that ended by <paramref name="nowSeconds"/> (and of windows dropped by eviction);
```

## `src/PTMod/Core/Logic/Mail/InboxOrder.cs` — `PTMod.Core.Logic.Mail`

```csharp
class InboxOrder   // The order of the mail list (FR-14 item 11, AC-14.10), as a pure function: newest date first; for equal dates the mail with the
    static int Compare(DateTime dateA, int idA, DateTime dateB, int idB)   // Negative when mail A goes above mail B, positive when below, 0 when the keys are equal.
    static int[] Order(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)   // The new order: element <c>k</c> of the result is the index (in the input lists) of the mail that belongs at position <c>k</c>.
    static int[] Positions(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)   // The inverse of <see cref="Order"/>: element <c>i</c> of the result is the position that mail <c>i</c> (in the input lists) gets,
    static bool IsOrdered(IReadOnlyList<DateTime> dates, IReadOnlyList<int> ids)   // True when the list is already in the right order (the sorter then skips the work and leaves the list alone).
```

## `src/PTMod/Core/Logic/Mail/MailPriority.cs` — `PTMod.Core`

```csharp
enum MailPriority   // Delivery priority of a mod mail (FR-14 item 8). Delivery order is <see cref="Critical"/>, <see cref="Normal"/>, <see cref="Low"/>;
```

## `src/PTMod/Core/Logic/Mail/MailQueue.cs` — `PTMod.Core.Logic.Mail`

```csharp
class MailQueue   // The store and the delivery queue of mod mail (FR-14), without Unity: every mod mail ever sent, in the order it was sent (which is the
    const int DefaultQueueLimit = 200   // The setting <c>mail.queueLimit</c> by default.
    const int InvalidId = -1   // Returned by <see cref="Enqueue"/> when the mail was not stored.
    MailQueue(int queueLimit = DefaultQueueLimit, MailQuota quota = null, Action<string> warn = null)
    MailQuota Quota   // The daily limits and counters; change the limits with <see cref="MailQuota.Configure"/>.
    void SetQueueLimit(int limit)   // Changes the limit of the queue; a queue that is already longer is cut at once (see the class remarks).
    int Enqueue(MailRecordModel record, DateTime today)   // Puts a mail into the queue: checks it (<see cref="MailRecordModel.Validate"/>), gives it the next id, stamps <see cref="MailRecordModel.SentOn"/>
    IReadOnlyList<MailRecordModel> DeliverDue(DateTime today, bool gameReady, bool isBatchRun)   // The delivery pass of one game day. Returns the mails that are delivered now, in delivery order, with
    MailRecordModel Get(int mailId)   // The mail with this id, delivered or queued; null for an unknown id.
    MailRecordModel Find(string featureId, string tag)   // The latest mail (the highest id, delivered or queued) of the feature with exactly this tag; null when there is none or the
    IReadOnlyList<MailRecordModel> FindAll(string featureId, string tagPrefix = null)   // The mails of the feature, delivered and queued, in sending order; with a prefix only those whose tag starts with it (ordinal,
    IReadOnlyList<MailRecordModel> GetQueued()   // The mails waiting for delivery in sending order (a snapshot).
    IReadOnlyList<MailRecordModel> GetDelivered()   // The delivered mails in sending order (a snapshot).
    MailStoreDto ToDto()   // The <c>mail</c> section: <c>nextId</c>, the delivered mails in <c>mails</c> and the queued ones in <c>queue</c>, each in sending
    void Load(MailStoreDto dto)   // Replaces everything with the content of the <c>mail</c> section (a null section means an empty store). A record that breaks
    void Clear()   // Forgets every mail and the daily counters (new game, exit to the menu, before a save is loaded). The limits stay.
```

## `src/PTMod/Core/Logic/Mail/MailQuota.cs` — `PTMod.Core.Logic.Mail`

```csharp
class MailQuota   // The daily delivery limits of mod mail (FR-14 item 8): at most <see cref="MaxPerDay"/> mails of <see cref="MailPriority.Normal"/> and
    const int DefaultMaxPerDay = 3
    const int DefaultMaxPerFeature = 2
    const int LimitCeiling = 1000   // Limits are clamped to [1, <see cref="LimitCeiling"/>]; a zero limit would hold every non-critical mail forever.
    MailQuota(int maxPerDay = DefaultMaxPerDay, int maxPerFeature = DefaultMaxPerFeature)
    int MaxPerDay   // Non-critical mails per game day, all features together.
    int MaxPerFeature   // Non-critical mails per game day from one feature.
    void Configure(int maxPerDay, int maxPerFeature)   // Changes both limits (settings changed in the hub); the counters stay.
    int DeliveredTodayBy(string featureId)   // Non-critical mails of the feature counted for <see cref="Day"/>.
    void StartDay(DateTime today)   // Makes <paramref name="today"/> (the time part is ignored) the day of the counters: another date than <see cref="Day"/> clears
    bool Allows(MailPriority priority, string featureId)   // True when one more mail of this priority from this feature may be delivered today.
    void Record(MailPriority priority, string featureId)   // Counts a delivered mail (nothing for <see cref="MailPriority.Critical"/>).
    void Reset()   // Forgets the day and all counters (new game, exit to the menu, before a save is loaded).
```

## `src/PTMod/Core/Logic/Mail/MailRecordModel.cs` — `PTMod.Core.Logic.Mail`

```csharp
class MailChoiceModel   // A one-shot answer button of a mail (the logic side of <c>ModMailChoice</c>): the first click resolves the mail.
    string Id
    string LabelKey
    LocText Label
    string TooltipKey
    bool Enabled   // False: the button is inactive and shows <see cref="DisabledReasonKey"/> as a hint and under the button.
    string DisabledReasonKey
class MailActionModel   // A button of a mail that calls the feature and does not close the mail (the logic side of <c>ModMailAction</c>).
    string Id
    string LabelKey
    LocText Label
    string TooltipKey
    bool Enabled
    string DisabledReasonKey
    bool Repeatable   // False: the action becomes inactive after the first click (<see cref="Used"/>).
    bool Used   // The action was clicked at least once.
class MailRecordModel   // One mod mail without Unity or game objects (FR-14): who sent it, the text as keys and arguments, priority, choices and actions,
    const int FirstId = 100000   // Lowest mail id; mirrors <c>ModMail.FirstModMailId</c>. ID 0 is the vanilla Year Summary.
    const int MaxTimeoutDays = 36500   // Longest answer period accepted (100 years); keeps date arithmetic far from <see cref="DateTime.MaxValue"/>.
    const string ResolutionPending = "Pending"
    const string ResolutionPlayer = "Player"
    const string ResolutionTimeout = "Timeout"
    const string ResolutionProgrammatic = "Programmatic"
    int MailId   // Mail id (>= <see cref="FirstId"/>), assigned by the queue; 0 while the record is not in a queue.
    string FeatureId   // Id of the sending feature.
    string Tag   // <c>&lt;prefix&gt;:&lt;...&gt;</c>; tells the mails of one feature apart and is what <c>Find</c> looks for.
    MailPriority Priority
    string TitleKey
    string BodyKey
    LocText Title
    LocText Body
    List<MailChoiceModel> Choices
    List<MailActionModel> Actions
    int TimeoutDays   // Answer period in days counted from the delivery date; 0 = no deadline.
    string DefaultChoiceId   // The choice applied when the deadline passes; required when <see cref="TimeoutDays"/> is above 0.
    DateTime SentOn   // The day the feature sent the mail (the date of the <c>Enqueue</c> call).
    DateTime? DeliveredOn   // The day the mail was delivered; null while it waits in the queue.
    DateTime? Deadline   // <see cref="DeliveredOn"/> + <see cref="TimeoutDays"/>; null without a deadline or before delivery.
    bool Read
    string ChoiceId   // The id of the chosen answer; null while the mail is unanswered.
    string Resolution   // One of the <c>Resolution*</c> constants: who resolved the mail.
    string StatusLine   // The line under the text (a feature timer); null = none.
    bool IsExpired(DateTime today)   // True for a delivered, unanswered mail whose deadline is <paramref name="today"/> or earlier (the default choice is due).
    MailChoiceModel FindChoice(string id)
    MailActionModel FindAction(string id)
    string Validate()   // Checks a mail that a feature wants to send. Returns null when it is acceptable, otherwise the first problem (English, one line):
    MailRecordDto ToDto()   // The side file form of the record (see the class remarks for the queued case). Empty strings and empty lists are written as null,
    static MailRecordModel FromDto(MailRecordDto dto, bool queued, Action<string> warn)   // Reads one record of the side file. <paramref name="queued"/> tells which list it came from (<c>queue</c> or <c>mails</c>).
```

## `src/PTMod/Core/Logic/Modifiers/BalanceRange.cs` — `PTMod.Core.Logic.Modifiers`

```csharp
struct BalanceRange : IEquatable<BalanceRange>   // Balance range of a channel (FR-12 item 18): the product of all multipliers on one company x CPU x segment is
    static readonly BalanceRange None = new BalanceRange()   // No balance range.
    BalanceRange(float min, float max)
    float Min
    float Max
    static BalanceRange FromSpec(ChannelSpec spec)   // The range of a channel declaration (<see cref="ChannelSpec.BalanceMin"/>, <see cref="ChannelSpec.BalanceMax"/>).
    float Clamp(float value)   // The value limited to the range; returned unchanged when there is no range.
    bool IsCapped(float value)   // True when there is a range and the value lies outside it (what the UI shows as "(capped)").
    override int GetHashCode()
```

## `src/PTMod/Core/Logic/Modifiers/ChannelFamilies.cs` — `PTMod.Core.Logic.Modifiers`

```csharp
class ChannelFamilies   // Channel names and families (FR-12 items 1, 15). A family is declared with a name ending in ".*"
    const string Wildcard = ".*"   // The suffix that makes a name a family.
    const string GpuDemandFamily = "gpu.demand.*"   // Family of the GPU market segments (owner: gpu-product-line).
    static bool IsFamilyName(string name)   // True for a non-empty prefix followed by ".*" and no other star: "gpu.demand.*".
    static string PrefixOf(string familyName)   // The text a family's channels start with ("gpu.demand.*" gives "gpu.demand."); null if it is not a family name.
    static bool Matches(string familyName, string channel)   // True when <paramref name="channel"/> is a plain name below the family: the prefix and then at least one more character.
    static bool IsValidChannelName(string name, bool allowFamily)   // Syntax of a channel name: letters, digits, '.', '_' and '-', no empty parts ("a..b", ".a", "a."), at most
    static string MarketDemand(Segment segment)   // Channel of the demand for one CPU segment: "market.demand.highend" and so on; null for <see cref="Segment.Any"/> and unknown values.
    static string GpuDemand(string segmentId)   // Channel of one GPU market segment, "gpu.demand.&lt;segmentId&gt;" (a member of the <see cref="GpuDemandFamily"/>
```

## `src/PTMod/Core/Logic/Modifiers/ChannelRegistry.cs` — `PTMod.Core.Logic.Modifiers`

```csharp
class ChannelInfo   // One row of the "Channels" block of the diagnostics tab: a declared channel (or family) or an undeclared name that has entries or providers.
    string Name   // The declared name (for a family, the family name such as "gpu.demand.*"), or the undeclared channel name.
    ChannelSpec Spec   // A copy of the declaration, or null for a channel without one.
    int EntryCount   // Entries on the channel (all features, applied or not); for a family, on all its members.
    int ProviderCount
class ChannelRegistry   // The channel engine of <c>ModModifiers</c> (FR-12) without any game type: channel declarations, saved entries,
    const float DefaultFuseMin = 0.1f   // Lower bound of the fuse of an undeclared channel.
    const float DefaultFuseMax = 10f   // Upper bound of the fuse of an undeclared channel.
    const int MaxEvaluationDepth = 8   // How deep evaluations may nest (a provider asking for another channel is fine, asking for its own is not).
    const int MaxDurationDays = 100000   // Longest duration of a temporary entry, in days.
    string FeatureId
    string Channel
    Func<ModifierQuery, float> Plain
    Func<ModifierQuery, ProviderResult> Reasoned
    string ReasonKey
    object Identity
    readonly string Name
    ModifierEntry[] Entries = NoEntries
    ProviderRecord[] Providers = NoProviders
    ChannelSpec Spec; // resolved declaration; null when there is none (valid only if SpecVersion is current)
    int SpecVersion = -1
    Bucket(string name)
    ChannelRegistry(Func<string, bool> isFeatureActive = null, DateTime today = default(DateTime))   // Tells whether a feature is enabled (its entries and providers apply); null means every feature is. Called
    event Action<string> ChannelChanged   // The result of a channel may have changed: an entry or a provider came or went, an entry ended, the channel was
    event Action<string, string> Warning   // A rejected call or an anomaly, as (feature id or null, English text). The host writes it to ModLog.
    event Action<string, string, Exception> ProviderFailed   // A provider threw or returned an unusable number: (feature id, channel, exception). The value used was x1.
    bool Declare(ChannelSpec spec)   // Declares a channel or a family. Accepted when the declaration is valid and the name is new; a repeat with the same
    bool IsDeclared(string channel)   // True when a declaration covers the name: the exact name or a family.
    ChannelSpec SpecOf(string channel)   // A copy of the declaration that covers the name (for a family member, the family's), or null.
    IReadOnlyList<ChannelSpec> DeclaredChannels()   // Copies of all declarations (families included), by name.
    IReadOnlyList<string> UndeclaredChannels()   // Names that have entries or providers but no declaration (a typo or a feature that never declared its channel), sorted.
    IReadOnlyList<ChannelInfo> Describe()   // Rows for the diagnostics tab: every declared channel with its numbers, plus the undeclared names that are in use; sorted by name.
    ModifierEntry Add(string featureId, string channel, ModifierScope scope, float multiplier,   // Adds a saved entry. <paramref name="durationDays"/> 0 means unlimited; otherwise the entry stops applying on
    ModifierEntry Restore(string id, string featureId, string channel, ModifierScope scope, float multiplier,   // Puts back an entry read from a save, with its own id and end date. Returns null when the data is invalid or the id is
    bool Remove(string entryId)   // Removes one entry by id; false if there is none.
    int RemoveAll(string featureId, string channel = null)   // Removes the entries of a feature, all of them or those of one channel; returns how many.
    int ClearEntries()   // Removes every entry (a new game or a save is about to be loaded); providers and declarations stay. The id counter starts again at 1.
    ModifierEntry Find(string entryId)   // The entry with this id, or null.
    IReadOnlyList<ModifierEntry> EntriesOf(string channel)   // Entries of one channel (a snapshot, in the order they were added).
    bool RegisterProvider(string featureId, string channel, Func<ModifierQuery, float> provider, string reasonKey = null, object identity = null)   // Registers a provider: a function of the query that returns a multiplier. Not saved; the feature registers it again at
    bool RegisterReasonedProvider(string featureId, string channel, Func<ModifierQuery, ProviderResult> provider, object identity = null)   // Registers a provider that returns a reason with every call (<see cref="ProviderResult"/>). The rest is as for <see cref="RegisterProvider"/>.
    int RemoveProviders(string featureId, string channel = null)   // Removes the providers of a feature, all of them or those of one channel; returns how many. Needed by tests and by hot reload, not by enabling and disabling.
    int ProvidersOf(string featureId, string channel = null)   // Number of providers a feature has registered, on all channels or on one.
    int ProviderFaults(string featureId)   // How often providers of a feature failed (threw or returned an unusable number) since start.
    bool HasAny(string channel)   // True when something is registered on the channel (an entry or a provider of any feature, applying to this query or not): the fast path of the patches.
    float Get(string channel, ModifierQuery query)   // The multiplier of a channel for a query (see the class remarks). 1 for a channel with nothing registered, without
    ModifierBreakdown Explain(string channel, ModifierQuery query)   // The same computation as <see cref="Get"/> with the list of multipliers behind it (those equal to 1 are left out), the
    IReadOnlyList<ModifierEntry> SetToday(DateTime day)   // Moves the registry to a game day: removes the entries whose end date has come (returned, so that the host can log them)
    void Invalidate(string channel = null)   // Drops the cached values of one channel, or of all channels when null (FR-12 item 17), and raises <see cref="ChannelChanged"/>.
    void Invalidate(string channel, string companyKey)   // Drops the cached values of one company in a channel (channel null: in all channels). Other companies keep theirs.
    void FeatureStateChanged(string featureId = null)   // A feature was enabled, disabled, faulted or became unavailable (<c>ModFeatures.StateChanged</c>): drops the cached values of
```

## `src/PTMod/Core/Logic/Modifiers/ChannelSpec.cs` — `PTMod.Core`

```csharp
class ChannelSpec   // Declaration of one channel (an entry of <c>core.json</c>, FR-12 items 1, 15, 18): the safety fuse
    string Name   // Exact name ("research.speed") or a family with the suffix ".*" ("gpu.demand.*").
    float Min, Max   // Safety fuse: the final multiplier never leaves [Min, Max].
    float BalanceMin, BalanceMax   // Balance range of the product (applied before the fuse); 0 and 0 mean "none". [0.5; 2.0] for market.rating.cpu and popularity.gain.
    bool Global, PerCompany, PerCpu, PerSegment   // Documentation of the dimensions the value depends on: one value for the whole game, per company, per CPU, per segment.
    bool Cacheable = true   // False: the value is never cached (<c>development.cost.design</c> changes on every move of the designer slider).
    string OwnerFeature   // "mod-framework" or null for the standard channels; the feature id for the channels of features (market-shocks, gpu-product-line, rival-acquisitions).
    string DescriptionKey   // ModL10n key of the description for the diagnostics block.
    ChannelSpec Clone()
    bool Validate(out string error)   // Checks the declaration: a valid name, a finite fuse with 0 &lt;= Min &lt;= Max and Max &gt; 0, and a balance
    bool SameBounds(ChannelSpec other)   // True when both declarations limit the channel in the same way (fuse, balance range, caching). A repeated
```

## `src/PTMod/Core/Logic/Modifiers/ModifierBreakdown.cs` — `PTMod.Core`

```csharp
struct AppliedModifier   // One multiplier that took part in a value, as <c>Explain</c> reports it (FR-12 item 9): the source, the reason
    AppliedModifier(string featureId, string entryId, string reasonKey, string[] reasonArgs, float multiplier, DateTime? expiresOn)
    string FeatureId   // Id of the feature that registered the entry or the provider (the source shown to the player).
    string EntryId   // Id of the saved entry, or null when the multiplier comes from a provider.
    string ReasonKey   // ModL10n key of the reason, or null ("no reason": the UI shows the multiplier and the source only).
    string[] ReasonArgs   // Arguments of <see cref="ReasonKey"/>; never null.
    float Multiplier
    DateTime? ExpiresOn   // First day the multiplier no longer applies; null for providers and unlimited entries.
class ModifierBreakdown : IReadOnlyList<AppliedModifier>   // Breakdown of a channel value for tooltips (<c>research-tree-ux</c>, <c>contract-transparency</c>): the applied
    const string CappedKey = "core.explain.capped"   // ModL10n key of the "(capped)" suffix.
    string Channel
    float Raw   // The product of all multipliers before the balance range and the fuse.
    float Total   // The value after the balance range and the fuse; equal to what <c>Get</c> returns for the same query.
    float BalanceMin   // Bounds of the balance range; 0 and 0 when the channel has none.
    float BalanceMax
    bool IsBalanceCapped   // The product left the balance range and was cut: show "(capped)".
    bool IsFuseCapped   // The value left the fuse range and was cut.
    float FuseMin
    float FuseMax
    bool IsDeclared   // False for a channel that is not declared in the registry (the default fuse was used).
    IEnumerator<AppliedModifier> GetEnumerator()
```

## `src/PTMod/Core/Logic/Modifiers/ModifierCache.cs` — `PTMod.Core.Logic.Modifiers`

```csharp
class ModifierCache   // Cache of channel values for one game day (FR-12: "result is cached until the end of the game day"). The key is
    const int MaxEntriesPerChannel = 50000   // Bound on cached values of one channel. A channel that reaches it is emptied: a day has far fewer distinct keys.
struct Key : IEquatable<Key>   // Everything besides the channel that a cached value depends on (<see cref="ModifierQuery"/> without the design flag and the host context).
    Key(string companyKey, int cpuSaveId, Segment segment, int contractId)
    string CompanyKey
    int CpuSaveId
    Segment Segment
    int ContractId
    static Key Of(ModifierQuery query)
    override int GetHashCode()
    int Hits   // Lookups that found a value since creation (diagnostics and tests).
    int Misses   // Lookups that found nothing.
    int CountOf(string channel)   // Number of cached values of one channel.
    bool TryGet(string channel, Key key, out float value)
    void Store(string channel, Key key, float value)
    void Invalidate(string channel)   // Drops every value of one channel.
    void Invalidate(string channel, string companyKey)   // Drops the values of one company (key null: the values asked without a company) in one channel, or in every channel
    void Clear()   // Drops everything (a new day, a change of feature states).
```

## `src/PTMod/Core/Logic/Modifiers/ModifierEntry.cs` — `PTMod.Core.Logic.Modifiers`

```csharp
class ModifierEntry   // One saved modifier (FR-12): a multiplier on a channel for a scope, owned by a feature, optionally limited in time
    string Id   // Unique id inside the registry ("m1", "m2"...); the key of <c>Remove</c>.
    string FeatureId   // Id of the feature that owns the entry (the feature whose state decides whether it applies).
    string Channel
    ModifierScope Scope
    float Multiplier   // Finite, 0 or more.
    DateTime? ExpiresOn   // First day on which the entry no longer applies, or null for an unlimited entry.
    string ReasonKey   // ModL10n key of the reason shown by Explain, or null.
    string[] ReasonArgs   // Arguments of <see cref="ReasonKey"/>; never null, never changes (do not modify the array).
    bool IsExpiredOn(DateTime day)   // True when the entry does not apply on <paramref name="day"/> because its end date has come: the end date itself is already outside.
```

## `src/PTMod/Core/Logic/Modifiers/ModifierScope.cs` — `PTMod.Core`

```csharp
enum ScopeKind   // Who a modifier entry applies to (FR-12 item 4).
struct ModifierScope : IEquatable<ModifierScope>   // Applicability of one entry: a <see cref="ScopeKind"/> and an optional segment filter. Keys are the company keys
    static ModifierScope Global(Segment segment = Segment.Any)
    static ModifierScope AllCompanies(Segment segment = Segment.Any)
    static ModifierScope Player(Segment segment = Segment.Any)
    static ModifierScope AIOnly(Segment segment = Segment.Any)
    static ModifierScope Company(string companyKey, Segment segment = Segment.Any)
    static ModifierScope Cpu(int cpuSaveId, Segment segment = Segment.Any)
    static bool TryCreate(string kindName, string companyKey, int cpuSaveId, int segment, out ModifierScope scope, out string error)   // Builds a scope from the fields of a saved entry (<c>scope</c>, <c>company</c>, <c>cpu</c>, <c>segment</c> of
    bool Validate(out string error)   // Checks what the factory methods do not: a Company scope needs a valid key (FR-13: <c>player</c> or <c>ai:&lt;id&gt;</c>), a Cpu scope a SaveID of 0 or more,
    bool Matches(ModifierQuery query)   // Rules of FR-12: Global is always true; AllCompanies needs a company key; Player is the key "player"; AIOnly is a key
    override int GetHashCode()
struct ModifierQuery   // What a formula asks the registry about (the game-free part of <c>ModifierContext</c>): the company key
    const int None = -1   // The value of "no CPU" and "no contract".
    ModifierQuery(string companyKey, int cpuSaveId = None, Segment segment = Segment.Any, int contractId = None,
    ModifierQuery WithCompany(string key)
    ModifierQuery WithCpu(int cpuSaveId)
    ModifierQuery WithSegment(Segment segment)
    ModifierQuery WithContract(int contractId)
    ModifierQuery AsDesign()
    ModifierQuery WithContext(object hostContext)
```

## `src/PTMod/Core/Logic/Modifiers/ProviderResult.cs` — `PTMod.Core`

```csharp
struct ProviderResult   // What a provider returns when it wants to explain its multiplier (FR-12 item 14): the multiplier and, for
    float Multiplier
    string ReasonKey   // ModL10n key of the reason (for example "press.mod.hype"), or null. Overrides the key given at registration.
    string[] ReasonArgs   // Arguments {0}, {1}... of <see cref="ReasonKey"/>, or null.
    ProviderResult(float multiplier, string reasonKey = null, string[] reasonArgs = null)
    static implicit operator ProviderResult(float multiplier)
```

## `src/PTMod/Core/Logic/Modifiers/Segment.cs` — `PTMod.Core`

```csharp
enum Segment   // Market segment of a CPU (FR-12, FR-23). <see cref="Any"/> is the wildcard: as a scope or channel filter it
```

## `src/PTMod/Core/Logic/Random/RngStreams.cs` — `PTMod.Core.Logic.Rng`

```csharp
class RngStreams   // The named random streams of one game (FR-9): a world seed plus a table stream id -> <see cref="Xoshiro128"/>.
    RngStreams(uint worldSeed)
    bool Contains(string streamId)
    IModRng Get(string streamId)   // The stream with this id; created at its initial state (from the world seed) on first use, the same object after.
    void Reset(uint newWorldSeed)   // New game: switches to a new world seed and puts every existing stream back to its initial state for that seed
    Dictionary<string, uint[]> ExportStates()   // The state of every stream, ready for a save section: id -> four words. Keys are in ordinal order (the dictionary
    IReadOnlyList<string> Restore(uint savedWorldSeed, IEnumerable<KeyValuePair<string, uint[]>> savedStates)   // Loads a save: takes <paramref name="savedWorldSeed"/> and the saved stream states. After the call every stream
    static uint DeriveStreamSeed(uint worldSeed, string streamId)   // Seed of the stream <paramref name="streamId"/> in the world <paramref name="worldSeed"/>: the seed XOR <see cref="Fnv1a"/> of the id.
    static uint Fnv1a(string text)   // FNV-1a, 32 bit, over the UTF-8 bytes of the text (offset basis 2166136261, prime 16777619). UTF-8 and not the
    static uint SeedFromClock(long ticks)   // A world seed from a clock reading (for a new game without a seed set by a scenario): the 64-bit tick count
```

## `src/PTMod/Core/Logic/Random/SplitMix32.cs` — `PTMod.Core.Logic.Rng`

```csharp
class SplitMix32   // SplitMix32: a 32-bit counter that moves by the golden-ratio constant 0x9E3779B9 and is scrambled by a two-round
    SplitMix32(uint seed)
    uint Next()   // The next 32-bit output.
    static uint Mix(uint value)   // The finalizer alone: a bijection of the 32-bit numbers (Mix(0) is 0).
```

## `src/PTMod/Core/Logic/Random/Xoshiro128.cs` — `PTMod.Core.Logic.Rng`

```csharp
interface IModRng   // A random stream of the mod (FR-9): what <c>ModRandom.Stream(id)</c> hands to features. Deterministic: the sequence
class Xoshiro128 : IModRng   // xoshiro128** (Blackman and Vigna, public domain): 32-bit output, 128-bit state in four <c>uint</c> words.
    const int StateWords = 4   // Number of 32-bit words in the state.
    Xoshiro128(uint seed)   // A generator whose state is derived from <paramref name="seed"/> (any value, 0 included).
    void Reseed(uint seed)   // Puts the generator in the state a new <c>Xoshiro128(seed)</c> has; the object stays the same.
    static bool IsValidState(uint[] state)   // True for an array of exactly <see cref="StateWords"/> words that are not all zero.
    uint[] GetState()   // A copy of the four state words, in the order the algorithm names them (s[0]..s[3]).
    bool TrySetState(uint[] state)   // Loads a state saved by <see cref="GetState"/>. Returns false, and leaves the generator as it was, if the array
    static bool TryFromState(uint[] state, out Xoshiro128 rng)   // A generator in the saved state, or false with a null <paramref name="rng"/> if the state is invalid.
    uint NextUInt32()   // The next 32 random bits (the ** scrambler: rotl(s1 * 5, 7) * 9).
    int Range(int minInclusive, int maxExclusive)
    float Value()
    bool Chance(float probability)
    int WeightedIndex(IList<float> weights)
    T Pick<T>(IList<T> items)
```

## `src/PTMod/Core/Logic/Reputation/ReputationStore.cs` — `PTMod.Core.Logic.Reputation`

```csharp
struct ReputationChange   // One entry of a company's reputation history: written by <c>Add</c> and <c>Set</c> only, never by the monthly drift.
    ReputationChange(DateTime date, float delta, float value, string reason)
    DateTime Date   // Game date of the change.
    float Delta
    float Value   // Reputation right after the change.
    string Reason   // Reason key (an L10n key of the feature that wrote it) or free text; never null.
class ReputationStore   // The state and rules of brand reputation (FR-13), without Unity and without game objects: companies are
    const float Min = 0f
    const float Max = 100f
    const float Default = 50f
    const int DefaultHistoryLimit = 50   // History length per company when the settings do not say otherwise (<c>reputation.historyPerCompany</c>).
    const float DriftSnap = 0.05f   // A drifted value closer to the target than this snaps to the target, so the exponential tail ends.
    float Value = Default
    readonly List<ReputationChange> History = new List<ReputationChange>()
    ReputationStore(int historyLimit = DefaultHistoryLimit, Action<string> warn = null)
    event Action<string, float, float, string> Changed   // Raised by <see cref="Add"/> and <see cref="Set"/> only: key, old value, new value, reason.
    event Action Drifted   // Raised once at the end of every <see cref="DriftAll"/> pass.
    void SetHistoryLimit(int limit)   // Changes the history length; longer histories are cut to the newest entries at once.
    float Get(string key)   // Current reputation; <see cref="Default"/> for a company that has none and for an invalid key.
    bool Add(string key, float delta, string reason, DateTime date)   // Changes the reputation by <paramref name="delta"/>, clamped to [<see cref="Min"/>, <see cref="Max"/>], writes a history
    bool Set(string key, float value, string reason, DateTime date)   // Sets the reputation to <paramref name="value"/> (clamped), writes a history entry with the effective change and
    bool Drift(string key, float rate, float target = Default)   // Moves one company towards <paramref name="target"/> by the share <paramref name="rate"/> of the distance
    int DriftAll(IReadOnlyList<string> keys, float rate, float target = Default)   // One monthly pass: <see cref="Drift"/> for every key of the list (invalid keys are skipped with a warning), then
    IReadOnlyList<ReputationChange> History(string key)   // The newest-last history of the company (a snapshot copy); empty for an unknown company and an invalid key.
    IReadOnlyList<string> Keys()   // Keys with a stored value or history, in ordinal order (stable output for the side file).
    bool Load(string key, float value, IEnumerable<ReputationChange> history)   // Restores one company from the side file: replaces its value and history. A value outside [0, 100] is clamped, history
    void Clear()   // Forgets everything (exit to the menu, start of a new game, before a save is loaded). No events.
```

## `src/PTMod/Core/Logic/Save/IFileSystem.cs` — `PTMod.Core.Logic.Save`

```csharp
interface IFileSystem   // The disk as seen by the persistence logic (side save file, settings, profile). The logic never touches
class SystemFileSystem : IFileSystem   // The real disk (<c>System.IO</c>). Thin on purpose: all decisions are made by the logic above it.
    bool FileExists(string path)
    bool DirectoryExists(string path)
    void CreateDirectory(string path)
    string ReadAllText(string path)
    void WriteAllText(string path, string contents)
    void Move(string source, string destination)
    void Replace(string source, string destination, string backup)
    void Delete(string path)
    string GetFullPath(string path)
class FileWriteResult   // Outcome of an atomic write: success, or the reason in plain English (for the log).
    bool Success
    string Path   // The file that was (or should have been) written.
    string Error   // Null on success.
    static FileWriteResult Ok(string path)
    static FileWriteResult Fail(string path, string error)
class AtomicFile   // The three disk operations shared by the side save file, the settings and the profile:
    const string TempSuffix = ".tmp"
    const string CorruptInfix = ".corrupt-"
    static FileWriteResult WriteJson(IFileSystem fs, string path, JsonNode document, string backupPath)   // Writes <paramref name="document"/> as JSON to <paramref name="path"/> atomically: <c>path.tmp</c> first, then
    static FileWriteResult WriteText(IFileSystem fs, string path, string text, string backupPath)   // The same atomic sequence for ready text (no size guard here: <see cref="WriteJson"/> applies it).
    static string Quarantine(IFileSystem fs, string path, DateTime timestamp, out string error)   // Renames a broken file to <c>path.corrupt-yyyyMMddHHmmss</c> (a numeric suffix <c>-2</c>, <c>-3</c>... is added when that
    static bool TryDelete(IFileSystem fs, string path)   // Best-effort delete; false when it failed (the reason is not interesting to anybody).
    static string Describe(Exception ex)   // "IOException: message" - the form used in warnings and errors.
```

## `src/PTMod/Core/Logic/Save/SideFileDto.cs` — `PTMod.Core.Logic.Save`

```csharp
class SideFileHeader   // Top-level fields of the file (everything except the <c>features</c> map).
    int schema; // format of the file itself (SideFileModel.FileSchema)
    string modVersion
    string gameVersion
    string saveName; // without extension, as the game named the save
    string gameDate; // yyyy-MM-dd
    string writtenUtc; // yyyy-MM-ddTHH:mm:ssZ
class CoreSectionDto   // Section "mod-framework". Note: the section version <c>_schema</c> is read and written by <see cref="SideFileModel"/>, not by the
    int _schema = 1
    string startDate; // "1975-01-01"
    RandomDto random
    Dictionary<string, Dictionary<string, int>> flags; // featureId -> key -> int (bool = 0/1)
    Dictionary<string, JsonNode> newGameOptions
    List<ModifierEntryDto> modifiers
    Dictionary<string, ReputationDto> reputation; // companyKey -> ...
    MailStoreDto mail
class RandomDto
    uint worldSeed
    Dictionary<string, uint[]> streams; // stream id -> 4 words of xoshiro128**
class ModifierEntryDto
    string id, feature, channel, scope, company, reason, expires
    string[] reasonArgs
    int cpu = -1
    int segment = -1
    float mult
class ReputationDto
    float value
    List<ReputationChangeDto> history; // drift is not written to the history
class ReputationChangeDto
    string date
    float delta, value
    string reason
class MailStoreDto
    int nextId
    List<MailRecordDto> mails
    List<MailRecordDto> queue; // not delivered yet
class MailRecordDto
    int id
    string feature, tag, titleKey, bodyKey, sent, delivered, deadline, choice, resolution, defaultChoice, priority, status
    LocTextDto title, body
    string[] args
    bool read
    List<MailChoiceDto> choices
    List<MailActionDto> actions
class MailChoiceDto
    string id, labelKey, tooltipKey, disabledReasonKey
    LocTextDto label
    string[] disabledReasonArgs
    bool enabled = true
class MailActionDto
    string id, labelKey, tooltipKey, disabledReasonKey
    LocTextDto label
    string[] disabledReasonArgs
    bool enabled = true
    bool repeatable = true
    bool used
class LocTextDto   // A text given inline instead of by L10n key (same three parts as <c>LocText</c>: <c>key</c>, <c>ru</c>, <c>en</c>).
    string key
    string ru
    string en
```

## `src/PTMod/Core/Logic/Save/SideFileIo.cs` — `PTMod.Core.Logic.Save`

```csharp
class SideFileWriteReport   // Outcome of <see cref="SideFileIo.Write"/>.
    bool Success   // True when the side file was written. On false the previous side file (if any) is untouched and the game's own save is not affected.
    string Path   // The side file path, or null when the save name was rejected.
    string Error   // Why nothing was written; null on success. For the log and the "Mod state not saved" notification.
    bool PathRejected   // The save name could not be turned into a path inside Saves/.
class SideFileIo   // Disk side of the side save file (FR-5): where the file lives, reading it into a <see cref="SideFileModel"/>, writing it atomically,
    const string Extension = ".ptmod.json"   // Extension that follows the save name.
    SideFileIo(IFileSystem fs, string savesDirectory, Func<DateTime> utcNow = null)
    bool TryGetPath(string saveName, out string path, out string reason)   // Full path of the side file for a save name (the name the game used, without extension, including the <c>Y_M_D</c> and
    SideFileReadReport Read(SideFileModel model, string saveName)   // Loads the side file of a save into the model. No file: defaults (<see cref="SideFileStatus.Missing"/>). Broken file: defaults,
    SideFileWriteReport Write(SideFileModel model, string saveName, SideFileHeader header)   // Collects the document from the model and writes it atomically. <paramref name="header"/> supplies modVersion, gameVersion and gameDate;
    bool Delete(string saveName, out string error)   // Deletes the side file (the player deleted the save in the load or save window) and a temporary file left by an interrupted write.
```

## `src/PTMod/Core/Logic/Save/SideFileModel.cs` — `PTMod.Core.Logic.Save`

```csharp
enum SideFileStatus   // State of the side save file for the save in play (<c>ModSave.Status</c>).
interface ISideFileParticipant   // A holder of one section of the side file. The shape of <c>IModSaveParticipant</c> (ModSave, TSK-025): that interface is
class SideFileReadReport   // What <see cref="SideFileModel"/> (and <see cref="SideFileIo"/>) found while loading, resetting or reading the side file.
    SideFileStatus Status
    string Error   // For <see cref="SideFileStatus.Corrupt"/>: why the file was rejected (JSON errors carry line and column). Otherwise null.
    bool FileIsNewer   // True when the file itself has a newer <c>schema</c> (then it is written back unchanged).
    string QuarantinedTo   // Set by <see cref="SideFileIo"/>: where a corrupt file was moved to, or null.
    string Path   // Set by <see cref="SideFileIo"/>: the path that was used, or null when the save name was rejected.
    bool PathRejected   // Set by <see cref="SideFileIo"/>: the save name could not be turned into a path inside Saves/.
class SideFileBuildReport   // The document to write, and the problems met while building it.
    JsonObject Document   // The whole file: header members and <c>features</c>.
class SideFileModel   // The pure model of the side save file <c>&lt;name&gt;.ptmod.json</c> (FR-5, decision 0004): distributes the sections of a
    const int FileSchema = 1   // Format version of the file itself (the top-level <c>schema</c>).
    const string SchemaKey = "_schema"   // Name of the section version field.
    SideFileModel(Func<string, bool> isActive = null)   // Is the feature of this section on (<c>ModSettings.IsFeatureEnabled</c> and not Unavailable/Faulted)? Null = every registered
    SideFileStatus Status
    bool TryRegister(ISideFileParticipant participant, out string problem)   // Registers a participant; false with the reason when the id is empty or already taken. Never throws.
    SideFileReadReport StartNewGame()   // A new game: every registered participant is reset, everything held back is dropped (a new save starts clean).
    SideFileReadReport ApplyMissing()   // The save has no side file: defaults for everybody.
    SideFileReadReport ApplyCorrupt(string reason)   // The file could not be used (unreadable or invalid): defaults for everybody, status <see cref="SideFileStatus.Corrupt"/>.
    SideFileReadReport Apply(string text)   // Parses the file text and hands the sections out: a participant gets <c>Read</c> (section found, version not newer) or
    SideFileReadReport RestoreParticipant(string sectionId)   // Gives a participant its data after the feature was switched on in the middle of a session (it was inactive at load time):
    SideFileBuildReport Build(SideFileHeader header)   // Collects the document: the header, then the sections. Active participants contribute <c>Write()</c> with their <c>_schema</c>;
```

## `src/PTMod/Core/Logic/Segments/SegmentRule.cs` — `PTMod.Core.Logic.Segments`

```csharp
class SegmentRule   // The one rule that puts a CPU into a market segment (FR-23 items 6-7): by its price against the budgets of the game's market
    const float DefaultBand = 1.25f   // The band of <c>core.json</c> (<c>segments.priceBand</c>) when the file does not say.
    static Segment ByPrice(float price, float midBudget, float lowBudget, float industriesBudget, float band = DefaultBand)   // The segment of a CPU sold at <paramref name="price"/>. The arithmetic is done in double so that a band such as 1.1 does not add the
    static Segment FromSales(int highEnd, int midRange, int lowEnd, int industries)   // The segment with the most sales; a tie goes to the smaller index; nothing sold gives <see cref="Segment.Any"/>.
```

## `src/PTMod/Core/Logic/Settings/ParamSpec.cs` — `PTMod.Core`

```csharp
enum ParamType   // Kind of a feature parameter: a switch, a whole number or a real number (all three are stored as a double).
class ParamSpec   // Declaration of one feature parameter: what the hub draws (a switch or a slider) and which values are legal. A feature declares it with
    string FeatureId   // Owner feature (the key of the <c>features</c> map in <c>ptmod_settings.json</c>).
    string Key   // Name inside the feature, e.g. <c>boost</c>; the full path in the file is <c>features.&lt;FeatureId&gt;.params.&lt;Key&gt;</c>.
    string LabelKey, TooltipKey   // L10n keys of the label and the tooltip in the hub (prefix of the owner feature).
    ParamType Type
    double Default   // The value when the file has none (or a bad one). For <see cref="ParamType.Bool"/>: 0 or 1.
    double Min, Max   // Legal range, ends included. <see cref="ParamType.Bool"/> always uses 0..1. Int ranges are rounded inward (the slider shows whole numbers).
    double Step   // Slider step; 0 or less = continuous (Float) / 1 (Int). The model does not snap values to it: it is a hint for the UI.
    bool RequiresReload   // A change takes effect after the game restarts (the hub shows a note).
    ParamSpec Clone()
    bool Normalize(out string problem)   // Checks the declaration and fixes what can be fixed in place: Bool gets 0..1, an Int range is rounded inward, a bad step is
    double Clamp(double value)   // Brings a value into the legal set: Bool becomes 0 or 1 (non-zero is true), Int is rounded (halves away from zero) and clamped,
    bool InRange(double value)   // The value is legal as it is (Bool: exactly 0 or 1, Int: a whole number in range, Float: in range). NaN is never legal.
    bool SameDefinition(ParamSpec other)   // Same type, range, default, step and reload flag (labels are ignored): a repeated declaration of the same parameter is harmless.
```

## `src/PTMod/Core/Logic/Settings/ProfileModel.cs` — `PTMod.Core.Logic.Settings`

```csharp
enum ProfileStatus   // How the profile (<c>ptmod_profile.json</c>) was obtained at start.
class ProfileLoadReport   // Result of <see cref="ProfileModel.Load"/>.
    ProfileStatus Status
    string Error   // Why the main file was rejected (RestoredFromBackup and Corrupt); null otherwise.
    string QuarantinedTo   // Where the broken main file was moved to, or null.
    FileWriteResult RestoreWrite   // RestoredFromBackup: the result of writing the restored data back to the main file (it is gone after the rename); null otherwise.
class ProfileModel   // The pure model of <c>ptmod_profile.json</c> (FR-6 items 5-9): global per-feature data (achievements, "seen the tutorial", best results),
    const int FileSchema = 1   // Format version of the file (top-level <c>schema</c>).
    const string BackupSuffix = ".bak"   // Appended to the file name for the previous version.
    JsonObject Data; // what the feature works with (no _schema)
    int Version; // written as _schema
    bool Kept; // the stored section is written back as read; Data is a throw-away empty object
    ProfileModel(IFileSystem fs, string path, Func<DateTime> utcNow = null,
    ProfileStatus Status
    ProfileLoadReport Load(double now)   // Reads the profile and recovers from a broken file (see the class comment). Never throws.
    JsonObject Section(string featureId, int schemaVersion = 1)   // The live object of a feature's section (created empty on the first call; the same object on later calls). <c>_schema</c> is not in it:
    int StoredSchema(string featureId)   // The <c>_schema</c> the section had in the file (1 when the field was missing), or 0 when the file has no such section.
    void MarkDirty(string featureId, double now)   // A feature changed its section. Starts the throttle window; the write happens in <see cref="Tick"/>.
    FileWriteResult Tick(double now)   // Call every frame (or every few frames) with the real time. Writes when the window has passed; null when nothing was due.
    FileWriteResult Save(double now)   // Writes now, whatever the throttle says (<c>ModProfile.Save()</c>).
    FileWriteResult FlushIfDirty(double now)   // The forced write on leaving to the main menu, on quit and on minimizing: writes only if something is dirty; null otherwise.
```

## `src/PTMod/Core/Logic/Settings/SettingsModel.cs` — `PTMod.Core.Logic.Settings`

```csharp
enum SettingsFileStatus   // How <see cref="SettingsModel.Load"/> got its document.
class SettingsLoadReport   // Result of <see cref="SettingsModel.Load"/>.
    SettingsFileStatus Status
    string Error   // Why a corrupt file was rejected (with line and column for a JSON error); null otherwise.
    string QuarantinedTo   // Where a corrupt file was moved to, or null.
    FileWriteResult WriteResult   // Result of writing the new file (Created and Corrupt); null when the file was only read.
class SettingsModel   // The pure model of <c>ptmod_settings.json</c> (FR-6 items 1-4): language, verbose log, per-feature switch and parameters,
    const int FileSchema = 1   // Format version of the file (top-level <c>schema</c>).
    const string DefaultLanguage = "ru"
    SettingsModel(IFileSystem fs, string path, bool debugBuild = false, Func<DateTime> utcNow = null)
    SettingsLoadReport Load()   // Reads the file. No file: defaults, and the file is created. Not valid JSON (or not an object): the file is renamed to
    FileWriteResult Save()   // Writes the file atomically (temporary file, then replace). On failure the previous file stays and <see cref="IsDirty"/> stays set.
    bool SetLanguage(string language)   // Accepts "ru" or "en" (any case); anything else is ignored. True when the language changed.
    bool SetVerboseLog(bool value)
    bool Debug(string flag)   // A flag of the <c>debug</c> section: <c>"debug.failInit"</c> or just <c>"failInit"</c>. Always false in a release build and for a flag
    bool? GetFeatureEnabled(string featureId)   // The explicit choice of the player, or null when the file has none (or a bad one): then the feature's own default decides.
    bool IsFeatureEnabled(string featureId, bool defaultEnabled)
    bool SetFeatureEnabled(string featureId, bool enabled)   // Records the player's choice. True when the stored choice changed (including from "no choice").
    bool Declare(ParamSpec spec, List<string> warnings)   // Registers a parameter and takes its value from the file: absent = default; wrong type or outside [Min, Max] = default plus a warning
    bool IsDeclared(string featureId, string key)
    ParamSpec GetSpec(string featureId, string key)   // A copy of the declaration, or null.
    IReadOnlyList<ParamSpec> SpecsOf(string featureId)   // Copies of the declarations of one feature in declaration order (the settings page of the hub).
    double GetValue(string featureId, string key)   // The current value (0 for a parameter nobody declared).
    bool GetBool(string featureId, string key)
    int GetInt(string featureId, string key)
    float GetFloat(string featureId, string key)
    bool Set(string featureId, string key, double value, out double stored)   // Sets a declared parameter, clamped by its spec (Bool: non-zero = true; Int: rounded; NaN is ignored). The new value goes into the
```

## `src/PTMod/Core/Logic/Settings/WriteThrottle.cs` — `PTMod.Core.Logic.Settings`

```csharp
class WriteThrottle   // Decides when a file with frequent small changes (the profile) may be written: not earlier than <see cref="WindowSeconds"/> after
    const double DefaultWindowSeconds = 2.0   // Default of <c>profile.saveThrottleSeconds</c>.
    WriteThrottle(double windowSeconds = DefaultWindowSeconds)
    void MarkDirty(double now)   // Something changed. Starts the window if the state was clean; otherwise only keeps the data dirty.
    bool IsDue(double now)   // True when there are changes and the window has passed: the caller should write now and report the result.
    void Saved(double now)   // The data were written (by the throttle's turn or by an explicit save): clean, and the next window counts from here.
    void Failed(double now)   // A write failed: still dirty, the next attempt is allowed one window from now.
    void Reset()   // Forgets everything (a different file was loaded).
```

## `src/PTMod/Core/Logic/Ticks/BatchCounter.cs` — `PTMod.Core.Logic.Ticks`

```csharp
class BatchCounter   // The depth counter of the batch run of a month (FR-4 items 8-9, P21): <c>DateController.NextMonth</c> plays up to 31 game days in one
    int Depth   // How many runs are open now: 0 outside <c>NextMonth</c>, 1 inside, 2 and more for nested calls.
    int Started   // Outer runs that started since the counter was created.
    int Completed   // Outer runs that ended (the number of <c>BatchEnded</c> the caller raised).
    int UnbalancedLeaves   // Leaves without a matching entry, ignored (a sign of a broken prefix; for the Diagnostics tab).
    int Abandoned   // Runs dropped by <see cref="Reset"/> while they were open.
    bool Enter()   // A run starts or nests. True when it is the outer one (depth was 0).
    bool Leave()   // A run returns, normally or by an exception. True when the outer run ended (depth is 0 now) and the end has to be reported; false
    bool Reset()   // Forgets an open run (the scene was left inside it, a finalizer never came): depth 0 without an end to report. True when a run was
```

## `src/PTMod/Core/Logic/Ui/SliderMath.cs` — `PTMod.Core.Logic.Ui`

```csharp
class SliderMath   // The arithmetic of <c>ModUI.Slider</c> (FR-16 item 5), without Unity: where a stepped value lands and how it is shown. The slider of
    static void Normalize(ref float min, ref float max, ref float step)   // Puts the arguments right: min below max (swapped if reversed, a hair apart if equal), a step that is NaN, negative or infinite becomes 0 (continuous).
    static float Snap(float value, float min, float max, float step)   // The value clamped to [min, max] and, for a positive step, moved to the nearest <c>min + k * step</c> (halves round up); a point past <paramref name="max"/>
    static int Decimals(float step)   // How many decimals show a step exactly, at most four: 1 -> 0, 0.5 -> 1, 0.05 -> 2; a step of zero or less -> 2.
    static string Format(float value, float step)   // The value as text with the decimals of the step, the invariant culture (a continuous slider shows up to two decimals).
```

## `src/PTMod/Core/Random/ModRandom.cs` — `PTMod.Core`

```csharp
class ModRandom   // The deterministic random numbers of the mod (FR-9): named streams derived from one world seed of the game, saved with the game. The
    static IModRng Stream(string streamId)   // The stream with this id: created on first use, the same object afterwards (it keeps its place in the sequence across saving and loading).
    static void SetSeedForNewGame(uint seed)   // A scenario's own world seed for the new game that is being started (<c>ModNewGame.BeforeStart</c>): the same scenario then plays the same
    void ResetToDefaults()
    void Read(JsonNode block, int schema)
    JsonNode Write()
```

## `src/PTMod/Core/Save/CoreSection.cs` — `PTMod.Core`

```csharp
    DelegatePart(string key, Action reset, Action<JsonNode> read, Func<JsonNode> write)
    string Key
    void ResetToDefaults()
    void Read(JsonNode block, int schema)
    JsonNode Write()
```

## `src/PTMod/Core/Save/ModFlags.cs` — `PTMod.Core`

```csharp
class ModFlags   // The feature flags of a save (<c>ModSave.Flags</c>; the block <c>flags</c> of the section <c>mod-framework</c>, FR-5): small named integers
    bool Get(string featureId, string key)   // True when the flag is set to anything but 0; an unset flag is false.
    void Set(string featureId, string key, bool value = true)   // Sets a flag: <c>Set(id, "welcomeSent")</c> raises it, <c>Set(id, key, false)</c> lowers it (the key stays, with 0).
    int GetInt(string featureId, string key, int fallback = 0)   // The value of the flag, or <paramref name="fallback"/> when it was never set (a flag set to 0 reads 0, not the fallback).
    void SetInt(string featureId, string key, int value)
    FlagsPart(ModFlags owner)
    void ResetToDefaults()
    void Read(JsonNode block, int schema)
    JsonNode Write()
```

## `src/PTMod/Core/Segments/ModSegments.cs` — `PTMod.Core`

```csharp
class ModSegments   // The market segment of a CPU (FR-23 items 6-7): the single classifier of the mod. Every feature that needs "which segment is this CPU
    static Segment Classify(Cpu cpu)   // The segment of <paramref name="cpu"/> by its price (<c>Cpu.Price</c>); <see cref="Segment.Any"/> for a null CPU or when it cannot be said.
    static Segment Classify(float price)   // The segment of a CPU sold at <paramref name="price"/> by the current budgets of the market and the band
    static Segment FromSales(int soldHighEnd, int soldMidRange, int soldLowEnd, int soldIndustries)   // The segment with the most sales (a tie goes to the smaller index: HighEnd, MidRange, LowEnd, Industries); nothing sold gives <see cref="Segment.Any"/>.
    static Segment DominantBySales(Cpu cpu)   // The segment where <paramref name="cpu"/> sold most yesterday (<c>SoldLastDayBy*</c>); <see cref="Segment.Any"/> for a null CPU or when nothing was sold.
```

## `src/PTMod/Core/Settings/ModProfile.cs` — `PTMod.Core`

```csharp
class ModProfile   // The global, save-independent data of the features (achievements, "seen the tutorial", best results; FR-6 items 5-9), kept in
    const string FileName = "ptmod_profile.json"   // File name inside <c>persistentDataPath</c>; the previous version is <c>ptmod_profile.json.bak</c>.
    const string BackupNoticeKey = "core.profile.corrupt"   // Notice key: the profile was damaged and restored from the backup; the argument is the backup file name.
    const string ResetNoticeKey = "core.profile.reset"   // Notice key: the profile and its backup were both unusable, an empty profile is in use; the argument is the file name the damaged
    string Key
    object[] Args
    static JsonObject Section(string featureId, int schemaVersion = 1)   // The live object of the feature's section (created empty on the first call, the same object afterwards). It does not contain
    static void MarkDirty(string featureId)   // The feature changed its section. Writes not earlier than the throttle window from now (2 s) and not more often than once per window.
    static void Save()   // Writes the profile now, atomically, keeping the previous file as <c>.bak</c>.
```

## `src/PTMod/Core/Settings/ModSettings.cs` — `PTMod.Core`

```csharp
class ModSettings   // The player's global settings (FR-6 items 1-4), kept in <c>Application.persistentDataPath/ptmod_settings.json</c>: the language of the
    const string FileName = "ptmod_settings.json"   // File name inside <c>persistentDataPath</c>.
    const string EnabledKey = "enabled"   // The <c>key</c> of <see cref="Changed"/> when a feature is switched on or off (the feature's parameters have their own keys).
    static event Action<string, string> Changed   // Raised after a setting changed and was saved: (featureId, key) - the parameter key, <see cref="EnabledKey"/>, or ("mod-framework", "language" / "verboseLog").
    static bool IsFeatureEnabled(string featureId)   // The player's choice for the feature, or its own default (<c>EnabledByDefault</c>) when the player has not chosen yet.
    static bool IsFeatureEnabled(string featureId, bool defaultEnabled)   // The player's choice for the feature, or <paramref name="defaultEnabled"/> when the file has none.
    static void SetFeatureEnabled(string featureId, bool enabled)   // Records the player's choice and saves. Raises <see cref="Changed"/> with <see cref="EnabledKey"/> when the stored choice changed.
    static void Declare(ParamSpec spec)   // Registers a feature parameter and takes its value from the file (the default when the file has none or a bad one - with a warning
    static bool GetBool(string featureId, string key)
    static int GetInt(string featureId, string key)
    static float GetFloat(string featureId, string key)
    static void Set(string featureId, string key, double value)   // Sets a declared parameter, clamped by its <see cref="ParamSpec"/> (a Bool is non-zero = true, an Int is rounded, NaN is ignored),
    static bool Debug(string flag)   // A flag of the <c>debug</c> section (<c>"debug.failInit"</c>, <c>"debug.brokenPatch"</c>, <c>"debug.throwOnTick"</c>, ...). Only the debug
```

## `src/PTMod/Core/Settings/RealFileSystem.cs` — `PTMod.Core`

```csharp
class RealFileSystem : IFileSystem   // The real disk (<c>System.IO</c>) behind <see cref="IFileSystem"/>: what <c>ModSettings</c>, <c>ModProfile</c>, <c>ModL10n</c>,
    static readonly RealFileSystem Instance = new RealFileSystem()   // The shared instance; the class is stateless, <c>new RealFileSystem()</c> is equivalent.
    bool FileExists(string path)
    bool DirectoryExists(string path)
    void CreateDirectory(string path)
    string ReadAllText(string path)
    void WriteAllText(string path, string contents)
    void Move(string source, string destination)
    void Replace(string source, string destination, string backup)
    void Delete(string path)
    string GetFullPath(string path)
```

## `src/PTMod/Core/Ticks/ModTicks.cs` — `PTMod.Core`

```csharp
enum GameReadyReason   // Why the game state became ready (<see cref="ModTicks.GameReady"/>).
class ModTicks   // The game day for the mod (FR-4): the phases of <c>DateController</c> with the guards against the fast-forward and the loading
    static IDisposable Subscribe(string featureId, TickPhase phase, Action handler, int priority = 0, TickOptions options = TickOptions.None)   // Subscribes <paramref name="handler"/> of <paramref name="featureId"/> to a phase (features use <c>ctx.OnTick</c>, which passes their
    static event Action<GameReadyReason> GameReady   // The game state is ready (FR-4 item 6): after a save was loaded or after the fast-forward of a new game. Once per game scene.
    static event Action BatchEnded   // The batch run of a month is over (FR-4 item 8): once after the outer <c>DateController.NextMonth</c> returned, also after an
    static TickStats StatsOf(TickPhase phase)   // The handler times of a phase over the last 30 game days (the Diagnostics tab).
    static TickStats StatsOfDay()   // The handler times of whole game days (the daily and monthly phases together) over the last 30 game days.
    void Dispose()
```

## `src/PTMod/Core/Ticks/TickDispatcher.cs` — `PTMod.Core`

```csharp
enum TickPhase   // The phases of a game day: one per event of <c>DateController</c>, in the order <c>DateController.TriggerTicks</c> raises them
enum TickOptions   // When a tick handler runs although the game is not in its normal course (FR-4 items 2-3). By default (<see cref="None"/>) a handler is
    const int PhaseCount = (int)TickPhase.FastForwardEnded + 1   // Number of <see cref="TickPhase"/> values.
    const int DayPhaseCount = (int)TickPhase.LateYearPassed + 1   // Phases of a regular game day (<see cref="TickPhase.EarlyTick"/> … <see cref="TickPhase.LateYearPassed"/>); the rest are one-off.
    static IDisposable Add(string featureId, TickPhase phase, Action handler, int priority, TickOptions options)   // Adds a subscriber (the checks of the arguments are the caller's, <see cref="ModTicks.Subscribe"/>). The same handler of the same
    static bool HasSubscribers(TickPhase phase)   // Some subscriber listens to the phase (active or not): the caller skips reading the game's state otherwise.
    static int CountOf(TickPhase? phase)   // Subscribers of the phase, or of all phases for null.
    static int Dispatch(TickPhase phase, bool fastForward, bool loading)   // Calls the subscribers of the phase in order. <paramref name="fastForward"/>: only subscribers with
    static IReadOnlyList<string> Describe(TickPhase phase)   // The subscribers of the phase in calling order, one line each: <c>feature: Type.Method@Phase (priority 0, RunDuringLoading)</c>.
    Subscription(string featureId, TickPhase phase, Action handler, int priority, TickOptions options)
    string FeatureId
    TickPhase Phase
    Action Handler
    int Priority
    TickOptions Options
    string Site   // switches off is this handler alone.
    bool Disposed
    void Dispose()
```

## `src/PTMod/Core/Ticks/TickStats.cs` — `PTMod.Core`

```csharp
struct TickStats   // How long the mod's tick handlers took over the last 30 game days (FR-4 item 7), for the Diagnostics tab: per phase
    TickPhase? Phase   // The phase, or null for the whole-day figures.
    int Subscribers   // Handlers subscribed now (to the phase, or to all phases).
    int Samples   // Dispatches with at least one handler run in the window (for the whole day: days with any handler time).
    int Days   // Game days the window covers: 30 once the game has run that long since the window was last cleared.
    double TotalMs   // Time of all samples in the window, ms.
    double MaxMs   // The slowest sample, ms.
    const int WindowDays = 30
    TickStatsWindow(int phases, int dayPhases)
    static long DayOf(DateTime date)   // The day number of a date: whole days since 0001-01-01.
    void BeginDay(long day)   // The game day <paramref name="day"/> has begun: the next samples belong to it. The days skipped since the last one are empty slots;
    void Record(int phase, double ms)   // One dispatch of <paramref name="phase"/> took <paramref name="ms"/>. Before any day it opens day 0 (a window of its own).
    void Reset()   // Empties the window (no day, nothing recorded).
    TickStats Of(int phase, TickPhase? label, int subscribers)   // The figures of one phase over the window.
    TickStats OfDay(int subscribers)   // The whole-day figures: a sample is a day with any handler time in the day phases, its value the sum over them.
    int SlowestPhase()   // The day phase with the most time in the window, or -1 when nothing was recorded.
```

## `src/PTMod/Core/UI/CloneSanitizer.cs` — `PTMod.Core`

```csharp
    GameObject Root   // The root of the clone: the window itself.
    RectTransform Content   // The area for the window's widgets (<c>Background</c>), already emptied.
    Transform TopBar   // The title bar (<c>TopBar</c>, the object with <c>ThemeableTopBar</c>).
    TMP_Text Title   // The caption in the title bar.
    Button CloseButton   // The button of <c>ButtonClose</c>; its <c>onClick</c> has no listeners except the ones the window adds (see <see cref="CloneSanitizer.BindClose"/>).
    readonly Dictionary<string, int> RemovedByType = new Dictionary<string, int>(StringComparer.Ordinal)
    int RemovedComponents
    int FailedRemovals   // Components that refused to go (Unity keeps a component another one requires); logged as a warning.
    int LostScripts   // Missing scripts the game's prefab already had (null components); they cannot be removed from the runtime and do nothing.
    int DuplicatesRemoved   // Second and later copies of a widget script on one object (the game's dropdowns and its start button carry two); see <see cref="CloneSanitizer"/>.
    int DisabledListeners
    int ResetButtons
    int ClearedChildren
    void Removed(string typeName)
```

## `src/PTMod/Core/UI/ConfirmDialog.cs` — `PTMod.Core.Logic.Ui`

```csharp
class ModUI   // The confirmation dialog of <see cref="ModUI"/> (FR-16 item 8): the <c>Confirm</c> half of the <c>partial</c> class, next to the tooltip bridge in
    static void Confirm(string title, string message, Action onYes, Action onNo = null,   // A modal "Yes / No" question on a <c>Window.Modal</c> window of the game, one window for the whole mod: time stands while it is open (the template's
    ConfirmRequest(string title, string message, string yesKey, string noKey, Action onYes, Action onNo)
    string Title
    string Message
    string YesKey
    string NoKey
    Action OnYes
    Action OnNo
    bool IsSameQuestion(ConfirmRequest other)   // Same texts and same callbacks (a delegate is equal when its target and method are): asking it twice is asking it once.
    ConfirmArrival Enqueue(ConfirmRequest request)   // A new question. Nothing open and nothing waiting makes it the open one (<see cref="ConfirmArrival.ShowNow"/>); otherwise it joins the end of the line, so a callback
    ConfirmRequest Take()   // The answer comes: hands over the open question (it is no longer open) - once. Null when none is open: a second click, or the close after an answer.
    ConfirmRequest Advance()   // Makes the first waiting question the open one and returns it; null while one is open or none waits.
    List<ConfirmRequest> Drain()   // Empties the dialog and returns what was in it, the open question first. The caller answers or drops them.
```

## `src/PTMod/Core/UI/ModUI.cs` — `PTMod.Core`

```csharp
class ModUI   // Runtime UI from the game's own templates (FR-16, decision 0005): windows, the theme, and (in <c>Widgets.cs</c>) the widgets. The file is one half of
    static event Action ThemeChanged   // The theme changed and the mod repainted its own parts (<see cref="ReapplyTheme"/>): repaint yours here. Raised from <c>ThemeManager.OnThemeChanged</c>;
    static ModWindow CreateWindow(string id, WindowTemplate template, string titleKey, Vector2 size)   // Makes a window from a template of the game, hidden, in the current scene; null when the template is not in the scene (the log names the row of the
    Graphic Target
    ThemeRole Role
    static void ReapplyTheme()   // Repaints the parts the mod built itself with the current theme (the clones repaint themselves through <c>Themeable*</c>), then raises
```

## `src/PTMod/Core/UI/ModUITemplates.cs` — `PTMod.Core`

```csharp
class UiTemplate   // One row of the template table (design.md, "Шаблоны"): where a template of the game's UI lives and what it has to contain. The same key may
    string Key   // The key features and <c>ModUI</c> use (<c>Window.Modal</c>, <c>Widget.Button</c>).
    int Scene   // Build index of the scene the row is for: 0 (<c>level0</c>, the main menu) or 1 (<c>level1</c>, the game).
    string Window   // Name of the window object ("" if the element is searched in the whole scene).
    string Element   // Name of the object inside the window ("" if the window itself is the template).
    string Path   // Full path from the root object of the scene, as <c>tools/dump_ui_templates.py</c> prints it.
    bool Required   // False for a template that only widens what the mod can do; a missing one is not an error of the self-check.
    IReadOnlyList<string> Expected   // Names of the components the template's subtree must have for a clone to work (e.g. <c>PauseTrigger</c> on the modal window).
class UiTemplateResult   // The outcome of looking for one row in the loaded scene.
    UiTemplate Template
    GameObject Target   // The template object of the game, or null (also when the scene has been unloaded since). Read-only for everyone: clone it, never change it.
    TemplateHow How
    string ActualPath   // Where the template really is; differs from <c>Template.Path</c> when <see cref="How"/> is <see cref="TemplateHow.Names"/>.
    int Candidates   // How many objects matched the names (1 for a path hit, more means the choice was ambiguous).
    IReadOnlyList<string> MissingComponents   // Expected components the template's subtree lacks; the template is still usable, the self-check reports them.
class ModUITemplates   // The templates of <c>ModUI</c> (FR-16, decision 0005): the game's own windows and widgets that the mod clones to get UI with the game's look, theme and
    const string StagingName = "PTModUIStaging"   // Name of the inactive container the clones are made under.
    const string WindowModal = "Window.Modal"
    const string WindowPanel = "Window.Panel"
    const string WindowMenuPanel = "Window.MenuPanel"
    const string ButtonTaskbar = "Button.Taskbar"
    const string ButtonMenuFallback = "Button.MenuFallback"
    const string ButtonNewGame = "Button.NewGame"
    const string WidgetButton = "Widget.Button"
    const string WidgetButtonCta = "Widget.ButtonCTA"
    const string WidgetToggle = "Widget.Toggle"
    const string WidgetSlider = "Widget.Slider"
    const string WidgetDropdown = "Widget.Dropdown"
    const string WidgetInfoText = "Widget.InfoText"
    const string MailHost = "Mail.Host"
    static event Action Ready   // Raised after the templates of a scene have been looked up (<see cref="IsReady"/> is true). A handler that throws is counted against its feature.
    static UiTemplateResult ResultOf(string key)   // The result for a key in the current scene, or null when the scene has no row for it (or the scene is not ready).
    static GameObject Get(string key)   // The template found for a key in the current scene, or null. Never modify it: clone it through <c>ModUI</c>.
    static bool Has(string key)
    static Transform ParentOf(string key)   // The parent the template sits under in the game (the canvas for a window, the button bar for a taskbar button); null if the template is missing.
```

## `src/PTMod/Core/UI/ModWindow.cs` — `PTMod.Core`

```csharp
enum WindowTemplate   // Which window of the game <see cref="ModUI.CreateWindow"/> clones (the keys of <see cref="ModUITemplates"/>).
class ModWindow   // A window of the mod (FR-16): a clone of a window of the game with its frame, title bar, theme and close button, and an empty content area for the
    string Id   // The id the window was made with (unique among the windows of the scene).
    WindowTemplate Template   // The template of the game the window was cloned from.
    event Action Opened   // The window was shown (not raised when <see cref="Open"/> finds it open).
    event Action Closed   // The window was hidden: by <see cref="Close"/>, by the cross of the title bar, when its feature stopped or when the scene was left.
    void Open()   // Shows the window in front of the others. A <see cref="WindowTemplate.Modal"/> window stops the time. Open already: only brings it to the front.
    void Close()   // Hides the window (and lets the time go on if it was modal). Closed already: does nothing.
    void Toggle()   // Opens a closed window, closes an open one.
    void SetTitle(string text)   // Sets the caption of the title bar to a ready text; from now on the caption no longer follows the language (use it for a window whose
```

## `src/PTMod/Core/UI/TemplateLocator.cs` — `PTMod.Core`

```csharp
enum TemplateHow   // How <see cref="TemplateLocator"/> found a template.
    GameObject Target
    TemplateHow How
    int Candidates   // How many objects matched the names (1 for a path hit; more than 1 means the search was ambiguous and the first in hierarchy order was taken).
    string ActualPath = ""   // The path of <see cref="Target"/> in the format of the table (<c>Root/Child/Leaf</c>).
    List<string> MissingComponents = new List<string>()   // Components of <see cref="UiTemplate.Expected"/> that the template's subtree does not have.
```

## `src/PTMod/Core/UI/TooltipBridge.cs` — `PTMod.Core.Logic.Ui`

```csharp
class ModUI   // The bridge to the game's tooltips (FR-16 item 9): the <c>Tooltip</c> half of the <c>partial</c> class, next to <c>Confirm</c> in <c>ConfirmDialog.cs</c>.
struct TooltipText   // The text of one tooltip: the header line (may be empty) and the body.
    string Header, Content
    TooltipText(string header, string content)
    static void Tooltip(GameObject target, string header, string content)   // A fixed tooltip on an element. Putting a second one on the same element replaces the first (the <c>TooltipData</c> is kept); two empty texts remove the tooltip's text, nothing is shown.
    static void Tooltip(GameObject target, Func<TooltipText> provider)   // A tooltip whose text is made when the pointer enters the element: <paramref name="provider"/> runs under the guard of the feature it belongs to (an exception is counted against it and
    static void ShowTooltip(string header, string content,   // Asks the game to show a tooltip at the pointer, after its own delay unless <paramref name="delayOverride"/> says otherwise (seconds, 0 or more). It replaces a tooltip that is waiting.
    static void HideTooltip()   // Cancels the tooltip that is waiting or open (the game's <c>CancelTrigger</c>). Does nothing, and throws nothing, where there is none.
    void OnPointerEnter(PointerEventData eventData)
    void OnPointerExit(PointerEventData eventData)
    bool Start(string header, string content)
    void Cancel()
    void Release()
    TooltipHover(ITooltipHost host, TooltipTextSource source)
    bool Showing   // The tooltip was asked for and not yet cancelled.
    void PointerEntered()   // The pointer entered the element.
    void PointerExited()   // The pointer left the element, or the element was hidden or destroyed.
```

## `src/PTMod/Core/UI/Widgets.cs` — `PTMod.Core`

```csharp
enum ButtonStyle   // The look of a button made by <see cref="ModUI.Button"/>.
class ModUI   // The widgets of <see cref="ModUI"/> (FR-16 item 5): button and call-to-action button, toggle, slider, dropdown, info text, text, vertical list, scroll view,
    static ButtonUI Button(Transform parent, string text, Action onClick, ButtonStyle style = ButtonStyle.Standard, string tooltip = null)   // A button, a clone of <c>Widget.Button</c> (or <c>Widget.ButtonCTA</c> for <see cref="ButtonStyle.CallToAction"/>), captioned <paramref name="text"/>; null if the template is missing.
    static ToggleUI Toggle(Transform parent, string label, bool value, Action<bool> onChanged, string tooltip = null)   // A switch with a caption, a clone of <c>Widget.Toggle</c>; null if the template is missing.
    static SliderUI Slider(Transform parent, string label, float min, float max, float step, float value, Action<float> onChanged, string tooltip = null)   // A slider with a caption and the value beside it, a clone of <c>Widget.Slider</c>; null if the template is missing. <paramref name="step"/> snaps the value to
    static DropdownUI Dropdown(Transform parent, string label, IList<string> options, int index, Action<int> onChanged, string tooltip = null)   // A dropdown list with a caption to its left (a row), a clone of <c>Widget.Dropdown</c>; null if the template is missing. The index is clamped to the options.
    static InfoText Info(Transform parent, string title, string text, string tooltip = null)   // A "title: value" pair, a clone of <c>Widget.InfoText</c>; null if the template is missing. Change it later with <c>InfoText.SetTexts</c>.
    static TMP_Text Text(Transform parent, string text, bool bold = false)   // A text in the font of the game and the colour of the current theme (repainted when the theme changes); wraps to the width it is given, left and up.
    static RectTransform VerticalList(Transform parent, float spacing = 6f)   // A container that stacks what is put into it from the top (a vertical layout group, full width). Inside a layout of its own the parent sizes it;
    static RectTransform ScrollView(Transform parent, float height)   // A scrolling area <paramref name="height"/> units high with a vertical bar; returns its content container, a vertical flow that grows with what is put into it
    static void SetWithoutNotify(ToggleUI toggle, bool on)   // Sets a toggle made by <see cref="Toggle"/> to <paramref name="on"/> without calling its callback (the hub refreshing its rows).
    static void SetWithoutNotify(SliderUI slider, float value)   // Sets a slider made by <see cref="Slider"/> to <paramref name="value"/> (snapped to its step) and redraws its value text, without calling its callback.
    static void SetWithoutNotify(DropdownUI dropdown, int index)   // Picks an entry of a dropdown made by <see cref="Dropdown"/> without calling its callback.
    float Min
    float Max
    float Step
    float Last
    void OnPointerEnter(PointerEventData eventData)
    void OnPointerExit(PointerEventData eventData)
```

