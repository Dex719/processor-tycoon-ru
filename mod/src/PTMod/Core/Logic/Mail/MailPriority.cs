namespace PTMod.Core
{
    /// <summary>
    /// Delivery priority of a mod mail (FR-14 item 8). Delivery order is <see cref="Critical"/>, <see cref="Normal"/>, <see cref="Low"/>;
    /// only <see cref="Normal"/> and <see cref="Low"/> are subject to the daily limits, and on queue overflow the oldest
    /// <see cref="Low"/> goes first, then the oldest <see cref="Normal"/>, never a <see cref="Critical"/>. The numeric values are part of the
    /// contract (a larger value is more urgent); the side file stores the name.
    /// </summary>
    public enum MailPriority
    {
        /// <summary>Digests and hints: delivered last, dropped first when the queue overflows.</summary>
        Low = 0,

        /// <summary>The default.</summary>
        Normal = 1,

        /// <summary>Bypasses the daily limits and is never dropped; still waits for <c>GameReady</c> and for the end of a batch run.</summary>
        Critical = 2,
    }
}
