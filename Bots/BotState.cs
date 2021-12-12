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

namespace AiEnabled.Bots
{
  public class BotState
  {
    public bool IsOnLadder, WasOnLadder, GoingDownLadder;
    public bool IsRunning;
    public bool IsCrouching;
    public bool IsFalling;
    public bool IsFlying;
    public BotBase Bot;

    public BotState(BotBase b)
    {
      Bot = b;
    }

    public void UpdateBotState()
    {
      if (Bot?.Character == null || Bot.Character.MarkedForClose)
        return;

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
            WasOnLadder = IsOnLadder;
            IsFlying = false;
            IsRunning = true;
            IsOnLadder = false;
            GoingDownLadder = false;
            IsCrouching = false;
            IsFalling = false;
            return;
          }
        case MyCharacterMovementEnum.LadderOut:
          {
            WasOnLadder = IsOnLadder;
            IsFlying = false;
            IsOnLadder = true;
            GoingDownLadder = false;
            IsRunning = false;
            IsCrouching = false;
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
            WasOnLadder = IsOnLadder;
            IsFlying = false;
            IsRunning = false;
            IsOnLadder = false;
            GoingDownLadder = false;
            IsCrouching = false;
            IsFalling = true;
            return;
          }
        case 7: // ladder
          {
            GoingDownLadder = state == MyCharacterMovementEnum.LadderDown;
            WasOnLadder = IsOnLadder;
            IsOnLadder = true;
            IsFlying = false;
            IsRunning = false;
            IsCrouching = false;
            return;
          }
        case 2: // crouching
          {
            WasOnLadder = IsOnLadder;
            IsCrouching = true;
            IsFlying = false;
            IsRunning = false;
            IsOnLadder = false;
            GoingDownLadder = false;
            IsFalling = false;
            return;
          }
        case 3: // flying
          {
            WasOnLadder = IsOnLadder;
            IsCrouching = false;
            IsRunning = false;
            IsOnLadder = false;
            GoingDownLadder = false;
            IsFalling = false;
            IsFlying = true;

            var jetpack = Bot.Character.Components?.Get<MyCharacterJetpackComponent>();
            if (jetpack != null)
            {
              if (jetpack.TurnedOn)
              {
                if (!Bot._canUseAirNodes)
                  jetpack.TurnOnJetpack(false);
              }
              else if (Bot._requiresJetpack)
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
            WasOnLadder = IsOnLadder;
            IsCrouching = false;
            IsRunning = false;
            IsOnLadder = false;
            GoingDownLadder = false;
            IsFalling = false;
            IsFlying = false;

            if (WasOnLadder && Bot._requiresJetpack && !Bot._jetpackEnabled)
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