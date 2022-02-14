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

      _deathSound = new MySoundPair("PlayVocDeath");
      _deathSoundString = "PlayVocDeath";
      _blockDamagePerSecond = 50;
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
      WantsTarget = false;

      if (!AiSession.Instance.SoundListStack.TryPop(out _attackSounds))
        _attackSounds = new List<MySoundPair>();
      else
        _attackSounds.Clear();

      if (!AiSession.Instance.StringListStack.TryPop(out _attackSoundStrings))
        _attackSoundStrings = new List<string>();
      else
        _attackSoundStrings.Clear();
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool shouldAttack;

      if (_shouldMove)
      {
        GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out shouldAttack, distanceCheck);
      }
      else
      {
        movement = Vector3.Zero;
        rotation = Vector2.Zero;
        roll = 0;
        shouldAttack = false;
      }

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

      MoveToPoint(movement, rotation, roll);
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

      if (jpEnabled)
      {
        var deviationAngle = MathHelper.PiOver2 - VectorUtils.GetAngleBetween(graphUpVector, botMatrix.Left);
        var botdotUp = botMatrix.Up.Dot(graphMatrix.Up);

        if (botdotUp < 0 || Math.Abs(deviationAngle) > _twoDegToRads)
        {
          var botLeftDotUp = -botMatrix.Left.Dot(graphUpVector);

          if (botdotUp < 0)
            roll = MathHelper.Pi * Math.Sign(botLeftDotUp);
          else
            roll = (float)deviationAngle * Math.Sign(botLeftDotUp);
        }
      }

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

        if (jpEnabled && Math.Abs(roll) < MathHelper.ToRadians(5))
        {
          var angleFwd = MathHelperD.PiOver2 - VectorUtils.GetAngleBetween(botMatrix.Forward, graphUpVector);
          var botDotUp = botMatrix.Up.Dot(graphMatrix.Up);

          if (botDotUp < 0 || Math.Abs(angleFwd) > _twoDegToRads)
          {
            var botFwdDotUp = botMatrix.Forward.Dot(graphMatrix.Up);

            if (botDotUp < 0)
              xRot = -MathHelper.Pi * Math.Sign(botFwdDotUp);
            else
              xRot = (float)angleFwd * Math.Sign(botFwdDotUp);
          }
        }

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
          if (CheckGraphNeeded && _currentGraph?.Ready == true)
          {
            var position = GetPosition();
            StartCheckGraph(ref position);
            return;
          }

          SimulateIdleMovement(true);

          if (_botState.IsRunning && Character.Definition.Id.SubtypeName.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) < 0)
            Character.SwitchWalk();
        }

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

      if (_shouldMove)
      {
        GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out roll, out shouldAttack);
      }
      else
      {
        movement = Vector3.Zero;
        rotation = Vector2.Zero;
        roll = 0;
        shouldAttack = false;
      }

      if (shouldAttack)
      {
        movement = Vector3.Zero;
        _stuckTimer = 0;
        _ticksSinceFoundTarget = 0;
        Attack();
      }

      Character.MoveAndRotate(movement, rotation, roll);
    }

    public void SetHostile(long attackerId)
    {
      var attacker = MyEntities.GetEntityById(attackerId) as IMyCharacter;
      if (attacker != null && Target.Entity == null)
      {
        Target.SetTarget(attacker);
        Behavior.Perform("Whatever");
        _performanceTimerTarget = 0;

        if (!_botState.IsRunning)
          Character.SwitchWalk();
      }
    }

    public void SetHostile(IMyCharacter attacker)
    {
      if (attacker != null && Target.Entity == null)
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
      DamagePending = true;

      Character.TriggerCharacterAnimationEvent("emote", true);
      Character.TriggerCharacterAnimationEvent("Police_Bot_Attack", true);
      PlaySound();
    }
  }
}
