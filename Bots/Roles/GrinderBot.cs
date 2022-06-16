﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Game.ObjectBuilders.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class GrinderBot : BotBase
  {
    public GrinderBot(IMyCharacter bot, GridBase gridBase, string toolType = null) : base(bot, 5, 15, gridBase)
    {
      Behavior = new ZombieBehavior(bot);
      var toolSubtype = toolType ?? "AngleGrinder2Item";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      _ticksBeforeDamage = 63;
      _ticksBetweenAttacks = 200;
      _deathSound = new MySoundPair("ZombieDeath");
      _deathSoundString = "ZombieDeath";
      _blockDamagePerSecond = 125;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      var jetRequired = bot.Definition.Id.SubtypeName == "Drone_Bot";
      var jetAllowed = jetRequired || AiSession.Instance.ModSaveData.AllowEnemiesToFly;

      RequiresJetpack = jetRequired;
      CanUseSpaceNodes = jetAllowed;
      CanUseAirNodes = jetAllowed;
      GroundNodesFirst = !jetRequired;
      EnableDespawnTimer = true;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = true;
      CanUseLadders = true;
      WantsTarget = true;

      if (!AiSession.Instance.SoundListStack.TryPop(out _attackSounds))
        _attackSounds = new List<MySoundPair>();
      else
        _attackSounds.Clear();

      if (!AiSession.Instance.StringListStack.TryPop(out _attackSoundStrings))
        _attackSoundStrings = new List<string>();
      else
        _attackSoundStrings.Clear();

      _attackSounds.Add(new MySoundPair("ZombieAttack001"));
      _attackSounds.Add(new MySoundPair("ZombieAttack002"));
      _attackSounds.Add(new MySoundPair("ZombieAttack003"));
      _attackSounds.Add(new MySoundPair("ZombieAttack004"));
      _attackSoundStrings.Add("ZombieAttack001");
      _attackSoundStrings.Add("ZombieAttack002");
      _attackSoundStrings.Add("ZombieAttack003");
      _attackSoundStrings.Add("ZombieAttack004");

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

      var grinderDefinition = ToolDefinition?.PhysicalItemId ?? new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), "AngleGrinder2Item");
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
          AiSession.Instance.Logger.Log($"GrinderBot.AddWeapon: WARNING! Added grinder but unable to switch to it!", MessageType.WARNING);
      }
      else
        AiSession.Instance.Logger.Log($"GrinderBot.AddWeapon: WARNING! Unable to add grinder to inventory!", MessageType.WARNING);
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack;

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, distanceCheck);
      
      if (shouldAttack && ((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;

        Attack();
      }
      else
      {
        TrySwitchWalk();
      }

      MoveToPoint(movement, rotation, roll);
    }

    internal override void Attack()
    {
      var tool = Character.EquippedTool as IMyAngleGrinder;
      if (tool != null)
      {
        if (_ticksSinceLastAttack >= 60)
        {
          _ticksSinceLastAttack = 0;

          if (!FireWeapon())
            return;
        }

        var tgtEnt = Target.Entity as IMyCharacter;
        var seat = Target.Entity as IMyCockpit;
        if (tgtEnt != null)
        {
          BotBase botTarget;
          var damage = 0.2f;
          if (AiSession.Instance.Players.ContainsKey(tgtEnt.ControllerInfo.ControllingIdentityId))
          {
            damage *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
          }
          else if (AiSession.Instance.Bots.TryGetValue(tgtEnt.EntityId, out botTarget) && botTarget != null)
          {
            var nomad = botTarget as NomadBot;
            if (nomad != null && nomad.Target.Entity == null)
            {
              nomad.SetHostile(Character);
            }
            else if (botTarget.Owner != null)
            {
              damage *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
            }
          }

          tgtEnt.DoDamage(damage, MyDamageType.Grind, true);
        }
        else if (seat != null)
        {
          var damage = 5f * AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
          var casterComp = tool.Components?.Get<MyCasterComponent>();
          if (casterComp != null && casterComp.HitBlock == null)
          {
            casterComp.SetPointOfReference(seat.CenterOfMass);
            seat.SlimBlock.DoDamage(damage, MyDamageType.Grind, true);
          }
        }
      }
      else if (_ticksSinceLastAttack >= _ticksBetweenAttacks)
      {
        _ticksSinceLastAttack = 0;
        _damageTicks = 0;
        DamagePending = true;

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

      if (!Target.PositionsValid)
        return;

      var actualPosition = Target.CurrentActualPosition;

      if (UsePathFinder)
      {
        var gotoPosition = Target.CurrentGoToPosition;
        UsePathfinder(gotoPosition, actualPosition);
        return;
      }

      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack;
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out roll, out shouldAttack);

      if (shouldAttack && ((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
      {
        movement = Vector3.Zero;
        Attack();
      }

      Character.MoveAndRotate(movement, rotation, roll);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out float roll, out bool fistAttack, double distanceCheck = 2.5)
    {
      roll = 0;
      var botPosition = GetPosition();
      var botMatrix = WorldMatrix;
      var graphMatrix = _currentGraph.WorldMatrix;
      var graphUpVector = graphMatrix.Up;
      var jpEnabled = JetpackEnabled;

      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(botMatrix));

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

      //if (jpEnabled)
      //{
      //  var deviationAngle = MathHelper.PiOver2 - VectorUtils.GetAngleBetween(graphUpVector, botMatrix.Left);
      //  var botdotUp = botMatrix.Up.Dot(graphMatrix.Up);

      //  if (botdotUp < 0 || Math.Abs(deviationAngle) > _twoDegToRads)
      //  {
      //    var botLeftDotUp = -botMatrix.Left.Dot(graphUpVector);

      //    if (botdotUp < 0)
      //      roll = MathHelper.Pi * Math.Sign(botLeftDotUp);
      //    else
      //      roll = (float)deviationAngle * Math.Sign(botLeftDotUp);
      //  }
      //}

      var projUp = VectorUtils.Project(vecToWP, botMatrix.Up);
      var reject = vecToWP - projUp;
      var angle = VectorUtils.GetAngleBetween(botMatrix.Forward, reject);
      var angleTwoOrLess = relVectorBot.Z < 0 && Math.Abs(angle) < _twoDegToRads;

      if (!WaitForStuckTimer && angleTwoOrLess)
      {
        rotation = Vector2.Zero;
      }
      else
      {
        float xRot = 0;

        //if (jpEnabled && Math.Abs(roll) < MathHelper.ToRadians(5))
        //{
        //  var angleFwd = MathHelperD.PiOver2 - VectorUtils.GetAngleBetween(botMatrix.Forward, graphUpVector);
        //  var botDotUp = botMatrix.Up.Dot(graphMatrix.Up);

        //  if (botDotUp < 0 || Math.Abs(angleFwd) > _twoDegToRads)
        //  {
        //    var botFwdDotUp = botMatrix.Forward.Dot(graphMatrix.Up);

        //    if (botDotUp < 0)
        //      xRot = -MathHelper.Pi * Math.Sign(botFwdDotUp);
        //    else
        //      xRot = (float)angleFwd * Math.Sign(botFwdDotUp);
        //  }
        //}

        rotation = new Vector2(xRot, (float)angle * Math.Sign(relVectorBot.X) * 75);
      }

      if (_currentGraph?.Ready == true)
      {
        var localPos = _currentGraph.WorldToLocal(botPosition);
        var worldPosAligned = _currentGraph.LocalToWorld(localPos);
        if (Vector3D.DistanceSquared(worldPosAligned, waypoint) >= _currentGraph.CellSize * _currentGraph.CellSize)
        {
          var relVectorWP = Vector3D.Rotate(waypoint - worldPosAligned, MatrixD.Transpose(botMatrix));
          var flattenedVecWP = new Vector3D(relVectorWP.X, 0, relVectorWP.Z);

          if (Vector3D.IsZero(flattenedVecWP, 0.1))
          {
            var absY = Math.Abs(relVectorBot.Y);
            if (!JetpackEnabled || absY <= 0.1)
            {
              if (!_currentGraph.IsGridGraph && absY > 0.5 && relVectorBot.Y < 0)
              {
                _pathCollection.ClearNode();
                rotation = Vector2.Zero;
                movement = Vector3.Forward;
              }
              else
              {
                movement = Vector3.Zero;
              }
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

      if (PathFinderActive)
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

      if (JetpackEnabled)
      {
        var vecToTgt = Target.CurrentActualPosition - botPosition;
        var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(botMatrix));
        var flatToTarget = new Vector3D(relToTarget.X, 0, relToTarget.Z);
        if (flatToTarget.LengthSquared() <= 10 && Math.Abs(relToTarget.Y) > 0.5)
        {
          movement = Vector3.Zero;
          relVectorBot = relToTarget;
        }

        if (Math.Abs(relVectorBot.Y) > 0.05)
          AdjustMovementForFlight(ref relVectorBot, ref movement, ref botPosition);
      }
    }

    bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyDeviceBase>;
      if (gun == null)
        return false;

      //MyGunStatusEnum gunStatus;
      //if (!gun.CanShoot(MyShootActionEnum.PrimaryAction, Character.ControllerInfo.ControllingIdentityId, out gunStatus))
      //  return false;

      if (!MySessionComponentSafeZones.IsActionAllowed(Character.WorldAABB.Center, CastHax(MySessionComponentSafeZones.AllowedActions, 16)))
        return false;

      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
        return false;

      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        var packet = new WeaponFirePacket(Character.EntityId, targetEnt.EntityId, 0.2f, 0f, null, TicksBetweenProjectiles, 100, true, false, false);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      AiSession.Instance.StartWeaponFire(Character.EntityId, targetEnt.EntityId, 0.2f, 0f, null, TicksBetweenProjectiles, 100, true, false, false);
      return true;
    }
  }
}
