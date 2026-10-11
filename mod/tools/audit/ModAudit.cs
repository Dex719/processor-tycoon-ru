using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// Offline audit of a built PTMod.dll against the game's assemblies (mod/tools/audit.py builds and runs it).
//
// What it checks, without starting the game and without running any game code:
//   1. Features: every [ModFeature] class is found, has a unique id that matches its namespace, its dependencies exist, its keys are not empty.
//   2. Patches: every [ModPatch] of the mod (core and features) is resolved with the core's own resolver (ModHooks.ResolveTarget), checked against the
//      table of reserved targets, the shape of the handler (static, return type) and the Harmony bindings of its parameters (__instance, __result, ___field,
//      the names of the target's parameters and their types). A required patch that cannot be applied makes its feature Unavailable in the game.
//   3. Overlaps: the methods that more than one feature (or the core) patches are listed, with the kinds, so that two transpilers or two skipping prefixes
//      on one method do not go unnoticed.
//   4. Resources: the embedded Content/ and L10n/ files of every feature.
//
// Exit code: 0 - no problem, 1 - at least one problem (ERROR lines), 2 - the tool could not start.
static class ModAudit
{
    private static string _managed, _original, _harmony;

    private static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: ModAudit <PTMod.dll> <game Managed folder> <original Assembly-CSharp.dll> <0Harmony.dll>");
            return 2;
        }

        string ptmod = args[0];
        _managed = args[1];
        _original = args[2];
        _harmony = args[3];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        Assembly mod;
        try
        {
            mod = Assembly.LoadFrom(ptmod);
            Assembly.LoadFrom(_original);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("cannot load the assemblies: " + ex.Message);
            return 2;
        }

        var report = new Report();
        try
        {
            CheckFeatures(mod, report);
            CheckPatches(mod, report);
            ListResources(mod, report);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("the audit failed: " + ex);
            return 2;
        }

        report.Print();
        return report.Errors == 0 ? 0 : 1;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        string name = new AssemblyName(e.Name).Name;
        try
        {
            if (name == "Assembly-CSharp") return Assembly.LoadFrom(_original);
            if (name == "0Harmony") return Assembly.LoadFrom(_harmony);
            string path = Path.Combine(_managed, name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        catch (Exception)
        {
        }
        return null;
    }

    // ---- features ----

    private static void CheckFeatures(Assembly mod, Report report)
    {
        Type attribute = mod.GetType("PTMod.Core.ModFeatureAttribute");
        Type contract = mod.GetType("PTMod.Core.IModFeature");
        MethodInfo slugOf = mod.GetType("PTMod.Core.Logic.Features.FeatureGraph").GetMethod("FeatureOfNamespace", BindingFlags.Public | BindingFlags.Static);

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Type type in Types(mod))
        {
            bool marked = type.GetCustomAttributes(attribute, false).Length > 0;
            bool implemented = contract.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface;
            if (implemented && !marked)
            {
                report.Error("feature: " + type.FullName + " implements IModFeature without [ModFeature]; the core does not register it");
                continue;
            }
            if (!marked) continue;

            try
            {
                object feature = Activator.CreateInstance(type);
                string id = (string)contract.GetProperty("Id").GetValue(feature, null);
                string nameKey = (string)contract.GetProperty("NameKey").GetValue(feature, null);
                string descriptionKey = (string)contract.GetProperty("DescriptionKey").GetValue(feature, null);
                string[] depends = (string[])contract.GetProperty("DependsOn").GetValue(feature, null) ?? new string[0];
                bool byDefault = (bool)contract.GetProperty("EnabledByDefault").GetValue(feature, null);
                bool hot = (bool)contract.GetProperty("SupportsHotToggle").GetValue(feature, null);
                string slug = (string)slugOf.Invoke(null, new object[] { type.Namespace });

                if (string.IsNullOrEmpty(id)) report.Error("feature: " + type.FullName + " has an empty Id");
                else if (ids.ContainsKey(id)) report.Error("feature: id '" + id + "' is used by " + ids[id] + " and " + type.FullName);
                else ids[id] = type.FullName;
                if (slug != id) report.Error("feature: " + type.FullName + " has the id '" + id + "' but its namespace belongs to '" + slug + "' (faults are counted by namespace)");
                if (string.IsNullOrEmpty(nameKey) || string.IsNullOrEmpty(descriptionKey)) report.Error("feature: '" + id + "' has no NameKey or DescriptionKey");
                dependencies[id ?? type.FullName] = depends;
                report.Info("feature " + id + ": depends on [" + string.Join(", ", depends) + "], enabled by default " + byDefault + ", hot toggle " + hot);
            }
            catch (Exception ex)
            {
                report.Error("feature: " + type.FullName + " cannot be created: " + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message);
            }
        }

        foreach (KeyValuePair<string, string[]> pair in dependencies)
        {
            foreach (string dependency in pair.Value)
            {
                if (!ids.ContainsKey(dependency)) report.Error("feature: '" + pair.Key + "' depends on '" + dependency + "', which is not a feature of this build");
            }
        }
        foreach (string id in dependencies.Keys)
        {
            if (HasCycle(id, dependencies, new HashSet<string>(StringComparer.Ordinal))) report.Error("feature: '" + id + "' is on a dependency cycle");
        }
        report.Summary("features", ids.Count + " feature(s)");
    }

    private static bool HasCycle(string id, Dictionary<string, string[]> dependencies, HashSet<string> path)
    {
        if (!path.Add(id)) return true;
        string[] next;
        if (dependencies.TryGetValue(id, out next))
        {
            foreach (string dependency in next)
            {
                if (HasCycle(dependency, dependencies, path)) return true;
            }
        }
        path.Remove(id);
        return false;
    }

    // ---- patches ----

    private sealed class Declared
    {
        public string Owner, Handler, Target, Kind;
        public int Priority;
        public bool Optional, ReturnsBool;
    }

    private static void CheckPatches(Assembly mod, Report report)
    {
        Type attribute = mod.GetType("PTMod.Core.ModPatchAttribute");
        Type kindType = mod.GetType("PTMod.Core.PatchKind");
        Type hooks = mod.GetType("PTMod.Core.ModHooks");
        Type reserved = mod.GetType("PTMod.Core.ReservedTargets");
        MethodInfo resolve = hooks.GetMethod("ResolveTarget", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo checkHandler = hooks.GetMethod("CheckHandler", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo find = reserved.GetMethod("Find", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo slugOf = mod.GetType("PTMod.Core.Logic.Features.FeatureGraph").GetMethod("FeatureOfNamespace", BindingFlags.Public | BindingFlags.Static);

        var byTarget = new SortedDictionary<string, List<Declared>>(StringComparer.Ordinal);
        int total = 0;
        foreach (Type type in Types(mod))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                object[] attributes;
                try
                {
                    attributes = method.GetCustomAttributes(attribute, false);
                }
                catch (Exception ex)
                {
                    report.Error("patch: cannot read the attributes of " + type.Name + "." + method.Name + ": " + ex.Message);
                    continue;
                }

                foreach (object patch in attributes)
                {
                    total++;
                    Type target = (Type)attribute.GetProperty("Target").GetValue(patch, null);
                    string name = (string)attribute.GetProperty("Method").GetValue(patch, null);
                    object kind = attribute.GetProperty("Kind").GetValue(patch, null);
                    Type[] argTypes = (Type[])attribute.GetProperty("ArgTypes").GetValue(patch, null);
                    string inner = (string)attribute.GetProperty("InnerType").GetValue(patch, null);
                    int priority = (int)attribute.GetProperty("Priority").GetValue(patch, null);
                    bool optional = (bool)attribute.GetProperty("Optional").GetValue(patch, null);
                    string owner = (string)slugOf.Invoke(null, new object[] { type.Namespace }) ?? "mod-framework";
                    string handler = type.Name + "." + method.Name;
                    string declared = (target == null ? "?" : target.Name) + (string.IsNullOrEmpty(inner) ? string.Empty : "." + inner) + "." + name;

                    object[] call = { target, inner, name, argTypes, null };
                    var resolved = (MethodBase)resolve.Invoke(null, call);
                    if (resolved == null)
                    {
                        string message = "patch: " + owner + " " + handler + ": the target " + declared + " is not found (" + call[4] + ")";
                        if (optional) report.Warn(message + "; the patch is optional"); else report.Error(message + "; the feature would be Unavailable");
                        continue;
                    }

                    string display = resolved.DeclaringType.FullName + "." + resolved.Name + "(" + string.Join(",", resolved.GetParameters().Select(p => p.ParameterType.Name)) + ")";
                    object reservation = find.Invoke(null, new object[] { resolved });
                    if (reservation != null)
                    {
                        bool rejected = (bool)reservation.GetType().GetMethod("Rejects").Invoke(reservation, new object[] { owner, kind });
                        if (rejected) report.Error("patch: " + owner + " " + handler + " patches " + display + ", which is reserved (" + reservation.GetType().GetProperty("Patch").GetValue(reservation, null) + ")");
                    }

                    string problem = (string)checkHandler.Invoke(null, new object[] { method, kind });
                    if (problem != null) report.Error("patch: " + owner + " " + handler + " " + problem);

                    if (kind.ToString() != "Transpiler")
                    {
                        foreach (string issue in CheckBindings(method, resolved, kind.ToString()))
                        {
                            bool error = issue.StartsWith("ERROR ");
                            string text = "patch: " + owner + " " + handler + " on " + display + ": " + issue.Substring(issue.IndexOf(' ') + 1);
                            if (error) report.Error(text); else report.Warn(text);
                        }
                    }

                    List<Declared> list;
                    if (!byTarget.TryGetValue(display, out list)) byTarget[display] = list = new List<Declared>();
                    list.Add(new Declared
                    {
                        Owner = owner, Handler = handler, Target = display, Kind = kind.ToString(), Priority = priority, Optional = optional,
                        ReturnsBool = method.ReturnType == typeof(bool),
                    });
                }
            }
        }

        foreach (KeyValuePair<string, List<Declared>> pair in byTarget)
        {
            List<Declared> list = pair.Value;
            if (list.Select(d => d.Owner).Distinct().Count() < 2) continue;

            report.Info("overlap " + pair.Key + ": " + string.Join("; ", list.Select(d => d.Owner + "/" + d.Kind + (d.Optional ? "(opt)" : string.Empty) + " p" + d.Priority + (d.ReturnsBool ? " [bool]" : string.Empty))));
            if (list.Count(d => d.Kind == "Transpiler") > 1) report.Error("patch: " + pair.Key + " has more than one transpiler: " + string.Join(", ", list.Where(d => d.Kind == "Transpiler").Select(d => d.Owner)));
            var skipping = list.Where(d => d.Kind == "Prefix" && d.ReturnsBool && d.Owner != "mod-framework").Select(d => d.Owner).Distinct().ToList();
            if (skipping.Count > 1) report.Warn("patch: " + pair.Key + " has prefixes of " + string.Join(", ", skipping) + " that may skip the original: check that they do not both decide");
        }
        report.Summary("patches", total + " declaration(s) on " + byTarget.Count + " method(s)");
    }

    private static IEnumerable<string> CheckBindings(MethodInfo handler, MethodBase target, string kind)
    {
        ParameterInfo[] targetParameters = target.GetParameters();
        Type returnType = target is MethodInfo ? ((MethodInfo)target).ReturnType : typeof(void);
        foreach (ParameterInfo parameter in handler.GetParameters())
        {
            string name = parameter.Name;
            Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
            if (name == "__instance")
            {
                if (target.IsStatic) yield return "ERROR __instance on a static target";
                else if (!type.IsAssignableFrom(target.DeclaringType) && !target.DeclaringType.IsAssignableFrom(type)) yield return "ERROR __instance is " + type.Name + ", the target's class is " + target.DeclaringType.Name;
            }
            else if (name == "__result")
            {
                if (returnType == typeof(void)) yield return "ERROR __result on a void target";
                else if (!type.IsAssignableFrom(returnType) && !returnType.IsAssignableFrom(type)) yield return "ERROR __result is " + type.Name + ", the target returns " + returnType.Name;
            }
            else if (name == "__state" || name == "__originalMethod" || name == "__args" || name == "__runOriginal")
            {
            }
            else if (name == "__exception")
            {
                if (kind != "Finalizer") yield return "WARN __exception outside a finalizer";
            }
            else if (name.StartsWith("___"))
            {
                string fieldName = name.Substring(3);
                FieldInfo field = null;
                for (Type t = target.DeclaringType; t != null && field == null; t = t.BaseType)
                {
                    field = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                }
                if (field == null) yield return "ERROR the injected field '" + fieldName + "' is not in " + target.DeclaringType.Name;
                else
                {
                    if (!type.IsAssignableFrom(field.FieldType) && !(parameter.ParameterType.IsByRef && field.FieldType == type)) yield return "ERROR the field '" + fieldName + "' is " + field.FieldType.Name + " but the parameter is " + type.Name;
                    if (!field.IsStatic && target.IsStatic) yield return "ERROR the instance field '" + fieldName + "' on a static target";
                }
            }
            else if (name.StartsWith("__") && name.Substring(2).All(char.IsDigit))
            {
                if (int.Parse(name.Substring(2)) >= targetParameters.Length) yield return "ERROR " + name + " is out of range";
            }
            else
            {
                ParameterInfo bound = targetParameters.FirstOrDefault(p => p.Name == name);
                if (bound == null) yield return "ERROR the parameter '" + name + "' is not in the target (" + string.Join(", ", targetParameters.Select(p => p.Name)) + ")";
                else
                {
                    Type boundType = bound.ParameterType.IsByRef ? bound.ParameterType.GetElementType() : bound.ParameterType;
                    if (!type.IsAssignableFrom(boundType)) yield return "ERROR the parameter '" + name + "' is " + boundType.Name + " in the target but " + type.Name + " in the patch";
                }
            }
        }
    }

    // ---- resources ----

    private static void ListResources(Assembly mod, Report report)
    {
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string resource in mod.GetManifestResourceNames())
        {
            string[] parts = resource.Split('/');
            string key = parts.Length > 1 && parts[0] == "content" ? parts[1] : parts.Length > 2 && parts[0] == "l10n" ? parts[2].Replace(".json", string.Empty) : parts[0];
            int n;
            count.TryGetValue(key, out n);
            count[key] = n + 1;
        }
        foreach (KeyValuePair<string, int> pair in count) report.Info("resources " + pair.Key + ": " + pair.Value);
        report.Summary("resources", mod.GetManifestResourceNames().Length + " embedded file(s)");
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.WriteLine("WARN some types could not be loaded: " + string.Join("; ", ex.LoaderExceptions.Select(e => e.Message).Distinct()));
            return ex.Types.Where(t => t != null).ToArray();
        }
    }

    private sealed class Report
    {
        private readonly List<string> _lines = new List<string>();
        private readonly List<string> _summaries = new List<string>();

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        public void Info(string text)
        {
            _lines.Add("  info  " + text);
        }

        public void Warn(string text)
        {
            Warnings++;
            _lines.Add("  WARN  " + text);
        }

        public void Error(string text)
        {
            Errors++;
            _lines.Add("  ERROR " + text);
        }

        public void Summary(string area, string text)
        {
            _summaries.Add(area + ": " + text);
        }

        public void Print()
        {
            var output = new StringBuilder();
            foreach (string line in _lines) output.AppendLine(line);
            foreach (string summary in _summaries) output.AppendLine(summary);
            output.AppendLine("audit: " + Errors + " error(s), " + Warnings + " warning(s)");
            Console.Write(output.ToString());
        }
    }
}
