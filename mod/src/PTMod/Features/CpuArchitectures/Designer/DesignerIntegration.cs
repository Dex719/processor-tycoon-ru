using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The list "Архитектура" in the CPU designer (FR-4 item 1; design.md Component 6): a dropdown of the game's look (<c>ModUI.Dropdown</c>, a clone
    /// of the template <c>Widget.Dropdown</c>) put right after the list of cores (<c>CreatorCpu.coreCountDropdown</c>). It lists the families the
    /// player may design on (<see cref="DesignerChoices.Options"/>) and opens on the family chosen last (<c>CompanyArchState.Active</c> of the player),
    /// or the newest one. A pick goes to <see cref="DesignContext.PlayerSelectedFamily"/> and to <c>Active</c> of the player (the choice is kept in the save).
    /// <para>
    /// Built by the postfix of <c>CreatorCpu.Initialize</c> (0.2 s after the scene starts); when the templates of <c>ModUI</c> are not ready then, by the
    /// next <c>CreatorCpu.UpdateUI</c> (the window opens, a part changes), which also refreshes the options. Hidden while the feature is off and when
    /// the part <c>Designer</c> did not pass the self-check.
    /// </para>
    /// <para>
    /// Under the list sits a line "Рейтинг по сегментам: ВС ×… · СС ×… · НС ×… · Пром ×…" (FR-4 item 7, TSK-010): the contribution of the picked family to
    /// the rating of the player's CPU in each segment this year, by the same math as the provider of <c>market.rating.cpu</c>
    /// (<see cref="RatingForecast"/>) and without the contributions of other features. It follows the pick, the strength knob and the language.
    /// </para>
    /// <para>
    /// At stage 2 two more rows may appear under it, each only while it has something to say: "Незрелые инструменты: +N % к стоимости, осталось M мес." for the first year of a
    /// family the player researched (FR-12 item 4, <see cref="ImmatureTools"/>), and the warning of the gates of the extensions, "Многоядерность раскроется при поддержке ≥ 40
    /// (сейчас 20)" (FR-13 item 2, <see cref="ExtensionWarnings"/>), for the cores and SMT of the design on the screen (<see cref="DesignContext.CoreCount"/>, noted by
    /// <see cref="ExtensionPatches"/>). The rating line takes the same cores and the ecosystem of the family, so it shows what the market will give the CPU.
    /// </para>
    /// </summary>
    internal sealed class DesignerIntegration
    {
        private const float PlacementGap = 4f;

        private readonly ArchitecturesFeature _feature;
        private readonly List<string> _ids = new List<string>();
        private CreatorCpu _creator;
        private DropdownUI _dropdown;
        private TMP_Dropdown _tmp;
        private GameObject _root;
        private InfoText _ratingLine;
        private GameObject _ratingRoot;
        private string _ratingText;
        private InfoText _immatureLine;
        private string _immatureText = string.Empty;
        private InfoText _extLine;
        private string _extText = string.Empty;
        private bool _shown;
        private string _language;
        private bool _visible = true;
        private bool _failureLogged;

        public DesignerIntegration(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        private bool Usable
        {
            get { return !_feature.IsNeutralNow && (_feature.Parts & ArchParts.Designer) != 0 && _feature.Registry != null; }
        }

        // ---- hooks ----

        /// <summary>Postfix of <c>CreatorCpu.Initialize</c>: builds the list next to the list of cores.</summary>
        public void OnCreatorInitialized(CreatorCpu creator, DropdownUI coreCountDropdown)
        {
            if (!Usable) return;
            EnsureBuilt(creator, coreCountDropdown);
            Refresh();
        }

        /// <summary>Postfix of <c>CreatorCpu.UpdateUI</c>: the window opened or a part changed - the list is built if it is not yet and its options refreshed.</summary>
        public void OnCreatorUiUpdated(CreatorCpu creator, DropdownUI coreCountDropdown)
        {
            if (!Usable)
            {
                ApplyVisibility(false);
                return;
            }
            EnsureBuilt(creator, coreCountDropdown);
            ApplyVisibility(_visible);
            Refresh();
        }

        /// <summary>The designer updated while the feature is neutral (off, or the fast-forward of a new game): a list made earlier stays hidden.</summary>
        public void OnCreatorNeutral()
        {
            ApplyVisibility(false);
        }

        /// <summary>Shows or hides the list (the feature switched on or off, FR-9 item 2).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyVisibility(visible && Usable);
            if (visible && _root != null) Refresh();
        }

        /// <summary>The knob of the hub or the language changed: the forecast line is written again for the family in the list.</summary>
        public void RefreshForecast()
        {
            UpdateRatingLine(DesignContext.PlayerSelectedFamily);
        }

        /// <summary>The cores or SMT of the design changed (<see cref="ExtensionPatches"/>): the forecast and the warning of the gates are written again.</summary>
        public void OnDesignChanged()
        {
            if (_root == null || !Usable) return;
            UpdateRatingLine(DesignContext.PlayerSelectedFamily);
        }

        // ---- the list ----

        private void EnsureBuilt(CreatorCpu creator, DropdownUI coreCountDropdown)
        {
            if (_root != null && _dropdown != null && ReferenceEquals(_creator, creator)) return;

            // A new designer (a new scene): the old list went with the old scene.
            _creator = null;
            _dropdown = null;
            _tmp = null;
            _root = null;
            _ratingLine = null;
            _ratingRoot = null;
            _ratingText = null;
            _immatureLine = null;
            _immatureText = string.Empty;
            _extLine = null;
            _extText = string.Empty;
            _ids.Clear();
            if (creator == null || coreCountDropdown == null) return;

            Transform anchor = coreCountDropdown.transform;
            Transform parent = anchor.parent;
            if (parent == null) return;
            if (parent.GetComponent<HorizontalLayoutGroup>() != null && parent.parent != null)
            {
                // The list of cores sits in a row with its caption: the new row goes after that row, not into it.
                anchor = parent;
                parent = parent.parent;
            }

            List<string> options = CurrentOptions();
            int index = DesignerChoices.DefaultIndex(_feature.Catalog, options, Preferred());
            DropdownUI dropdown = ModUI.Dropdown(parent, ModL10n.L("arch.designer.label"), Names(options), Math.Max(0, index), OnPicked,
                                                 ModL10n.L("arch.designer.tip"));
            if (dropdown == null)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    ModLog.Warn(ArchitecturesFeature.FeatureId, "designer: the dropdown template is not available yet; the list is built on the next refresh");
                }
                return;
            }

            Transform rootTransform = dropdown.transform.parent != null && dropdown.transform.parent != parent ? dropdown.transform.parent : dropdown.transform;
            Place(rootTransform, anchor, parent);

            // The forecast of the rating: one more row right under the list. Without its template the list still works.
            InfoText line = ModUI.Info(parent, ModL10n.L("arch.designer.ratingTitle"), string.Empty, ModL10n.L("arch.designer.ratingTip"));
            if (line != null)
            {
                Place(line.transform, rootTransform, parent);
                // NFR-6: the tooltip carries the formula in numbers for the family in the list (it replaces the fixed text of the row; the explanation is its first lines).
                ModUI.Tooltip(line.gameObject, RatingTooltip);
            }

            // Stage 2: the notes under the forecast (immature tools, gates of the extensions), made hidden and shown while they have a text.
            InfoText immature = ModUI.Info(parent, ModL10n.L("arch.designer.immatureTitle"), string.Empty, ModL10n.L("arch.designer.immatureTip"));
            if (immature != null)
            {
                Place(immature.transform, line != null ? line.transform : rootTransform, parent);
                immature.gameObject.SetActive(false);
            }
            InfoText extensions = ModUI.Info(parent, ModL10n.L("arch.designer.extTitle"), string.Empty, ModL10n.L("arch.designer.extTip"));
            if (extensions != null)
            {
                Place(extensions.transform, immature != null ? immature.transform : (line != null ? line.transform : rootTransform), parent);
                extensions.gameObject.SetActive(false);
            }

            _creator = creator;
            _dropdown = dropdown;
            _tmp = dropdown.GetComponent<TMP_Dropdown>();
            _root = rootTransform.gameObject;
            _ratingLine = line;
            _ratingRoot = line == null ? null : line.gameObject;
            _immatureLine = immature;
            _extLine = extensions;
            _ids.AddRange(options);
            _language = ModL10n.Language;
            ApplyVisibility(_visible && Usable);
            ModLog.Info(ArchitecturesFeature.FeatureId, "designer: the list of architectures is in the CPU designer (" + options.Count + " families, "
                                                        + (parent.GetComponent<LayoutGroup>() != null ? "in the layout of " : "placed under ") + anchor.name + ")");
        }

        /// <summary>
        /// Puts the new row right after the list of cores: in a layout group by its order among the children, otherwise by position (the same
        /// anchors, one row lower).
        /// </summary>
        private static void Place(Transform root, Transform anchor, Transform parent)
        {
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                root.SetSiblingIndex(Math.Min(anchor.GetSiblingIndex() + 1, parent.childCount - 1));
                return;
            }

            var rootRect = root as RectTransform;
            var anchorRect = anchor as RectTransform;
            if (rootRect == null || anchorRect == null) return;

            float height = anchorRect.rect.height > 0f ? anchorRect.rect.height : rootRect.rect.height;
            rootRect.anchorMin = anchorRect.anchorMin;
            rootRect.anchorMax = anchorRect.anchorMax;
            rootRect.pivot = anchorRect.pivot;
            rootRect.sizeDelta = new Vector2(anchorRect.sizeDelta.x, height);
            rootRect.anchoredPosition = anchorRect.anchoredPosition - new Vector2(0f, height + PlacementGap);
        }

        /// <summary>The options and the selection follow the player's families; nothing is rebuilt when they did not change.</summary>
        private void Refresh()
        {
            if (_root == null || _dropdown == null) return;

            List<string> options = CurrentOptions();
            int index = DesignerChoices.DefaultIndex(_feature.Catalog, options, Preferred());
            if (index < 0) return;

            bool rebuilt = false;
            string language = ModL10n.Language;
            if (!DesignerChoices.SameOptions(options, _ids) || language != _language)
            {
                _ids.Clear();
                _ids.AddRange(options);
                _language = language;
                if (_tmp != null)
                {
                    _tmp.ClearOptions();   // no event: TMP_Dropdown raises onValueChanged only for a pick or a set value
                    _tmp.AddOptions(Names(options));
                    rebuilt = true;
                }
            }

            if (_tmp != null && (rebuilt || _tmp.value != index))
            {
                _tmp.SetValueWithoutNotify(index);
                _tmp.RefreshShownValue();
            }
            DesignContext.PlayerSelectedFamily = _ids[index];
            UpdateRatingLine(_ids[index]);
        }

        /// <summary>The player picked a family: it is the family of the next CPU and the default of the list from now on (kept in the save).</summary>
        private void OnPicked(int index)
        {
            if (index < 0 || index >= _ids.Count) return;
            string id = _ids[index];
            DesignContext.PlayerSelectedFamily = id;
            UpdateRatingLine(id);

            CompanyArchState player = PlayerState();
            if (player != null && player.Active != id)
            {
                player.Active = id;
                _feature.Registry.NotifyCompanyChanged(player.CompanyId);
            }
            ModLog.Debug(ArchitecturesFeature.FeatureId, "designer: the player picked " + id);
        }

        private void ApplyVisibility(bool visible)
        {
            _shown = visible;
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
            if (_ratingRoot != null && _ratingRoot.activeSelf != visible) _ratingRoot.SetActive(visible);
            ApplyNote(_immatureLine, _immatureText);
            ApplyNote(_extLine, _extText);
        }

        /// <summary>A note row is on the screen while the list is and while it has a text.</summary>
        private void ApplyNote(InfoText line, string text)
        {
            if (line == null) return;
            bool on = _shown && !string.IsNullOrEmpty(text);
            if (line.gameObject.activeSelf != on) line.gameObject.SetActive(on);
        }

        // ---- the forecast line ----

        /// <summary>
        /// "Рейтинг по сегментам": the contribution of the picked family in the four segments this year (<see cref="RatingForecast.All"/>), the same
        /// numbers the provider gives a CPU of that family - at stage 2 with the support of the player, the ecosystem of the family and the cores and SMT of the design on the
        /// screen. Written only when the text changed (the window refreshes it often). Also writes the two notes under it (<see cref="UpdateNotes"/>).
        /// </summary>
        private void UpdateRatingLine(string familyId)
        {
            if (_ratingLine == null) return;

            string text = string.Empty;
            ArchFamily family = familyId == null ? null : _feature.Catalog.Get(familyId);
            FamilyState state = null;
            DateTime today = ModTicks.Today;
            if (family != null)
            {
                BalanceConfig balance = _feature.Balance;
                int year = today == default(DateTime) ? family.Year : today.Year;

                // The support of the player for the family counts at stage 2 only; so do the ecosystem and the gates of the cores and SMT of the design.
                CompanyArchState player = PlayerState();
                state = player == null ? null : player.GetFamily(family.Id);
                float support = state == null ? 0f : state.Support;

                float[] values = RatingForecast.All(family, year, balance, support, _feature.EcosystemOf(family.Id), DesignContext.CoreCount, DesignContext.Smt, _feature.RatingStrength);
                string[] texts = RatingForecast.FormatAll(values, ModL10n.Language);
                text = ModL10n.L("arch.segments", texts[0], texts[1], texts[2], texts[3]);
            }

            if (!string.Equals(text, _ratingText, StringComparison.Ordinal))
            {
                _ratingText = text;
                _ratingLine.SetTexts(ModL10n.L("arch.designer.ratingTitle"), text);
            }
            UpdateNotes(family, state, today);
        }

        /// <summary>
        /// The notes of stage 2 (FR-12 item 4, FR-13 item 2): the immature tools of a family researched less than a year ago, and the extensions of the design that wait for more support.
        /// Empty at stage 1 and for a family the player was given at the start.
        /// </summary>
        private void UpdateNotes(ArchFamily family, FamilyState state, DateTime today)
        {
            string immature = string.Empty;
            string extensions = string.Empty;

            BalanceConfig balance = _feature.Balance;
            if (family != null && balance.IsFullStage && today != default(DateTime))
            {
                float surcharge = ImmatureTools.Surcharge(state, today, balance.migration);
                if (surcharge > 0f)
                {
                    int months = ImmatureTools.MonthsSince(state.ResearchedYear, state.ResearchedMonth, today);
                    immature = ModL10n.L("arch.designer.immature", ImmatureTools.Percent(surcharge), ImmatureTools.MonthsLeft(months, balance.migration));
                }

                float support = state == null ? 0f : state.Support;
                List<ExtensionWarning> warnings = ExtensionWarnings.For(balance.extensions, support, DesignContext.CoreCount, DesignContext.Smt);
                for (int i = 0; i < warnings.Count; i++)
                {
                    ExtensionWarning warning = warnings[i];
                    string key = ExtensionWarnings.NameKey(warning.Id);
                    string name = key == null ? warning.Id : ModL10n.L(key);
                    if (extensions.Length > 0) extensions += "; ";
                    extensions += ModL10n.L("arch.designer.extWarn", name, SupportMath.Score(warning.Required), SupportMath.Score(warning.Support));
                }
            }

            SetNote(_immatureLine, ref _immatureText, "arch.designer.immatureTitle", immature);
            SetNote(_extLine, ref _extText, "arch.designer.extTitle", extensions);
        }

        private void SetNote(InfoText line, ref string current, string titleKey, string text)
        {
            if (line == null) return;
            if (!string.Equals(text, current, StringComparison.Ordinal))
            {
                current = text;
                line.SetTexts(ModL10n.L(titleKey), text);
            }
            ApplyNote(line, current);
        }

        /// <summary>
        /// The tooltip of the line "Рейтинг по сегментам" (NFR-6): what the line is, then the formula in numbers for the family in the list and the cores and SMT of the design on the
        /// screen: "Высокий сегмент: ×1,05 сродство ×1,04 поддержка ×1,03 экосистема = ×1,12" for each segment.
        /// </summary>
        private ModUI.TooltipText RatingTooltip()
        {
            string body = ModL10n.L("arch.designer.ratingTip");
            ArchFamily family = DesignContext.PlayerSelectedFamily == null ? null : _feature.Catalog.Get(DesignContext.PlayerSelectedFamily);
            if (family != null && Usable)
            {
                BalanceConfig balance = _feature.Balance;
                DateTime today = ModTicks.Today;
                int year = today == default(DateTime) ? family.Year : today.Year;
                CompanyArchState player = PlayerState();
                FamilyState state = player == null ? null : player.GetFamily(family.Id);
                float support = state == null ? 0f : state.Support;

                RatingTerms[] terms = RatingBreakdown.All(family, year, balance, support, _feature.EcosystemOf(family.Id), DesignContext.CoreCount, DesignContext.Smt, _feature.RatingStrength);
                body += "\n" + ModL10n.L(family.NameKey) + ". " + ModL10n.L("arch.card.tip.rating") + "\n" + RatingTooltipText.Lines(terms, _feature.RatingStrength, ModL10n.Language);
            }
            return new ModUI.TooltipText(ModL10n.L("arch.designer.ratingTitle"), body);
        }

        // ---- data ----

        private CompanyArchState PlayerState()
        {
            ArchRegistry registry = _feature.Registry;
            if (registry == null) return null;
            ICompany company = ModCompanies.Resolve(ModCompanies.PlayerKey);
            return ReferenceEquals(company, null) ? registry.Player : registry.GetOrNull(company.SaveID);
        }

        private List<string> CurrentOptions()
        {
            return DesignerChoices.Options(_feature.Catalog, PlayerState());
        }

        /// <summary>The family chosen last: <c>Active</c> of the player, or the pick of this session when the player has no state yet.</summary>
        private string Preferred()
        {
            CompanyArchState player = PlayerState();
            return player != null && player.Active != null ? player.Active : DesignContext.PlayerSelectedFamily;
        }

        private List<string> Names(List<string> ids)
        {
            var names = new List<string>(ids.Count);
            foreach (string id in ids)
            {
                ArchFamily family = _feature.Catalog.Get(id);
                names.Add(family == null ? id : ModL10n.L(family.NameKey));
            }
            return names;
        }
    }
}
