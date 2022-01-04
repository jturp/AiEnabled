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

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class SoldierBot : BotBase
  {
    uint _lineOfSightTimer;
    int _lastMovementX, _ticksSinceFirePacket;
    bool _firePacketSent;

    public SoldierBot(IMyCharacter bot, GridBase gridBase) : base(bot, 5, 15, gridBase)
    {
      Behavior = new EnemyBehavior(bot);
      ToolSubtype = "RapidFireAutomaticRifleItem";

      _ticksSinceFoundTarget = 241;
      _ticksBetweenAttacks = 200;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      RequiresJetpack = bot.Definition.Id.SubtypeName == "Drone_Bot";
      CanUseSpaceNodes = RequiresJetpack;
      CanUseAirNodes = RequiresJetpack;
      GroundNodesFirst = !RequiresJetpack;
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

      _attackSounds.Add(new MySoundPair("Enemy"));
      _attackSoundStrings.Add("Enemy");

      MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");
    }

    public override void AddWeapon()
    {
      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"SoliderBot.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      var weaponDefinition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype);

      if (inventory.CanItemsBeAdded(1, weaponDefinition))
      {
       var weapon = (MyObjectBuilder_PhysicalGunObject)MyObjectBuilderSerializer.CreateNewObject(weaponDefinition);
       inventory.AddItems(1, weapon);

        var ammoDefinition = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "NATO_5p56x45mm");

        if (inventory.CanItemsBeAdded(10, ammoDefinition))
        {
          var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
          inventory.AddItems(10, ammo);

          var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
          if (charController.CanSwitchToWeapon(weaponDefinition))
          {
            charController.SwitchToWeapon(weaponDefinition);
            HasWeaponOrTool = true;
            SetShootInterval();
          }
          else
            AiSession.Instance.Logger.Log($"SoldierBot.AddWeapon: WARNING! Added rifle and ammo but unable to swith to weapon!", MessageType.WARNING);
        }
        else
          AiSession.Instance.Logger.Log($"SoldierBot.AddWeapon: WARNING! Added rifle but unable to add ammo!", MessageType.WARNING);
      }
      else
        AiSession.Instance.Logger.Log($"SoldierBot.AddWeapon: WARNING! Unable to add rifle to inventory!", MessageType.WARNING);
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

      if (UsePathFinder)
      {
        UsePathfinder(gotoPosition, actualPosition);
        return;
      }

      Vector3 movement;
      Vector2 rotation;
      bool shouldAttack, shouldFire;
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out shouldAttack, out shouldFire);
      CheckFire(shouldFire, shouldAttack, ref movement);
      Character.MoveAndRotate(movement, rotation, 0f);
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (_firePacketSent)
      {
        _ticksSinceFirePacket++;
        if (_ticksSinceFirePacket > TicksBetweenProjectiles * 20)
          _firePacketSent = false;
      }

      if (WaitForLOSTimer)
      {
        ++_lineOfSightTimer;
        if (_lineOfSightTimer > 100)
        {
          _lineOfSightTimer = 0;
          WaitForLOSTimer = false;
        }
      }

      if (HasWeaponOrTool)
      {
        var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
        if (gun != null)
        {
          var ammoCount = gun.CurrentMagazineAmount;
          if (ammoCount <= 0 && !MyAPIGateway.Session.CreativeMode)
          {
            var inventory = Character.GetInventory();
            var ammoDefinition = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "NATO_5p56x45mm");

            if (inventory.CanItemsBeAdded(10, ammoDefinition))
            {
              var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
              inventory.AddItems(10, ammo);
            }
          }
          else if (Target.HasTarget && !(Character.Parent is IMyCockpit))
            MyAPIGateway.Utilities.InvokeOnGameThread(CheckLineOfSight, "AiEnabled");
          else
            HasLineOfSight = false;
        }
        else
          HasLineOfSight = false;
      }

      return true;
    }

    readonly List<float> _randoms = new List<float>();

    bool FireWeapon()
    {
      var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      if (gun == null)
        return false;

      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
        return false;

      if (gun.NeedsReload)
      {
        gun.Reload();
        return false;
      }
      else if (gun.IsReloading)
        return false;

      if (!_firePacketSent)
      {
        _randoms.Clear();
        for (int i = 0; i < 10; i++)
        {
          var rand = MyUtils.GetRandomFloat(0, 1);
          _randoms.Add(rand);
        }

        var ammoCount = MyAPIGateway.Session.CreativeMode ? 30 : gun.GunBase.CurrentAmmo;
        if (MyAPIGateway.Multiplayer.MultiplayerActive)
        {
          var packet = new WeaponFirePacket(Character.EntityId, targetEnt.EntityId, 1.5f, _randoms, TicksBetweenProjectiles, ammoCount, false, false);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        AiSession.Instance.StartWeaponFire(Character.EntityId, targetEnt.EntityId, 1.5f, _randoms, TicksBetweenProjectiles, ammoCount, false, false);
        
        _firePacketSent = true;
        _ticksSinceFirePacket = 0;
      }

      return true;
    }

    bool _moveFromLadder;

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out bool fistAttack, out bool rifleAttack, double distanceCheck = 4)
    {
      rifleAttack = false;
      var botPosition = Position;

      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(WorldMatrix));

      if (_botState.IsOnLadder)
      {
        movement = relVectorBot.Y > 0 ? Vector3.Forward : Vector3.Backward;
        rotation = Vector2.Zero;
        fistAttack = false;
        return;
      }

      var flattenedVector = new Vector3D(relVectorBot.X, 0, relVectorBot.Z);
      var flattenedLengthSquared = flattenedVector.LengthSquared();
      var distanceSqd = relVectorBot.LengthSquared();

      Vector3D gotoPosition, actualPosition;
      Target.GetTargetPosition(out gotoPosition, out actualPosition);

      double distanceSqdToTarget = double.MaxValue;
      double distanceToCheck = 10000;
      if (HasWeaponOrTool && Target.HasTarget)
      {
        distanceSqdToTarget = Vector3D.DistanceSquared(actualPosition, botPosition);

        if (Target.Entity is IMyLargeTurretBase)
          distanceToCheck = 15625;
      }

      if (distanceSqdToTarget <= distanceToCheck)
      {
        if (HasLineOfSight)
        {
          rifleAttack = true;

          if (_moveFromLadder)
          {
            _moveFromLadder = false;
            _sideNode = null;
          }
        }
      }
      else if (!_moveFromLadder)
        _sideNode = null;

      if (_sideNode.HasValue && Vector3D.DistanceSquared(botPosition, _sideNode.Value) > 400)
        _sideNode = null;

      var projUp = VectorUtils.Project(vecToWP, WorldMatrix.Up);
      var reject = vecToWP - projUp;
      var angle = VectorUtils.GetAngleBetween(WorldMatrix.Forward, reject);
      var angleTwoOrLess = relVectorBot.Z < 0 && Math.Abs(angle) < MathHelperD.ToRadians(2);

      if (!WaitForStuckTimer && angleTwoOrLess)
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
            if (!JetpackEnabled || Math.Abs(relVectorBot.Y) < 0.1)
            {
              movement = Vector3.Zero;
            }
            else
            {
              rotation = Vector2.Zero;
              movement = Math.Sign(relVectorBot.Y) * Vector3.Up * 2;
            }

            fistAttack = !HasWeaponOrTool && isTarget && angleTwoOrLess && distanceSqd <= distanceCheck;
            return;
          }
        }
      }

      if (rifleAttack || _sideNode.HasValue)
      {
        if (UsePathFinder)
        {
          var vecToTgt = actualPosition - botPosition;
          var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(WorldMatrix));
          projUp = VectorUtils.Project(vecToTgt, WorldMatrix.Up);
          reject = vecToTgt - projUp;
          angle = VectorUtils.GetAngleBetween(WorldMatrix.Forward, reject);

          if (relToTarget.Z < 0 && Math.Abs(angle) < MathHelperD.ToRadians(2))
          {
            rotation = Vector2.Zero;
          }
          else
          {
            if (rifleAttack && Target.Entity is IMyLargeTurretBase && Math.Abs(angle) > VectorUtils.PiOver3)
              rifleAttack = false;

            rotation = new Vector2(0, (float)angle * Math.Sign(relToTarget.X) * 75);
          }

          var door = Target.Entity as IMyDoor;
          Vector3D doorPos = Vector3D.Zero;
          if (door != null)
          {
            doorPos = door.WorldAABB.Center;

            if (door is IMyAirtightHangarDoor)
              doorPos += door.WorldMatrix.Down * door.CubeGrid.GridSize;
          }

          if (isTarget && door != null && _currentGraph.WorldToLocal(botPosition) == _currentGraph.WorldToLocal(doorPos))
          {
            movement = Vector3D.Backward;
          }
          else
          {
            Vector3I node;
            if (_sideNode.HasValue)
            {
              //if (_isShooting)
              //{
              //  movement = Vector3.Zero;
              //}
              //else
              {
                var dirVec = _sideNode.Value - botPosition;
                var relativeDir = Vector3D.TransformNormal(dirVec, MatrixD.Transpose(WorldMatrix));
                var absX = Math.Abs(relativeDir.X);
                var absZ = Math.Abs(relativeDir.Z);

                var xMove = absX <= 0.5 ? 0 : Math.Sign(relativeDir.X);
                var zMove = absZ <= 0.5 ? 0 : Math.Sign(relativeDir.Z);
                if (xMove == 0 && zMove == 0)
                  _sideNode = null;

                movement = new Vector3(xMove, 0, zMove);
              }
            }
            else if (_currentGraph != null && _currentGraph.GetRandomNodeNearby(this, waypoint, out node))
            {
              var worldNode = _currentGraph.LocalToWorld(node);
              _sideNode = worldNode;

              if (Vector3D.DistanceSquared(worldNode, botPosition) > 400)
              {
                AiSession.Instance.Logger.Log($"Side node set to something far far away! Node was {node}, WorldNode was {worldNode}, Distance = {Vector3D.Distance(worldNode, botPosition):0.##}");
              }

              if (Vector3D.DistanceSquared(worldNode, actualPosition) < 10)
              {
                Node testNode;
                var dir = Vector3D.Normalize(worldNode - actualPosition);
                _currentGraph.GetRandomOpenNode(this, botPosition + dir * 15, out testNode);

                if (testNode != null)
                {
                  Vector3D? addVec = (_currentGraph.LocalToWorld(testNode.Position) + testNode.Offset) - botPosition;
                  _sideNode += addVec;
                }
                else
                  _sideNode += new Vector3D?(dir * 5);
              }

              var dirVec = _sideNode.Value - botPosition;
              var relativeDir = Vector3D.TransformNormal(dirVec, MatrixD.Transpose(WorldMatrix));

              var xMove = Math.Abs(relativeDir.X) < 0.5 ? 0 : Math.Sign(relativeDir.X);
              var zMove = Math.Abs(relativeDir.Z) < 0.5 ? 0 : Math.Sign(relativeDir.Z);
              movement = new Vector3(xMove, 0, zMove);
            }
            else
              movement = Vector3.Zero;
          }
        }
        else
        {
          if (Math.Abs(flattenedVector.Z) < 20 && relVectorBot.Y > 5)
            movement = Vector3.Forward;
          else if (flattenedLengthSquared < 900)
            movement = Vector3.Backward;
          else
          {
            if (_xMoveTimer > 100)
            {
              _xMoveTimer = 0;
              var num = MyUtils.GetRandomInt(0, 8);
              _lastMovementX = (num < 3) ? -1 : (num < 5) ? 0 : 1;
            }

            movement = new Vector3(_lastMovementX, 0, 0);
          }
        }
      }
      else if (PathFinderActive)
      {
        if (flattenedLengthSquared > distanceCheck || Math.Abs(relVectorBot.Y) > distanceCheck)
        {
          if (_currentGraph.IsGridGraph)
          {
            MyCubeBlock ladder;
            if (flattenedLengthSquared <= distanceCheck && relVectorBot.Y > 0 && Target.OnLadder(out ladder))
            {
              var gridGraph = _currentGraph as CubeGridMap;
              _sideNode = gridGraph.GetLastValidNodeOnLine(ladder.PositionComp.WorldAABB.Center, ladder.WorldMatrix.Forward, 20);
              _moveFromLadder = true;
            }
            else
              _moveFromLadder = false;
          }
          else
            _moveFromLadder = false;

          if (!rifleAttack)
          {
            var ch = Character as Sandbox.Game.Entities.IMyControllableEntity;
            var distanceToTarget = Vector3D.DistanceSquared(gotoPosition, botPosition);

            var botRunning = _botState.IsRunning;
            if (distanceToTarget > 100)
            {
              if (!botRunning)
                ch.SwitchWalk();
            }
            else if (botRunning)
              ch.SwitchWalk();
          }

          movement = _moveFromLadder ? Vector3.Zero : Vector3.Forward;
        }
        else
          movement = Vector3.Zero;
      }
      else if (HasWeaponOrTool && WaitForLOSTimer)
      {
        int zMove;
        if (Math.Abs(flattenedVector.Z) < 30 && relVectorBot.Y > 5)
          zMove = 1;
        else
          zMove = (flattenedLengthSquared > 100) ? -1 : 0;

        movement = new Vector3(1, 0, zMove);
      }
      else if (flattenedLengthSquared > distanceCheck && _ticksSinceFoundTarget > 240)
        movement = Vector3.Forward;
      else
        movement = Vector3.Zero;

      fistAttack = isTarget && !HasWeaponOrTool && angleTwoOrLess && distanceSqd <= distanceCheck;

      if (!fistAttack && isTarget && !HasWeaponOrTool && angleTwoOrLess && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
        movement = Vector3.Forward * 0.5f;

      if (JetpackEnabled && Math.Abs(relVectorBot.Y) > 0.05)
        AdjustMovementForFlight(ref relVectorBot, ref movement, ref botPosition);
    }

    void CheckFire(bool shouldFire, bool shouldAttack, ref Vector3 movement)
    {
      var isCrouching = _botState.IsCrouching;
      IsShooting = false;

      if (shouldFire)
      {
        if (isCrouching)
        {
          Character.Crouch();
          Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        }

        if (HasLineOfSight && ((byte)MySessionComponentSafeZones.AllowedActions & 2) != 0 && FireWeapon())
        {
          IsShooting = true;
          _stuckTimer = 0;
          _ticksSinceFoundTarget = 0;

          //if (!MyAPIGateway.Multiplayer.MultiplayerActive && !isCrouching && _crouchTimer > 10) // crouching is wonky for bots, had to remove it :(
          //{
          //  _crouchTimer = 0;
          //  Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
          //  Character.Crouch();
          //}

          //movement = Vector3.Zero;
        }
        //else if (!MyAPIGateway.Multiplayer.MultiplayerActive && isCrouching && _crouchTimer > 10)
        //{
        //  _crouchTimer = 0;
        //  Character.Crouch();
        //  Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        //}
      }
      else
      {
        if (isCrouching)
        {
          Character.Crouch();
          Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        }

        if (shouldAttack)
        {
          _stuckTimer = 0;
          _ticksSinceFoundTarget = 0;
          Attack();
        }
        else
        {
          TrySwitchWalk();
        }
      }
    }
    
    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      bool shouldAttack, shouldFire;
      TrySwitchWalk();

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out shouldAttack, out shouldFire, distanceCheck);
      CheckFire(shouldFire, shouldAttack, ref movement);
      MoveToPoint(movement, rotation);
    }
  }
}