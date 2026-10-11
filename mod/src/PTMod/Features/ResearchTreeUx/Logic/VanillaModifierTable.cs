using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The permanent perks of the game, <c>Modifiers.GetModifier(type, company)</c>, as a pure function (FR-5 items 2 and 6): a transcription of
    /// the table of the game, so the panel can show the part of a value that belongs to the game and the part that belongs to the mods
    /// (the game's value times the mods' <c>Explain.Total</c> is the value the game uses, patch P1 of the core). Nothing here is a formula of the
    /// feature: the numbers are the constants of the game (1.05, 0.5, 0.8, 0.67, 6.0, 0.25).
    /// <list type="bullet">
    /// <item>0 research speed: ×1.05 for a company with its own fab and development (IDM).</item>
    /// <item>1 factory upkeep: ×0.5 for a foundry.</item>
    /// <item>2 construction: ×0.8 for a foundry, then −0.1 for TCMS (so ×0.9 for TCMS alone, ×0.7 for both).</item>
    /// <item>3 development: ×0.67 for a fabless company.</item>
    /// <item>4 research cost: ×6.0 for the rival that lacks patents.</item>
    /// <item>5 taxes: ×0.67 for TCMS. 6 interest: ×0.25 for TCMS.</item>
    /// </list>
    /// Everything else is 1.
    /// </summary>
    public static class VanillaModifierTable
    {
        /// <summary>The types of the game's <c>ModifierType</c>: 0 to 6.</summary>
        public const int TypeCount = 7;

        public const int ResearchSpeed = 0;
        public const int MaintenanceCost = 1;
        public const int ConstructionCost = 2;
        public const int DevelopmentCost = 3;
        public const int ResearchCost = 4;
        public const int Taxes = 5;
        public const int Interest = 6;

        public const string ReasonIdm = "rtux.mod.reason.idm";
        public const string ReasonFoundry = "rtux.mod.reason.foundry";
        public const string ReasonFabless = "rtux.mod.reason.fabless";
        public const string ReasonTcms = "rtux.mod.reason.tcms";
        public const string ReasonUnlucky = "rtux.mod.reason.unlucky";

        /// <summary>
        /// The value of <c>Modifiers.GetModifier</c> for a type and a company with these facts, without the mods. Float arithmetic in the same
        /// order as the game's, so the results are bit for bit its own. A type outside 0...6 gives 1.
        /// </summary>
        public static float Get(int type, CompanyFacts facts)
        {
            float value = 1.0f;
            switch (type)
            {
                case ResearchSpeed:
                    if (facts.IsIdm) value = 1.05f;
                    break;
                case MaintenanceCost:
                    if (facts.IsFoundry) value = 0.5f;
                    break;
                case ConstructionCost:
                    if (facts.IsFoundry) value = 0.8f;
                    if (facts.IsTcms) value = value - 0.1f;
                    break;
                case DevelopmentCost:
                    if (facts.IsFabless) value = 0.67f;
                    break;
                case ResearchCost:
                    if (facts.IsUnlucky) value = 6.0f;
                    break;
                case Taxes:
                    if (facts.IsTcms) value = 0.67f;
                    break;
                case Interest:
                    if (facts.IsTcms) value = 0.25f;
                    break;
            }
            return value;
        }

        /// <summary>
        /// The keys of the reasons behind the value of <see cref="Get"/>, in the order they apply: "bonus IDM", "foundry", "fabless", "TCMS",
        /// "lacks patents". Empty when the value is the plain 1 (and for a type outside 0...6).
        /// </summary>
        public static string[] Reasons(int type, CompanyFacts facts)
        {
            var reasons = new List<string>(2);
            switch (type)
            {
                case ResearchSpeed:
                    if (facts.IsIdm) reasons.Add(ReasonIdm);
                    break;
                case MaintenanceCost:
                    if (facts.IsFoundry) reasons.Add(ReasonFoundry);
                    break;
                case ConstructionCost:
                    if (facts.IsFoundry) reasons.Add(ReasonFoundry);
                    if (facts.IsTcms) reasons.Add(ReasonTcms);
                    break;
                case DevelopmentCost:
                    if (facts.IsFabless) reasons.Add(ReasonFabless);
                    break;
                case ResearchCost:
                    if (facts.IsUnlucky) reasons.Add(ReasonUnlucky);
                    break;
                case Taxes:
                case Interest:
                    if (facts.IsTcms) reasons.Add(ReasonTcms);
                    break;
            }
            return reasons.ToArray();
        }

        /// <summary>The key of the main reason (the first of <see cref="Reasons"/>), or null when the game's value is the plain 1.</summary>
        public static string ReasonKey(int type, CompanyFacts facts)
        {
            string[] reasons = Reasons(type, facts);
            return reasons.Length == 0 ? null : reasons[0];
        }
    }
}
