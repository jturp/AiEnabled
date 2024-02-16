using AiEnabled.Ai.Support;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Networking;
using AiEnabled.Utilities;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using SpaceEngineers.Game.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots
{
  public class FriendlyBotBase : BotBase
  {
    internal uint _lineOfSightTimer;
    internal int _lastMovementX, _ticksSinceFirePacket;
    internal bool _firePacketSent, _moveFromLadder, _tooCloseUseSideNode;
    internal byte _sideNodeTimer, _sideNodeWaitTime;

    internal List<float> _randoms = new List<float>(10);

    public FriendlyBotBase(IMyCharacter bot, float minDamage, float maxDamage, GridBase gridBase, long ownerId, AiSession.ControlInfo ctrlInfo) : base(bot, minDamage, maxDamage, gridBase, ctrlInfo)
    {
      Owner = AiSession.Instance.Players.GetValueOrDefault(ownerId, null);

      bool hasOwner = Owner != null;
      var jetpack = bot.Components.Get<MyCharacterJetpackComponent>();
      var jetpackWorldSetting = MyAPIGateway.Session.SessionSettings.EnableJetpack;
      var jetRequired = jetpack != null && bot.Definition.Id.SubtypeName == "Drone_Bot";
      var jetAllowed = jetpack != null && jetpackWorldSetting && (jetRequired || AiSession.Instance.ModSaveData.AllowHelpersToFly);

      _followDistanceSqd = 25;
      RequiresJetpack = jetRequired;
      CanUseSpaceNodes = jetRequired || jetAllowed;
      CanUseAirNodes = jetRequired || jetAllowed;
      GroundNodesFirst = !jetRequired;
      EnableDespawnTimer = !hasOwner;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = true;
      CanUseLadders = true;
      WantsTarget = true;
      ShouldLeadTargets = true;
      CanDamageGrid = true;

      _attackSounds = AiSession.Instance.SoundListPool.Get();
      _attackSoundStrings = AiSession.Instance.StringListPool.Get();

      if (RequiresJetpack && jetpack != null && !jetpack.TurnedOn)
      {
        var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
        MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
        jetpack.TurnOnJetpack(true);
        MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
      }
    }

    internal override bool IsInRangeOfTarget() => true;

    internal override void Close(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        _randoms?.Clear();
        _randoms = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in FriendlyBotBase.Close: {ex}");
      }
      finally
      {
        base.Close(cleanConfig, removeBot);
      }
    }

    internal override void SetTargetInternal()
    {
      var ownerCharacter = Owner?.Character;
      if (ownerCharacter == null || !WantsTarget || IsDead || Target == null)
      {
        return;
      }

      if (FollowMode)
      {
        var ownerParent = ownerCharacter.GetTopMostParent();
        var currentEnt = Target.Entity as IMyEntity;

        if (currentEnt?.EntityId != ownerParent.EntityId)
        {
          Target.SetTarget(ownerParent);
          CleanPath();
        }

        return;
      }

      var botPosition = BotInfo.CurrentBotPositionActual;
      if (Target.IsDestroyed())
      {
        Target.RemoveTarget();
      }
      else if (Target.Entity != null)
      {
        // if the current target is viable there's not reason to keep trying to switch targets

        bool allowReturn = ToolDefinition == null || ToolDefinition.WeaponType == MyItemWeaponType.None;

        if (allowReturn)
        {
          var player = Target.Player ?? MyAPIGateway.Players.GetPlayerControllingEntity(Target.Entity as IMyCharacter);
          if (player != null && player.IdentityId != Owner?.IdentityId)
          {
            MyAdminSettingsEnum adminSettings;
            if (MyAPIGateway.Session.TryGetAdminSettings(player.SteamUserId, out adminSettings))
            {
              if ((adminSettings & MyAdminSettingsEnum.Untargetable) != 0)
              {
                allowReturn = false;
                Target.RemoveTarget();
              }
            }
          }
        }

        if (allowReturn && HasLineOfSight)
        {
          var ent = Target.Entity as IMyEntity;
          var cube = Target.Entity as IMyCubeBlock;
          var slim = cube?.SlimBlock;
          if (slim == null)
            slim = Target.Entity as IMySlimBlock;

          if ((ent == null && slim != null && !slim.IsDestroyed) || (ent != null && !ent.MarkedForClose && ent.EntityId != Owner?.Character?.EntityId))
          {
            Vector3D entCenter;
            if (cube != null)
              entCenter = cube.GetPosition();
            else if (slim != null)
              slim.ComputeWorldCenter(out entCenter);
            else
              entCenter = ent.WorldAABB.Center;

            var maxRange = (double)AiSession.Instance.ModSaveData.MaxBotHuntingDistanceFriendly * 0.5;
            if (Vector3D.DistanceSquared(entCenter, botPosition) < maxRange * maxRange)
            {
              if (cube != null)
              {
                if (cube.IsFunctional && !cube.SlimBlock.IsBlockUnbuilt())
                  return;
              }
              else
                return;
            }
          }
        }
      }

      List<IHitInfo> hitList = AiSession.Instance.HitListPool.Get();
      List<MyEntity> entities = AiSession.Instance.EntListPool.Get();
      List<MyEntity> blockTargets = AiSession.Instance.EntListPool.Get();
      List<IMyCubeGrid> gridGroups = AiSession.Instance.GridGroupListPool.Get();
      HashSet<long> checkedGridIDs = AiSession.Instance.GridCheckHashPool.Get();
      List<IMySlimBlock> blockList = AiSession.Instance.SlimListPool.Get();
      List<MyLineSegmentOverlapResult<MyEntity>> resultList = AiSession.Instance.OverlapResultListPool.Get();
      List<Vector3I> cellList = AiSession.Instance.LineListPool.Get();

      var onPatrol = PatrolMode && _patrolList?.Count > 0;
      var ownerPos = ownerCharacter.WorldAABB.Center;
      var ownerHeadPos = ownerCharacter.GetHeadMatrix(true).Translation;
      var botHeadPos = Character.GetHeadMatrix(true).Translation;

      var centerPoint = ownerPos;
      var distance = AiSession.Instance.ModSaveData.MaxBotHuntingDistanceFriendly;

      if (onPatrol)
      {
        centerPoint = botPosition;
        var enemyHuntDistance = AiSession.Instance.ModSaveData.MaxBotHuntingDistanceEnemy;

        if (enemyHuntDistance > distance && distance < 300)
          distance = Math.Min(300, enemyHuntDistance);
      }

      var sphere = new BoundingSphereD(centerPoint, distance);
      var blockDestroEnabled = MyAPIGateway.Session.SessionSettings.DestructibleBlocks;
      var queryType = blockDestroEnabled ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
      var hasWeapon = ToolDefinition != null && ToolDefinition.WeaponType != MyItemWeaponType.None;
      var allowGridCheck = hasWeapon && blockDestroEnabled && CanDamageGrid;

      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, queryType);
      entities.ShellSort(centerPoint);

      object tgt = null;
      List<BotBase> helpers;
      AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers);
      var ownerId = Owner?.IdentityId ?? BotIdentityId;
      var botMatrix = WorldMatrix;
      var muzzlePosition = botPosition + botMatrix.Up * 0.4; // close to the muzzle height

      _taskPrioritiesTemp.Clear();
      for (int i = 0; i < entities.Count; i++)
      {
        var ent = entities[i];
        if (ent == null || ent.MarkedForClose)
          continue;

        var tgtPosition = ent.PositionComp.WorldAABB.Center;

        var ch = ent as IMyCharacter;
        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == ownerCharacter.EntityId || ch.EntityId == Character.EntityId)
            continue;

          long ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;
          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot))
          {
            if (bot == null || bot.IsDead)
              continue;

            if (bot.BotInfo?.IsOnLadder == true)
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

            ownerIdentityId = bot.Owner?.IdentityId ?? bot.BotIdentityId;
          }
          else if (ch.IsPlayer)
          {
            if (ch.Parent is IMyShipController)
            {
              var p = MyAPIGateway.Players.GetPlayerControllingEntity(ch.Parent);
              if (p != null)
                ownerIdentityId = p.IdentityId;
            }

            IMyPlayer player;
            if (!AiSession.Instance.Players.TryGetValue(ownerIdentityId, out player) || player == null)
              continue;

            MyAdminSettingsEnum adminSettings;
            if (MyAPIGateway.Session.TryGetAdminSettings(player.SteamUserId, out adminSettings))
            {
              if ((adminSettings & MyAdminSettingsEnum.Untargetable) != 0)
              {
                continue;
              }
            }
          }

          var relation = MyIDModule.GetRelationPlayerPlayer(ownerId, ownerIdentityId, MyRelationsBetweenFactions.Neutral);
          if (relation != MyRelationsBetweenPlayers.Enemies)
          {
            continue;
          }

          if (onPatrol)
          {
            _taskPrioritiesTemp.Add(ent);
          }
          else
          {
            var ignoreEnts = new MyEntity[helpers?.Count + 1 ?? 2];
            ignoreEnts[0] = ent;

            if (helpers?.Count > 0)
            {
              for (int h = 0; h < helpers.Count; h++)
                ignoreEnts[h + 1] = (MyEntity)helpers[h].Character;
            }
            else
            {
              ignoreEnts[1] = (MyEntity)Character;
            }

            tgtPosition = ch.GetHeadMatrix(true).Translation;
            if (AiUtils.CheckLineOfSight(ref muzzlePosition, ref tgtPosition, cellList, resultList, _currentGraph?.RootVoxel, ignoreEnts))
              _taskPrioritiesTemp.Add(ent);
          }
        }
        else if (allowGridCheck)
        {
          var grid = ent as MyCubeGrid;
          if (grid?.Physics != null && !grid.IsPreview && !grid.MarkedForClose && !checkedGridIDs.Contains(grid.EntityId))
          {
            gridGroups.Clear();
            grid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroups);

            foreach (var g in gridGroups)
            {
              var myGrid = g as MyCubeGrid;
              if (myGrid == null || myGrid.IsPreview || myGrid.MarkedForClose)
                continue;

              foreach (var cpit in myGrid.OccupiedBlocks)
              {
                if (cpit.Pilot != null)
                  entities.Add(cpit.Pilot);
              }

              checkedGridIDs.Add(g.EntityId);
              long myGridOwner;
              try
              {
                myGridOwner = myGrid.BigOwners?.Count > 0 ? myGrid.BigOwners[0] : myGrid.SmallOwners?.Count > 0 ? myGrid.SmallOwners[0] : 0L;
              }
              catch
              {
                continue;
              }

              if (myGridOwner == 0)
                continue;

              var relation = MyIDModule.GetRelationPlayerPlayer(myGridOwner, ownerId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
              if (relation != MyRelationsBetweenPlayers.Enemies)
                continue;

              blockList.Clear();
              g.GetBlocks(blockList);

              for (int k = blockList.Count - 1; k >= 0; k--)
              {
                var block = blockList[k];
                if (block?.CubeGrid == null || block.IsDestroyed || block.CubeGrid.EntityId != g.EntityId)
                {
                  blockList.RemoveAtFast(k);
                  continue;
                }

                var fat = block.FatBlock;
                if (fat != null)
                {
                  tgtPosition = block.FatBlock.WorldAABB.Center;

                  if (fat is IMyAirtightHangarDoor)
                    tgtPosition += fat.WorldMatrix.Down * g.GridSize;
                }
                else
                {
                  block.ComputeWorldCenter(out tgtPosition);
                }

                cellList.Clear();
                g.RayCastCells(muzzlePosition, tgtPosition, cellList);

                var localEnd = g.WorldToGridInteger(tgtPosition);
                var endBlock = g.GetCubeBlock(localEnd);
                var allowedDistance = g.GridSizeEnum == MyCubeSize.Large ? 5 : 10;
                var line = new LineD(muzzlePosition, tgtPosition);
                bool add = true;

                foreach (var cell in cellList)
                {
                  var otherBlock = g.GetCubeBlock(cell);
                  if (otherBlock != null && cell != localEnd && otherBlock != endBlock)
                  {
                    var otherFat = otherBlock.FatBlock;
                    if (otherFat != null)
                    {
                      MyIntersectionResultLineTriangleEx? hit;
                      if (otherFat.GetIntersectionWithLine(ref line, out hit, IntersectionFlags.ALL_TRIANGLES) && hit.HasValue)
                      {
                        if (!hasWeapon || Vector3D.DistanceSquared(hit.Value.IntersectionPointInWorldSpace, tgtPosition) > allowedDistance * allowedDistance)
                        {
                          add = false;
                          break;
                        }
                      }
                    }
                    else if (!hasWeapon || Vector3D.DistanceSquared(grid.GridIntegerToWorld(cell), tgtPosition) > allowedDistance * allowedDistance)
                    {
                      add = false;
                      break;
                    }
                  }
                }

                if (!add)
                  blockList.RemoveAtFast(k);
              }

              blockList.ShellSort(botPosition);
              _taskPrioritiesTemp.AddRange(blockList);
            }
          }
        }
      }

      AiSession.Instance.SlimListPool?.Return(ref blockList);

      _taskPrioritiesTemp.PrioritySort(_taskPriorities, TargetPriorities, botPosition);
      bool damageToDisable = TargetPriorities.DamageToDisable;
      var huntDistanceSqd = distance * distance;

      foreach (var priKvp in _taskPriorities)
      {
        for (int j = 0; j < priKvp.Value.Count; j++)
        {
          var obj = priKvp.Value[j];
          var ch = obj as IMyCharacter;
          if (ch != null)
          {
            if (!ch.IsDead && !ch.MarkedForClose)
            {
              tgt = ch;
              break;
            }
          }
          else
          {
            var slim = obj as IMySlimBlock;
            if (slim != null && !slim.IsDestroyed)
            {
              var funcBlock = slim.FatBlock as IMyFunctionalBlock;
              if (damageToDisable && funcBlock != null && !funcBlock.IsFunctional)
                continue;
              else if (slim.FatBlock is IMyDoor && slim.IsBlockUnbuilt())
                continue;

              Vector3D slimWorld;
              if (slim.FatBlock != null)
                slimWorld = slim.FatBlock.GetPosition();
              else
                slim.ComputeWorldCenter(out slimWorld);

              if (Vector3D.DistanceSquared(botPosition, slimWorld) > huntDistanceSqd)
                continue;

              if (!GridBase.PointInsideVoxel(slimWorld, _currentGraph?.RootVoxel))
              {
                tgt = obj;
                break;
              }
            }
          }
        }

        if (tgt != null)
          break;
      }

      AiSession.Instance.HitListPool?.Return(ref hitList);
      AiSession.Instance.EntListPool?.Return(ref entities);
      AiSession.Instance.EntListPool?.Return(ref blockTargets);
      AiSession.Instance.GridGroupListPool?.Return(ref gridGroups);
      AiSession.Instance.GridCheckHashPool?.Return(ref checkedGridIDs);
      AiSession.Instance.OverlapResultListPool?.Return(ref resultList);
      AiSession.Instance.LineListPool?.Return(ref cellList);

      if (tgt == null)
      {
        if (onPatrol)
        {
          if (Target.Entity != null)
            Target.RemoveTarget();

          if (Target.Override.HasValue)
            return;

          _patrolWaitTime--;

          if (_patrolWaitTime <= 0)
          {
            var patrolPoint = GetNextPatrolPoint();

            if (patrolPoint.HasValue)
            {
              var patrolPointWorld = _currentGraph.LocalToWorld(patrolPoint.Value);
              Target.SetOverride(patrolPointWorld);
            }
          }

          return;
        }
        else
        {
          tgt = ownerCharacter;
        }
      }

      if (onPatrol && Target.Override.HasValue)
      {
        _patrolIndex = Math.Max((short)-1, (short)(_patrolIndex - 1));
        Target.RemoveOverride(false);
      }

      var tgtChar = tgt as IMyCharacter;
      var parent = (tgtChar != null && tgtChar.Parent != null) ? tgtChar.Parent : tgt;
      if (ReferenceEquals(Target.Entity, parent))
        return;

      Target.SetTarget(null, parent);
      CleanPath();
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack, shouldFire;

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, out shouldFire, distanceCheck);
      CheckBlockTarget(ref isTgt, ref shouldAttack, ref movement, ref rotation, ref distanceCheck);
      CheckFire(shouldFire, shouldAttack, ref movement, ref rotation, ref roll);
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
      CheckFire(shouldFire, shouldAttack, ref movement, ref rotation, ref roll);
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

      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(botMatrix));
      var tgtFriendly = Target.IsFriendly();
      var isFriendly = isTarget && tgtFriendly;

      if (isFriendly && (FollowMode || Owner != null) && Target.Override.HasValue && _currentGraph.GetBlockAtPosition(Target.Override.Value)?.FatBlock is IMyCockpit)
      {
        isFriendly = false;
      }

      var flatDistanceCheck = isFriendly ? _followDistanceSqd : distanceCheck;
      var hasWeapon = HasWeaponOrTool && Character.EquippedTool != null && !(Character.EquippedTool is IMyAngleGrinder) && !(Character.EquippedTool is IMyWelder);

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
      var isOwnerTgt = Target.Player?.IdentityId == Owner.IdentityId;

      var gotoPosition = Target.CurrentGoToPosition;
      var actualPosition = Target.CurrentActualPosition;

      var maxProjectileDistance = AiSession.Instance.ModSaveData.MaxBotProjectileDistance * 0.9;
      double distanceSqdToTarget = (HasWeaponOrTool && Target.Entity != null) ? Vector3D.DistanceSquared(actualPosition, botPosition) : double.MaxValue;
      double distanceToCheck = maxProjectileDistance * maxProjectileDistance;

      if (distanceSqdToTarget <= distanceToCheck)
      {
        if (HasLineOfSight)
        {
          rifleAttack = !tgtFriendly;

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

      if (_currentGraph != null)
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

            bool checkFist2 = !isOwnerTgt && !HasWeaponOrTool && isTarget;
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

      if (!rifleAttack && !tgtFriendly && hasWeapon && Target.Entity != null && !_sideNode.HasValue)
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

          if (_tooCloseUseSideNode || (relToTarget.Z < 0 && Math.Abs(angle) < _twoDegToRads))
          {
            rotation = Vector2.Zero;
          }
          else
          {
            if (rifleAttack && Target.Entity is IMyLargeTurretBase && Math.Abs(angle) > AiUtils.PiOver3)
              rifleAttack = false;

            rotation = new Vector2(0, (float)angle * Math.Sign(relToTarget.X) * 75);
          }

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

          if (!JetpackEnabled && !IsWolf && Owner?.Character != null && Target.Player?.IdentityId == Owner.IdentityId)
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
                var botRunning = BotInfo.IsRunning;
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

      bool checkFist = !isOwnerTgt && !HasWeaponOrTool && isTarget;
      bool aligned = angleTwoOrLess && distanceSqd <= distanceCheck;

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
      else if (!fistAttack && isTarget && !isOwnerTgt && !HasWeaponOrTool && angleTwoOrLess && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
        movement = Vector3.Forward * 0.5f;

      if (JetpackEnabled)
      {
        var vecToTgt = actualPosition - botPosition;
        var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(botMatrix));
        var flatToTarget = new Vector3D(relToTarget.X, 0, relToTarget.Z);
        if (flatToTarget.LengthSquared() <= flatDistanceCheck && Math.Abs(relToTarget.Y) > 0.5)
        {
          movement = Vector3.Zero;
          relVectorBot = relToTarget;
        }

        if (Math.Abs(relVectorBot.Y) > 0.05)
          AdjustMovementForFlight(ref relVectorBot, ref movement, ref botPosition);
      }
    }

    internal virtual void CheckFire(bool shouldFire, bool shouldAttack, ref Vector3 movement, ref Vector2 rotation, ref float roll)
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

    bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
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
            var packet = new WeaponFirePacket(Character.EntityId, topMost.EntityId, 10, _shotAngleDeviationTan, _randoms, TicksBetweenProjectiles, ammoCount, false, false, leadTargets, slim?.Position);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.StartWeaponFire(Character.EntityId, topMost.EntityId, 10, _shotAngleDeviationTan, _randoms, TicksBetweenProjectiles, ammoCount, false, false, leadTargets, slim?.Position);
        }

        _firePacketSent = true;
        _ticksSinceFirePacket = 0;
      }

      return true;
    }

    internal bool IsWithinSearchRadius(Vector3D worldPosition, ref float lastRadius, List<MyOrientedBoundingBoxD> patrolOBBs)
    {
      var radius = AiSession.Instance.PlayerToRepairRadius[Owner.IdentityId];

      if (PatrolMode && _patrolList.Count > 0)
      {
        if (_patrolList.Count == 1)
        {
          var worldPoint = _currentGraph.LocalToWorld(_patrolList[0]);
          var sphere = new BoundingSphereD(worldPoint, radius);
          return sphere.Contains(worldPosition) == ContainmentType.Contains;
        }
        else
        {
          if (radius != lastRadius)
          {
            UpdatePatrolOBBCache(ref lastRadius, patrolOBBs);
          }

          for (int i = 0; i < patrolOBBs.Count; i++)
          {
            if (patrolOBBs[i].Contains(ref worldPosition))
              return true;
          }

          return false;
        }
      }
      else
      {
        var sphere = new BoundingSphereD(BotInfo.CurrentBotPositionActual, radius);
        return sphere.Contains(worldPosition) == ContainmentType.Contains;
      }
    }

    internal void UpdatePatrolOBBCache(ref float lastRadius, List<MyOrientedBoundingBoxD> patrolOBBs)
    {
      patrolOBBs.Clear();
      var radius = AiSession.Instance.PlayerToRepairRadius[Owner.IdentityId];
      lastRadius = radius;

      for (int i = 0; i < _patrolList.Count; i++)
      {
        var curIdx = i;
        var nexIdx = curIdx + 1;
        if (nexIdx >= _patrolList.Count)
          break;

        var currPoint = _currentGraph.LocalToWorld(_patrolList[curIdx]);
        var nextPoint = _currentGraph.LocalToWorld(_patrolList[nexIdx]);

        var center = (currPoint + nextPoint) * 0.5;
        var vector = nextPoint - currPoint;
        var length = vector.Normalize() + radius;
        var up = Vector3D.CalculatePerpendicularVector(vector);
        var matrix = MatrixD.CreateWorld(center, vector, up);

        var halfExt = new Vector3D(radius, radius, length) * 0.5;
        var obb = new MyOrientedBoundingBoxD(center, halfExt, Quaternion.CreateFromRotationMatrix(matrix));

        patrolOBBs.Add(obb);
      }
    }
  }
}