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

    public override bool Received(NetworkHandler netHandler)
    {
      if (AiSession.Instance.IsServer)
      {
        if (PlayerId > 0)
        {
          var playerId = PlayerId.Value;
          if (RepairSearchRadius.HasValue)
          {
            AiSession.Instance.Logger.Log($"Setting repair radius to {RepairSearchRadius.Value} for playerId {PlayerId}");
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
