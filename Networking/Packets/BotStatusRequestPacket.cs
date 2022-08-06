using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;

using ProtoBuf;

using Sandbox.ModAPI;

namespace AiEnabled.Networking.Packets
{
  [ProtoContract]
  public class BotStatusRequestPacket : PacketBase
  {
    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance != null && AiSession.Instance.Registered && SenderId != 0)
      {
        var playerId = MyAPIGateway.Players.TryGetIdentityId(SenderId);

        List<BotBase> bots;
        if (playerId > 0 && AiSession.Instance.PlayerToHelperDict.TryGetValue(playerId, out bots) && bots?.Count > 0)
        {
          List<BotStatus> stats;
          if (!AiSession.Instance.BotStatusListStack.TryPop(out stats) || stats == null)
            stats = new List<BotStatus>();
          else
            stats.Clear();

          for (int i = 0; i < bots.Count; i++)
          {
            var helper = bots[i];
            if (helper == null || helper.IsDead)
              continue;

            BotStatus bs;
            if (!AiSession.Instance.BotStatusStack.TryPop(out bs) || bs == null)
              bs = new BotStatus();

            if (bs.Update(helper))
              stats.Add(bs);
            else
              AiSession.Instance.BotStatusStack.Push(bs);
          }

          if (stats.Count > 0)
          {
            var pkt = new BotStatusPacket(stats);
            AiSession.Instance.Network.SendToPlayer(pkt, SenderId);
          }

          for (int i = 0; i < stats.Count; i++)
          {
            var stat = stats[i];
            stat.Reset();
            AiSession.Instance.BotStatusStack.Push(stat);
          }

          stats.Clear();
          AiSession.Instance.BotStatusListStack.Push(stats);
        }
      }

      return false;
    }
  }
}
