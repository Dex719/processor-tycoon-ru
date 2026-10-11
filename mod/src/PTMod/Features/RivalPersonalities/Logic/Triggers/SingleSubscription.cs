using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// A subscription to an event of an object that lives as long as the game scene (design.md, "Подписки на события": <c>BusinessContractManager.
    /// OnPlayerSignedContract</c>), made again for every scene and never twice for the same object (FR-5 item 6, TSK-011: "повторная подписка после
    /// перезагрузки сцены не дублирует обработчик"). Pure: the game side gives the two actions that add and remove the handler.
    /// <list type="bullet">
    /// <item><see cref="Attach"/> with the object it is subscribed to already does nothing; with a new object (the new scene's) it removes the
    /// handler from the old one and adds it to the new one; with null it only removes.</item>
    /// <item><see cref="Active"/> is 1 while a subscription exists and never more (the number that the log shows); <see cref="Made"/> counts the
    /// subscriptions made since the start.</item>
    /// </list>
    /// </summary>
    public sealed class SingleSubscription<TSource> where TSource : class
    {
        private readonly Action<TSource> _subscribe;
        private readonly Action<TSource> _unsubscribe;
        private TSource _source;

        public SingleSubscription(Action<TSource> subscribe, Action<TSource> unsubscribe)
        {
            _subscribe = subscribe;
            _unsubscribe = unsubscribe;
        }

        /// <summary>1 while the handler is subscribed to an object, else 0.</summary>
        public int Active
        {
            get { return _source != null ? 1 : 0; }
        }

        /// <summary>How many times the handler was subscribed since the start (an attach to the same object is not counted).</summary>
        public int Made { get; private set; }

        /// <summary>
        /// Subscribes the handler to <paramref name="source"/>. True when a new subscription was made. The handler of a previous object is removed
        /// first (a failure of that removal is ignored: the old object is gone with its scene).
        /// </summary>
        public bool Attach(TSource source)
        {
            if (source == null)
            {
                Detach();
                return false;
            }
            if (ReferenceEquals(_source, source)) return false;

            Detach();
            _subscribe(source);
            _source = source;
            Made++;
            return true;
        }

        /// <summary>Removes the handler from the object it is subscribed to, if any.</summary>
        public void Detach()
        {
            TSource old = _source;
            _source = null;
            if (old == null) return;

            try
            {
                _unsubscribe(old);
            }
            catch (Exception)
            {
                // The object of an old scene may be destroyed already; the subscription went with it.
            }
        }
    }
}
