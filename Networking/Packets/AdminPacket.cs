using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.ConfigData;
using AiEnabled.Support;

using ProtoBuf;

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
    [ProtoMember(15)] List<SerializableBotPrice> BotRequirements;
    [ProtoMember(16)] List<string> AdditionalHelperSubtypes;


    public AdminPacket() { }

    public AdminPacket(bool allowMusic, bool? allowEnemyFlight, bool? allowNeutral)
    {
      AllowMusic = allowMusic;
      AllowEnemyFlight = allowEnemyFlight;
      AllowNeutralTargets = allowNeutral;
    }

    public AdminPacket(int numBots, int numHelpers, int projectileDistance, bool allowMusic, List<SerializableBotPrice> botPrices, List<string> helperSubtypes)
    {
      MaxBots = numBots;
      MaxHelpers = numHelpers;
      MaxProjectileDistance = projectileDistance;
      AllowMusic = allowMusic;
      BotRequirements = botPrices;
      AdditionalHelperSubtypes = helperSubtypes;
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
            AiSession.Instance.ModSaveData.BotWeaponDamageModifier = DamageMultiplier.Value;
          else
            AiSession.Instance.ModSaveData.PlayerWeaponDamageModifier = DamageMultiplier.Value;
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

        if (BotRequirements?.Count > 0)
        {
          for (int i = 0; i < BotRequirements.Count; i++)
          {
            var botReq = BotRequirements[i];
            var botType = (AiSession.BotType)botReq.BotType;

            AiSession.Instance.BotComponents[botType] = botReq.Components;
            AiSession.Instance.BotPrices[botType] = botReq.SpaceCredits;
          }
        }

        if (AdditionalHelperSubtypes?.Count > 0)
        {
          var sb = new StringBuilder(32);
          foreach (var subtype in AdditionalHelperSubtypes)
          {
            sb.Clear();

            foreach (var ch in subtype)
            {
              if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            }

            var hashId = MyStringId.GetOrCompute(sb.ToString());
            if (!AiSession.Instance.BotModelDict.ContainsKey(hashId))
              AiSession.Instance.BotModelDict[hashId] = subtype;
          }

          sb.Clear();
        }

        AiSession.Instance.BotModelList.Clear();
        foreach (var kvp in AiSession.Instance.BotModelDict)
        {
          AiSession.Instance.BotModelList.Add(kvp.Key);
        }

        var efficiency = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
        var fixedPoint = (MyFixedPoint)(1f / efficiency);
        var remainder = 1 - (fixedPoint * efficiency);
        var componentReqs = new Dictionary<MyDefinitionId, float>(MyDefinitionId.Comparer);

        foreach (var kvp in AiSession.Instance.BotComponents)
        {
          var bType = kvp.Key;
          var subtype = $"AiEnabled_Comp_{bType}BotMaterial";
          var comp = new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype);
          var bpDef = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(comp);

          if (bpDef != null)
          {
            var items = kvp.Value;
            if (items.Count == 0)
              items.Add(new SerialId(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), 1));

            componentReqs.Clear();
            for (int i = 0; i < items.Count; i++)
            {
              var item = items[i];
              var amount = item.Amount;

              var compBp = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(item.DefinitionId);
              if (compBp != null)
              {
                var compReqs = compBp.Prerequisites;
                if (compReqs?.Length > 0)
                {
                  for (int j = 0; j < compReqs.Length; j++)
                  {
                    var compReq = compReqs[j];

                    float num;
                    componentReqs.TryGetValue(compReq.Id, out num);
                    componentReqs[compReq.Id] = num + (float)compReq.Amount * amount;
                  }
                }
              }
            }

            if (componentReqs.Count == 0)
              componentReqs[new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Iron")] = 100 * efficiency;

            var reqs = new MyBlueprintDefinitionBase.Item[componentReqs.Count];
            int k = 0;

            foreach (var item in componentReqs)
            {
              var req = new MyBlueprintDefinitionBase.Item
              {
                Amount = (MyFixedPoint)item.Value,
                Id = item.Key
              };

              req.Amount *= efficiency;

              if (remainder > 0)
                req.Amount += req.Amount * remainder + remainder;

              reqs[k] = req;
              k++;
            }

            bpDef.Atomic = true;
            bpDef.Prerequisites = reqs;
          }
        }

        componentReqs.Clear();
        componentReqs = null;
      }

      return false;
    }
  }
}
