using System;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The null-object of <see cref="IRivalPersonalitiesApi"/> (FR-13 item 7): what a caller gets while the feature is off or has not
    /// registered its implementation. <see cref="IsEnabled"/> is false, every read gives null, <see cref="ApplyPriceCut"/> gives 0, the other
    /// calls do nothing, the event never fires. Stateless; one shared instance.
    /// </summary>
    internal sealed class NullRivalPersonalitiesApi : IRivalPersonalitiesApi
    {
        internal static readonly NullRivalPersonalitiesApi Instance = new NullRivalPersonalitiesApi();

        private NullRivalPersonalitiesApi()
        {
        }

        public bool IsEnabled
        {
            get { return false; }
        }

        public float? GetAttitude(ICompany company)
        {
            return null;
        }

        public AttitudeLevel? GetAttitudeLevel(ICompany company)
        {
            return null;
        }

        public string GetArchetypeId(ICompany company)
        {
            return null;
        }

        public void ChangeAttitude(ICompany company, float delta, string reason)
        {
        }

        public void SetDormant(ICompany company, bool dormant)
        {
        }

        public event Action<int, string> ReactionStarted
        {
            add { }
            remove { }
        }

        public int ApplyPriceCut(string reactionId, ICompany rival, Segment segment, float depth, float floorMargin)
        {
            return 0;
        }

        public void RestorePriceCut(string reactionId)
        {
        }
    }
}
