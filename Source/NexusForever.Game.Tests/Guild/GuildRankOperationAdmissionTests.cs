using System.Reflection;
using Moq;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Guild;
using NexusForever.Game.Static.Guild;
using NexusForever.Network.Internal;
using NexusForever.Network.World.Message.Model.Guild;
using GuildEntity = NexusForever.Game.Guild.Guild;

namespace NexusForever.Game.Tests.Guild
{
    public sealed class GuildRankOperationAdmissionTests
    {
        public static IEnumerable<object[]> RankOperations
        {
            get
            {
                yield return ["GuildOperationRankAdd", GuildRankPermission.CreateAndRemoveRank];
                yield return ["GuildOperationRankDelete", GuildRankPermission.CreateAndRemoveRank];
                yield return ["GuildOperationRankPermissions", GuildRankPermission.EditLowerRankPermissions];
                yield return ["GuildOperationRankRename", GuildRankPermission.RenameRank];
            }
        }

        [Theory]
        [MemberData(nameof(RankOperations))]
        public void AuthorisedInternalRankOperation_RejectsAliasedRankBeforeLookupOrMutation(
            string methodName,
            GuildRankPermission requiredPermission)
        {
            var realmContext = new Mock<IRealmContext>(MockBehavior.Strict);
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var guild = new GuildEntity(realmContext.Object, publisher.Object);
            var memberRank = new Mock<IGuildRank>(MockBehavior.Strict);
            memberRank.Setup(value => value.HasPermission(requiredPermission)).Returns(true);
            var member = new Mock<IGuildMember>(MockBehavior.Strict);
            member.SetupGet(value => value.Rank).Returns(memberRank.Object);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var targetRank = new Mock<IGuildRank>(MockBehavior.Strict);
            AddRank(guild, 2, targetRank.Object);

            IGuildResultInfo result = Invoke(
                guild,
                methodName,
                member.Object,
                player.Object,
                CreateOperation(258u));

            Assert.Equal(GuildResult.InvalidRank, result.Result);
            Assert.Equal(258u, result.ReferenceId);
            member.VerifyGet(value => value.Rank, Times.Once);
            memberRank.Verify(value => value.HasPermission(requiredPermission), Times.Once);
            member.VerifyNoOtherCalls();
            memberRank.VerifyNoOtherCalls();
            targetRank.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            realmContext.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(RankOperations))]
        public void UnauthorisedInternalRankOperation_PreservesPermissionFailurePrecedence(
            string methodName,
            GuildRankPermission requiredPermission)
        {
            var realmContext = new Mock<IRealmContext>(MockBehavior.Strict);
            var publisher = new Mock<IInternalMessagePublisher>(MockBehavior.Strict);
            var guild = new GuildEntity(realmContext.Object, publisher.Object);
            var memberRank = new Mock<IGuildRank>(MockBehavior.Strict);
            memberRank.Setup(value => value.HasPermission(requiredPermission)).Returns(false);
            var member = new Mock<IGuildMember>(MockBehavior.Strict);
            member.SetupGet(value => value.Rank).Returns(memberRank.Object);
            var player = new Mock<IPlayer>(MockBehavior.Strict);
            var targetRank = new Mock<IGuildRank>(MockBehavior.Strict);
            AddRank(guild, 2, targetRank.Object);

            IGuildResultInfo result = Invoke(
                guild,
                methodName,
                member.Object,
                player.Object,
                CreateOperation(258u));

            Assert.Equal(GuildResult.RankLacksRankRenamePermission, result.Result);
            member.VerifyGet(value => value.Rank, Times.Once);
            memberRank.Verify(value => value.HasPermission(requiredPermission), Times.Once);
            member.VerifyNoOtherCalls();
            memberRank.VerifyNoOtherCalls();
            targetRank.VerifyNoOtherCalls();
            player.VerifyNoOtherCalls();
            realmContext.VerifyNoOtherCalls();
            publisher.VerifyNoOtherCalls();
        }

        private static IGuildResultInfo Invoke(
            GuildEntity guild,
            string methodName,
            IGuildMember member,
            IPlayer player,
            ClientGuildOperation operation)
        {
            MethodInfo method = typeof(GuildBase).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsAssignableFrom<IGuildResultInfo>(method.Invoke(
                guild,
                [member, player, operation]));
        }

        private static ClientGuildOperation CreateOperation(uint rank)
        {
            var operation = new ClientGuildOperation();
            typeof(ClientGuildOperation)
                .GetProperty(nameof(ClientGuildOperation.Rank), BindingFlags.Instance | BindingFlags.Public)
                .SetValue(operation, rank);
            return operation;
        }

        private static void AddRank(GuildBase guild, byte index, IGuildRank rank)
        {
            FieldInfo field = typeof(GuildBase).GetField(
                "ranks",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var ranks = Assert.IsType<SortedDictionary<byte, IGuildRank>>(field.GetValue(guild));
            ranks.Add(index, rank);
        }
    }
}
