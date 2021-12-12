using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Support;
using AiEnabled.Utilities;

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
  public class BruiserBot : BotBase
  {
    public BruiserBot(IMyCharacter bot, GridBase gridBase) : base(bot, 15, 25, gridBase)
    {
      Behavior = new EnemyBehavior(bot);

      _ticksBeforeDamage = 35;
      _ticksBetweenAttacks = 400;
      _blockDamagePerSecond = 360;
      _blockDamagePerAttack = _blockDamagePerSecond * 0.25f * (_ticksBetweenAttacks / 60f); // 4-punch combo, so divide by 4 per attack

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
        new MySoundPair("DroneLoopMedium")
      };

      _attackSoundStrings = new List<string>
      {
        "DroneLoopMedium"
      };
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
      else
      {
        TrySwitchWalk();
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

      if (_usePathFinder)
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
      else
      {
        TrySwitchWalk();
      }

      Character.MoveAndRotate(movement, rotation, 0f);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out bool fistAttack, double distanceCheck = 4)
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

            fistAttack = isTarget && distanceSqd <= distanceCheck && angleTwoOrLess;
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

    internal override void Attack()
    {
      if (_ticksSinceLastAttack < _ticksBetweenAttacks)
        return;

      _ticksSinceLastAttack = 0;
      _damageTicks = 0;
      _damagePending = true;

      Character.TriggerCharacterAnimationEvent("emote", true);
      Character.TriggerCharacterAnimationEvent("QuadPunch", true);
      PlaySound();
    }

    internal override void UpdateDamagePending()
    {
      ++_damageTicks;

      if (_damageTicks == 65 || _damageTicks == 79 || _damageTicks == 93 || _damageTicks == 107)
      {
        var damage = MyUtils.GetRandomFloat(_minDamage, _maxDamage) * 0.2f;
        DoDamage(damage);
      }

      if (_damageTicks >= 107)
        _damagePending = false;
    }

    internal override void DoDamage(float amount = 0)
    {
      IMyDestroyableObject destroyable;
      var cube = Target.Entity as IMyCubeBlock;
      if (cube != null)
      {
        destroyable = cube.SlimBlock;
      }
      else
      {
        destroyable = Target.Entity as IMyDestroyableObject;
      }

      if (destroyable == null || !destroyable.UseDamageSystem || destroyable.Integrity <= 0)
        return;

      var character = destroyable as IMyCharacter;
      if (character == null)
      {
        PlaySoundServer("ImpMetalMetalCat3", cube.EntityId);
        destroyable.DoDamage(_blockDamagePerAttack, MyStringHash.GetOrCompute("Punch"), true);
      }
      else
      {
        var statComp = character.Components.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
        var health = statComp?.Health.Value;

        base.DoDamage(amount);
        if (health == statComp?.Health.Value)
          return;

        if (Target.Player != null)
          PlaySoundServer("PlayVocPain", character.EntityId);
      }
    }
  }
}
