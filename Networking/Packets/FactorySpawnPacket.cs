using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.GameLogic;
using AiEnabled.Support;

using ProtoBuf;

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class FactorySpawnPacket : PacketBase
  {
    [ProtoMember(1)] readonly long BlockId;
    [ProtoMember(2)] readonly long OwnerId;
    [ProtoMember(3)] readonly long Price;
    [ProtoMember(4)] readonly long CreditsInInventory;
    [ProtoMember(5)] readonly int BotType;
    [ProtoMember(6)] readonly string BotModel;
    [ProtoMember(7)] string BotName;
    [ProtoMember(8)] Color? BotColor;
    [ProtoMember(9)] readonly List<KeyValuePair<string, bool>> RepairPriorities;
    [ProtoMember(10)] readonly List<KeyValuePair<string, bool>> TargetPriorities;
    [ProtoMember(11)] readonly bool AdminSpawned;

    public FactorySpawnPacket() { }

    public FactorySpawnPacket(AiSession.BotType botType, string botModel, string botName, long blockId, long ownerId, long price, long creditsFromInv, Color? botClr, List<KeyValuePair<string, bool>> repairPris, List<KeyValuePair<string, bool>> targetPris, bool adminSpawned)
    {
      BlockId = blockId;
      OwnerId = ownerId;
      Price = price;
      CreditsInInventory = creditsFromInv;
      BotType = (int)botType;
      BotModel = botModel;
      BotName = botName;
      BotColor = botClr;
      RepairPriorities = repairPris;
      TargetPriorities = targetPris;
      AdminSpawned = adminSpawned;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      IMyPlayer player;
      if (!AiSession.Instance.Players.TryGetValue(OwnerId, out player))
      {
        AiSession.Instance.Logger.Log($"FactorySpawnPacket.Received: Unable to get player from OwnerId", Utilities.MessageType.WARNING);
        return false;
      }

      var block = MyEntities.GetEntityById(BlockId) as IMyTerminalBlock;
      var gameLogic = block?.GameLogic.GetAs<Factory>();
      if (gameLogic == null)
      {
        AiSession.Instance.Logger.Log($"FactorySpawnPacket.Received: GameLogic was null", Utilities.MessageType.WARNING);
        return false;
      }

      if (AiSession.Instance.BotNumber >= AiSession.Instance.ModSaveData.MaxBotsInWorld)
      {
        var pkt = new MessagePacket($"The maximum number of bots has been reached ({AiSession.Instance.ModSaveData.MaxBotsInWorld})");
        netHandler.SendToPlayer(pkt, SenderId);
        return false;
      }

      string subtype;
      var bType = (AiSession.BotType)BotType;
      var bModel = MyStringId.GetOrCompute(BotModel);
      bool needsName = string.IsNullOrWhiteSpace(BotName);

      if (!AdminSpawned)
      {
        var compSubtype = $"AiEnabled_Comp_{bType}BotMaterial";
        var comp = new MyDefinitionId(typeof(MyObjectBuilder_Component), compSubtype);

        var blockInv = block.GetInventory() as MyInventory;
        var amountInBlock = (int)(blockInv?.GetItemAmount(comp) ?? 0);

        if (amountInBlock > 0)
        {
          blockInv.RemoveItemsOfType(1, comp);
        }
        else
        {
          var inv = player.Character?.GetInventory() as MyInventory;
          var amountInPlayer = (int)(inv?.GetItemAmount(comp) ?? 0);

          if (amountInPlayer > 0)
          {
            if (AiSession.Instance.IsServer)
            {
              inv.RemoveItemsOfType(1, comp);
            }
          }
          else
          {
            var def = AiSession.Instance.AllGameDefinitions[comp];
            var pkt = new MessagePacket($"Missing {def?.DisplayNameText ?? comp.SubtypeName}");
            netHandler.SendToPlayer(pkt, SenderId);
            return false;
          }
        }
      }

      gameLogic.SelectedRole = bType;
      gameLogic.SelectedModel = bModel;
      gameLogic.BotColor = BotColor;

      if (needsName)
        BotName = bType == AiSession.BotType.Combat ? "CombatBot" : bType == AiSession.BotType.Repair ? "RepairBot" : bType == AiSession.BotType.Crew ? "CrewBot" : "ScavengerBot";

      if (bModel == AiSession.Instance.MODEL_DEFAULT)
      {
        var modelDict = AiSession.Instance.BotModelDict;
        MyStringId hash;

        switch (bType)
        {
          case AiSession.BotType.Repair:
            if (needsName)
              BotName = "RepairBot";

            hash = MyStringId.GetOrCompute("Drone Bot");
            if (!modelDict.TryGetValue(hash, out subtype))
            {
              if (modelDict.Count > 1)
                subtype = modelDict.FirstOrDefault(x => x.Value != "Default").Value;
            }

            break;
          case AiSession.BotType.Combat:
            if (needsName)
              BotName = "CombatBot";

            hash = MyStringId.GetOrCompute("Target Dummy");
            if (!modelDict.TryGetValue(hash, out subtype))
            {
              if (modelDict.Count > 1)
                subtype = modelDict.FirstOrDefault(x => x.Value != "Default").Value;
            }

            break;
          case AiSession.BotType.Scavenger:
            if (needsName)
              BotName = "ScavengerBot";

            hash = MyStringId.GetOrCompute("Robo Dog");
            if (!modelDict.TryGetValue(hash, out subtype))
            {
              if (modelDict.Count > 1)
                subtype = modelDict.FirstOrDefault(x => x.Value != "Default").Value;
            }

            break;
          case AiSession.BotType.Crew:
            if (needsName)
              BotName = "CrewBot";

            subtype = MyUtils.GetRandomInt(0, 10) >= 5 ? "Default_Astronaut" : "Default_Astronaut_Female";
            break;
          default:
            return false;
        }
      }
      else
      {
        subtype = AiSession.Instance.BotModelDict[bModel];
      }

      if (string.IsNullOrEmpty(subtype) || subtype == "Default")
        subtype = "Default_Astronaut";

      if (needsName)
      {
        BotName = BotFactory.GetUniqueName(BotName);
      }
      else
      {
        int num = 1;
        var name = BotName;
        while (MyEntities.EntityExists(BotName))
          BotName = $"{name}{num++}";
      }

      var position = block.WorldMatrix.Translation + block.WorldMatrix.Backward + block.WorldMatrix.Down;
      var posOr = new MyPositionAndOrientation(position, (Vector3)block.WorldMatrix.Backward, (Vector3)block.WorldMatrix.Up);
      player.RequestChangeBalance(-Price);
      if (CreditsInInventory > 0)
      {
        var credit = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalObject), "SpaceCredit");
        var inv = player.Character?.GetInventory() as MyInventory;
        inv?.RemoveItemsOfType((MyFixedPoint)(float)CreditsInInventory, credit);
      }

      var tuple = BotFactory.CreateBotObject(subtype, BotName, posOr, player.IdentityId, BotColor);
      var helper = tuple.Item1;
      if (helper != null)
      {
        helper.SetPosition(position);
        if (helper.Physics != null && block.CubeGrid.Physics != null)
        {
          var gridPhysics = block.CubeGrid.Physics;
          helper.Physics.LinearVelocity = gridPhysics.LinearVelocity;
          helper.Physics.AngularVelocity = gridPhysics.AngularVelocity;

          var controlEnt = helper as Sandbox.Game.Entities.IMyControllableEntity;
          controlEnt.RelativeDampeningEntity = (MyEntity)block.CubeGrid;
        }

        if (gameLogic.RepairPriorities?.PriorityTypes == null)
        {
          gameLogic.RepairPriorities = new RepairPriorities(RepairPriorities);
        }
        else if (RepairPriorities?.Count > 0)
        {
          gameLogic.RepairPriorities.PriorityTypes.Clear();
          gameLogic.RepairPriorities.PriorityTypes.AddList(RepairPriorities);
        }
        else
        {
          gameLogic.RepairPriorities.AssignDefaults();
        }

        if (gameLogic.TargetPriorities?.PriorityTypes == null)
        {
          gameLogic.TargetPriorities = new TargetPriorities(TargetPriorities);
        }
        else if (TargetPriorities?.Count > 0)
        {
          gameLogic.TargetPriorities.PriorityTypes.Clear();
          gameLogic.TargetPriorities.PriorityTypes.AddList(RepairPriorities);
        }
        else
        {
          gameLogic.TargetPriorities.AssignDefaults();
        }

        gameLogic.SetHelper(tuple, player);
      }
      else
        AiSession.Instance.Logger.Log($"FactorySpawnPacket.Received: Helper was null after creation", Utilities.MessageType.WARNING);

      return false;
    }
  }
}
