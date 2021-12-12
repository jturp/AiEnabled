using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

using VRageMath;

namespace AiEnabled.Bots.Roles
{
  public class NomadBot : BotBase
  {
    uint _performanceTimer = 500;
    uint _performanceTimerTarget = 500;
    bool _shouldMove = true;

    public NomadBot(IMyCharacter bot, GridBase gridBase) : base(bot, 7, 15, gridBase)
    {
      Behavior = new NeutralBehavior(bot);

      _wantsTarget = false;
      _deathSound = new MySoundPair("PlayVocDeath");
      _deathSoundString = "PlayVocDeath";
      _blockDamagePerSecond = 50;
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

    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      bool shouldAttack;

      if (_shouldMove)
      {
        GetMovementAndRotation(isTgt, point, out movement, out rotation, out shouldAttack, distanceCheck);
      }
      else
      {
        movement = Vector3.Zero;
        rotation = Vector2.Zero;
        shouldAttack = false;
      }

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

      MoveToPoint(movement, rotation);
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

    internal override bool Update()
    {
      _performanceTimer++;
      _shouldMove = _performanceTimer > _performanceTimerTarget;

      return base.Update();
    }

    internal override void MoveToTarget()
    {
      //MyAPIGateway.Utilities.ShowNotification($"BotState = {_botState.IsRunning}, MoveState = {Character.CurrentMovementState}", 16);
      if (!IsInRangeOfTarget())
      {
        if (!UseAPITargets)
        {
          if (_checkGraph && _currentGraph?.Ready == true)
          {
            var position = Position;
            StartCheckGraph(ref position);
            return;
          }

          SimulateIdleMovement(true);

          if (_botState.IsRunning)
            Character.SwitchWalk();
        }

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

      if (_shouldMove)
      {
        GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out shouldAttack);
      }
      else
      {
        movement = Vector3.Zero;
        rotation = Vector2.Zero;
        shouldAttack = false;
      }

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

      Character.MoveAndRotate(movement, rotation, 0f);
    }

    public void SetHostile(long attackerId)
    {
      var attacker = MyEntities.GetEntityById(attackerId) as IMyCharacter;
      if (attacker != null)
      {
        Target.SetTarget(attacker);
        Behavior.Perform("Whatever");
        _performanceTimerTarget = 0;

        if (!_botState.IsRunning)
          Character.SwitchWalk();
      }
    }

    internal override void UseBehavior()
    {
      if (!Target.HasTarget)
      {
        _performanceTimer = 0;
        Behavior.Perform();

        var lastAction = Behavior.LastAction;
        if (!string.IsNullOrWhiteSpace(lastAction))
        {
          if (lastAction.StartsWith("Dance"))
          {
            _performanceTimerTarget = 600;
          }
          else if (lastAction.StartsWith("Bed"))
          {
            _performanceTimerTarget = 1000;
          }
          else
          {
            _performanceTimerTarget = 500;
          }
        }
      }
    }

    internal override void Attack()
    {
      if (_ticksSinceLastAttack < _ticksBetweenAttacks)
        return;

      _ticksSinceLastAttack = 0;
      _damageTicks = 0;
      _damagePending = true;

      Character.TriggerCharacterAnimationEvent("emote", true);
      Character.TriggerCharacterAnimationEvent("Police_Bot_Attack", true);
      PlaySound();
    }
  }
}
