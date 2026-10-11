using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// What the dialog of the founding shows (FR-1 items 3-6, 9; AC-1.1, AC-1.3, AC-1.4, AC-1.5): the price (<c>founding.cost x inflation</c>), the money after
    /// paying, the parts of the tree that open at once, and - when the button must be locked - the first reason with its text. The checks are the ones of
    /// <see cref="GpuDivision.Quote"/>; this class only writes them down.
    /// </summary>
    public sealed class GpuFoundModel
    {
        /// <summary>The division can be founded now.</summary>
        public bool CanFound;

        /// <summary>The first reason the button is locked (<see cref="FoundingBlock.None"/> when it is not).</summary>
        public FoundingBlock Block;

        /// <summary>The reason written for the player (an empty string when there is none).</summary>
        public string ReasonText = string.Empty;

        /// <summary>The price of the founding, money.</summary>
        public float Cost;

        public string Title = string.Empty;
        public string Intro = string.Empty;
        public string CostLine = string.Empty;
        public string MoneyLine = string.Empty;
        public string NodesTitle = string.Empty;

        /// <summary>The lines of the parts that open at once: "name (year)".</summary>
        public List<string> NodeLines = new List<string>();

        /// <summary>The year up to which the tree opens.</summary>
        public int CatchUpYear;

        /// <summary>The question of the confirmation (price and what opens).</summary>
        public string ConfirmTitle = string.Empty;

        public string ConfirmMessage = string.Empty;

        /// <summary>Builds the dialog's data from the services; never null.</summary>
        public static GpuFoundModel Build(GpuDivision division, GpuContentDb content, IGpuGame game, IGpuText text)
        {
            var model = new GpuFoundModel();
            GpuFoundingQuote quote = division.Quote();
            string player = game.PlayerKey;
            float money = game.Money(player);

            model.Block = quote.Block;
            model.Cost = quote.Cost;
            model.CatchUpYear = quote.CatchUpYear;
            model.CanFound = quote.Block == FoundingBlock.None;
            if (!string.IsNullOrEmpty(quote.ReasonKey)) model.ReasonText = text.L(quote.ReasonKey, ArgsOf(quote.ReasonArgs));

            model.Title = text.L("gpu.found.title");
            model.Intro = text.L("gpu.found.intro", quote.CatchUpYear);
            model.CostLine = text.L("gpu.found.cost", text.Money(quote.Cost));
            model.MoneyLine = text.L("gpu.found.money", text.Money(money), text.Money(money - quote.Cost));
            model.NodesTitle = text.L("gpu.found.nodes", quote.FreeNodes != null ? quote.FreeNodes.Count : 0);

            if (quote.FreeNodes != null)
            {
                foreach (string id in quote.FreeNodes)
                {
                    GpuResearchNodeDto node = content.Node(id);
                    string name = GpuNames.Node(content, text, id);
                    model.NodeLines.Add(node != null ? text.L("gpu.found.nodeLine", name, node.year) : name);
                }
            }

            model.ConfirmTitle = text.L("gpu.found.confirm.title");
            model.ConfirmMessage = text.L("gpu.found.confirm.message", text.Money(quote.Cost), model.NodeLines.Count);
            return model;
        }

        private static object[] ArgsOf(string[] args)
        {
            if (args == null) return new object[0];
            var objects = new object[args.Length];
            for (int i = 0; i < args.Length; i++) objects[i] = args[i];
            return objects;
        }
    }
}
