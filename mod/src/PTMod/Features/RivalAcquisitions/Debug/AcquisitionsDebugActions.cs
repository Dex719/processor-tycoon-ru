#if PTMOD_DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery;
using UnityEngine;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameDataFinder = ProcessorTycoon.DataFinder;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The debug actions of the page of the feature in the hub (TSK-021; design.md "Ручные сценарии в игре", the preparation of M1-M5), so the owner can bring a rival to
    /// default or to bankruptcy without waiting for the game to do it:
    /// <list type="bullet">
    /// <item><b>Give the player N million</b> (M1: 2 000 million): <c>CapitalLedger.CapitalIn</c>, the same way the deals move the player's money.</item>
    /// <item><b>Target: balance = k × D</b> (M2: k = -1.2, M5: k = -1.5): the balance of the chosen rival, <c>D</c> being its debt limit (<c>ModCredit.DebtLimitOf</c>).</item>
    /// <item><b>Target: DaysInDefault = N</b> (M5: 119): the count of days in default of the chosen rival.</item>
    /// <item><b>Target: bankrupt</b> (M3): the vanilla way - a balance far under the threshold (-100 × D, see <see cref="AcqDebugMath.BankruptRatio"/>: -1.2 × D would let an
    /// AI company with a factory sell lines on the last day and climb out of default) and 119 days in default, so the game's own check declares the bankruptcy at the next
    /// day (the toast, the mail about the auction, the CPUs of the snapshot).</item>
    /// <item>Two more for the checks of the owner: <b>end the auction</b> of the chosen bankrupt (its last day becomes yesterday: "after the term there are no deals") and
    /// <b>reset the cooldown</b> between hostile takeovers (ten deals of different kinds in a row).</item>
    /// </list>
    /// Every action writes a line to the log of the feature marked <c>debug:</c> (<see cref="Journal"/>), shows a short note, and does nothing to the books of the feature
    /// except the two extras. The block is on the page only when the debug build (<c>PTMOD_DEBUG</c>; a release build has no trace of this file) is run with
    /// <c>debug.acqActions</c> set in the <c>debug</c> section of <c>ptmod_settings.json</c>; the spikes of TSK-003 and TSK-004 (<see cref="SpikeCpuTransfer"/>,
    /// <see cref="SpikeAssets"/>) join it under <c>debug.acqSpikes</c>. The arithmetic is <see cref="AcqDebugMath"/>'s (tested).
    /// </summary>
    internal static class AcquisitionsDebugActions
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;

        /// <summary>The flag of the <c>debug</c> section that shows the debug actions on the page.</summary>
        internal const string FlagActions = "debug.acqActions";

        /// <summary>The flag of the <c>debug</c> section that adds the spikes of TSK-003 and TSK-004 to the page.</summary>
        internal const string FlagSpikes = "debug.acqSpikes";

        // What the player chose survives the rebuild of the block (an action refreshes the state of the companies it shows).
        private static int _millions = AcqDebugMath.DefaultMillions;
        private static double _ratio = AcqDebugMath.DefaultRatio;
        private static int _days = AcqDebugMath.LastDayBeforeBankruptcy;
        private static int _targetSaveId = -1;
        private static RectTransform _list;

        // ---- the block on the page ----

        /// <summary>Puts the block into the page when the flag asks for it (called by the page of the feature in the hub, debug build only).</summary>
        internal static void Build(RectTransform page)
        {
            if (page == null || !ModSettings.Debug(FlagActions)) return;

            _list = ModUI.VerticalList(page, 8f);
            ModHooks.Guard(Feature, "debug/build", FillBlock);
        }

        private static void Refresh()
        {
            AcqUi.Defer(() =>
            {
                if (_list == null) return;
                AcqUi.Clear(_list);
                FillBlock();
            });
        }

        private static void FillBlock()
        {
            RectTransform list = _list;
            if (list == null) return;

            ModUI.Text(list, ModL10n.L("acq.debug.heading"), true);
            ModUI.Text(list, ModL10n.L("acq.debug.hint"));
            if (RivalAcquisitionsFeature.Instance == null || !ModTicks.IsGameReady)
            {
                ModUI.Text(list, ModL10n.L("acq.debug.notReady"));
                return;
            }

            ModUI.Slider(list, ModL10n.L("acq.debug.cash.amount"), AcqDebugMath.MinMillions, AcqDebugMath.MaxMillions, AcqDebugMath.MillionsStep, _millions,
                value => _millions = (int)Math.Round(value));
            ModUI.Button(list, ModL10n.L("acq.debug.cash.give"), GiveCash);

            List<GameAiCompany> rivals = Rivals();
            if (rivals.Count == 0)
            {
                ModUI.Text(list, ModL10n.L("acq.debug.noRivals"));
                return;
            }

            int index = 0;
            for (int i = 0; i < rivals.Count; i++)
            {
                if (rivals[i].SaveID == _targetSaveId) index = i;
            }
            _targetSaveId = rivals[index].SaveID;

            var labels = new List<string>();
            foreach (GameAiCompany rival in rivals) labels.Add(LabelOf(rival));
            ModUI.Dropdown(list, ModL10n.L("acq.debug.target"), labels, index, chosen =>
            {
                if (chosen >= 0 && chosen < rivals.Count) _targetSaveId = rivals[chosen].SaveID;
                Refresh();   // the lists of the spikes follow the target, and so does the line with its numbers
            });
            ModUI.Text(list, InfoOf(rivals[index]));

            ModUI.Slider(list, ModL10n.L("acq.debug.ratio"), (float)AcqDebugMath.MinRatio, (float)AcqDebugMath.MaxRatio, (float)AcqDebugMath.RatioStep, (float)_ratio,
                value => _ratio = value);
            ModUI.Button(list, ModL10n.L("acq.debug.ratio.set"), SetBalance);

            ModUI.Slider(list, ModL10n.L("acq.debug.days"), 0f, AcqDebugMath.LastDayBeforeBankruptcy, 1f, _days, value => _days = AcqDebugMath.ClampDays(value));
            ModUI.Button(list, ModL10n.L("acq.debug.days.set"), SetDays);

            ModUI.Button(list, ModL10n.L("acq.debug.bankrupt"), Bankrupt, ButtonStyle.Standard, ModL10n.L("acq.debug.bankrupt.tip"));
            ModUI.Button(list, ModL10n.L("acq.debug.auction.expire"), EndAuction, ButtonStyle.Standard, ModL10n.L("acq.debug.auction.expire.tip"));
            ModUI.Button(list, ModL10n.L("acq.debug.cooldown.reset"), ResetCooldown, ButtonStyle.Standard, ModL10n.L("acq.debug.cooldown.reset.tip"));

            if (ModSettings.Debug(FlagSpikes))
            {
                SpikeCpuTransfer.Build(list);
                SpikeAssets.Build(list);
            }
        }

        // ---- the actions ----

        private static void GiveCash()
        {
            ModHooks.Guard(Feature, "debug/cash", () =>
            {
                double amount = AcqDebugMath.Dollars(_millions);
                if (!(amount > 0.0))
                {
                    Journal("give cash refused: the amount " + Number(_millions) + " million is not positive (the slider cannot give it)");
                    return;
                }
                if (!CapitalLedger.IsAvailable)
                {
                    Refuse("give cash: the money operations of the mods (CapitalLedger) are not available", "acq.debug.fail.ledger");
                    return;
                }

                CapitalLedger.CapitalIn(Feature, amount, "acq.debug.cash", "debug: " + Number(_millions) + " million for the manual scenarios");
                Journal("gave the player " + Number(_millions) + " million");
                Say("acq.debug.done.cash", Number(_millions));
            });
        }

        private static void SetBalance()
        {
            OnTarget("balance", ai =>
            {
                double limit = ModCredit.DebtLimitOf(ai);
                if (!(limit > 0.0))
                {
                    Refuse("balance: " + Describe(ai) + " has no debt limit D (" + Number(limit) + ")", "acq.debug.fail.limit");
                    return;
                }

                double balance = AcqDebugMath.BalanceFor(_ratio, limit);
                ai.MoneyAmount = (float)balance;
                Journal("target " + Describe(ai) + ": balance = " + _ratio.ToString("0.0", CultureInfo.InvariantCulture) + " x D = " + Number(Math.Round(balance))
                        + " (D = " + Number(Math.Round(limit)) + ")");
                Say("acq.debug.done.balance", AcqUi.Plain(ai.Name), AcqUi.Format.M(balance), _ratio.ToString("0.0", CultureInfo.InvariantCulture));
            });
        }

        private static void SetDays()
        {
            OnTarget("days", ai =>
            {
                ai.DaysInDefault = _days;
                Journal("target " + Describe(ai) + ": DaysInDefault = " + Number(_days));
                Say("acq.debug.done.days", AcqUi.Plain(ai.Name), Number(_days));
            });
        }

        private static void Bankrupt()
        {
            OnTarget("bankrupt", ai =>
            {
                double limit = ModCredit.DebtLimitOf(ai);
                if (!(limit > 0.0))
                {
                    Refuse("bankrupt: " + Describe(ai) + " has no debt limit D (" + Number(limit) + ")", "acq.debug.fail.limit");
                    return;
                }

                double balance = AcqDebugMath.BankruptBalance(limit);
                ai.MoneyAmount = (float)balance;
                ai.DaysInDefault = AcqDebugMath.LastDayBeforeBankruptcy;
                Journal("target " + Describe(ai) + ": the vanilla way to bankruptcy - balance " + Number(Math.Round(balance)) + " (" + AcqDebugMath.BankruptRatio.ToString("0.0", CultureInfo.InvariantCulture)
                        + " x D), DaysInDefault " + Number(AcqDebugMath.LastDayBeforeBankruptcy) + "; the game declares it at the next day");
                Say("acq.debug.done.bankrupt", AcqUi.Plain(ai.Name), AcqUi.Format.M(balance), Number(AcqDebugMath.LastDayBeforeBankruptcy));
            });
        }

        private static void EndAuction()
        {
            ModHooks.Guard(Feature, "debug/auction", () =>
            {
                RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
                GameAiCompany ai = SelectedTarget();
                if (feature == null || ai == null || !ai.IsBankrupt)
                {
                    Refuse("end auction: the feature is not running, or the chosen company is not a bankrupt (" + (ai == null ? "none chosen" : Describe(ai)) + ")", "acq.debug.fail.auction");
                    return;
                }

                AuctionData auction = feature.State.OpenAuctionOf(ai.UniqueID);
                if (auction == null || auction.targetSaveId != ai.SaveID)
                {
                    Refuse("end auction: " + Describe(ai) + " has no open auction", "acq.debug.fail.auction");
                    return;
                }

                string yesterday = AcqDebugMath.YesterdayText(ModTicks.Today);
                if (yesterday.Length == 0)
                {
                    Refuse("end auction: the game has no date", "acq.debug.fail.clock");
                    return;
                }

                string was = auction.until;
                auction.until = yesterday;   // the status rules read the date at once; the monthly pass closes the record
                Journal("target " + Describe(ai) + ": the auction ends yesterday (the last day " + was + " -> " + yesterday + ")");
                Say("acq.debug.done.auction", AcqUi.Plain(ai.Name), yesterday);
                Refresh();
            });
        }

        private static void ResetCooldown()
        {
            ModHooks.Guard(Feature, "debug/cooldown", () =>
            {
                RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
                if (feature == null)
                {
                    Refuse("reset the cooldown: the feature is not running", "acq.debug.notReady");
                    return;
                }

                string was = feature.State.lastHostileOn;
                feature.State.lastHostileOn = "";
                Journal("the cooldown between hostile takeovers is reset (the last one was '" + (was ?? "") + "')");
                Say("acq.debug.done.cooldown");
            });
        }

        // ---- shared with the spikes ----

        /// <summary>A line of the log of the feature marked as a debug action (the "journal" of TSK-021: every action leaves one).</summary>
        internal static void Journal(string text)
        {
            ModLog.Info(Feature, "debug: " + text);
        }

        /// <summary>
        /// A refused action: a line of the log marked as a debug action (as the finished ones leave), then the note with the reason. The notes of refusals say "details in Player.log",
        /// so a click that did nothing is as visible in the log as a click that did something. <paramref name="reason"/> is English and free; <paramref name="key"/> is a key of the tables.
        /// </summary>
        internal static void Refuse(string reason, string key, params object[] args)
        {
            Journal("refused: " + reason);
            Say(key, args);
        }

        /// <summary>A short note on the screen, from a key of the tables.</summary>
        internal static void Say(string key, params object[] args)
        {
            ModNotify.ShowKey(Feature, key, args);
        }

        /// <summary>The rival chosen in the drop-down list, or null when the list has none (or the game is not ready).</summary>
        internal static GameAiCompany SelectedTarget()
        {
            foreach (GameAiCompany ai in Rivals())
            {
                if (ai.SaveID == _targetSaveId) return ai;
            }
            return null;
        }

        /// <summary>A number the way the log writes it.</summary>
        internal static string Number(double value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        /// <summary>A company for a log line: the name and the ids the logs of the feature use.</summary>
        internal static string Describe(ICompany company)
        {
            if (company == null) return "?";
            var ai = company as GameAiCompany;
            return "'" + company.Name + "' (" + (ai != null ? "ai:" + ai.UniqueID.ToString(CultureInfo.InvariantCulture) + ", " : "") + "save "
                   + company.SaveID.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // ---- helpers ----

        /// <summary>Runs an action on the chosen rival that is not a bankrupt, under the guard; refreshes the block after it, so the line with the numbers is current.</summary>
        private static void OnTarget(string name, Action<GameAiCompany> body)
        {
            bool ran = ModHooks.Guard(Feature, "debug/" + name, () =>
            {
                GameAiCompany ai = SelectedTarget();
                if (ai == null || ai.IsBankrupt)
                {
                    Refuse(name + ": the target must be a rival that is not bankrupt (" + (ai == null ? "none chosen" : Describe(ai)) + ")", "acq.debug.fail.target");
                    return;
                }
                body(ai);
            });
            if (ran) Refresh();
        }

        private static List<GameAiCompany> Rivals()
        {
            var result = new List<GameAiCompany>();
            List<ICompany> all = GameDataFinder.FindAllAICompanies(true);
            if (all == null) return result;
            foreach (ICompany company in all)
            {
                var ai = company as GameAiCompany;
                if (ai != null) result.Add(ai);
            }
            return result;
        }

        private static string LabelOf(GameAiCompany ai)
        {
            string state = ai.IsBankrupt ? ModL10n.L("acq.debug.state.bankrupt")
                : ai.IsInDefault ? ModL10n.L("acq.debug.state.default", Number(ai.DaysInDefault))
                : ModL10n.L("acq.debug.state.healthy");
            return AcqUi.Plain(ai.Name) + " (ai:" + ai.UniqueID.ToString(CultureInfo.InvariantCulture) + "): " + state;
        }

        private static string InfoOf(GameAiCompany ai)
        {
            return ModL10n.L("acq.debug.target.info", AcqUi.Format.M(ai.MoneyAmount), AcqUi.Format.M(ModCredit.DebtLimitOf(ai)), Number(ai.DaysInDefault));
        }
    }
}
#endif
