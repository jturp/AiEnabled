using AiEnabled.Ai.Support;
using AiEnabled.Bots.Roles;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Networking;
using AiEnabled.Particles;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots
{
  public class EnemyBotBase : BotBase
  {
    internal bool _allowedToSwitchWalk;
    internal bool _moveFromLadder;
    internal bool _tooCloseUseSideNode;
    internal bool _firePacketSent;
    internal uint _lineOfSightTimer;
    internal int _lastMovementX, _ticksSinceFirePacket;
    internal byte _sideNodeTimer, _sideNodeWaitTime;
    List<float> _randoms = new List<float>(10);

    public EnemyBotBase(IMyCharacter bot, float minDamage, float maxDamage, GridBase gridBase, AiSession.ControlInfo ctrlInfo) : base(bot, minDamage, maxDamage, gridBase, ctrlInfo)
    {
      var jetpack = bot.Components.Get<MyCharacterJetpackComponent>();
      var jetpackWorldSetting = MyAPIGateway.Session.SessionSettings.EnableJetpack;
      var jetRequired = jetpack != null && bot.Definition.Id.SubtypeName == "Drone_Bot";
      var jetAllowed = jetpack != null && jetpackWorldSetting && (jetRequired || AiSession.Instance.ModSaveData.AllowEnemiesToFly);

      RequiresJetpack = jetRequired;
      CanUseSpaceNodes = jetRequired || jetAllowed;
      CanUseAirNodes = jetRequired || jetAllowed;
      GroundNodesFirst = !jetRequired;
      EnableDespawnTimer = true;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = true;
      CanUseLadders = true;
      WantsTarget = true;
      CanDamageGrid = true;

      _attackSounds = AiSession.Instance.SoundListStack.Get();
      _attackSoundStrings = AiSession.Instance.StringListStack.Get();

      if (RequiresJetpack && jetpack != null && !jetpack.TurnedOn)
      {
        var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
        MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
        jetpack.TurnOnJetpack(true);
        MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
      }
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (HasWeaponOrTool && ToolDefinition != null && ToolDefinition.WeaponType != MyItemWeaponType.None)
      {
        var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
        if (gun != null)
        {
          var ammoCount = _wcWeaponMagsLeft ?? gun.CurrentMagazineAmount;
          if (ammoCount <= 0 && !MyAPIGateway.Session.CreativeMode && !MyAPIGateway.Session.SessionSettings.InfiniteAmmo)
          {
            var inventory = Character.GetInventory();
            string ammoSubtype = null;

            var weaponDefinition = ToolDefinition.PhysicalItemId;

            List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>> magList;
            if (AiSession.Instance.WcAPILoaded && AiSession.Instance.NpcSafeCoreWeaponMagazines.TryGetValue(weaponDefinition, out magList))
            {
              ammoSubtype = magList[0].Item2.Item1.SubtypeName;
            }
            else
            {
              var weaponItemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(weaponDefinition) as MyWeaponItemDefinition;
              if (weaponItemDef != null)
              {
                var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponItemDef.WeaponDefinitionId);
                ammoSubtype = weaponDef?.AmmoMagazinesId?.Length > 0 ? weaponDef.AmmoMagazinesId[0].SubtypeName : null;
              }
              else if (ToolDefinition.WeaponType == MyItemWeaponType.Rifle)
              {
                ammoSubtype = "NATO_5p56x45mm";
              }
              else if (ToolDefinition.WeaponType == MyItemWeaponType.RocketLauncher)
              {
                ammoSubtype = "Missile200mm";
              }
              else if (weaponDefinition.SubtypeName.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                ammoSubtype = weaponDefinition.SubtypeName.StartsWith("Full") ? "FullAutoPistolMagazine" : "SemiAutoPistolMagazine";
              }
              else
              {
                ammoSubtype = "ElitePistolMagazine";
              }
            }

            var ammoDefinition = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), ammoSubtype);
            var amountThatFits = ((MyInventory)inventory).ComputeAmountThatFits(ammoDefinition);
            var amount = Math.Min((int)amountThatFits, 10);

            if (inventory.CanItemsBeAdded(amount, ammoDefinition))
            {
              var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
              inventory.AddItems(amount, ammo);
            }
          }
          else if (Target.HasTarget && !(Character.Parent is IMyCockpit))
            AiSession.Instance.Scheduler.Schedule(CheckLineOfSight);
            //MyAPIGateway.Utilities.InvokeOnGameThread(CheckLineOfSight, "AiEnabled");
          else
            HasLineOfSight = false;
        }
        else
          HasLineOfSight = false;
      }

      return true;
    }

    internal override void Close(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        _randoms?.Clear();
        _randoms = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in EnemyBotBase.Close: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.Close(cleanConfig, removeBot);
      }
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack;
      bool shouldFire;

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, out shouldFire, distanceCheck);
      CheckBlockTarget(ref isTgt, ref shouldAttack, ref movement, ref rotation, ref distanceCheck);
      CheckFire(shouldFire, shouldAttack, ref movement);
      MoveToPoint(movement, rotation, roll);
    }

    internal override void MoveToTarget()
    {
      if (GrenadeThrown)
        return;

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
      bool shouldAttack, shouldFire;
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out roll, out shouldAttack, out shouldFire);
      CheckFire(shouldFire, shouldAttack, ref movement);
      Character.MoveAndRotate(movement, rotation, roll);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out float roll, out bool fistAttack, out bool rifleAttack, double distanceCheck = 4)
    {
      roll = 0;
      rifleAttack = false;
      var botPosition = BotInfo.CurrentBotPositionAdjusted;
      var botMatrix = WorldMatrix;
      var graphMatrix = _currentGraph?.WorldMatrix ?? botMatrix;
      var graphUpVector = graphMatrix.Up;
      var jpEnabled = JetpackEnabled;
      var hasWeapon = HasWeaponOrTool && !(Character.EquippedTool is IMyAngleGrinder) && !(Character.EquippedTool is IMyWelder);

      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(botMatrix));

      if (BotInfo.IsOnLadder)
      {
        movement = relVectorBot.Y > 0 ? Vector3.Forward : Vector3.Backward;
        rotation = Vector2.Zero;
        fistAttack = false;

        if (movement == Vector3.Backward && !NextIsLadder)
        {
          DismountLadder(waypoint, botPosition);
        }

        return;
      }

      var projUp = AiUtils.Project(vecToWP, botMatrix.Up);
      var reject = vecToWP - projUp;
      var angle = AiUtils.GetAngleBetween(botMatrix.Forward, reject);
      var angleTwoOrLess = relVectorBot.Z < 0 && Math.Abs(angle) < _twoDegToRads;
      var absRelVector = Vector3D.Abs(relVectorBot);

      if (!WaitForStuckTimer && angleTwoOrLess)
      {
        rotation = Vector2.Zero;
      }
      else
      {
        rotation = new Vector2(0, (float)angle * Math.Sign(relVectorBot.X) * 75);
      }

      var flattenedVector = new Vector3D(relVectorBot.X, 0, relVectorBot.Z);
      var flattenedLengthSquared = flattenedVector.LengthSquared();
      var distanceSqd = relVectorBot.LengthSquared();

      var actualPosition = Target.CurrentActualPosition;
      var maxProjectileDistance = AiSession.Instance.ModSaveData.MaxBotProjectileDistance * 0.9;
      double distanceSqdToTarget = double.MaxValue;
      double distanceToCheck = maxProjectileDistance * maxProjectileDistance;

      if (hasWeapon && Target.Entity != null)
      {
        distanceSqdToTarget = Vector3D.DistanceSquared(actualPosition, botPosition);

        if (Target.Entity is IMyLargeTurretBase)
          distanceToCheck = Math.Min(distanceToCheck, 15625);
      }

      if (distanceSqdToTarget <= distanceToCheck)
      {
        if (HasLineOfSight)
        {
          rifleAttack = true;

          if (_moveFromLadder)
          {
            _tooCloseUseSideNode = false;
            _moveFromLadder = false;
            _sideNode = null;
          }
        }
      }
      else if (!_moveFromLadder || (_sideNode.HasValue && Vector3D.DistanceSquared(botPosition, _sideNode.Value) > 400))
      {
        _tooCloseUseSideNode = false;
        _sideNode = null;
      }

      if (_currentGraph != null && _currentGraph.Ready)
      {
        var localPos = _currentGraph.WorldToLocal(botPosition);
        var worldPosAligned = _currentGraph.LocalToWorld(localPos);
        var cellSize = _currentGraph.CellSize * 1.25f;
        if (Vector3D.DistanceSquared(worldPosAligned, waypoint) > cellSize * cellSize)
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

            bool checkFist2 = !hasWeapon && isTarget;
            bool aligned2 = angleTwoOrLess && distanceSqd <= distanceCheck;

            var slim2 = Target.Entity as IMySlimBlock;
            if (checkFist2 && !aligned2 && slim2 != null)
            {
              double checkD;
              if (slim2.FatBlock != null)
              {
                checkD = slim2.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 6f;
              }
              else
              {
                var size = Math.Max(1, (slim2.Max - slim2.Min).Length());
                checkD = size * slim2.CubeGrid.GridSize * 0.5 + 6;
              }

              bool allowSecondGuess = (relVectorBot.Z <= 0 && relVectorBot.Z > -3) || (absRelVector.X < 0.1 && absRelVector.Z < 0.1);

              if (allowSecondGuess && distanceSqd <= checkD)
                aligned2 = true;
            }

            fistAttack = checkFist2 && aligned2;

            if (fistAttack && slim2 != null && slim2.CubeGrid.Physics.LinearVelocity.LengthSquared() < 1)
              movement = Vector3.Zero;

            return;
          }
        }
      }

      if (!rifleAttack && hasWeapon && Target.Entity != null && !_sideNode.HasValue)
      {
        var dirToWP = botMatrix.GetClosestDirection(vecToWP);
        if ((dirToWP == Base6Directions.Direction.Down || dirToWP == Base6Directions.Direction.Up) && vecToWP.LengthSquared() < 100)
        {
          var clearDirection = GetClearTravelDirection();
          _sideNode = botPosition + clearDirection * 5;
          _tooCloseUseSideNode = true;
        }
      }
      else if (_tooCloseUseSideNode && rifleAttack)
        _tooCloseUseSideNode = false;

      if (rifleAttack || _sideNode.HasValue)
      {
        if (UsePathFinder)
        {
          var vecToTgt = actualPosition - botPosition;
          var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(botMatrix));
          projUp = AiUtils.Project(vecToTgt, botMatrix.Up);
          reject = vecToTgt - projUp;
          angle = AiUtils.GetAngleBetween(botMatrix.Forward, reject);

          if (relToTarget.Z < 0 && Math.Abs(angle) < _twoDegToRads)
          {
            rotation = Vector2.Zero;
          }
          else
          {
            if (rifleAttack && Target.Entity is IMyLargeTurretBase && Math.Abs(angle) > AiUtils.PiOver3)
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
              _sideNodeTimer = 0;
              var dirVec = _sideNode.Value - botPosition;
              var relativeDir = Vector3D.TransformNormal(dirVec, MatrixD.Transpose(botMatrix));
              var absX = Math.Abs(relativeDir.X);
              var absZ = Math.Abs(relativeDir.Z);

              var xMove = absX <= 0.5 ? 0 : Math.Sign(relativeDir.X);
              var zMove = absZ <= 0.5 ? 0 : Math.Sign(relativeDir.Z);
              if (xMove == 0 && zMove == 0)
              {
                _tooCloseUseSideNode = false;
                _sideNode = null;
              }

              movement = new Vector3(xMove, 0, zMove);
            }
            else if (_currentGraph != null && _sideNodeTimer > _sideNodeWaitTime && _currentGraph.GetRandomNodeNearby(this, waypoint, out node))
            {
              var worldNode = _currentGraph.LocalToWorld(node);
              _sideNode = worldNode;
              _sideNodeTimer = 0;
              _tooCloseUseSideNode = false;

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
                else if (_currentGraph.IsPositionValid(_sideNode.Value + dir * 5))
                  _sideNode += new Vector3D?(dir * 5);
              }

              var dirVec = _sideNode.Value - botPosition;
              var relativeDir = Vector3D.TransformNormal(dirVec, MatrixD.Transpose(botMatrix));

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
        _tooCloseUseSideNode = false;
        double flatDistanceCheck = distanceCheck;

        if (PatrolMode && _pathCollection != null && !_pathCollection.HasPath && _pathCollection.HasNode) // if this is the final waypoint
          flatDistanceCheck = 0.25;

        if (flattenedLengthSquared > flatDistanceCheck || Math.Abs(relVectorBot.Y) > distanceCheck)
        {
          if (_currentGraph.IsGridGraph)
          {
            MyCubeBlock ladder;
            if (flattenedLengthSquared <= flatDistanceCheck && relVectorBot.Y > 0 && Target.IsOnLadder(out ladder))
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

          if (!rifleAttack && _allowedToSwitchWalk)
          {
            var ch = Character as Sandbox.Game.Entities.IMyControllableEntity;
            var distanceToTarget = Vector3D.DistanceSquared(Target.CurrentGoToPosition, botPosition);

            var botRunning = BotInfo.IsRunning;
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
      else if (hasWeapon && WaitForLOSTimer)
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

      bool checkFist = !hasWeapon && isTarget;
      bool aligned = angleTwoOrLess && (distanceSqd <= distanceCheck);

      var slim = Target.Entity as IMySlimBlock;
      if (checkFist && !aligned && slim != null)
      {
        double checkD;
        if (slim.FatBlock != null)
        {
          checkD = slim.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 6f;
        }
        else
        {
          var size = Math.Max(1, (slim.Max - slim.Min).Length());
          checkD = size * slim.CubeGrid.GridSize * 0.5 + 6;
        }

        bool allowSecondGuess = (relVectorBot.Z <= 0 && relVectorBot.Z > -3) || (absRelVector.X < 0.1 && absRelVector.Z < 0.1);

        if (allowSecondGuess && distanceSqd <= checkD)
          aligned = true;
      }

      fistAttack = checkFist && aligned;

      if (fistAttack && slim != null && slim.CubeGrid.Physics.LinearVelocity.LengthSquared() < 1)
        movement = Vector3.Zero;
      else if (!fistAttack && isTarget && !hasWeapon && angleTwoOrLess && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
        movement = Vector3.Forward * 0.5f;

      if (jpEnabled)
      {
        var vecToTgt = actualPosition - botPosition;
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

    void CheckFire(bool shouldFire, bool shouldAttack, ref Vector3 movement)
    {
      var isCrouching = BotInfo.IsCrouching;
      IsShooting = false;

      if (shouldFire)
      {
        if (isCrouching)
        {
          Character.Crouch();
          Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        }

        if (BotInfo.IsRunning)
          Character.SwitchWalk();

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
        if (!_moveFromLadder && !_tooCloseUseSideNode)
          _sideNode = null;

        if (isCrouching)
        {
          Character.Crouch();
          Character.CurrentMovementState = MyCharacterMovementEnum.Standing;
        }

        if (shouldAttack && Character?.EquippedTool is IMyAngleGrinder && ((byte)MySessionComponentSafeZones.AllowedActions & 16) == 0)
          shouldAttack = false;

        if (shouldAttack)
        {
          movement = Vector3.Zero;
          _stuckTimer = 0;
          _ticksSinceFoundTarget = 0;
          Attack();
        }
        else if (_allowedToSwitchWalk)
        {
          TrySwitchWalk();
        }
      }
    }

    internal virtual bool FireWeapon()
    {
      var gun = Character?.EquippedTool as IMyHandheldGunObject<MyGunBase>;
      if (gun == null)
        return false;

      IMySlimBlock slim = null;
      Vector3D tgtPosition;
      var targetEnt = Target.Entity as IMyEntity;
      if (targetEnt == null)
      {
        slim = Target.Entity as IMySlimBlock;
        if (slim == null)
          return false;

        slim.ComputeWorldCenter(out tgtPosition);
      }
      else
      {
        tgtPosition = targetEnt.WorldAABB.Center;
      }

      if (!MySessionComponentSafeZones.IsActionAllowed(Character.WorldAABB.Center, AiUtils.CastHax(MySessionComponentSafeZones.AllowedActions, 2)))
        return false;

      var physGunObj = ToolDefinition.PhysicalItemId;

      List<VRage.MyTuple<int, VRage.MyTuple<MyDefinitionId, string, string, bool>>> magList;
      if (AiSession.Instance.WcAPILoaded && AiSession.Instance.NpcSafeCoreWeaponMagazines.TryGetValue(physGunObj, out magList))
      {
        if (!_wcShotFired && !_wcWeaponReloading)
        {
          object tgt;
          if (magList[0].Item2.Item4 && targetEnt != null)
          {
            tgt = targetEnt;
            AiSession.Instance.WcAPI.SetAiFocus((MyEntity)Character, (MyEntity)targetEnt);
          }
          else
          {
            tgt = (Object)tgtPosition;
          }

          if (AiSession.Instance.WcAPI.ShootRequest((MyEntity)Character.EquippedTool, tgt))
          {
            _wcShotFired = true;
          }
        }

        return _wcShotFired;
      }

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

        bool isLauncher = ToolDefinition.WeaponType == MyItemWeaponType.RocketLauncher;

        if (isLauncher)
        {
          AiSession.Instance.Projectiles.AddMissileForBot(this, targetEnt, slim);
        }
        else
        {
          int ammoCount;
          if (MyAPIGateway.Session.CreativeMode || AiSession.Instance.InfiniteAmmoEnabled)
          {
            if (ToolDefinition.PhysicalItemId.SubtypeName.IndexOf("pistol", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              ammoCount = ToolDefinition.PhysicalItemId.SubtypeName.IndexOf("fullauto", StringComparison.OrdinalIgnoreCase) >= 0 ? 20 : 10;
            }
            else
            {
              ammoCount = 30;
            }
          }
          else
          {
            ammoCount = _wcWeaponAmmoCount ?? gun.GunBase.CurrentAmmo;
          }

          bool leadTargets = ShouldLeadTargets;
          var topMost = slim?.CubeGrid ?? targetEnt;

          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new WeaponFirePacket(Character.EntityId, topMost.EntityId, 1.5f, _shotAngleDeviationTan, _randoms, TicksBetweenProjectiles, ammoCount, false, false, leadTargets, slim?.Position);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.StartWeaponFire(Character.EntityId, topMost.EntityId, 1.5f, _shotAngleDeviationTan, _randoms, TicksBetweenProjectiles, ammoCount, false, false, leadTargets, slim?.Position);
        }

        _firePacketSent = true;
        _ticksSinceFirePacket = 0;
      }

      return true;
    }

    internal override bool DoDamage(float amount = 0)
    {
      IMyDestroyableObject destroyable;

      var slim = Target.Entity as IMySlimBlock;
      if (slim != null)
      {
        destroyable = slim;
        amount = _blockDamagePerAttack;
      }
      else
      {
        var cube = Target.Entity as IMyCubeBlock;
        if (cube != null)
        {
          slim = cube.SlimBlock;
          destroyable = slim;
        }
        else
          destroyable = Target.Entity as IMyDestroyableObject;
      }

      if (destroyable == null || !destroyable.UseDamageSystem || destroyable.Integrity <= 0)
        return false;

      var character = destroyable as IMyCharacter;
      bool isCharacter = character != null;
      var rand = amount > 0 ? amount : isCharacter ? MyUtils.GetRandomFloat(_minDamage, _maxDamage) : _blockDamagePerAttack;

      BotBase botTarget = null;
      bool isPlayer = false;

      if (isCharacter)
      {
        if (character.Parent is IMyShipController || character.Parent is IMyCockpit)
        {
          var p = MyAPIGateway.Players.GetPlayerControllingEntity(character.Parent);
          isPlayer = p != null && AiSession.Instance.Players.ContainsKey(p.IdentityId);
        }
        else
        {
          isPlayer = AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId);
        }
      }

      if (slim != null || isPlayer)
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }
      else if (isCharacter && AiSession.Instance.Bots.TryGetValue(character.EntityId, out botTarget) && botTarget?.Owner != null)
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }

      if (!isCharacter)
      {
        if (slim != null)
        {
          if (slim.FatBlock != null)
          {
            PlaySoundServer("ImpMetalMetalCat3", slim.FatBlock.EntityId);
          }
          else
          {
            Vector3D position;
            slim.ComputeWorldCenter(out position);
            PlaySoundServer("ImpMetalMetalCat3", position);
          }
        }

        destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);
      }
      else
      {
        var statComp = character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
        var health = statComp?.Health.Value;

        base.DoDamage(rand);
        if (health == statComp?.Health.Value)
          return false;

        if (Target.Player != null && !(this is GhostBot))
          PlaySoundServer("PlayVocPain", character.EntityId);

        var neutralBot = botTarget as NeutralBotBase;
        if (neutralBot != null && neutralBot.Target.Entity == null)
        {
          neutralBot.SetHostile(Character);
        }
      }

      return isCharacter;
    }
  }
}
