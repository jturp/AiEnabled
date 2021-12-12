using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class GrinderBot : BotBase
  {
    public GrinderBot(IMyCharacter bot, GridBase gridBase) : base(bot, 5, 15, gridBase)
    {
      Behavior = new ZombieBehavior(bot);
      ToolSubtype = "AngleGrinder2Item";

      _ticksBeforeDamage = 63;
      _ticksBetweenAttacks = 200;
      _deathSound = new MySoundPair("ZombieDeath");
      _deathSoundString = "ZombieDeath";
      _blockDamagePerSecond = 125;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      _requiresJetpack = bot.Definition.Id.SubtypeName == "Drone_Bot";
      _canUseSpaceNodes = _requiresJetpack;
      _canUseAirNodes = _requiresJetpack;
      _groundNodesFirst = !_requiresJetpack;
      _enableDespawnTimer = true;
      _canUseWaterNodes = true;
      _waterNodesOnly = false;
      _canUseSeats = true;
      _canUseLadders = true;

      _attackSounds = new List<MySoundPair>
      {
        new MySoundPair("ZombieAttack001"),
        new MySoundPair("ZombieAttack002"),
        new MySoundPair("ZombieAttack003"),
        new MySoundPair("ZombieAttack004")
      };

      _attackSoundStrings = new List<string>
      {
        "ZombieAttack001",
        "ZombieAttack002",
        "ZombieAttack003",
        "ZombieAttack004"
      };

      MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");
    }

    public override void AddWeapon()
    {
      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"GrinderBot.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      var grinderDefinition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype);
      if (inventory.CanItemsBeAdded(1, grinderDefinition))
      {
        var welder = (MyObjectBuilder_PhysicalGunObject)MyObjectBuilderSerializer.CreateNewObject(grinderDefinition);
        inventory.AddItems(1, welder);

        var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
        if (charController.CanSwitchToWeapon(grinderDefinition))
        {
          charController.SwitchToWeapon(grinderDefinition);
          HasWeaponOrTool = true;
          SetShootInterval();
        }
        else
          AiSession.Instance.Logger.Log($"GrinderBot.AddWeapon: WARNING! Added welder but unable to swith to it!", MessageType.WARNING);
      }
      else
        AiSession.Instance.Logger.Log($"GrinderBot.AddWeapon: WARNING! Unable to add welder to inventory!", MessageType.WARNING);
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      bool shouldAttack;
      TrySwitchWalk();

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out shouldAttack, distanceCheck);
      
      if (shouldAttack && ((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;

        Attack();
      }

      MoveToPoint(movement, rotation);
    }

    internal override void Attack()
    {
      var tool = Character.EquippedTool as IMyAngleGrinder;
      if (tool != null)
      {
        if (_ticksSinceLastAttack >= 60)
        {
          _ticksSinceLastAttack = 0;
          FireWeapon();
        }

        var tgtEnt = Target.Entity as IMyCharacter;
        var seat = Target.Entity as IMyCockpit;
        if (tgtEnt != null)
        {
          tgtEnt.DoDamage(0.2f, MyDamageType.Grind, true);
        }
        else if (seat != null)
        {
          var casterComp = tool.Components?.Get<MyCasterComponent>();
          if (casterComp != null && casterComp.HitBlock == null)
          {
            casterComp.SetPointOfReference(seat.CenterOfMass);
            seat.SlimBlock.DoDamage(5f, MyDamageType.Grind, true);
          }
        }
      }
      else if (_ticksSinceLastAttack >= _ticksBetweenAttacks)
      {
        _ticksSinceLastAttack = 0;
        _damageTicks = 0;
        _damagePending = true;

        Character.TriggerCharacterAnimationEvent("Attack", true);
        PlaySound();
      }
    }

    internal override void MoveToTarget()
    {
      if (!IsInRangeOfTarget())
      {
        if (!UseAPITargets)
          SimulateIdleMovement(true);

        return;
      }

      Vector3D gotoPosition, actualPosition;
      if (!Target.GetTargetPosition(out gotoPosition, out actualPosition))
        return;

      if (_usePathFinder)
      {
        UsePathfinder(gotoPosition, actualPosition);
        return;
      }

      Vector3 movement;
      Vector2 rotation;
      bool shouldAttack;
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out shouldAttack);

      if (shouldAttack && ((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
      {
        movement = Vector3.Zero;
        Attack();
      }

      Character.MoveAndRotate(movement, rotation, 0f);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out bool fistAttack, double distanceCheck = 2.5)
    {
      var botPosition = Position;
      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(WorldMatrix));

      if (_botState.IsOnLadder)
      {
        movement = relVectorBot.Y > 0 ? Vector3.Forward * 1.5f : Vector3.Backward;
        rotation = Vector2.Zero;
        fistAttack = false;
        return;
      }

      var flattenedVector = new Vector3D(relVectorBot.X, 0, relVectorBot.Z);
      var flattenedLengthSquared = flattenedVector.LengthSquared();
      var distanceSqd = relVectorBot.LengthSquared();

      var projUp = VectorUtils.Project(vecToWP, WorldMatrix.Up);
      var reject = vecToWP - projUp;
      var angle = VectorUtils.GetAngleBetween(WorldMatrix.Forward, reject);
      var angleTwoOrLess = relVectorBot.Z < 0 && Math.Abs(angle) < MathHelperD.ToRadians(2);

      if (!_waitForStuckTimer && angleTwoOrLess)
      {
        rotation = Vector2.Zero;
      }
      else
      {
        rotation = new Vector2(0, (float)angle * Math.Sign(relVectorBot.X) * 75);
      }

      if (_currentGraph?.Ready == true)
      {
        var localPos = _currentGraph.WorldToLocal(botPosition);
        var worldPosAligned = _currentGraph.LocalToWorld(localPos);
        if (Vector3D.DistanceSquared(worldPosAligned, waypoint) >= _currentGraph.CellSize * _currentGraph.CellSize)
        {
          var relVectorWP = Vector3D.Rotate(waypoint - worldPosAligned, MatrixD.Transpose(WorldMatrix));
          var flattenedVecWP = new Vector3D(relVectorWP.X, 0, relVectorWP.Z);

          if (Vector3D.IsZero(flattenedVecWP, 0.1))
          {
            if (!_jetpackEnabled || Math.Abs(relVectorBot.Y) < 0.1)
            {
              movement = Vector3.Zero;
            }
            else
            {
              rotation = Vector2.Zero;
              movement = Math.Sign(relVectorBot.Y) * Vector3.Up * 2;
            }

            fistAttack = isTarget && angleTwoOrLess && distanceSqd <= distanceCheck;
            return;
          }
        }
      }

      if (_pathFinderActive)
      {
        if (flattenedLengthSquared > distanceCheck || Math.Abs(relVectorBot.Y) > distanceCheck)
          movement = Vector3.Forward * 1.5f;
        else
          movement = Vector3.Zero;
      }
      else if (flattenedLengthSquared > distanceCheck && _ticksSinceFoundTarget > 240)
        movement = Vector3.Forward * 1.5f;
      else
        movement = Vector3.Zero;

      fistAttack = isTarget && angleTwoOrLess && distanceSqd <= distanceCheck;

      if (!fistAttack && isTarget && angleTwoOrLess && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
        movement = Vector3.Forward * 0.5f;

      if (_jetpackEnabled && Math.Abs(relVectorBot.Y) > 0.05)
        AdjustMovementForFlight(ref relVectorBot, ref movement, ref botPosition);
    }

    bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyDeviceBase>;
      if (gun == null)
        return false;

      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
        return false;

      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        var packet = new WeaponFirePacket(Character.EntityId, targetEnt.EntityId, 0.2f, null, TicksBetweenProjectiles, 100, true, false);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      AiSession.Instance.StartWeaponFire(Character.EntityId, targetEnt.EntityId, 0.2f, null, TicksBetweenProjectiles, 100, true, false);
      return true;
    }
  }
}
