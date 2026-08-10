using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using NexusForever.Database.Character.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Entity;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Static;

namespace NexusForever.Game.Tests.Entity
{
    public sealed class XpManagerTests
    {
        [Fact]
        public void Constructor_MaxLevelLoginDoesNotReadLevel51()
        {
            var player = new Mock<IPlayer>();
            player.SetupGet(value => value.Level).Returns(50u);
            var gameTableManager = new Mock<IGameTableManager>(MockBehavior.Strict);

            var manager = new XpManager(player.Object, new CharacterModel
            {
                LastOnline = DateTime.UtcNow.AddHours(-1d),
                RestBonusXp = 100u
            }, gameTableManager.Object);

            Assert.Equal(0u, manager.RestBonusXp);
            gameTableManager.VerifyNoOtherCalls();
        }

        [Fact]
        public void GrantXp_ReachingLevelCapDoesNotReadLevel51()
        {
            GameTable<XpPerLevelEntry> xpTable = CreateGameTable(new XpPerLevelEntry
            {
                Id = 50u,
                MinXpForLevel = 100u
            });
            var gameTableManager = new Mock<IGameTableManager>();
            gameTableManager.SetupGet(manager => manager.XpPerLevel).Returns(xpTable);

            var spellManager = new Mock<ISpellManager>();
            var player = new Mock<IPlayer>();
            player.SetupProperty(value => value.Level, 49u);
            player.SetupGet(value => value.Session).Returns(Mock.Of<IGameSession>());
            player.SetupGet(value => value.SpellManager).Returns(spellManager.Object);
            var manager = new XpManager(player.Object, new CharacterModel
            {
                TotalXp = 90u
            }, gameTableManager.Object);

            manager.GrantXp(10u, ExpReason.Quest);

            Assert.Equal(50u, player.Object.Level);
            Assert.Equal(100u, manager.TotalXp);
            spellManager.Verify(value => value.GrantSpells(), Times.Once);
        }

        private static GameTable<T> CreateGameTable<T>(params T[] entries) where T : class, new()
        {
            var table = (GameTable<T>)RuntimeHelpers.GetUninitializedObject(typeof(GameTable<T>));
            typeof(GameTable<T>).GetProperty(nameof(GameTable<T>.Entries))?.SetValue(table, entries);

            FieldInfo idField = typeof(T).GetFields().First();
            uint maximumId = entries.Select(entry => (uint)idField.GetValue(entry)).DefaultIfEmpty().Max();
            int[] lookup = Enumerable.Repeat(-1, checked((int)maximumId + 1)).ToArray();
            for (int i = 0; i < entries.Length; i++)
                lookup[(uint)idField.GetValue(entries[i])] = i;

            typeof(GameTable<T>).GetField("lookup", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(table, lookup);
            typeof(GameTable<T>).GetField("header", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(table,
                new GameTableHeader
                {
                    MaxId = maximumId + 1ul
                });
            return table;
        }
    }
}
