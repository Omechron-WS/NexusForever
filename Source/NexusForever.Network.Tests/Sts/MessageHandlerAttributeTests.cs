using System.Reflection;
using NexusForever.Network.Sts;
using NexusForever.StsServer.Network.Message;
using NexusForever.StsServer.Network.Message.Handler;

namespace NexusForever.Network.Tests.Sts
{
    public class MessageHandlerAttributeTests
    {
        [Theory]
        [InlineData(typeof(StsHandler), nameof(StsHandler.HandleConnect), SessionState.None)]
        [InlineData(typeof(StsHandler), nameof(StsHandler.HandleConnect), SessionState.Authenticated)]
        [InlineData(typeof(AuthenticationHandler), nameof(AuthenticationHandler.HandleLoginStart), SessionState.Connected)]
        [InlineData(typeof(AuthenticationHandler), nameof(AuthenticationHandler.HandleLoginStart), SessionState.Authenticated)]
        [InlineData(typeof(AuthenticationHandler), nameof(AuthenticationHandler.HandleKeyData), SessionState.LoginStart)]
        [InlineData(typeof(AuthenticationHandler), nameof(AuthenticationHandler.HandleLoginFinish), SessionState.KeyData)]
        [InlineData(typeof(AuthenticationHandler), nameof(AuthenticationHandler.HandleRequestGameToken), SessionState.Authenticated)]
        [InlineData(typeof(GameAccountHandler), nameof(GameAccountHandler.HandleListMyAccounts), SessionState.Authenticated)]
        public void Handler_RequiresExpectedSessionState(Type handlerType, string methodName, SessionState expectedState)
        {
            MessageHandlerAttribute attribute = GetAttribute(handlerType, methodName);

            Assert.Contains(expectedState, attribute.States);
        }

        [Fact]
        public void PingHandler_IsStateAgnostic()
        {
            MessageHandlerAttribute attribute = GetAttribute(typeof(StsHandler), nameof(StsHandler.HandlePing));

            Assert.Empty(attribute.States);
        }

        private static MessageHandlerAttribute GetAttribute(Type handlerType, string methodName)
        {
            MethodInfo method = handlerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            MessageHandlerAttribute attribute = method.GetCustomAttribute<MessageHandlerAttribute>();
            Assert.NotNull(attribute);
            return attribute;
        }
    }
}
