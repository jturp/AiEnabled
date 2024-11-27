using AiEnabled.Bots.Roles.Helpers;

using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;

using VRageMath;

namespace AiEnabled.Bots
{
  public class BotState
  {
    [Flags]
    enum State : byte
    {
      None = 0,
      IsOnLadder = 1,
      WasOnLadder = 2,
      GoingDownLadder = 4,
      IsRunning = 8,
      IsCrouching = 16,
      IsFalling = 32,
      IsFlying = 64,
      IsJumping = 128
    }

    public Vector3D CurrentBotPositionAtFeet { get;private set; }
    public Vector3D CurrentBotPositionAdjusted { get; private set; }
    public Vector3D CurrentBotPositionActual { get; private set; }
    public Vector3D CurrentGravityAtBotPosition_Nat { get; private set; }
    public Vector3D CurrentGravityAtBotPosition_Art { get; private set; }
    public Vector3D CurrentGravityAtBotPosition_Normalized { get; private set; }

    public bool IsOnLadder => (_state & State.IsOnLadder) > 0;
    public bool WasOnLadder => (_state & State.WasOnLadder) > 0;
    public bool GoingDownLadder => (_state & State.GoingDownLadder) > 0;
    public bool IsRunning => (_state & State.IsRunning) > 0;
    public bool IsCrouching => (_state & State.IsCrouching) > 0;
    public bool IsFalling => (_state & State.IsFalling) > 0;
    public bool IsFlying => (_state & State.IsFlying) > 0;
    public bool IsJumping => (_state & State.IsJumping) > 0;

    public BotBase Bot;
    State _state;

    public BotState(BotBase b)
    {
      Bot = b;
    }

    public void UpdateBotState()
    {
      if (Bot?.Character == null || Bot.Character.MarkedForClose)
        return;

      CurrentBotPositionActual = Bot.Character.WorldAABB.Center;
      CurrentBotPositionAtFeet = Bot.Character.GetPosition();
      CurrentBotPositionAdjusted = Bot.GetPosition();

      float interference;
      CurrentGravityAtBotPosition_Nat = MyAPIGateway.Physics.CalculateNaturalGravityAt(CurrentBotPositionActual, out interference);
      CurrentGravityAtBotPosition_Art = MyAPIGateway.Physics.CalculateArtificialGravityAt(CurrentBotPositionActual, interference);

      CurrentGravityAtBotPosition_Normalized = CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? CurrentGravityAtBotPosition_Nat : CurrentGravityAtBotPosition_Art;
      if (CurrentGravityAtBotPosition_Normalized.LengthSquared() > 0)
        CurrentGravityAtBotPosition_Normalized = Vector3D.Normalize(CurrentGravityAtBotPosition_Normalized);

      var state = Bot.Character.CurrentMovementState;

      switch (state)
      {
        case MyCharacterMovementEnum.Sprinting:
        case MyCharacterMovementEnum.Running:
        case MyCharacterMovementEnum.RunningLeftBack:
        case MyCharacterMovementEnum.RunningLeftFront:
        case MyCharacterMovementEnum.RunningRightBack:
        case MyCharacterMovementEnum.RunningRightFront:
        case MyCharacterMovementEnum.RunStrafingLeft:
        case MyCharacterMovementEnum.RunStrafingRight:
          {
            var onLadder = IsOnLadder;
            _state = State.IsRunning;

            if (onLadder)
              _state |= State.WasOnLadder;

            return;
          }
        case MyCharacterMovementEnum.LadderOut:
          {
            var onLadder = IsOnLadder;
            _state = State.IsOnLadder;

            if (onLadder)
              _state |= State.WasOnLadder;

            return;
          }
        default:
          {
            break;
          }
      }

      /*
          Standing = 0,
          Sitting = 1,
          Crouching = 2,
          Flying = 3,
          Falling = 4,
          Jump = 5,
          Died = 6,
          Ladder = 7,
      */

      var mode = state.GetMode();

      switch (mode)
      {
        case 4: // falling
        {
          var onLadder = IsOnLadder;
          _state = State.IsFalling;

          if (onLadder)
            _state |= State.WasOnLadder;

          return;
        }
        case 5: // jumping
        {
          var onLadder = IsOnLadder;
          _state = State.IsJumping;

          if (onLadder)
            _state |= State.WasOnLadder;

          return;
        }
        case 7: // ladder
        {
          var onLadder = IsOnLadder;
          _state = State.IsOnLadder;

          if (onLadder)
            _state |= State.WasOnLadder;

          if (state == MyCharacterMovementEnum.LadderDown)
            _state |= State.GoingDownLadder;

          return;
        }
        case 2: // crouching
        {
          var onLadder = IsOnLadder;
          _state = State.IsCrouching;

          if (onLadder)
            _state |= State.WasOnLadder;

          return;
        }
        case 3: // flying
        {
          var onLadder = IsOnLadder;
          _state = State.IsFlying;

          if (onLadder)
            _state |= State.WasOnLadder;

          var jetpack = Bot.Character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null)
          {
            if (jetpack.TurnedOn)
            {
              if (!Bot.CanUseAirNodes)
                jetpack.TurnOnJetpack(false);
            }
            else if (Bot.RequiresJetpack)
            {
              var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
              MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
              jetpack.TurnOnJetpack(true);
              MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
            }
          }

          return;
        }
        default:
        {
          var onLadder = IsOnLadder;
          _state = State.None;

          if (onLadder)
            _state |= State.WasOnLadder;

          if (WasOnLadder && Bot.RequiresJetpack && !Bot.JetpackEnabled)
          {
            var jetpack = Bot.Character.Components?.Get<MyCharacterJetpackComponent>();
            if (jetpack != null && !jetpack.TurnedOn)
            {
              var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
              MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
              jetpack.TurnOnJetpack(true);
              MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
            }
          }

          return;
        }
      }
    }
  }
}