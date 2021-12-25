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

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots.Roles.Helpers
{
  public class CombatBot : BotBase
  {
    uint _lineOfSightTimer;
    int _lastMovementX, _ticksSinceFirePacket;
    bool _firePacketSent;

    public CombatBot(IMyCharacter bot, GridBase gridBase, long ownerId) : base(bot, 3, 10, gridBase)
    {
      Owner = AiSession.Instance.Players[ownerId];
      Behavior = new FriendlyBehavior(bot);
      ToolSubtype = "RapidFireAutomaticRifleItem";
      bool hasOwner = Owner != null;

      _followDistanceSqd = 25;
      _ticksSinceFoundTarget = 241;
      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 200;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      RequiresJetpack = bot.Definition.Id.SubtypeName == "Drone_Bot";
      CanUseSpaceNodes = RequiresJetpack || hasOwner;
      CanUseAirNodes = RequiresJetpack || hasOwner;
      GroundNodesFirst = !RequiresJetpack;
      EnableDespawnTimer = !hasOwner;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = true;
      CanUseLadders = true;
      WantsTarget = true;

      MyAPIGateway.Utilities.InvokeOnGameThread(AddWeapon, "AiEnabled");

      if (!AiSession.Instance.SoundListStack.TryPop(out _attackSounds))
        _attackSounds = new List<MySoundPair>();
      else
        _attackSounds.Clear();

      if (!AiSession.Instance.StringListStack.TryPop(out _attackSoundStrings))
        _attackSoundStrings = new List<string>();
      else
        _attackSoundStrings.Clear();

      _attackSounds.Add(new MySoundPair("DroneLoopSmall"));
      _attackSoundStrings.Add("DroneLoopSmall");
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

        if (inventory.CanItemsBeAdded(50, ammoDefinition))
        {
          var ammo = (MyObjectBuilder_AmmoMagazine)MyObjectBuilderSerializer.CreateNewObject(ammoDefinition);
          inventory.AddItems(50, ammo);

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
        if (_ticksSinceFirePacket > TicksBetweenProjectiles * 15)
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
            var ammoType = gun.GunBase.CurrentAmmoMagazineDefinition;
            var controlEnt = Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
            HasWeaponOrTool = HasLineOfSight = false;

            if (Owner != null)
            {
              var pkt = new MessagePacket($"{Character.Name} is out of ammunition ({ammoType.DisplayNameText})!");
              AiSession.Instance.Network.SendToPlayer(pkt, Owner.SteamUserId);
            }

            if (_pathCollection != null)
              _pathCollection.Dirty = true;
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

    internal override bool IsInRangeOfTarget() => true;

    public override void SetTarget()
    {
      var character = Owner?.Character;
      if (character == null || !WantsTarget)
      {
        return;
      }

      var botPosition = Position;
      if (Target.IsDestroyed())
      {
        Target.RemoveTarget();
      }
      else if (Target.Entity != null && HasLineOfSight)
      {
        // if the current target is viable there's not reason to keep trying to switch targets
        var ch = Target.Entity as IMyCharacter;
        if (ch != null && ch.EntityId != character.EntityId && Vector3D.DistanceSquared(ch.WorldAABB.Center, botPosition) < 3000)
          return;
      }

      List<IHitInfo> hitList;
      if (!AiSession.Instance.HitListStack.TryPop(out hitList))
        hitList = new List<IHitInfo>();
      else
        hitList.Clear();

      List<MyEntity> entities;
      if (!AiSession.Instance.EntListStack.TryPop(out entities))
        entities = new List<MyEntity>();
      else
        entities.Clear();

      List<MyEntity> gridTurrets;
      if (!AiSession.Instance.EntListStack.TryPop(out gridTurrets))
        gridTurrets = new List<MyEntity>();

      List<IMyCubeGrid> gridGroups;
      if (!AiSession.Instance.GridGroupListStack.TryPop(out gridGroups))
        gridGroups = new List<IMyCubeGrid>();

      HashSet<long> checkedGridIDs;
      if (!AiSession.Instance.GridCheckHashStack.TryPop(out checkedGridIDs))
        checkedGridIDs = new HashSet<long>();
      else
        checkedGridIDs.Clear();

      var ownerPos = character.WorldAABB.Center;
      var ownerHeadPos = character.GetHeadMatrix(true).Translation;
      var botHeadPos = Character.GetHeadMatrix(true).Translation;
      var sphere = new BoundingSphereD(ownerPos, 150);
      var blockDestroEnabled = MyAPIGateway.Session.SessionSettings.DestructibleBlocks;
      var queryType = blockDestroEnabled ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, queryType);
      entities.ShellSort(ownerPos);

      IMyEntity tgt = character;
      List<BotBase> helpers;
      AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers);

      for (int i = 0; i < entities.Count; i++)
      {
        var ent = entities[i];
        if (ent?.MarkedForClose != false)
          continue;

        long entOwnerId;
        var ch = ent as IMyCharacter;
        var grid = ent as MyCubeGrid;

        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == character.EntityId || ch.EntityId == Character.EntityId)
            continue;

          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot) && bot != null && !bot.IsDead)
          {
            if (bot._botState?.IsOnLadder == true)
              continue;

            if (helpers != null)
            {
              bool found = false;
              foreach (var otherBot in helpers)
              {
                if (ch.EntityId == otherBot.Character?.EntityId)
                {
                  found = true;
                  break;
                }
              }

              if (found)
                continue;
            }
          }

          var worldHeadPos = ch.GetHeadMatrix(true).Translation;
          MyAPIGateway.Physics.CastRay(ownerHeadPos, worldHeadPos, hitList, CollisionLayers.CharacterCollisionLayer);

          if (hitList.Count > 0)
          {
            bool valid = true;
            foreach (var hit in hitList)
            {
              var hitEnt = hit.HitEntity as IMyCharacter;
              if (hitEnt != null)
              {
                if (hitEnt.EntityId == character.EntityId || hitEnt.EntityId == Character.EntityId || hitEnt.EntityId == ch.EntityId)
                  continue;

                if (helpers != null)
                {
                  bool found = false;
                  foreach (var otherBot in helpers)
                  {
                    if (hitEnt.EntityId == otherBot.Character?.EntityId)
                    {
                      found = true;
                      break;
                    }
                  }

                  if (found)
                    continue;
                }

                valid = false;
                break;
              }
              else
              {
                valid = false;
                break;
              }
            }

            if (!valid)
            {
              hitList.Clear();
              MyAPIGateway.Physics.CastRay(botHeadPos, worldHeadPos, hitList, CollisionLayers.CharacterCollisionLayer);

              valid = true;
              foreach (var hit in hitList)
              {
                var hitEnt = hit.HitEntity as IMyCharacter;
                if (hitEnt != null)
                {
                  if (hitEnt.EntityId == character.EntityId || hitEnt.EntityId == Character.EntityId || hitEnt.EntityId == ch.EntityId)
                    continue;

                  if (helpers != null)
                  {
                    bool found = false;
                    foreach (var otherBot in helpers)
                    {
                      if (hitEnt.EntityId == otherBot.Character?.EntityId)
                      {
                        found = true;
                        break;
                      }
                    }

                    if (found)
                      continue;
                  }

                  valid = false;
                  break;
                }
                else
                {
                  valid = false;
                  break;
                }
              }

              if (!valid)
                continue;
            }
          }

          entOwnerId = ch.ControllerInfo.ControllingIdentityId;
        }
        else if (HasWeaponOrTool && blockDestroEnabled && grid?.Physics != null)
        {
          if (grid.IsPreview || grid.MarkedAsTrash || grid.MarkedForClose)
            continue;

          if (checkedGridIDs.Contains(grid.EntityId))
          {
            continue;
          }

          gridTurrets.Clear();
          gridGroups.Clear();
          MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, gridGroups);

          foreach (var g in gridGroups)
          {
            checkedGridIDs.Add(g.EntityId);

            var myGrid = g as MyCubeGrid;
            if (myGrid.BlocksCount > grid.BlocksCount)
              grid = myGrid;

            var gatlings = myGrid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_LargeGatlingTurret), 0);
            var missiles = myGrid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_LargeMissileTurret), 0);
            var interiors = myGrid.BlocksCounters.GetValueOrDefault(typeof(MyObjectBuilder_InteriorTurret), 0);

            if (gatlings > 0 || missiles > 0 || interiors > 0)
            {
              var blocks = myGrid.GetFatBlocks();
              for (int j = 0; j < blocks.Count; j++)
              {
                var turret = blocks[j] as IMyLargeTurretBase;
                if (turret != null && !turret.MarkedForClose && turret.IsFunctional)
                  gridTurrets.Add(turret as MyEntity);
              }
            }
          }

          var playerControlling = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
          if (playerControlling != null)
            entOwnerId = playerControlling.IdentityId;
          else if (grid.BigOwners?.Count > 0)
            entOwnerId = grid.BigOwners[0];
          else if (grid.SmallOwners?.Count > 0)
            entOwnerId = grid.SmallOwners[0];
          else
            continue;

          var dToTurret = double.MaxValue;
          MyEntity turretEnt = null;

          // check for turrets only
          for (int j = gridTurrets.Count - 1; j >= 0; j--)
          {
            var turret = gridTurrets[j];

            var d = Vector3D.DistanceSquared(turret.PositionComp.GetPosition(), botPosition);
            if (d < dToTurret)
            {
              turretEnt = turret as MyEntity;
              dToTurret = d;
            }
          }

          if (turretEnt == null)
            continue;

          ent = turretEnt;
        }
        else
          continue;

        var relationship = MyIDModule.GetRelationPlayerBlock(Owner.IdentityId, entOwnerId, MyOwnershipShareModeEnum.Faction);
        if (relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
        {
          tgt = ent;
          break;
        }
      }

      hitList.Clear();
      entities.Clear();
      gridTurrets.Clear();
      gridGroups.Clear();
      checkedGridIDs.Clear();

      AiSession.Instance.HitListStack.Push(hitList);
      AiSession.Instance.EntListStack.Push(entities);
      AiSession.Instance.EntListStack.Push(gridTurrets);
      AiSession.Instance.GridGroupListStack.Push(gridGroups);
      AiSession.Instance.GridCheckHashStack.Push(checkedGridIDs);

      var parent = tgt is IMyCharacter ? tgt.GetTopMostParent() : tgt;
      var currentTgt = Target.Entity as IMyEntity;
      if (currentTgt?.EntityId == parent.EntityId)
        return;

      Target.SetTarget(parent);
      _pathCollection?.CleanUp(true);
    }

    List<float> _randoms = new List<float>();

    bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
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
          var packet = new WeaponFirePacket(Character.EntityId, targetEnt.EntityId, 10, _randoms, TicksBetweenProjectiles, ammoCount, false, false);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        AiSession.Instance.StartWeaponFire(Character.EntityId, targetEnt.EntityId, 10, _randoms, TicksBetweenProjectiles, ammoCount, false, false);

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
      var tgtFriendly = Target.IsFriendly();
      var isFriendly = isTarget && tgtFriendly;
      var flatDistanceCheck = isFriendly ? _followDistanceSqd : distanceCheck;

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
      var isOwnerTgt = Target.Player?.IdentityId == Owner.IdentityId;

      Vector3D gotoPosition, actualPosition;
      Target.GetTargetPosition(out gotoPosition, out actualPosition);

      double distanceSqdToTarget = double.MaxValue;
      double distanceToCheck = 15625;
      if (HasWeaponOrTool && !isOwnerTgt && !isFriendly && Target.HasTarget)
      {
        distanceSqdToTarget = Vector3D.DistanceSquared(actualPosition, botPosition);
      }

      if (distanceSqdToTarget <= distanceToCheck)
      {
        if (HasLineOfSight)
        {
          rifleAttack = !tgtFriendly;

          if (_moveFromLadder)
          {
            _moveFromLadder = false;
            _sideNode = null;
          }
        }
      }
      else if (!_moveFromLadder)
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

      if (_currentGraph != null)
      {
        var localPos = _currentGraph.WorldToLocal(botPosition);
        var worldPosAligned = _currentGraph.LocalToWorld(localPos);
        if (Vector3D.DistanceSquared(worldPosAligned, waypoint) >= _currentGraph.CellSize * _currentGraph.CellSize)
        {
          var relVectorWP = Vector3D.Rotate(waypoint - worldPosAligned, MatrixD.Transpose(WorldMatrix));
          var flattenedVecWP = new Vector3D(relVectorWP.X, 0, relVectorWP.Z);

          if (Vector3D.IsZero(flattenedVecWP, 0.1))
          {
            if (Math.Abs(relVectorBot.Y) < 0.1)
            {
              movement = Vector3.Zero;
            }
            else if (!JetpackEnabled)
            {
              movement = Vector3.Forward;
            }
            else
            {
              rotation = Vector2.Zero;
              movement = Math.Sign(relVectorBot.Y) * Vector3.Up * 2;
            }

            fistAttack = !isOwnerTgt && !HasWeaponOrTool && isTarget && angleTwoOrLess && distanceSqd <= distanceCheck;
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

          Vector3I node;
          if (_sideNode.HasValue)
          {
            //if (_isShooting)
            //  movement = Vector3.Zero;
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
        if (flattenedLengthSquared > flatDistanceCheck || Math.Abs(relVectorBot.Y) > distanceCheck)
        {
          if (_currentGraph.IsGridGraph)
          {
            MyCubeBlock ladder;
            if (flattenedLengthSquared <= flatDistanceCheck && relVectorBot.Y > 0 && Target.OnLadder(out ladder))
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

          if (!JetpackEnabled && Owner?.Character != null && Target.Player?.IdentityId == Owner.IdentityId)
          {
            var ch = Character as Sandbox.Game.Entities.IMyControllableEntity;
            var distanceToTarget = Vector3D.DistanceSquared(gotoPosition, botPosition);
            if (distanceToTarget > 200)
            {
              ch.Sprint(true);
            }
            else
            {
              ch.Sprint(false);

              if (!rifleAttack)
              {
                var botRunning = _botState.IsRunning;
                if (distanceToTarget > 100)
                {
                  if (!botRunning)
                    ch.SwitchWalk();
                }
                else if (botRunning)
                  ch.SwitchWalk();
              }
            }
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
      else if (flattenedLengthSquared > flatDistanceCheck && _ticksSinceFoundTarget > 240)
        movement = Vector3.Forward;
      else
        movement = Vector3.Zero;

      fistAttack = isTarget && !isOwnerTgt && !HasWeaponOrTool && angleTwoOrLess && distanceSqd <= distanceCheck;

      if (!fistAttack && isTarget && !isOwnerTgt && !HasWeaponOrTool && angleTwoOrLess && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
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
  }
}
