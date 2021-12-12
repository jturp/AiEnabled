using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Support;

using ProtoBuf;

using Sandbox.ModAPI;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class AdminPacket : PacketBase
  {
    [ProtoMember(1)] long? PlayerId;
    [ProtoMember(2)] long? BotEntityId;
    [ProtoMember(3)] long? OwnerId;
    [ProtoMember(4)] int? MaxBots;
    [ProtoMember(5)] int? MaxHelpers;
    [ProtoMember(6)] bool? AllowMusic;
    [ProtoMember(7)] bool? ShowHealthBars;

    public AdminPacket() { }

    public AdminPacket(bool allowMusic)
    {
      AllowMusic = allowMusic;
    }

    public AdminPacket(int numBots, int numHelpers)
    {
      MaxBots = numBots;
      MaxHelpers = numHelpers;
    }

    public AdminPacket(long identityId, bool showHealthBars)
    {
      PlayerId = identityId;
      ShowHealthBars = showHealthBars;
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
        if (MaxBots > 0 && MaxHelpers >= 0)
        {
          if (MyAPIGateway.Session.Player != null && AiSession.Instance?.PlayerMenu != null)
          {
            AiSession.Instance.PlayerMenu.UpdateMaxBots(MaxBots.Value);
            AiSession.Instance.PlayerMenu.UpdateMaxHelpers(MaxHelpers.Value);
          }
          else
          {
            AiSession.Instance.MaxBots = MaxBots.Value;
            AiSession.Instance.MaxHelpers = MaxBots.Value;
          }

          AiSession.Instance.StartAdminUpdateCounter();
          return MyAPIGateway.Multiplayer.MultiplayerActive;
        }
        else if (AllowMusic.HasValue)
        {
          AiSession.Instance.AllowMusic = AllowMusic.Value;
          AiSession.Instance.ModSaveData.AllowBotMusic = AllowMusic.Value;
          AiSession.Instance.SaveModData(true);
        }
        else if (ShowHealthBars.HasValue && PlayerId > 0)
        {
          bool show = ShowHealthBars.Value;
          var playerId = PlayerId.Value;

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
      else if (PlayerId.HasValue)
      {
        if (BotEntityId.HasValue)
          AiSession.Instance.UpdateControllerForPlayer(PlayerId.Value, BotEntityId.Value, OwnerId);
        else
          AiSession.Instance.CheckControllerForPlayer(PlayerId.Value, 0L);
      }
      else if (MaxBots >= 0 && MaxHelpers >= 0 && AiSession.Instance?.PlayerMenu != null)
      {
        AiSession.Instance.PlayerMenu.UpdateMaxBots(MaxBots.Value);
        AiSession.Instance.PlayerMenu.UpdateMaxHelpers(MaxHelpers.Value);
      }

      return false;
    }
  }
}
