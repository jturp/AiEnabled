﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Particles;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class ZombieBot : BotBase
  {
    EnemyParticleInfo _particleInfo;
    MyConsumableItemDefinition _consumable;

    public ZombieBot(IMyCharacter bot, GridBase gridBase) : base(bot, 1, 1, gridBase)
    {
      Behavior = new ZombieBehavior(this);

      _ticksBeforeDamage = 63;
      _ticksBetweenAttacks = 200;
      _blockDamagePerSecond = 150;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);
      _deathSound = new MySoundPair("ZombieDeath");
      _deathSoundString = "ZombieDeath";

      var jetRequired = bot.Definition.Id.SubtypeName == "Drone_Bot";

      RequiresJetpack = jetRequired;
      CanUseSpaceNodes = jetRequired;
      CanUseAirNodes = jetRequired;
      GroundNodesFirst = !jetRequired;
      EnableDespawnTimer = true;
      CanUseWaterNodes = true;
      WaterNodesOnly = false;
      CanUseSeats = true;
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

      _attackSounds.Add(new MySoundPair("ZombieAttack001"));
      _attackSounds.Add(new MySoundPair("ZombieAttack002"));
      _attackSounds.Add(new MySoundPair("ZombieAttack003"));
      _attackSounds.Add(new MySoundPair("ZombieAttack004"));
      _attackSoundStrings.Add("ZombieAttack001");
      _attackSoundStrings.Add("ZombieAttack002");
      _attackSoundStrings.Add("ZombieAttack003");
      _attackSoundStrings.Add("ZombieAttack004");

      _consumable = new MyConsumableItemDefinition
      {
        Stats = new List<MyConsumableItemDefinition.StatValue>() { new MyConsumableItemDefinition.StatValue("Health", -0.01f, 5) }
      };
    }

    internal override void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        if (!BugZapped && _particleInfo?.Bot != null)
        {
          var packet = new ParticlePacket(_particleInfo.Bot.EntityId, _particleInfo.Type, remove: true);

          if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
            AiSession.Instance.Network.RelayToClients(packet);

          if (MyAPIGateway.Session.Player != null)
            packet.Received(AiSession.Instance.Network);
        }

        _consumable = null;
        _particleInfo?.Close();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in ZombieBot.CleanUp: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        base.CleanUp(cleanConfig, removeBot);
      }
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack;

      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, distanceCheck);

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
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

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

      Character.MoveAndRotate(movement, rotation, 0f);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out float roll, out bool fistAttack, double distanceCheck = 4)
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

    internal override void DoDamage(float amount = 0)
    {
      IMyDestroyableObject destroyable;
      var cube = Target.Entity as IMyCubeBlock;
      if (cube != null)
      {
        destroyable = cube.SlimBlock;
        amount = _blockDamagePerAttack;
      }
      else
      {
        destroyable = Target.Entity as IMyDestroyableObject;
      }

      if (destroyable == null || !destroyable.UseDamageSystem || destroyable.Integrity <= 0)
        return;

      var character = destroyable as IMyCharacter;
      bool isCharacter = character != null;
      var rand = amount > 0 ? amount : isCharacter ? MyUtils.GetRandomFloat(_minDamage, _maxDamage) : _blockDamagePerAttack;

      BotBase botTarget = null;

      if (cube != null || (isCharacter && AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId)))
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }
      else if (isCharacter && AiSession.Instance.Bots.TryGetValue(character.EntityId, out botTarget) && botTarget?.Owner != null)
      {
        rand *= AiSession.Instance.ModSaveData.BotWeaponDamageModifier;
      }

      if (!isCharacter)
      {
        PlaySoundServer("ImpMetalMetalCat3", cube.EntityId);
        destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);
      }
      else
      {
        var statComp = character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
        var health = statComp?.Health.Value;

        base.DoDamage(rand);
        if (health == statComp?.Health.Value)
          return;

        if (Target.Player != null)
          PlaySoundServer("PlayVocPain", character.EntityId);

        AddDamageOverTime(statComp);

        var nomad = botTarget as NomadBot;
        if (nomad != null && nomad.Target.Entity == null)
        {
          nomad.SetHostile(Character);
        }
      }
    }

    internal void AddDamageOverTime(MyCharacterStatComponent statComp)
    {
      if (_consumable == null || statComp == null)
        return;

      //if (_consumable.Stats?.Count > 0)
      //{
      //  var value = 0.01f * AiSession.Instance.ModSaveData.BotDamageModifier;
      //  var stat = _consumable.Stats[0];
      //  if (stat.Value != value)
      //  {
      //    stat.Value = value;
      //    _consumable.Stats[0] = stat;
      //  }
      //}

      statComp.Consume(1, _consumable);

      var targetId = Target.Player?.SteamUserId;
      if (targetId.HasValue)
      {
        if (targetId == MyAPIGateway.Multiplayer.MyId)
          AiSession.Instance.ShowMessage("You have been poisoned!");
        else if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
        {
          var packet = new MessagePacket("You have been poisoned!");
          AiSession.Instance.Network.SendToPlayer(packet, targetId.Value);
        }
      }
    }

    internal override bool Update()
    {
      if (BugZapped)
        return false;

      if (_particleInfo == null)
      {
        if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
        {
          var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Zombie);
          AiSession.Instance.Network.RelayToClients(packet);
        }

        _particleInfo = new EnemyParticleInfo(Character, "ZombieGas");
      }

      if (MyAPIGateway.Session.Player != null)
        _particleInfo.Update();
  
      return base.Update();
    }
  }
}
