using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game.ModAPI;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class SpawnPacketClient : PacketBase
  {
    [ProtoMember(1)] public long BotEntityId;
    [ProtoMember(2)] public bool RemoveBot;

    public SpawnPacketClient() { }

    public SpawnPacketClient(long id, bool remove)
    {
      BotEntityId = id;
      RemoveBot = remove;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      var player = MyAPIGateway.Session?.LocalHumanPlayer;
      if (player == null)
        return false;

      List<long> helperIds;
      if (!AiSession.Instance.PlayerToHelperIdentity.TryGetValue(player.IdentityId, out helperIds))
      {
        helperIds = new List<long>();
        AiSession.Instance.PlayerToHelperIdentity[player.IdentityId] = helperIds;
      }

      List<long> activeHelperIds;
      if (!AiSession.Instance.PlayerToActiveHelperIds.TryGetValue(player.IdentityId, out activeHelperIds))
      {
        activeHelperIds = new List<long>();
        AiSession.Instance.PlayerToActiveHelperIds[player.IdentityId] = activeHelperIds;
      }

      if (RemoveBot)
      {
        AiSession.Instance.RemoveGPSForBot(BotEntityId);
        helperIds.Remove(BotEntityId);
        activeHelperIds.Remove(BotEntityId);
      }
      else
      {
        var bot = MyEntities.GetEntityById(BotEntityId) as IMyCharacter;
        AiSession.Instance.AddGPSForBot(bot);

        if (!helperIds.Contains(BotEntityId))
          helperIds.Add(BotEntityId);

        if (!activeHelperIds.Contains(BotEntityId))
          activeHelperIds.Add(BotEntityId);

        AiSession.Instance.PendingBotRespawns.Remove(bot.Name);
      }

      return false;
    }
  }
}
