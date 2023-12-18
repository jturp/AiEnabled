using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Support;

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
    [ProtoMember(1)] readonly long _factoryBlockId;
    [ProtoMember(2)] readonly long _botEntityId;
    [ProtoMember(3)] readonly long _ownerIdentityId;
    [ProtoMember(4)] readonly List<KeyValuePair<string, bool>> _priorities;

    public FactoryRecallPacket() { }

    public FactoryRecallPacket(long blockId, long botId, long ownerId, List<KeyValuePair<string, bool>> priorityList)
    {
      _factoryBlockId = blockId;
      _botEntityId = botId;
      _ownerIdentityId = ownerId;
      _priorities = priorityList;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      try
      {
        var block = MyEntities.GetEntityById(_factoryBlockId) as IMyTerminalBlock;
        if (block == null || block.MarkedForClose)
        {
          AiSession.Instance.Logger.Log($"Attempted to recall a bot to a null or closed block", Utilities.MessageType.WARNING);
          var returnPkt = new SpawnPacketClient(_botEntityId, false);
          netHandler.SendToPlayer(returnPkt, SenderId);
          return false;
        }

        IMyPlayer player;
        var bot = MyEntities.GetEntityById(_botEntityId) as IMyCharacter;
        if (bot != null)
        {
          BotBase helper;
          if (bot.IsDead || bot.MarkedForClose || !AiSession.Instance.Bots.TryGetValue(bot.EntityId, out helper) || helper == null)
          {
            AiSession.Instance.Logger.Log($"Attempted to recall a dead or missing bot", Utilities.MessageType.WARNING);
            var pkt = new SpawnPacketClient(_botEntityId, false);
            netHandler.SendToPlayer(pkt, SenderId);
            return false;
          }

          var botPos = block.WorldMatrix.Translation + block.WorldMatrix.Backward + block.WorldMatrix.Down;
          var botMatrix = MatrixD.CreateWorld(botPos, block.WorldMatrix.Backward, block.WorldMatrix.Up);
          bot.Physics.SetSpeeds(Vector3.Zero, Vector3.Zero);
          bot.SetWorldMatrix(botMatrix);
          bot.SetPosition(botPos);

          helper.UseAPITargets = false;
          helper.PatrolMode = false;
          helper.FollowMode = false;
          helper._patrolList?.Clear();
          helper.CleanPath();

          if (helper.Target != null)
          {
            helper.Target.RemoveInventory();
            helper.Target.RemoveOverride(false);
            helper.Target.RemoveTarget();
            helper.SetTarget();

            helper.Target.Update();
            var actualPosition = helper.Target.CurrentActualPosition;
            helper.StartCheckGraph(ref actualPosition, true);
          }

          var returnPkt = new SpawnPacketClient(_botEntityId, false);
          netHandler.SendToPlayer(returnPkt, SenderId);
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

                  helper.Position = block.WorldMatrix.Translation + block.WorldMatrix.Backward + block.WorldMatrix.Down;
                  helper.GridEntityId = block.CubeGrid.EntityId;

                  if (_priorities != null)
                  {
                    if (helper.Priorities == null)
                      helper.Priorities = new List<string>();
                    else
                      helper.Priorities.Clear();

                    foreach (var item in _priorities)
                    {
                      var prefix = item.Value ? "[X]" : "[  ]";
                      helper.Priorities.Add($"{prefix} {item.Key}");
                    }
                  }

                  var future = new FutureBot(helper, _ownerIdentityId);
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
        AiSession.Instance.Logger.Log($"Exception in FactoryRecallPacket.Received: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
        return false;
      }
    }
  }
}
