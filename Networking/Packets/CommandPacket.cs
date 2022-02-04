using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using VRageMath;

namespace AiEnabled.Networking
{
  [ProtoContract]
  public class CommandPacket : PacketBase
  {
    [ProtoMember(1)] bool Stay;
    [ProtoMember(2)] bool Follow;
    [ProtoMember(3)] bool Resume;
    [ProtoMember(4)] bool Patrol;
    [ProtoMember(5)] Vector3D? GoTo;
    [ProtoMember(6)] long BotEntityId;
    [ProtoMember(7)] List<Vector3D> PatrolNodes;

    public CommandPacket() { }

    public CommandPacket(long botId, bool stay = false, bool follow = false, bool resume = false, bool patrol = false, Vector3D? goTo = null, List<Vector3D> patrolList = null)
    {
      BotEntityId = botId;
      Stay = stay;
      Resume = resume;
      Patrol = patrol;
      Follow = follow;
      GoTo = goTo;
      PatrolNodes = patrolList;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(BotEntityId, out bot) || bot?.Character == null || bot.Character.MarkedForClose || bot.Character.IsDead)
        return false;

      if (Stay || GoTo.HasValue)
      {
        bot.Target.RemoveTarget();
        bot.Target.RemoveOverride(false);
        bot.PatrolMode = false;
        bot.FollowMode = false;
        bot.UseAPITargets = true;

        if (GoTo.HasValue)
        {
          var seat = bot.Character.Parent as IMyCockpit;
          if (seat != null)
          {
            seat.RemovePilot();
            Vector3D position = seat.WorldAABB.Center + seat.WorldMatrix.Forward * 2;
            bot.Character.SetPosition(position);
          }

          bot.Target.SetOverride(GoTo.Value);
        }
      }
      else if (Resume)
      {
        bot.PatrolMode = false;
        bot.FollowMode = false;
        bot.UseAPITargets = false;
        bot.Target.RemoveTarget();
        bot.Target.RemoveOverride(false);

        var seat = bot.Character.Parent as IMyCockpit;
        if (seat != null)
        {
          seat.RemovePilot();
          Vector3D relPosition;
          if (!AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition))
            relPosition = Vector3D.Forward * 2.5 + Vector3D.Up;

          var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;
          bot.Character.SetPosition(position);

          var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null)
          {
            if (bot.RequiresJetpack)
            {
              if (!jetpack.TurnedOn)
              {
                var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
                jetpack.TurnOnJetpack(true);
                MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
              }
            }
            else if (jetpack.TurnedOn)
              jetpack.SwitchThrusts();
          }
        }
      }
      else if (Follow)
      {
        bot.UseAPITargets = false;
        bot.PatrolMode = false;
        bot.FollowMode = true;
        bot.Target.RemoveTarget();
        bot.Target.RemoveOverride(false);

        var seat = bot.Character.Parent as IMyCockpit;
        if (seat != null)
        {
          seat.RemovePilot();
          Vector3D relPosition;
          if (!AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition))
            relPosition = Vector3D.Forward * 2.5 + Vector3D.Up;

          var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;
          bot.Character.SetPosition(position);

          var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null)
          {
            if (bot.RequiresJetpack)
            {
              if (!jetpack.TurnedOn)
              {
                var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
                jetpack.TurnOnJetpack(true);
                MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
              }
            }
            else if (jetpack.TurnedOn)
              jetpack.SwitchThrusts();
          }
        }
      }
      else if (Patrol && PatrolNodes?.Count > 0)
      {
        bot.UseAPITargets = false;
        bot.PatrolMode = true;
        bot.FollowMode = false;
        bot.Target.RemoveOverride(false);

        if (bot is RepairBot)
        {
          bot.Target.RemoveTarget();
        }

        bot.UpdatePatrolPoints(PatrolNodes);

        var seat = bot.Character.Parent as IMyCockpit;
        if (seat != null)
        {
          seat.RemovePilot();
          Vector3D relPosition;
          if (!AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition))
            relPosition = Vector3D.Forward * 2.5 + Vector3D.Up;

          var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;
          bot.Character.SetPosition(position);

          var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null)
          {
            if (bot.RequiresJetpack)
            {
              if (!jetpack.TurnedOn)
              {
                var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
                jetpack.TurnOnJetpack(true);
                MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
              }
            }
            else if (jetpack.TurnedOn)
              jetpack.SwitchThrusts();
          }
        }
      }

      return false;
    }
  }
}
