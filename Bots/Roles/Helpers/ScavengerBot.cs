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
  public class ScavengerBot : FriendlyBotBase
  {
    bool _performing;
    bool _sitting;
    bool _awaitItem;
    int _performTimer;

    public ScavengerBot(IMyCharacter bot, GridBase gridBase, long ownerId) : base(bot, 10, 15, gridBase, ownerId)
    {
      BotType = AiSession.BotType.Scavenger;
      Behavior = new ScavengerBehavior(this);

      _ticksBetweenAttacks = 150;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      CanUseSeats = false;
      CanUseLadders = false;

      _attackSounds.Add(new MySoundPair("DroneLoopSmall"));
      _attackSoundStrings.Add("DroneLoopSmall");
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
              var pkt = new MessagePacket($"[{Character.Name}] has found something!", "White", 3000);
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

    internal override void UseBehavior(bool force = false)
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

    internal override bool DoDamage(float amount = 0)
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
        return false;

      var character = Target.Entity as IMyCharacter;
      bool isCharacter = character != null;

      var rand = amount > 0 ? amount : isCharacter ? MyUtils.GetRandomFloat(_minDamage, _maxDamage) : _blockDamagePerAttack;
      if (isCharacter && amount == 0 && Owner != null && !AiSession.Instance.Players.ContainsKey(character.ControllerInfo.ControllingIdentityId))
        rand *= 4;

      destroyable.DoDamage(rand, MyStringHash.GetOrCompute("Punch"), true);

      if (isCharacter)
      {
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

      return isCharacter;
    }

    internal override void CheckFire(bool shouldFire, bool shouldAttack, ref Vector3 movement, ref Vector2 rotation, ref float roll)
    {
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
    }
  }
}
