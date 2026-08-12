using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.PublicEvent;
using NexusForever.Game.Static.PublicEvent;

namespace NexusForever.Game.PublicEvent
{
    public class PublicEventCharacter : IPublicEventCharacter
    {
        public ulong CharacterId { get; private set; }

        private readonly Dictionary<uint, IPublicEvent> events = [];

        #region Dependency Injection

        private readonly ILogger<PublicEventCharacter> log;

        public PublicEventCharacter(
            ILogger<PublicEventCharacter> log)
        {
            this.log = log;
        }

        #endregion

        /// <summary>
        /// Initialise <see cref="PublicEventCharacter"/> information for <see cref="IPlayer"/>.
        /// </summary>
        public void Initialise(IPlayer player)
        {
            if (CharacterId != 0ul)
                throw new InvalidOperationException($"Public event information for character {CharacterId} is already initialised!");

            CharacterId = player.CharacterId;

            log.LogTrace($"Public event information initialised for character {CharacterId}.");
        }

        /// <summary>
        /// Start tracking a new <see cref="IPublicEvent"/> for character.
        /// </summary>
        public void AddEvent(IPublicEvent publicEvent)
        {
            if (!events.TryAdd(publicEvent.Id, publicEvent))
                throw new InvalidOperationException($"Character {CharacterId} is already participating in event {publicEvent.Guid}!");

            log.LogTrace($"Public event {publicEvent.Guid} added for character {CharacterId}.");
        }

        /// <summary>
        /// Stop tracking a <see cref="IPublicEvent"/> for character.
        /// </summary>
        public void RemoveEvent(IPublicEvent publicEvent)
        {
            if (!events.TryGetValue(publicEvent.Id, out IPublicEvent currentEvent)
                || !ReferenceEquals(currentEvent, publicEvent)
                || !events.Remove(publicEvent.Id))
                throw new InvalidOperationException($"Character {CharacterId} is not participating in event {publicEvent.Guid}!");

            log.LogTrace($"Public event {publicEvent.Guid} removed for character {CharacterId}.");
        }

        /// <summary>
        /// Invoked when <see cref="IPlayer"/> is removed from the map.
        /// </summary>
        public void OnRemoveFromMap(IPlayer player)
        {
            InvokeEvents(
                @event => @event.LeaveEvent(player, PublicEventRemoveReason.LeftArea),
                "leave");
        }

        /// <summary>
        /// Update any objective for any public event <see cref="IPlayer"/> is part of that meets the supplied <see cref="PublicEventObjectiveType"/>, objectId and count.
        /// </summary>
        public void UpdateObjective(IPlayer player, PublicEventObjectiveType type, uint objectId, int count)
        {
            InvokeEvents(
                @event => @event.UpdateObjective(player, type, objectId, count),
                "update objective for");
        }

        /// <summary>
        /// Update stat for any public event <see cref="IPlayer"/> is part of with the supplied <see cref="PublicEventStat"/> and value.
        /// </summary>
        public void UpdateStat(IPlayer player, PublicEventStat stat, uint value)
        {
            InvokeEvents(
                @event => @event.UpdateStat(player, stat, value),
                "update stat for");
        }

        /// <summary>
        /// Update custom stat for any public event <see cref="IPlayer"/> is part of with the supplied index and value.
        /// </summary>
        public void UpdateCustomStat(IPlayer player, uint index, uint value)
        {
            InvokeEvents(
                @event => @event.UpdateCustomStat(player, index, value),
                "update custom stat for");
        }

        /// <summary>
        /// Respond to vote in a specific public event for the <see cref="IPlayer"/> with the supplied choice.
        /// </summary>
        public void RespondVote(IPlayer player, uint publicEventId, uint choice)
        {
            if (!events.TryGetValue(publicEventId, out IPublicEvent @event))
                return;

            @event.RespondVote(player, choice);
        }

        private void InvokeEvents(Action<IPublicEvent> action, string operation)
        {
            foreach (KeyValuePair<uint, IPublicEvent> eventEntry in events.ToArray())
            {
                if (!events.TryGetValue(eventEntry.Key, out IPublicEvent currentEvent)
                    || !ReferenceEquals(currentEvent, eventEntry.Value))
                    continue;

                try
                {
                    action(currentEvent);
                }
                catch (Exception exception)
                {
                    log.LogError(
                        exception,
                        $"Failed to {operation} public event {eventEntry.Key} for character {CharacterId}.");
                }
            }
        }
    }
}
