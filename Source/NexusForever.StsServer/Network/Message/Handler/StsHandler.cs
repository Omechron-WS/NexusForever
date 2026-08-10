using NexusForever.Network.Sts;
using NexusForever.Network.Sts.Model;

namespace NexusForever.StsServer.Network.Message.Handler
{
    public static class StsHandler
    {
        [MessageHandler("/Sts/Connect", SessionState.None, SessionState.Authenticated)]
        public static void HandleConnect(StsSession session, ClientConnectMessage connect)
        {
            session.State = SessionState.Connected;
        }

        [MessageHandler("/Sts/Ping")]
        public static void HandlePing(StsSession session, PingMessage ping)
        {
            session.Heartbeat.OnHeartbeat();
        }
    }
}
