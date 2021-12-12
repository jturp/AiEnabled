using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;

using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class FactoryRecallPacket : PacketBase
  {
    [ProtoMember(1)] long _factoryBlockId;
    [ProtoMember(2)] long _botEntityId;
    [ProtoMember(3)] long _ownerIdentityId;

    public FactoryRecallPacket() { }

    public FactoryRecallPacket(long blockId, long botId, long ownerId)
    {
      _factoryBlockId = blockId;
      _botEntityId = botId;
      _ownerIdentityId = ownerId;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      try
      {
        var block = MyEntities.GetEntityById(_factoryBlockId) as IMyTerminalBlock;
        if (block == null || block.MarkedForClose)
        {
          AiSession.Instance.Logger.Log($"Attempted to recall a bot to a null or closed block", Utilities.MessageType.WARNING);
          return false;
        }

        IMyPlayer player;
        var bot = MyEntities.GetEntityById(_botEntityId) as IMyCharacter;
        if (bot != null)
        {
          BotBase helper;
          if (bot.IsDead || bot.MarkedForClose || !AiSession.Instance.Bots.TryGetValue(bot.EntityId, out helper))
          {
            AiSession.Instance.Logger.Log($"Attempted to recall a dead or missing bot", Utilities.MessageType.WARNING);
            return false;
          }

          var botPos = block.WorldMatrix.Translation + block.WorldMatrix.Backward + block.WorldMatrix.Down;
          bot.Physics.SetSpeeds(Vector3.Zero, Vector3.Zero);
          bot.SetPosition(botPos);

          helper.UseAPITargets = false;
          helper._needsTransition = false;
          helper._pathCollection.CleanUp(true);
          helper.Target.RemoveTarget();
          helper.SetTarget();

          Vector3D gotoPosition, actualPosition;
          helper.Target.GetTargetPosition(out gotoPosition, out actualPosition);
          helper.StartCheckGraph(ref actualPosition, true);
        }
        else if (AiSession.Instance.Players.TryGetValue(_ownerIdentityId, out player))
        {
          var saveData = AiSession.Instance.ModSaveData.PlayerHelperData;
          for (int i = 0; i < saveData.Count; i++)
          {
            var playerData = saveData[i];
            if (playerData.OwnerIdentityId == _ownerIdentityId)
            {
              var helperData = playerData.Helpers;
              for (int j = 0; j < helperData.Count; j++)
              {
                var helper = helperData[j];
                if (helper.HelperId == _botEntityId)
                {
                  helperData.RemoveAt(j);
                  var pkt = new ClientHelperPacket(helperData);
                  AiSession.Instance.Network.SendToPlayer(pkt, player.SteamUserId);

                  var matrix = block.WorldMatrix;
                  matrix.Translation = block.WorldMatrix.Translation + block.WorldMatrix.Backward + block.WorldMatrix.Down;

                  var future = new AiSession.FutureBot(helper.Subtype, helper.DisplayName, _ownerIdentityId, _botEntityId, block.CubeGrid.EntityId, (AiSession.BotType)helper.Role, matrix);
                  AiSession.Instance.FutureBotQueue.Enqueue(future);

                  return false;
                }
              }
            }
          }
        }
        else
          AiSession.Instance.Logger.Log($"FactoryRecallPacket.Received: Unable to find player in dictionary", Utilities.MessageType.WARNING);

        return false;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in FactoryRecallPacket.Received: {ex.Message}\n{ex.StackTrace}");
        return false;
      }
    }

  }
}
