using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    // The engine around the moments the core announces, shared by the tests of the saves, the pauses and the batch runs (TSK-021..024). Everything is internal.

    /// <summary>
    /// A whole engine without the game: the choices, the letters and the windows of <see cref="ChoiceRig"/>, the section behind the <see cref="SessionGate"/> and the
    /// <see cref="EventRuntime"/> that the engine's handlers of <c>GameReady</c>, <c>OnEnabled</c> and <c>OnDisabled</c> call. <see cref="GameReady"/> plays the part of
    /// <c>ModTicks.IsGameReady</c>, <see cref="FeatureOn"/> the part of the switch of the feature that the core's channel registry reads.
    /// </summary>
    internal sealed class LifecycleRig
    {
        public readonly ChoiceRig Choice;
        public readonly EventSaveSection Section;
        public readonly SessionGate Gate;
        public readonly EventRuntime Lifecycle;
        public readonly List<string> SaveWarnings = new List<string>();
        public readonly List<string> Faults = new List<string>();
        public readonly List<string> Infos = new List<string>();

        /// <summary><c>ModTicks.IsGameReady</c>.</summary>
        public bool GameReady;

        /// <summary>The switch of the feature as the core reads it (<c>ModFeatures.IsActive</c>): off, the registry passes by the provider of the engine.</summary>
        public bool FeatureOn = true;

        public int PackLoads;
        public int PackSyncs;

        public LifecycleRig(params EventDefinition[] definitions)
            : this(new FakeCatalog(FakeCatalog.ConfigWith(10), definitions))
        {
        }

        public LifecycleRig(FakeCatalog catalog)
        {
            var sink = new RegistrySink(feature => FeatureOn || feature != "event-system");
            Choice = new ChoiceRig(catalog, sink);
            Section = new EventSaveSection(Choice.Rig.State, Choice.Rig.History, line => SaveWarnings.Add(line), Choice.Rig.Runtime);
            Gate = new SessionGate(Section, () => GameReady, line => Infos.Add(line));
            Lifecycle = new EventRuntime(Choice.Rig.Runtime, Choice.Rig.Scheduler, Choice.Choices, Choice.Ui, Gate, () => Choice.Today,
                () => PackLoads++, () => PackSyncs++, (site, error) => Faults.Add(site + ": " + error.Message), line => Infos.Add(line));
        }

        public EffectsRig Rig
        {
            get { return Choice.Rig; }
        }

        public int Today
        {
            get { return Choice.Today; }
        }

        /// <summary>The text of the section as the core would write it into the side file.</summary>
        public string Save()
        {
            return ModJson.Write(Gate.Write(), false);
        }

        /// <summary>
        /// A save is loaded into this engine: the core reads the side file (the game is not ready), then <c>GameReady</c> comes and the handler of the engine runs. A null
        /// text is a save without the section.
        /// </summary>
        public void Load(string text, ReadyKind kind = ReadyKind.Loaded)
        {
            GameReady = false;
            if (text == null) Gate.ResetToDefaults();
            else Gate.Read(Parse(text), EventSaveSection.Schema);
            GameReady = true;
            Lifecycle.OnGameReady(kind);
        }

        /// <summary>The player switches the feature off: the day is remembered, the core stops passing its providers.</summary>
        public void SwitchOff()
        {
            FeatureOn = false;
            Rig.Sink.Registry.FeatureStateChanged("event-system");
            Lifecycle.OnDisabled();
        }

        /// <summary>The player switches the feature on: the core passes its providers again, <c>OnEnabled</c> runs.</summary>
        public void SwitchOn()
        {
            FeatureOn = true;
            Rig.Sink.Registry.FeatureStateChanged("event-system");
            Lifecycle.OnEnabled();
        }

        public static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the section is JSON: " + error);
            return (JsonObject)node;
        }

        /// <summary>The mail service restored the letters of the save (<c>ModMail.RestoreInbox</c>): the same letters, the same ids, in the new engine's mail.</summary>
        public static void CopyLetters(FakeMail from, FakeMail to)
        {
            foreach (FakeMail.Letter letter in from.Letters)
            {
                var copy = new FakeMail.Letter
                {
                    Id = letter.Id, Plan = letter.Plan, Delivered = letter.Delivered, DeliveredDay = letter.DeliveredDay, Status = letter.Status, Answer = letter.Answer,
                };
                foreach (KeyValuePair<string, FakeMail.Button> button in letter.Buttons)
                {
                    copy.Buttons[button.Key] = new FakeMail.Button { Enabled = button.Value.Enabled, ReasonKey = button.Value.ReasonKey, ReasonArgs = button.Value.ReasonArgs };
                }
                to.Letters.Add(copy);
            }
        }
    }
}
