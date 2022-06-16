using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace AiEnabled.Bots.Roles.Helpers
{
  public class ScavengerBot : BotBase
  {
    bool _moveFromLadder;
    bool _performing;
    bool _sitting;
    bool _awaitItem;
    int _performTimer;

    public ScavengerBot(IMyCharacter bot, GridBase gridBase, long ownerId) : base(bot, 10, 15, gridBase)
    {
      BotType = AiSession.BotType.Scavenger;
      Owner = AiSession.Instance.Players[ownerId];
      Behavior = new ScavengerBehavior(bot);

      _followDistanceSqd = 25;
      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      bool hasOwner = Owner != null;
      var jetRequired = bot.Definition.Id.SubtypeName == "Drone_Bot";
      var jetAllowed = jetRequired || hasOwner || AiSession.Instance.ModSaveData.AllowEnemiesToFly;

      RequiresJetpack = jetRequired;
      CanUseSpaceNodes = jetAllowed;
      CanUseAirNodes = jetAllowed;
      GroundNodesFirst = !jetRequired;
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

      var jetpack = bot.Components.Get<MyCharacterJetpackComponent>();
      if (jetpack != null)
      {
        if (RequiresJetpack)
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
        {
          jetpack.SwitchThrusts();
        }
      }
    }

    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (_performing)
      {
        _performTimer++;

        if (_performTimer > 240)
        {
          if (_awaitItem)
          {
            var items = AiSession.Instance.ScavengerItemList;
            var rand = MyUtils.GetRandomInt(0, items.Count);
            var id = items[rand];

            var item = new MyPhysicalInventoryItem()
            {
              Amount = 1,
              Content = MyObjectBuilderSerializer.CreateNewObject(id) as MyObjectBuilder_PhysicalObject
            };

            var matrix = WorldMatrix;
            matrix.Translation += matrix.Up + GetTravelDirection() * 1.5;

            MyFloatingObjects.Spawn(item, matrix, Character.Physics, null);
            _awaitItem = false;

            if (Owner?.SteamUserId > 0)
            {
              var pkt = new MessagePacket($"[{Character.Name}] has found something!");
              AiSession.Instance.Network.SendToPlayer(pkt, Owner.SteamUserId);
            }

            Behavior.Perform("RoboDog_Spin");
            Behavior.Speak();
            _performTimer = 60;
          }
          else
          {
            _performing = false;
          }
        }
      }

      return true;
    }

    internal override void UseBehavior()
    {
      if (Target.Entity != null && Target.GetDistanceSquared() < 2500 && !Target.IsFriendly())
      {
        if (AiSession.Instance?.GlobalSpeakTimer > 1000)
        {
          AiSession.Instance.GlobalSpeakTimer = 0;
          Behavior?.Speak();
        }
      }
      else if (Owner?.Character != null && Vector3D.DistanceSquared(Owner.Character.WorldAABB.Center, GetPosition()) < 2500)
      {
        _performTimer = 0;
        var rand = MyUtils.GetRandomInt(0, 100);

        if (rand < 40)
        {
          // sit and pant
          Behavior.Speak("RoboDogPant001");

          if (!_sitting && Character.LastMotionIndicator == Vector3.Zero && Character.LastRotationIndicator == Vector3.Zero)
          {
            Behavior.Perform("RoboDog_Sitting");
            _sitting = true;
            _performing = true;
          }
        }
        else
        {
          // dig, sniff and find something

          if (_sitting)
          {
            _sitting = false;
            Behavior.Perform("RoboDog_Sitting");
          }

          Behavior.Speak("RoboDogSniff001");
          Behavior.Perform("RoboDog_Digging");

          rand = MyUtils.GetRandomInt(1, 101);
          _awaitItem = rand > 30;
          _performing = true;
        }
      }
    }

    internal override void DoDamage(float amount = 0)
    {
      IMyDestroyableObject destroyable;
      var cube = Target.Entity as IMyCubeBlock;
      if (cube != null)
      {
        destroyable = cube.SlimBlock;
        PlaySoundServer("ImpMetalMetalCat3", cube.EntityId);
      }
      else
      {
        destroyable = Target.Entity as IMyDestroyableObject;
      }

      if (destroyable == null || !destroyable.UseDamageSystem || destroyable.Integrity <= 0)
        return;

      var character = Target.Entity as IMyCharacter;
      bool isCharacter = character != null;

      var rand = amount > 0 ? amount : isCharacter ? MyUtils.GetRandomFloat(_minDamage, _maxDamage) : _blockDamagePerAttack;
      if (isCharacter && amount == 0 && Owner != null && !AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId))
        rand *= 4;

      destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);

      if (!isCharacter)
        return;

      BotBase botTarget;
      if (AiSession.Instance.Bots.TryGetValue(character.EntityId, out botTarget) && botTarget != null)
      {
        botTarget._ticksSinceFoundTarget = 0;

        if (Owner != null)
        {
          HealthInfoStat infoStat;
          if (!AiSession.Instance.PlayerToHealthBars.TryGetValue(Owner.IdentityId, out infoStat))
          {
            infoStat = new HealthInfoStat();
            AiSession.Instance.PlayerToHealthBars[Owner.IdentityId] = infoStat;
          }

          infoStat.BotEntityIds.Add(character.EntityId);
        }
      }
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack;

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, distanceCheck);

      if (shouldAttack || _performing)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;

        if (_performing)
        {
          rotation = Vector2.Zero;
          roll = 0;
        }
        else if (shouldAttack)
        {
          Attack();
        }
      }
      else if (movement != Vector3.Zero)
      {
        if (_sitting)
        {
          _sitting = false;
          Behavior.Perform("RoboDog_Sitting");
        }

        TrySwitchWalk();
      }

      MoveToPoint(movement, rotation, roll);
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

      if (shouldAttack || _performing)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;

        if (_performing)
        {
          rotation = Vector2.Zero;
          roll = 0;
        }
        else if (shouldAttack)
        {
          Attack();
        }
      }

      Character.MoveAndRotate(movement, rotation, roll);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out float roll, out bool shouldAttack, double distanceCheck = 4)
    {
      roll = 0;
      var botPosition = GetPosition();
      var botMatrix = WorldMatrix;
      var graphMatrix = _currentGraph.WorldMatrix;
      var graphUpVector = graphMatrix.Up;
      var jpEnabled = JetpackEnabled;

      var vecToWP = waypoint - botPosition;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(botMatrix));
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
            var distanceToTarget = Vector3D.DistanceSquared(Target.CurrentGoToPosition, botPosition);
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

      if (JetpackEnabled)
      {
        var vecToTgt = Target.CurrentActualPosition - botPosition;
        var relToTarget = Vector3D.TransformNormal(vecToTgt, MatrixD.Transpose(botMatrix));
        var flatToTarget = new Vector3D(relToTarget.X, 0, relToTarget.Z);
        if (flatToTarget.LengthSquared() <= flatDistanceCheck && Math.Abs(relToTarget.Y) > 0.5)
        {
          movement = Vector3.Zero;
          relVectorBot = relToTarget;
        }

        if (Math.Abs(relVectorBot.Y) > 0.05)
        {
          bool towardBlock = isTarget && Target.IsSlimBlock;
          AdjustMovementForFlight(ref relVectorBot, ref movement, ref botPosition, towardBlock);
        }
      }
    }

    public override void SetTarget()
    {
      var character = Owner?.Character;
      if (character == null || !WantsTarget)
      {
        return;
      }

      if (FollowMode)
      {
        var ownerParent = character.GetTopMostParent();
        var currentEnt = Target.Entity as IMyEntity;

        if (currentEnt?.EntityId != ownerParent.EntityId)
        {
          Target.SetTarget(ownerParent);
          _pathCollection?.CleanUp(true);
        }

        return;
      }

      var botPosition = GetPosition();
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

      var onPatrol = PatrolMode && _patrolList?.Count > 0;
      var ownerPos = character.WorldAABB.Center;
      var ownerHeadPos = character.GetHeadMatrix(true).Translation;
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
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entities, MyEntityQueryType.Dynamic);
      entities.ShellSort(centerPoint);
      IMyEntity tgt = null;

      List<BotBase> helpers;
      AiSession.Instance.PlayerToHelperDict.TryGetValue(Owner.IdentityId, out helpers);
      var ownerId = Owner?.IdentityId ?? Character.ControllerInfo.ControllingIdentityId;

      for (int i = 0; i < entities.Count; i++)
      {
        var ent = entities[i];
        if (ent?.MarkedForClose != false)
          continue;

        var ch = ent as IMyCharacter;
        if (ch == null || ch.IsDead || ch.MarkedForClose || ch.EntityId == character.EntityId || ch.EntityId == Character.EntityId)
          continue;

        long ownerIdentityId = ch.ControllerInfo.ControllingIdentityId;
        BotBase bot;
        if (AiSession.Instance.Bots.TryGetValue(ch.EntityId, out bot))
        {
          if (bot == null || bot.IsDead)
            continue;

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

          if (bot.Owner != null)
            ownerIdentityId = bot.Owner.IdentityId;
        }
        else if (ch.IsPlayer)
        {
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

        var worldHeadPos = ch.GetHeadMatrix(true).Translation;

        if (!onPatrol)
          MyAPIGateway.Physics.CastRay(ownerHeadPos, worldHeadPos, hitList, CollisionLayers.CharacterCollisionLayer);

        if (onPatrol || hitList.Count > 0)
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

        tgt = ent;
        break;
      }

      hitList.Clear();
      entities.Clear();

      AiSession.Instance.HitListStack.Push(hitList);
      AiSession.Instance.EntListStack.Push(entities);

      if (tgt == null)
      {
        if (onPatrol)
        {
          if (Target.Entity != null)
            Target.RemoveTarget();

          if (Target.Override.HasValue)
            return;

          var patrolPoint = GetNextPatrolPoint();

          if (patrolPoint.HasValue)
          {
            Target.SetOverride(patrolPoint.Value);
          }

          return;
        }
        else
        {
          tgt = character;
        }
      }

      if (onPatrol && Target.Override.HasValue)
      {
        _patrolIndex = Math.Max((short)-1, (short)(_patrolIndex - 1));
        Target.RemoveOverride(false);
      }

      var parent = tgt is IMyCharacter ? tgt.GetTopMostParent() : tgt;
      var currentTgt = Target.Entity as IMyEntity;
      if (currentTgt?.EntityId == parent.EntityId)
        return;

      Target.SetTarget(parent);
      _pathCollection?.CleanUp(true);
    }
  }
}
