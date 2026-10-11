using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The call of a member of a provider of another feature (FR-7 item 3, decision D11), without the game: it runs under a <c>try/catch</c>; when
    /// the provider throws, the provider alone is switched off in its book and the failure is handed to <c>report</c> with the id of the feature
    /// that owns the code of the provider (the adapter passes <c>ModHooks.ReportFault</c>), never with the id of <c>research-tree-ux</c>, so another
    /// feature's mistake cannot bring this one to the state Faulted. A provider that is off is not called again. The exception never leaves.
    /// </summary>
    public static class ProviderCalls
    {
        /// <summary>Runs a call that returns nothing. True when it ran to the end.</summary>
        public static bool Run<T>(ProviderBook<T> book, ProviderEntry<T> entry, string site, Action<T> call, Action<string, string, Exception> report)
            where T : class
        {
            if (book == null || entry == null || call == null || entry.Disabled) return false;

            try
            {
                call(entry.Provider);
                return true;
            }
            catch (Exception e)
            {
                Fail(book, entry, site, e, report);
                return false;
            }
        }

        /// <summary>Runs a call that returns a value; <paramref name="fallback"/> when the provider is off or throws.</summary>
        public static TResult Run<T, TResult>(ProviderBook<T> book, ProviderEntry<T> entry, string site, Func<T, TResult> call, TResult fallback,
            Action<string, string, Exception> report) where T : class
        {
            if (book == null || entry == null || call == null || entry.Disabled) return fallback;

            try
            {
                return call(entry.Provider);
            }
            catch (Exception e)
            {
                Fail(book, entry, site, e, report);
                return fallback;
            }
        }

        /// <summary>
        /// Switches the provider off and reports the failure, for a failure that is not an exception thrown by a call (a tab that returned no root).
        /// False when the provider was off already (reported before).
        /// </summary>
        public static bool Fail<T>(ProviderBook<T> book, ProviderEntry<T> entry, string site, Exception error, Action<string, string, Exception> report)
            where T : class
        {
            if (book == null || entry == null || !book.Disable(entry)) return false;

            if (report != null)
            {
                try
                {
                    report(entry.Owner, site, error);
                }
                catch (Exception)
                {
                    // The reporter is the core's fault counter and does not throw; if it did, the provider is off anyway and the caller goes on.
                }
            }
            return true;
        }

        /// <summary>
        /// Copies what a provider returns into a list inside the call, so an iterator that throws while it is walked fails here, under the guard,
        /// and not later in the code that uses the result. A null sequence and null items give nothing; at most <paramref name="max"/> items are kept.
        /// </summary>
        public static List<TItem> Materialize<TItem>(IEnumerable<TItem> items, int max) where TItem : class
        {
            var list = new List<TItem>();
            if (items == null) return list;

            foreach (TItem item in items)
            {
                if (item == null) continue;
                if (list.Count >= max) break;
                list.Add(item);
            }
            return list;
        }
    }
}
