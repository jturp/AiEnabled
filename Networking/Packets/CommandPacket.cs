using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;

using ProtoBuf;

using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using VRage.Game.Entity;

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
    [ProtoMember(7)] long GridEntityId;
    [ProtoMember(8)] List<Vector3I> PatrolNodesLocal;
    [ProtoMember(9)] List<Vector3D> PatrolNodesWorld;
    [ProtoMember(10)] string PatrolName;

    public CommandPacket() { }

    public CommandPacket(long botId, bool stay = false, bool follow = false, bool resume = false)
    {
      BotEntityId = botId;
      Stay = stay;
      Resume = resume;
      Follow = follow;
    }

    public CommandPacket(long botId, Vector3D? goTo = null)
    {
      BotEntityId = botId;
      GoTo = goTo;
    }

    public CommandPacket(long botId, List<Vector3I> patrolList, long gridId, string patrolName)
    {
      BotEntityId = botId;
      GridEntityId = gridId;
      PatrolNodesLocal = patrolList;
      PatrolName = patrolName;
      Patrol = true;
    }

    public CommandPacket(long botId, List<Vector3D> patrolList, string patrolName)
    {
      BotEntityId = botId;
      PatrolNodesWorld = patrolList;
      PatrolName = patrolName;
      Patrol = true;
    }

    public override bool Received(NetworkHandler netHandler)
    {
      BotBase bot;
      if (!AiSession.Instance.Bots.TryGetValue(BotEntityId, out bot) || bot?.Character == null || bot.Character.MarkedForClose || bot.Character.IsDead)
        return false;

      bot.CleanPath();
      var crewBot = bot as CrewBot;
      var isCrew = crewBot != null;
      MyCubeBlock cube = null;

      if (GoTo.HasValue)
      {
        // turning GoTo into a patrol with one waypoint if the position isn't a seat (otherwise bot should sit down)

        List<MyEntity> entList = AiSession.Instance.EntListStack.Get();

        var sphere = new BoundingSphereD(GoTo.Value, 1);
        MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere, entList);

        bool shouldSit = false;

        for (int i = 0; i < entList.Count; i++)
        {
          var ent = entList[i] as IMyCockpit;
          if (ent == null)
            continue;

          cube = ent as MyCubeBlock;
          shouldSit = true;
          break;
        }

        if (!shouldSit)
        {
          if (isCrew)
          {
            entList.Clear();
            sphere = new BoundingSphereD(GoTo.Value, 3);
            MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere, entList);
            double distanceSqd = double.MaxValue;

            for (int i = 0; i < entList.Count; i++)
            {
              var terminal = entList[i] as IMyTerminalBlock;

              if (terminal != null && (terminal is IMyPowerProducer || terminal is IMyShipController || terminal is IMyProductionBlock || terminal is IMyLargeTurretBase
                || terminal.BlockDefinition.SubtypeName.IndexOf("kitchen", StringComparison.OrdinalIgnoreCase) >= 0))
              {

                var d = Vector3D.DistanceSquared(terminal.GetPosition(), GoTo.Value);
                if (d < distanceSqd)
                {
                  distanceSqd = d;
                  cube = terminal as MyCubeBlock;
                }
              }
            }

            crewBot.AssignToCrew(cube);
          }
          else
          {
            PatrolNodesWorld = new List<Vector3D>() { GoTo.Value };
  
            GoTo = null;
            Patrol = true;
            PatrolName = "GoTo Route";
          }
        }
        else if (isCrew)
        {
          crewBot.AssignToCrew(cube);
        }

        AiSession.Instance.EntListStack.Return(entList);
      }

      if (Stay || GoTo.HasValue)
      {
        bot.Target.RemoveTarget();
        bot.Target.RemoveOverride(false);
        bot._transitionPoint = null;
        bot.NeedsTransition = false;
        bot.PatrolMode = false;
        bot.FollowMode = false;
        bot.UseAPITargets = Stay || !isCrew;

        if (GoTo.HasValue)
        {
          var seat = bot.Character.Parent as IMyCockpit;
          if (seat != null)
          {
            if (seat.CubeGrid.GridSize < 1)
            {
              // need to ensure the bot updates its map accordingly
              BotFactory.RemoveBotFromSeat(bot);
            }
            else
            {
              seat.RemovePilot();
              Vector3D relPosition;
              if (!AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out relPosition))
                relPosition = Vector3D.Forward * 2.5 + Vector3D.Up;

              var position = seat.GetPosition() + Vector3D.Rotate(relPosition, seat.WorldMatrix) + bot.WorldMatrix.Down;
              bot.Character.SetPosition(position);

              var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
              if (jetpack != null && !jetpack.TurnedOn)
              {
                if (bot.RequiresJetpack)
                {
                  var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
                  MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
                  jetpack.TurnOnJetpack(true);
                  MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
                }
                else if (bot.CanUseAirNodes && MyAPIGateway.Session.SessionSettings.EnableJetpack)
                {
                  jetpack.TurnOnJetpack(true);
                }
              }
            }
          }

          if (cube?.CubeGrid != null && cube.CubeGrid.GridSize < 1)
          {
            var cpit = cube as IMyCockpit;
            if (cpit != null)
            {
              Vector3D relPosition;
              var vector = bot.BotInfo.CurrentBotPositionActual - cube.PositionComp.WorldAABB.Center;
              if (vector.LengthSquared() > 1000)
                relPosition = Vector3D.Forward * 2.5 + Vector3D.Up * 2.5;
              else
                relPosition = Vector3D.Rotate(vector, MatrixD.Transpose(cube.WorldMatrix));

              AiSession.Instance.BotToSeatRelativePosition[bot.Character.EntityId] = relPosition;
              BotFactory.TrySeatBot(bot, cpit);
            }
          }
          else
          {
            bot.Target.SetOverride(GoTo.Value);
          }
        }
      }
      else if (Resume)
      {
        bot.PatrolMode = false;
        bot.FollowMode = false;
        bot.UseAPITargets = false;
        bot._transitionPoint = null;
        bot.NeedsTransition = false;
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
          if (jetpack != null && bot.RequiresJetpack && !jetpack.TurnedOn)
          {
            var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
            MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
            jetpack.TurnOnJetpack(true);
            MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
          }
        }
      }
      else if (Follow)
      {
        bot.UseAPITargets = false;
        bot.PatrolMode = false;
        bot.FollowMode = true;
        bot._transitionPoint = null;
        bot.NeedsTransition = false;
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
          if (jetpack != null && bot.RequiresJetpack && !jetpack.TurnedOn)
          {
            var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
            MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
            jetpack.TurnOnJetpack(true);
            MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
          }
        }
      }
      else if (Patrol && (PatrolNodesLocal?.Count > 0 || PatrolNodesWorld?.Count > 0))
      {
        bot.UseAPITargets = false;
        bot.PatrolMode = true;
        bot.FollowMode = false;
        bot._transitionPoint = null;
        bot.NeedsTransition = false;
        bot.Target.RemoveOverride(false);

        if (bot is RepairBot)
        {
          bot.Target.RemoveTarget();
        }

        if (PatrolNodesLocal?.Count > 0)
        {
          if (PatrolNodesWorld == null)
            PatrolNodesWorld = new List<Vector3D>(PatrolNodesLocal.Count);
          else
            PatrolNodesWorld.Clear();
  
          var grid = MyEntities.GetEntityById(GridEntityId) as MyCubeGrid;
          if (grid != null)
          {
            for (int i = 0; i < PatrolNodesLocal.Count; i++)
            {
              var localPt = PatrolNodesLocal[i];
              PatrolNodesWorld.Add(grid.GridIntegerToWorld(localPt));
            }
          }
        }

        if (PatrolNodesWorld.Count > 0)
          bot.UpdatePatrolPoints(PatrolNodesWorld, PatrolName);

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
          if (jetpack != null && bot.RequiresJetpack && !jetpack.TurnedOn)
          {
            var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
            MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
            jetpack.TurnOnJetpack(true);
            MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
          }
        }
      }

      return false;
    }
  }
}
