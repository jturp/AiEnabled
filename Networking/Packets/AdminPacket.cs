using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;
using AiEnabled.Support;

using ProtoBuf;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Utils;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class AdminPacket : PacketBase
  {
    [ProtoMember(1)] long? PlayerId;
    [ProtoMember(2)] long? BotEntityId;
    [ProtoMember(3)] long? OwnerId;
    [ProtoMember(4)] bool? ShowHealthBars;
    [ProtoMember(5)] float? RepairSearchRadius;
    [ProtoMember(6)] bool? KillAllBots;
    [ProtoMember(7)] bool? KillFriendlyBots;

    public AdminPacket() { }

    public AdminPacket(long identityId, bool showHealthBars, float searchRadius)
    {
      PlayerId = identityId;
      ShowHealthBars = showHealthBars;
      RepairSearchRadius = searchRadius;
    }

    public AdminPacket(long identityId, long? botEntity, long? ownerId)
    {
      PlayerId = identityId;
      BotEntityId = botEntity;
      OwnerId = ownerId;
    }

    public AdminPacket(bool killFriendly)
    {
      KillAllBots = true;
      KillFriendlyBots = killFriendly;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance.IsServer)
      {
        if (KillAllBots == true)
        {
          foreach (var bot in AiSession.Instance.Bots)
          {
            if (bot.Value?.Owner != null && !KillFriendlyBots.Value)
              continue;

            bot.Value.Close(true);
          }
        }
        else if (PlayerId > 0)
        {
          var playerId = PlayerId.Value;
          if (RepairSearchRadius.HasValue)
          {
            AiSession.Instance.PlayerToRepairRadius[playerId] = RepairSearchRadius.Value;
          }

          if (ShowHealthBars.HasValue)
          {
            bool show = ShowHealthBars.Value;

            HealthInfoStat infoStat;
            if (!AiSession.Instance.PlayerToHealthBars.TryGetValue(playerId, out infoStat))
            {
              infoStat = new HealthInfoStat();
              AiSession.Instance.PlayerToHealthBars[playerId] = infoStat;
            }

            infoStat.ShowHealthBars = show;

            if (!show)
            {
              infoStat.BotEntityIds.Clear();
            }
          }
        }
      }
      else if (PlayerId.HasValue)
      {
        if (BotEntityId.HasValue)
          AiSession.Instance.UpdateControllerForPlayer(PlayerId.Value, BotEntityId.Value, OwnerId);
        else
          AiSession.Instance.CheckControllerForPlayer(PlayerId.Value, 0L);
      }

      return false;
    }
  }
}
