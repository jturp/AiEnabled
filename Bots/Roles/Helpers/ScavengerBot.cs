using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Utilities;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots.Roles.Helpers
{
  public class ScavengerBot : BotBase
  {
    bool _moveFromLadder;

    public ScavengerBot(IMyCharacter bot, GridBase gridBase, long ownerId) : base(bot, 10, 15, gridBase)
    {
      Owner = AiSession.Instance.Players[ownerId];
      Behavior = new ScavengerBehavior(bot);
      bool hasOwner = Owner != null;

      _followDistanceSqd = 25;
      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      RequiresJetpack = bot.Definition.Id.SubtypeName == "Drone_Bot";
      CanUseSpaceNodes = RequiresJetpack || hasOwner;
      CanUseAirNodes = RequiresJetpack || hasOwner;
      GroundNodesFirst = !RequiresJetpack;
      EnableDespawnTimer = !hasOwner;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = false;
      CanUseLadders = false;
      WantsTarget = true;

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

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      bool shouldAttack;
      TrySwitchWalk();

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out shouldAttack, distanceCheck);

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

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
      bool shouldAttack;
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out shouldAttack);

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

      Character.MoveAndRotate(movement, rotation, 0f);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out bool shouldAttack, double distanceCheck = 4)
    {
      var botPosition = Position;
      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(WorldMatrix));
      var isFriendly = isTarget && Target.IsFriendly();
      var flatDistanceCheck = isFriendly ? _followDistanceSqd : distanceCheck;

      if (_botState.IsOnLadder)
      {
        movement = relVectorBot.Y > 0 ? Vector3.Forward : Vector3.Backward;
        rotation = Vector2.Zero;
        shouldAttack = false;
        return;
      }

      var flattenedVector = new Vector3D(relVectorBot.X, 0, relVectorBot.Z);
      var flattenedLengthSquared = flattenedVector.LengthSquared();
      var distanceSqd = relVectorBot.LengthSquared();
      var isOwnerTgt = Target.Player?.IdentityId == Owner.IdentityId;

      Vector3D gotoPosition, actualPosition;
      Target.GetTargetPosition(out gotoPosition, out actualPosition);

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

            shouldAttack = !isOwnerTgt && !HasWeaponOrTool && isTarget && angleTwoOrLess && distanceSqd <= distanceCheck;
            return;
          }
        }
      }

      if (PathFinderActive)
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

      bool validTarget = isTarget && !isOwnerTgt && angleTwoOrLess;
      shouldAttack = validTarget && distanceSqd <= distanceCheck;

      if (!shouldAttack && validTarget && Vector3.IsZero(movement) && Vector2.IsZero(ref rotation))
        movement = Vector3.Forward * 0.5f;
    }

    public override void SetTarget()
    {
      var character = Owner?.Character;
      if (character == null || !WantsTarget)
      {
        return;
      }

      if (Target.IsDestroyed())
      {
        Target.RemoveTarget();
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

      var ownerPos = character.WorldAABB.Center;
      var ownerHeadPos = character.GetHeadMatrix(true).Translation;
      var botHeadPos = Character.GetHeadMatrix(true).Translation;
      var sphere = new BoundingSphereD(ownerPos, 75);
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Dynamic);
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
        if (ch != null)
        {
          if (ch.IsDead || ch.MarkedForClose || ch.EntityId == character.EntityId || ch.EntityId == Character.EntityId)
            continue;

          BotBase bot;
          if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot) && bot?._botState?.IsOnLadder == true)
            continue;

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

      AiSession.Instance.HitListStack.Push(hitList);
      AiSession.Instance.EntListStack.Push(entities);

      var parent = tgt is IMyCharacter ? tgt.GetTopMostParent() : tgt;
      var currentTgt = Target.Entity as IMyEntity;
      if (currentTgt?.EntityId == parent.EntityId)
        return;

      Target.SetTarget(parent);
      _pathCollection?.CleanUp(true);
    }
  }
}
