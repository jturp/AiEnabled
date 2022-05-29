using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;
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
    [ProtoMember(6)] int? MaxHuntDistanceEnemy;
    [ProtoMember(7)] int? MaxHuntDistanceFriendly;
    [ProtoMember(8)] int? MaxProjectileDistance;
    [ProtoMember(9)] bool? AllowMusic;
    [ProtoMember(10)] bool? AllowEnemyFlight;
    [ProtoMember(11)] bool? ShowHealthBars;
    [ProtoMember(12)] bool? AllowNeutralTargets;
    [ProtoMember(13)] bool? IsBotMultiplier;
    [ProtoMember(14)] float? DamageMultiplier;
    [ProtoMember(15)] List<SerialId> RepairBotRequirements;
    [ProtoMember(16)] List<SerialId> CombatBotRequirements;
    [ProtoMember(17)] List<SerialId> ScavengerBotRequirements;


    public AdminPacket() { }

    public AdminPacket(bool allowMusic, bool? allowEnemyFlight, bool? allowNeutral)
    {
      AllowMusic = allowMusic;
      AllowEnemyFlight = allowEnemyFlight;
      AllowNeutralTargets = allowNeutral;
    }

    public AdminPacket(int numBots, int numHelpers, int projectileDistance, bool allowMusic, List<SerialId> repairReqs, List<SerialId> combatReqs, List<SerialId> scavengerReqs)
    {
      MaxBots = numBots;
      MaxHelpers = numHelpers;
      MaxProjectileDistance = projectileDistance;
      AllowMusic = allowMusic;
      RepairBotRequirements = repairReqs;
      CombatBotRequirements = combatReqs;
      ScavengerBotRequirements = scavengerReqs;
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

    public AdminPacket(int? maxHuntE, int? maxHuntF, int? maxBulletDist)
    {
      MaxHuntDistanceEnemy = maxHuntE;
      MaxHuntDistanceFriendly = maxHuntF;
      MaxProjectileDistance = maxBulletDist;
    }

    public AdminPacket(float damageMultiplier, bool isBotMulti)
    {
      DamageMultiplier = damageMultiplier;
      IsBotMultiplier = isBotMulti;
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

          if (MaxProjectileDistance.HasValue)
            AiSession.Instance.MaxBotProjectileDistance = MaxProjectileDistance.Value;

          if (AllowMusic.HasValue)
            AiSession.Instance.AllowMusic = AllowMusic.Value;

          AiSession.Instance.StartAdminUpdateCounter();
          return MyAPIGateway.Multiplayer.MultiplayerActive;
        }
        else if (MaxHuntDistanceFriendly.HasValue || MaxHuntDistanceEnemy.HasValue || MaxProjectileDistance.HasValue)
        {
          var modData = AiSession.Instance.ModSaveData;

          if (MaxHuntDistanceEnemy.HasValue)
            modData.MaxBotHuntingDistanceEnemy = MaxHuntDistanceEnemy.Value;
          else if (MaxHuntDistanceFriendly.HasValue) 
            modData.MaxBotHuntingDistanceFriendly = MaxHuntDistanceFriendly.Value;
          else
            modData.MaxBotProjectileDistance = MaxProjectileDistance.Value;

          AiSession.Instance.StartAdminUpdateCounter();
          return false;
        }
        else if (AllowMusic.HasValue)
        {
          AiSession.Instance.AllowMusic = AllowMusic.Value;
          AiSession.Instance.ModSaveData.AllowBotMusic = AllowMusic.Value;

          if (AllowEnemyFlight.HasValue)
            AiSession.Instance.ModSaveData.AllowEnemiesToFly = AllowEnemyFlight.Value;

          if (AllowNeutralTargets.HasValue)
            AiSession.Instance.ModSaveData.AllowNeutralTargets = AllowNeutralTargets.Value;
  
          AiSession.Instance.SaveModData(true);
        }
        else if (DamageMultiplier.HasValue)
        {
          if (IsBotMultiplier == true)
            AiSession.Instance.ModSaveData.BotDamageModifier = DamageMultiplier.Value;
          else
            AiSession.Instance.ModSaveData.PlayerDamageModifier = DamageMultiplier.Value;
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
      else if (MaxBots >= 0 && MaxHelpers >= 0 && AiSession.Instance != null)
      {
        if (AiSession.Instance.PlayerMenu != null)
        {
          AiSession.Instance.PlayerMenu.UpdateMaxBots(MaxBots.Value);
          AiSession.Instance.PlayerMenu.UpdateMaxHelpers(MaxHelpers.Value);
        }
        else
        {
          AiSession.Instance.MaxBots = MaxBots.Value;
          AiSession.Instance.MaxHelpers = MaxHelpers.Value;
        }

        if (MaxProjectileDistance.HasValue)
          AiSession.Instance.MaxBotProjectileDistance = MaxProjectileDistance.Value;

        if (AllowMusic.HasValue)
          AiSession.Instance.AllowMusic = AllowMusic.Value;

        AiSession.Instance.BotComponents[AiSession.BotType.Repair] = RepairBotRequirements ?? new List<SerialId>();
        AiSession.Instance.BotComponents[AiSession.BotType.Combat] = CombatBotRequirements ?? new List<SerialId>();
        AiSession.Instance.BotComponents[AiSession.BotType.Scavenger] = ScavengerBotRequirements ?? new List<SerialId>();
      }

      return false;
    }
  }
}
