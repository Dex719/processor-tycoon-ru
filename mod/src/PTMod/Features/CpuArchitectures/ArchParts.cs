using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The parts of the feature that switch off one by one when the self-check does not find what they stand on (NFR-2): a missing private field of
    /// <c>CreatorCpu</c> takes only the designer away, the rating and the research go on. The value in the log (<c>Player.log</c>) is the set that works.
    /// </summary>
    [Flags]
    public enum ArchParts
    {
        None = 0,

        /// <summary>The shared lab: the public API of <c>ResearchSector</c> the research calls.</summary>
        Research = 1,

        /// <summary>The CPU designer and the design of the AI: <c>CreatorCpu</c> and <c>DataConverter.CpuDesignToCpu</c>.</summary>
        Designer = 2,

        /// <summary>The binding of a CPU to a family: <c>Project.SetProjectType</c>, <c>ProjectCompletedHandler.AddCpu</c>.</summary>
        Binder = 4,

        /// <summary>The channels of the modifiers the feature writes to (<c>market.rating.cpu</c>, <c>development.cost.design</c>).</summary>
        Market = 8,

        /// <summary>The architecture in the inspector: <c>CpuUI.SetCpu</c> and its <c>architectureText</c>.</summary>
        Inspector = 16,

        /// <summary>The AI researches architectures: <c>AIBehaviourResearch.ChooseANewResearch</c> and its <c>company</c>.</summary>
        Ai = 32,

        /// <summary>The panel, the tab and the window of the hub.</summary>
        Ui = 64,

        /// <summary>Interrupting the player's technology: the private <c>targetTechnology</c> and <c>Hide</c> of <c>CurrentResearchUI</c>.</summary>
        LabUi = 128,

        /// <summary>The monthly upkeep of the software support (stage 2): <c>ICompany.SpendMoney</c>, <c>IsInDefault</c> and the inflation of the game.</summary>
        Support = 256,

        /// <summary>The ecosystem and the market data the AI reads (stage 2): the monthly series of <c>CpuBase.MarketData</c> of every CPU (<c>CpuDataProvider.GetAllCpus</c>).</summary>
        Ecosystem = 512,

        /// <summary>The royalty for x86 without a licence (stage 2): <c>ICompany.EarnMoney</c> and <c>LicenseHelper.X86LicenseHasBeenRevoked</c>.</summary>
        Royalty = 1024,

        /// <summary>The warning of the extension gates in the designer (stage 2): the private <c>currentCoreCount</c> and <c>smtEnabled</c> of <c>CreatorCpu</c> and its <c>UpdateNumbers</c>.</summary>
        Extensions = 2048,
    }
}
