using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class PatronBot : NeutralBotBase
  {
    int _seatedCounter;
    IMyCockpit _lastSeat;
    List<IMyCockpit> _seatList = new List<IMyCockpit>();

    public PatronBot(IMyCharacter bot, GridBase gridBase, AiSession.ControlInfo ctrlInfo, string toolType = null)
      : base(bot, 7, 15, gridBase, ctrlInfo)
    {
      Behavior = new NeutralBehavior(this);
      CanUseSeats = true;
      CanTransitionMaps = false;
      

      if (!string.IsNullOrWhiteSpace(toolType))
      {
        ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolType));

        if (ToolDefinition != null)
        {
          AiSession.Instance.Scheduler.Schedule(AddWeapon);

          if (AiSession.Instance.WcAPILoaded)
          {
            AiSession.Instance.WcAPI.ShootRequestHandler(Character.EntityId, false, WCShootCallback);
          }
        }
      }
    }

    internal override void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        _seatList?.Clear();
        _seatList = null;
        _lastSeat = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log(ex.ToString());
      }
      finally
      {
        base.CleanUp(cleanConfig, removeBot);
      }
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (_tickCount % 100 == 0)
      {
        if (Target.Entity != null && Target.PositionsValid)
        {
          if (Vector3D.DistanceSquared(Character.WorldAABB.Center, Target.CurrentActualPosition) > 150 * 150)
            Target.RemoveTarget();
        }

        if (Target.Entity == null || Target.IsDestroyed())
        {
          if (BotInfo.IsRunning)
            Character.SwitchWalk();

          if (HasWeaponOrTool)
          {
            var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
            gun?.OnControlReleased();

            var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
            HasWeaponOrTool = false;
            HasLineOfSight = false;
          }
        }
      }

      return true;
    }

    internal override void SetTargetInternal()
    {
      if (Target.HasTarget)
      {
        if (_lastSeat?.Pilot != null && _lastSeat.Pilot.EntityId != Character.EntityId)
        {
          // someone else sat there!
          _lastSeat = null;
          BotFactory.ResetBotTargeting(this);
        }

        return;
      }

      var gridGraph = _currentGraph as CubeGridMap;
      if (gridGraph != null && gridGraph.Ready)
      {
        bool allowNewTarget;
        bool anyPlayerPresent = false;
        foreach (var kvp in AiSession.Instance.Players)
        {
          var player = kvp.Value;
          if (player?.Character == null)
            continue;

          if (Vector3D.DistanceSquared(player.Character.WorldAABB.Center, BotInfo.CurrentBotPositionActual) < 250 * 250)
          {
            anyPlayerPresent = true;
            break;
          }
        }

        var seat = Character?.Parent as IMyCockpit;
        if (seat != null)
        {
          ++_seatedCounter;
          if (_seatedCounter >= 30 || !anyPlayerPresent)
          {
            BotFactory.RemoveBotFromSeat(this, false);
            BotFactory.ResetBotTargeting(this);
            AllowIdleMovement = true;
          }

          return;
        }
        else
        {
          --_seatedCounter;
          allowNewTarget = _seatedCounter <= 0;
        }

        if (allowNewTarget && anyPlayerPresent)
        {
          _seatedCounter = 0;

          var grid = gridGraph.MainGrid;
          if (grid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_Cockpit)) > 0)
          {
            _seatList.Clear();
            var airNodesAllowed = CanUseAirNodes;

            var fatBlocks = grid.GetFatBlocks();
            foreach (var block in fatBlocks)
            {
              var cpit = block as IMyCockpit;
              if (cpit != null && !cpit.CanControlShip && cpit.Pilot == null && (_lastSeat == null || cpit.EntityId != _lastSeat.EntityId))
              {
                var subtype = cpit.BlockDefinition.SubtypeName;

                if (subtype.IndexOf("desk", StringComparison.OrdinalIgnoreCase) >= 0
                  || subtype.IndexOf("couch", StringComparison.OrdinalIgnoreCase) >= 0
                  || subtype.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0
                  || subtype.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
                  || subtype.IndexOf("passenger", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  if (MaxTravelDistance > 0)
                  {
                    var maxDistance = MaxTravelDistance * MaxTravelDistance;
                    if (Vector3D.DistanceSquared(SpawnPosition, cpit.WorldAABB.Center) > maxDistance)
                      continue;
                  }

                  Vector3I node;
                  if (_currentGraph.GetClosestValidNode(this, cpit.Position, out node, isSlimBlock: true, allowAirNodes: airNodesAllowed))
                    _seatList.Add(cpit);
                }
              }
            }

            if (_seatList.Count > 0)
            {
              AllowIdleMovement = false;
              var rand = MyUtils.GetRandomInt(_seatList.Count);
              seat = _seatList[rand];

              Target.SetOverride(seat.WorldAABB.Center);
              _lastSeat = seat;
              return;
            }
          }
        }
      }

      base.SetTargetInternal();
    }
  }
}
