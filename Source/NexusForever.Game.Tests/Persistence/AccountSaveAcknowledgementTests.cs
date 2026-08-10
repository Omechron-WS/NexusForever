using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Moq;
using NexusForever.Database;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;
using NexusForever.Game.Abstract.Account;
using NexusForever.Game.Abstract.Account.Costume;
using NexusForever.Game.Abstract.RBAC;
using NexusForever.Game.Account.Costume;
using NexusForever.Game.Option;
using NexusForever.Game.RBAC;
using NexusForever.Game.Static.RBAC;
using NexusForever.Network.World.Message.Model.Option;
using NetworkBinding = NexusForever.Network.World.Message.Model.Shared.Binding;

namespace NexusForever.Game.Tests.Persistence
{
    public class AccountSaveAcknowledgementTests
    {
        [Fact]
        public void AccountRoleSave_KeepsCreateDirtyUntilCommitIsAcknowledged()
        {
            var rbacRole = new Mock<IRBACRole>();
            rbacRole.SetupGet(role => role.Role).Returns(Role.Player);
            var accountRole = new AccountRole(17u, rbacRole.Object);
            using AuthContext context = CreateContext();
            var commitScope = new SaveCommitScope();

            accountRole.Save(context, commitScope);

            Assert.True(accountRole.PendingCreate);
            Assert.Equal(EntityState.Added, Assert.Single(context.ChangeTracker.Entries<AccountRoleModel>()).State);

            commitScope.CreateAcknowledgement().Acknowledge();

            Assert.False(accountRole.PendingCreate);
        }

        [Fact]
        public void AccountCostumeSave_RemovesCommittedDeleteFromMemoryOnlyAfterAcknowledgement()
        {
            var accountModel = new AccountModel
            {
                Id = 23u,
                AccountCostumeUnlock =
                [
                    new AccountCostumeUnlockModel
                    {
                        Id     = 23u,
                        ItemId = 101u
                    }
                ]
            };
            var manager = new AccountCostumeManager(Mock.Of<IAccount>(), accountModel);
            Dictionary<uint, ICostumeUnlock> unlocks = GetCostumeUnlocks(manager);
            unlocks[101u].EnqueueDelete(true);
            using AuthContext context = CreateContext();
            var commitScope = new SaveCommitScope();

            manager.Save(context, commitScope);

            Assert.Single(unlocks);
            Assert.False(manager.HasItemUnlock(101u));

            commitScope.CreateAcknowledgement().Acknowledge();

            Assert.Empty(unlocks);
            Assert.False(manager.HasItemUnlock(101u));
        }

        [Fact]
        public void KeybindingSave_PreservesMutationMadeWhileCreateCommitIsPending()
        {
            var bindingSet = new KeybindingSet(new AccountModel { Id = 31u });
            bindingSet.Update(CreateKeySet(7u));
            using AuthContext createContext = CreateContext();
            var createScope = new SaveCommitScope();
            bindingSet.Save(createContext, createScope);

            bindingSet.Update(CreateKeySet(19u));
            createScope.CreateAcknowledgement().Acknowledge();

            using AuthContext updateContext = CreateContext();
            var updateScope = new SaveCommitScope();
            bindingSet.Save(updateContext, updateScope);
            AccountKeybindingModel update = Assert.Single(updateContext.ChangeTracker.Entries<AccountKeybindingModel>()).Entity;

            Assert.Equal(19u, update.Code00);
            Assert.True(updateContext.Entry(update).Property(model => model.Code00).IsModified);

            updateScope.CreateAcknowledgement().Acknowledge();
        }

        [Fact]
        public void KeybindingSave_RemovesDeleteOnlyAfterCommitIsAcknowledged()
        {
            var bindingSet = new KeybindingSet(new AccountModel { Id = 37u });
            bindingSet.Update(CreateKeySet(11u));
            using (AuthContext createContext = CreateContext())
            {
                var createScope = new SaveCommitScope();
                bindingSet.Save(createContext, createScope);
                createScope.CreateAcknowledgement().Acknowledge();
            }

            bindingSet.Update(new BiInputKeySet());
            using AuthContext deleteContext = CreateContext();
            var deleteScope = new SaveCommitScope();
            bindingSet.Save(deleteContext, deleteScope);

            Assert.Equal(1u, bindingSet.Count);
            Assert.Empty(bindingSet);

            deleteScope.CreateAcknowledgement().Acknowledge();

            Assert.Equal(0u, bindingSet.Count);
        }

        private static TestAuthContext CreateContext()
        {
            return new TestAuthContext();
        }

        private static BiInputKeySet CreateKeySet(uint code)
        {
            return new BiInputKeySet
            {
                Bindings =
                [
                    new NetworkBinding
                    {
                        InputActionId = 3,
                        Code00        = code
                    }
                ]
            };
        }

        private static Dictionary<uint, ICostumeUnlock> GetCostumeUnlocks(AccountCostumeManager manager)
        {
            FieldInfo field = typeof(AccountCostumeManager).GetField("costumeUnlocks", BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<Dictionary<uint, ICostumeUnlock>>(field.GetValue(manager));
        }

        private sealed class TestAuthContext : AuthContext
        {
            public TestAuthContext()
                : base(new DbContextOptionsBuilder<AuthContext>()
                    .UseMySql(
                        "Server=localhost;Database=nexus_forever_test;User=test;Password=test;",
                        new MySqlServerVersion(new Version(8, 0, 36)))
                    .Options)
            {
            }
        }
    }
}
