using System.Collections;
using NexusForever.Database;
using NexusForever.Database.Character;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Persistence;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NLog;

namespace NexusForever.Game.Entity
{
    public class CurrencyManager : ICurrencyManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private IPlayer player;
        private readonly Dictionary<CurrencyType, ICurrency> currencies = [];

        #region Depenedency Injection

        private readonly IGameTableManager gameTableManager;

        public CurrencyManager(
            IGameTableManager gameTableManager)
        {
            this.gameTableManager = gameTableManager;
        }

        #endregion

        /// <summary>
        /// Initialise <see cref="ICurrencyManager"/> from <see cref="CharacterModel"/> database model.
        /// </summary>
        public void Initialise(IPlayer owner, CharacterModel model)
        {
            if (player != null)
                throw new InvalidOperationException("CurrencyManager has already been initialised!");

            player = owner;

            foreach (CharacterCurrencyModel currencyModel in model.Currency)
            {
                var currency = new Currency(currencyModel);
                currencies.Add(currency.Id, currency);
            }
        }

        public void Save(CharacterContext context)
        {
            Save(context, ImmediateSaveCommitScope.Instance);
        }

        /// <summary>
        /// Stage currency changes and acknowledge them after the character database commits.
        /// </summary>
        public void Save(CharacterContext context, ISaveCommitScope commitScope)
        {
            foreach (ICurrency currency in currencies.Values)
                currency.Save(context, commitScope);
        }

        /// <summary>
        /// Returns total amount of acquired <see cref="CurrencyType"/>.
        /// </summary>
        private ulong? GetCurrency(CurrencyType currencyId)
        {
            if (!currencies.TryGetValue(currencyId, out ICurrency currency))
                return null;
            return currency.Amount;
        }

        /// <summary>
        /// Returns if <see cref="IPlayer"/> has the supplied amount of <see cref="CurrencyType"/>.
        /// </summary>
        public bool CanAfford(CurrencyType currencyId, ulong amount)
        {
            CurrencyTypeEntry currencyEntry = gameTableManager.CurrencyType.GetEntry((ulong)currencyId);
            if (currencyEntry == null)
                throw new ArgumentNullException();

            // it is possible to check for a currency amount of 0 without having the currency in the store
            // in this scenario we should allow the check to pass
            if (amount == 0)
                return true;

            return GetCurrency(currencyId) >= amount;
        }

        /// <summary>
        /// Add supplied amount of <see cref="CurrencyType"/> currency.
        /// </summary>
        public void CurrencyAddAmount(CurrencyType currencyId, ulong amount, bool isLoot = false)
        {
            CurrencyTypeEntry currencyEntry = gameTableManager.CurrencyType.GetEntry((ulong)currencyId);
            if (currencyEntry == null)
                throw new ArgumentNullException();

            CurrencyAddAmount(currencyEntry, amount, isLoot);
        }

        private void CurrencyAddAmount(CurrencyTypeEntry currencyEntry, ulong amount, bool isLoot = false)
        {
            if (currencyEntry == null)
                throw new ArgumentNullException();

            if (!currencies.TryGetValue((CurrencyType)currencyEntry.Id, out ICurrency currency))
                currency = CurrencyCreate(currencyEntry);

            ulong previousAmount = currency.Amount;
            ulong updatedAmount;
            if (currency.Entry.CapAmount > 0uL)
            {
                if (previousAmount >= currency.Entry.CapAmount)
                    updatedAmount = previousAmount;
                else
                    updatedAmount = previousAmount + Math.Min(amount, currency.Entry.CapAmount - previousAmount);
            }
            else
                updatedAmount = checked(previousAmount + amount);

            try
            {
                CurrencyAmountUpdate(currency, updatedAmount, isLoot);
            }
            finally
            {
                NotifyCurrencyGranted(currency, previousAmount);
            }
        }

        private void NotifyCurrencyGranted(ICurrency currency, ulong previousAmount)
        {
            if (currency == null || player == null || currency.Amount <= previousAmount)
                return;

            ulong grantedAmount = currency.Amount - previousAmount;
            uint progress = (uint)Math.Min(grantedAmount, uint.MaxValue);

            try
            {
                player.QuestManager?.ObjectiveUpdate(QuestObjectiveType.EarnCurrency, (uint)currency.Id, progress);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to update EarnCurrency quest objectives for currency {currency.Id} after granting {grantedAmount}.");
            }
        }

        private ICurrency CurrencyCreate(CurrencyTypeEntry currencyEntry)
        {
            if (currencyEntry == null)
                return null;

            if (currencies.ContainsKey((CurrencyType)currencyEntry.Id))
                throw new ArgumentException($"Currency {currencyEntry.Id} is already added to the player!");

            var currency = new Currency(player.CharacterId, currencyEntry);
            currencies.Add(currency.Id, currency);
            return currency;
        }

        /// <summary>
        /// Remove supplied amount of <see cref="CurrencyType"/> currency.
        /// </summary>
        public void CurrencySubtractAmount(CurrencyType currencyId, ulong amount, bool isLoot = false)
        {
            CurrencyTypeEntry currencyEntry = gameTableManager.CurrencyType.GetEntry((ulong)currencyId);
            if (currencyEntry == null)
                throw new ArgumentNullException();

            CurrencySubtractAmount(currencyEntry, amount, isLoot);
        }

        private void CurrencySubtractAmount(CurrencyTypeEntry currencyEntry, ulong amount, bool isLoot = false)
        {
            if (currencyEntry == null)
                throw new ArgumentNullException();

            // it is possible to subtract a currency amount of 0 without having the currency in the store
            // in this scenario we should allow this to pass
            if (amount == 0)
                return;

            if (!currencies.TryGetValue((CurrencyType)currencyEntry.Id, out ICurrency currency))
                throw new ArgumentException($"Cannot create currency {currencyEntry.Id} with a negative amount!");
            if (currency.Amount < amount)
                throw new ArgumentException($"Trying to remove more currency {currencyEntry.Id} than the player has!");

            CurrencyAmountUpdate(currency, currency.Amount - amount, isLoot);
        }

        /// <summary>
        /// Update <see cref="ICurrency"/> with supplied amount of currency.
        /// </summary>
        private void CurrencyAmountUpdate(ICurrency currency, ulong amount, bool isLoot = false)
        {
            if (currency == null)
                throw new ArgumentNullException();

            if (isLoot)
            {
                player.Session.EnqueueMessageEncrypted(new ServerChannelUpdateLoot
                {
                    CurrencyId = currency.Id,
                    Amount = amount - currency.Amount
                });
            }

            currency.Amount = amount;

            player.Session.EnqueueMessageEncrypted(new ServerCombatReward
            {
                Stat = (byte)(currency.Id - 1),
                NewValue = currency.Amount
            });
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<ICurrency> GetEnumerator()
        {
            return currencies.Values.GetEnumerator();
        }
    }
}
