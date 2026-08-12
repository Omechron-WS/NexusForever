using NexusForever.Game.Abstract.Spell.Event;

namespace NexusForever.Game.Spell.Event
{
    public class SpellEventManager : ISpellEventManager
    {
        public bool HasPendingEvent => events.Count != 0;

        private readonly List<EventRegistration> events = new();

        public void Update(double lastTick)
        {
            foreach (EventRegistration registration in events.ToArray())
            {
                if (!IsRegistered(registration))
                    continue;

                ISpellEvent spellEvent = registration.SpellEvent;
                spellEvent.Update(lastTick);

                if (!IsRegistered(registration))
                    continue;

                double delay = spellEvent.Delay;
                if (!IsRegistered(registration))
                    continue;

                if (delay <= 0d)
                {
                    Action callback = spellEvent.Callback;
                    if (!IsRegistered(registration))
                        continue;

                    callback.Invoke();
                    RemoveRegistration(registration);
                }
            }
        }

        public void EnqueueEvent(ISpellEvent spellEvent)
        {
            events.Add(new EventRegistration(spellEvent));
        }

        public void CancelEvents()
        {
            events.Clear();
        }

        private bool IsRegistered(EventRegistration registration)
        {
            return events.Any(candidate => ReferenceEquals(candidate, registration));
        }

        private void RemoveRegistration(EventRegistration registration)
        {
            int index = events.FindIndex(candidate => ReferenceEquals(candidate, registration));
            if (index >= 0)
                events.RemoveAt(index);
        }

        private sealed class EventRegistration
        {
            public ISpellEvent SpellEvent { get; }

            public EventRegistration(ISpellEvent spellEvent)
            {
                SpellEvent = spellEvent;
            }
        }
    }
}
