using System;
using System.Collections.Generic;
using Glass.Core.Logging;

namespace Glass.Core.Signals;

///////////////////////////////////////////////////////////////////////////////////////////////
// SignalBus
//
// General-purpose publish/subscribe notification mechanism.  Publishers raise
// strongly-typed signal payloads; subscribers register handlers keyed by the
// signal payload type and receive every instance of that type.
//
// Delivery is synchronous on the publisher's thread, in subscription order
// within a type.  A subscriber that throws does not prevent later subscribers
// from running.  Subscribers that require a specific thread (such as the UI
// thread) marshal at the subscribe site; the bus itself is thread-agnostic.
//
// Subscription is removed explicitly via Unsubscribe.  No automatic lifetime
// management — callers are responsible for unsubscribing when they no longer
// want delivery.
//
// Signal payload types should be reference types (classes), conventionally
// named Signal* (SignalSessionAdded, SignalLowHealth, etc.).  Value-type
// payloads work but will be boxed at the Publish boundary.
//
// Per-type subscriber lists are stored as strongly-typed Action<T> in a
// generic holder, so delivery uses direct delegate invocation rather than
// DynamicInvoke.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SignalBus
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SubscriberList
    //
    // Type-erased base for the per-type subscriber lists.  The bus stores
    // these by Type key; the concrete generic derived class holds the
    // strongly-typed Action<T> delegates and performs direct invocation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private abstract class SubscriberList
    {
        ///////////////////////////////////////////////////////////////////////////////////////
        // InvokeAll
        //
        // Snapshots the current subscribers and invokes each one with the
        // given payload.  The payload is the runtime instance from Publish;
        // the concrete derived class casts to its T.
        //
        // payload:  The signal instance to deliver.
        // payloadTypeName:  The runtime type name, used only for logging.
        ///////////////////////////////////////////////////////////////////////////////////////
        public abstract void InvokeAll(object payload, string payloadTypeName);

        ///////////////////////////////////////////////////////////////////////////////////////
        // Count
        ///////////////////////////////////////////////////////////////////////////////////////
        public abstract int Count
        {
            get;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SubscriberList<T>
    //
    // Concrete strongly-typed subscriber list.  Stores Action<T> delegates
    // and invokes them directly without reflection.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private sealed class SubscriberList<T> : SubscriberList
    {
        private readonly List<Action<T>> _handlers;
        private readonly object _listLock;

        ///////////////////////////////////////////////////////////////////////////////////////
        // SubscriberList (constructor)
        ///////////////////////////////////////////////////////////////////////////////////////
        public SubscriberList()
        {
            _handlers = new List<Action<T>>();
            _listLock = new object();
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // Add
        //
        // Adds a handler.  Returns true if added, false if it was already
        // present (idempotent subscribe).
        //
        // handler:  The delegate to add.
        ///////////////////////////////////////////////////////////////////////////////////////
        public bool Add(Action<T> handler)
        {
            lock (_listLock)
            {
                if (_handlers.Contains(handler))
                {
                    return false;
                }
                _handlers.Add(handler);
                return true;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // Remove
        //
        // Removes a handler.  Returns true if it was present and removed,
        // false if it was not subscribed.
        //
        // handler:  The delegate to remove.
        ///////////////////////////////////////////////////////////////////////////////////////
        public bool Remove(Action<T> handler)
        {
            lock (_listLock)
            {
                return _handlers.Remove(handler);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // Count
        ///////////////////////////////////////////////////////////////////////////////////////
        public override int Count
        {
            get
            {
                lock (_listLock)
                {
                    return _handlers.Count;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////
        // InvokeAll
        //
        // Snapshots the handler list under the per-type lock, then invokes
        // each handler directly (no reflection) outside the lock.  A handler
        // that throws is logged and the next handler still runs.
        //
        // payload:          The signal instance, cast to T for invocation.
        // payloadTypeName:  Runtime type name for log context.
        ///////////////////////////////////////////////////////////////////////////////////////
        public override void InvokeAll(object payload, string payloadTypeName)
        {
            Action<T>[] snapshot;
            lock (_listLock)
            {
                if (_handlers.Count == 0)
                {
                    return;
                }
                snapshot = _handlers.ToArray();
            }

            T typed = (T)payload;

            for (int i = 0; i < snapshot.Length; i++)
            {
                Action<T> handler = snapshot[i];

                try
                {
                    handler(typed);
                }
                catch (Exception ex)
                {
                    DebugLog.Write(LogChannel.SignalBus,
                        "SignalBus.Publish<" + payloadTypeName + ">: subscriber " + i
                        + " threw " + ex.GetType().Name
                        + ": " + ex.Message);
                    DebugLog.Write(LogChannel.SignalBus,
                        "SignalBus.Publish<" + payloadTypeName + ">: stack: " + ex.StackTrace);
                }
            }
        }
    }

    private readonly Dictionary<Type, SubscriberList> _listsByType;
    private readonly object _mapLock;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SignalBus (constructor)
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SignalBus()
    {
        _listsByType = new Dictionary<Type, SubscriberList>();
        _mapLock = new object();

        DebugLog.Write(LogChannel.SignalBus, "SignalBus.ctor: created");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Subscribe
    //
    // Adds a handler for the given signal type.  Idempotent — subscribing the
    // same handler twice for the same type does not deliver twice.
    //
    // T:        The signal payload type the handler will receive.
    // handler:  The delegate to invoke for each published signal of type T.
    //           Must not be null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Subscribe<T>(Action<T> handler)
    {
        if (handler == null)
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Subscribe<" + typeof(T).Name + ">: null handler, ignoring");
            return;
        }

        SubscriberList<T> list = GetOrCreateList<T>();
        bool added = list.Add(handler);

        if (added)
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Subscribe<" + typeof(T).Name
                + ">: added handler, subscriber count for type is now " + list.Count);
        }
        else
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Subscribe<" + typeof(T).Name
                + ">: handler already subscribed, ignoring");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Unsubscribe
    //
    // Removes a handler for the given signal type.  No-op if the handler was
    // not subscribed or no list exists for the type.  An empty subscriber
    // list for a type is retained rather than removed; the cost of an empty
    // list entry is negligible and avoids dictionary churn for types with
    // intermittent subscribers.
    //
    // T:        The signal payload type the handler was registered for.
    // handler:  The delegate to remove.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Unsubscribe<T>(Action<T> handler)
    {
        if (handler == null)
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Unsubscribe<" + typeof(T).Name + ">: null handler, ignoring");
            return;
        }

        SubscriberList<T>? list = GetListOrNull<T>();
        if (list == null)
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Unsubscribe<" + typeof(T).Name
                + ">: no subscribers for type, ignoring");
            return;
        }

        bool removed = list.Remove(handler);

        if (removed)
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Unsubscribe<" + typeof(T).Name
                + ">: removed handler, subscriber count for type is now " + list.Count);
        }
        else
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Unsubscribe<" + typeof(T).Name
                + ">: handler was not subscribed, ignoring");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Publish
    //
    // Delivers a signal to every current subscriber of its runtime type,
    // synchronously, in subscription order.  A subscriber that throws is
    // logged and the next subscriber still runs.
    //
    // The runtime type of signal is the dispatch key, not the static type T.
    // A Publish<BaseSignal>(derived) call delivers only to subscribers of the
    // derived type, not to subscribers of the base type.  Signal types should
    // be concrete leaf types to keep this unambiguous.
    //
    // T:       The static type of the signal reference.  The runtime type is
    //          what actually drives dispatch.
    // signal:  The signal payload.  Must not be null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Publish<T>(T signal)
    {
        if (signal == null)
        {
            DebugLog.Write(LogChannel.SignalBus,
                "SignalBus.Publish<" + typeof(T).Name + ">: null signal, ignoring");
            return;
        }

        Type runtimeType = signal.GetType();
        SubscriberList? list;
        lock (_mapLock)
        {
            if (!_listsByType.TryGetValue(runtimeType, out list))
            {
                return;
            }
        }

        list.InvokeAll(signal, runtimeType.Name);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SubscriberCount
    //
    // Returns the count of registered handlers for the given signal type.
    //
    // T:  The signal payload type.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int SubscriberCount<T>()
    {
        SubscriberList<T>? list = GetListOrNull<T>();
        if (list == null)
        {
            return 0;
        }
        return list.Count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOrCreateList
    //
    // Returns the strongly-typed subscriber list for T, creating and
    // inserting a new empty list if one does not yet exist.
    //
    // T:  The signal payload type.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SubscriberList<T> GetOrCreateList<T>()
    {
        lock (_mapLock)
        {
            SubscriberList? existing;
            if (_listsByType.TryGetValue(typeof(T), out existing))
            {
                return (SubscriberList<T>)existing;
            }

            SubscriberList<T> created = new SubscriberList<T>();
            _listsByType[typeof(T)] = created;
            return created;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetListOrNull
    //
    // Returns the strongly-typed subscriber list for T, or null if no list
    // has been created yet (no subscribe has ever happened for this type).
    //
    // T:  The signal payload type.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SubscriberList<T>? GetListOrNull<T>()
    {
        lock (_mapLock)
        {
            SubscriberList? existing;
            if (!_listsByType.TryGetValue(typeof(T), out existing))
            {
                return null;
            }
            return (SubscriberList<T>)existing;
        }
    }
}