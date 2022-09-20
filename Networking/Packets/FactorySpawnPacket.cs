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

    public FactorySpawnPacket() { }

    public FactorySpawnPacket(AiSession.BotType botType, string botModel, string botName, long blockId, long ownerId, long price, long creditsFromInv, Color? botClr)
    {
      BlockId = blockId;
      OwnerId = ownerId;
      Price = price;
      CreditsInInventory = creditsFromInv;
      BotType = (int)botType;
      BotModel = botModel;
      BotName = botName;
      BotColor = botClr;
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

      gameLogic.SelectedRole = bType;
      gameLogic.SelectedModel = bModel;
      gameLogic.BotColor = BotColor;

      if (needsName)
        BotName = bType == AiSession.BotType.Combat ? "CombatBot" : bType == AiSession.BotType.Repair ? "RepairBot" : "ScavengerBot";

      if (bModel == AiSession.Instance.MODEL_DEFAULT)
      {
        switch (bType)
        {
          case AiSession.BotType.Repair:
            if (needsName)
              BotName = "RepairBot";

            subtype = "Drone_Bot";
            break;
          case AiSession.BotType.Combat:
            if (needsName)
              BotName = "CombatBot";

            subtype = "Target_Dummy";
            break;
          case AiSession.BotType.Scavenger:
            if (needsName)
              BotName = "ScavengerBot";

            subtype = "RoboDog";
            break;
          case AiSession.BotType.Crew:
            if (needsName)
              BotName = "CrewBot";

            subtype = MyUtils.GetRandomInt(0, 10) > 4 ? "Default_Astronaut" : "Default_Astronaut_Female";
            break;
          //case AiSession.BotType.Medic:
          // if (needsName)
          //   displayName = "MedicBot";
          //
          // subtype = "Police_Bot";
          // break;
          default:
            return false;
        }
      }
      else
      {
        subtype = AiSession.Instance.BotModelDict[bModel];
      }

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

        gameLogic.SetHelper(tuple, player);
      }
      else
        AiSession.Instance.Logger.Log($"FactorySpawnPacket.Received: Helper was null after creation", Utilities.MessageType.WARNING);

      return false;
    }
  }
}
