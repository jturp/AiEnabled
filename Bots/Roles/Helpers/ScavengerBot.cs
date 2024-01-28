using AiEnabled.Ai.Support;
using AiEnabled.Bots.Behaviors;
using AiEnabled.Networking;
using AiEnabled.Particles;
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
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
    float _lastRadius = -1;
    List<MyOrientedBoundingBoxD> _patrolOBBs = new List<MyOrientedBoundingBoxD>();
    List<MyEntity> _threadOnlyEntList, _noThreadEntList;

    public ScavengerBot(IMyCharacter bot, GridBase gridBase, long ownerId, AiSession.ControlInfo ctrlInfo) : base(bot, 10, 15, gridBase, ownerId, ctrlInfo)
    {
      BotType = AiSession.BotType.Scavenger;
      Behavior = new ScavengerBehavior(this);

      _ticksBetweenAttacks = 180;
      _blockDamagePerSecond = 175;
      _blockDamagePerAttack = _blockDamagePerSecond * (_ticksBetweenAttacks / 60f);

      CanUseSeats = false;
      CanUseLadders = false;

      _attackSounds.Add(new MySoundPair("DroneLoopSmall"));
      _attackSoundStrings.Add("DroneLoopSmall");

      _threadOnlyEntList = AiSession.Instance.EntListStack.Get();
      _noThreadEntList = AiSession.Instance.EntListStack.Get();
    }

    internal override void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      if (AiSession.Instance?.Registered == true)
      {
        if (_threadOnlyEntList != null)
        {
          AiSession.Instance.EntListStack?.Return(_noThreadEntList);
        }

        if (_noThreadEntList != null)
        {
          AiSession.Instance.EntListStack?.Return(_noThreadEntList);
        }
      }
      else
      {
        _threadOnlyEntList?.Clear();
        _noThreadEntList?.Clear();

        _threadOnlyEntList = null;
        _noThreadEntList = null;
      }

      _patrolOBBs?.Clear();
      _patrolOBBs = null;

      base.CleanUp(cleanConfig, removeBot);
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
            SpawnItem();
          }
          else
          {
            _performing = false;
          }
        }
      }

      return true;
    }

    void SpawnItem()
    {
      var items = AiSession.Instance?.ScavengerItemList;

      if (items?.Count > 0)
      {
        var rand = MyUtils.GetRandomInt(0, items.Count);
        var id = items[rand];

        var item = new MyPhysicalInventoryItem()
        {
          Amount = 1,
          Content = MyObjectBuilderSerializer.CreateNewObject(id) as MyObjectBuilder_PhysicalObject
        };

        var inv = Character.GetInventory() as MyInventory;
        if (inv != null && inv.CanItemsBeAdded(1, id))
        {
          inv.AddItems(1, item.Content);
        }
        else
        {
          var matrix = WorldMatrix;
          matrix.Translation += matrix.Up + GetTravelDirection() * 1.5;

          MyFloatingObjects.Spawn(item, matrix, Character.Physics, null);
        }

        _awaitItem = false;

        if (Owner?.SteamUserId > 0)
        {
          var pkt = new MessagePacket($"[{Character.Name}] has found something!", "White", 3000);
          AiSession.Instance.Network.SendToPlayer(pkt, Owner.SteamUserId);
        }
      }

      Behavior.Perform("RoboDog_Spin");
      Behavior.Speak();
      _performTimer = 60;
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
      else if (Owner?.Character != null && Vector3D.DistanceSquared(Owner.Character.WorldAABB.Center, BotInfo.CurrentBotPositionActual) < 2500)
      {
        _performTimer = 0;
        var rand = MyUtils.GetRandomInt(0, 100);
        var ableToAnimate = !BotInfo.IsFlying && !BotInfo.IsFalling;

        if (rand < 40)
        {
          // sit and pant
          Behavior.Speak("RoboDogPant001");

          if (ableToAnimate && !_sitting && Character.LastMotionIndicator == Vector3.Zero && Character.LastRotationIndicator == Vector3.Zero)
          {
            Behavior.Perform("RoboDog_Sitting");
            _sitting = true;
            _performing = true;
          }
        }
        else if (ableToAnimate && AiSession.Instance.ModSaveData.AllowScavengerDigging)
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
          _awaitItem = rand > 80;
          _performing = true;
        }
        else
        {
          _performing = false;
          _awaitItem = false;
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

    internal override void SetTargetInternal()
    {
      if (!WantsTarget || _currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty || Target == null)
        return;

      var character = Owner?.Character;
      if (character == null)
        return;

      if (FollowMode)
      {
        var ownerParent = character.GetTopMostParent();
        var currentEnt = Target.Entity as IMyEntity;

        if (currentEnt?.EntityId != ownerParent.EntityId)
        {
          Target.SetTarget(ownerParent);
          CleanPath();
        }

        return;
      }

      if (Target.IsInventory || _currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
        return;

      bool isInventory = false;
      object tgt = null;
      var graph = _currentGraph as CubeGridMap;
      var isGridGraph = graph?.MainGrid?.MarkedForClose == false;
      var botPosition = BotInfo.CurrentBotPositionActual;

      var inv = Character.GetInventory() as MyInventory;
      if (inv != null)
      {
        var invRatioOK = ((float)inv.CurrentVolume / (float)inv.MaxVolume) < 0.9f;

        if (invRatioOK && AiSession.Instance.ModSaveData.AllowScavengerLooting)
        {
          var floatingObj = Target.Entity as MyFloatingObject;
          if (floatingObj != null && !floatingObj.MarkedForClose && floatingObj.Item.Content != null
            && !GridBase.PointInsideVoxel(floatingObj.PositionComp.WorldAABB.Center, _currentGraph.RootVoxel))
          {
            return;
          }

          var searchRadius = AiSession.Instance.PlayerToRepairRadius.GetValueOrDefault(Owner.IdentityId, 0f);
          _threadOnlyEntList.Clear();
          MyGamePruningStructure.GetAllEntitiesInOBB(ref _currentGraph.OBB, _threadOnlyEntList);

          _threadOnlyEntList.ShellSort(botPosition, true);

          var gravity = BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? BotInfo.CurrentGravityAtBotPosition_Nat : BotInfo.CurrentGravityAtBotPosition_Art;
          if (gravity.LengthSquared() > 0)
            gravity.Normalize();

          for (int i = _threadOnlyEntList.Count - 1; i >= 0; i--)
          {
            if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
              break;

            var ent = _threadOnlyEntList[i];
            if (ent == null || ent.MarkedForClose || ent.IsPreview)
              continue;

            var charTgt = ent as IMyCharacter;
            if (ent.Physics == null)
            {
              bool skip = true;

              if (charTgt != null && charTgt.IsDead && AiSession.Instance.ModSaveData.DisableCharacterCollisionOnBotDeath && AiSession.Instance.Bots.ContainsKey(charTgt.EntityId))
              {
                var charDef = charTgt.Definition as MyCharacterDefinition;
                if (!charDef.EnableSpawnInventoryAsContainer && charTgt.GetInventory()?.ItemCount > 0)
                  skip = false;
              }

              if (skip)
                continue;
            }

            var entPosition = ent.PositionComp.WorldAABB.Center;
            if (GridBase.PointInsideVoxel(entPosition, _currentGraph.RootVoxel))
              continue;

            if (searchRadius > 0 && !IsWithinSearchRadius(entPosition, ref _lastRadius, _patrolOBBs))
              continue;

            var floater = ent as MyFloatingObject;
            if (floater != null)
            {
              if (floater.Physics == null || floater.Item.Content == null)
                continue;

              if (!inv.CanItemsBeAdded(1, floater.ItemDefinition.Id))
              {
                // inv too full, send bot to drop off current stock

                var botLocal = _currentGraph.WorldToLocal(botPosition);
                var invBlock = graph?.InventoryCache.GetClosestInventory(botLocal, this);
                if (invBlock != null)
                {
                  Target.SetInventory(invBlock);
                  isInventory = true;
                }

                break;
              }

              var floaterPosition = floater.PositionComp.WorldAABB.Center;

              if (gravity.LengthSquared() > 0)
                floaterPosition -= gravity;

              if (_currentGraph.IsPositionValid(floaterPosition))
              {
                Vector3I _;
                var node = _currentGraph.WorldToLocal(floaterPosition);
                if (_currentGraph.GetClosestValidNode(this, node, out _)) //, WorldMatrix.Up))
                {
                  tgt = ent;
                  break;
                }
              }

              continue;
            }

            var invBagEntity = ent as MyInventoryBagEntity;
            if (invBagEntity != null)
            {
              var loot = invBagEntity.GetInventory();
              if (loot?.ItemCount > 0)
              {
                var firstItem = loot.GetItemAt(0);

                if (!inv.CanItemsBeAdded(1, firstItem.Value.Type))
                {
                  // inv too full, send bot to drop off current stock

                  var botLocal = _currentGraph.WorldToLocal(botPosition);
                  var invBlock = graph?.InventoryCache.GetClosestInventory(botLocal, this);
                  if (invBlock != null)
                  {
                    tgt = invBlock;
                    Target.SetInventory(invBlock);
                    isInventory = true;
                  }

                  break;
                }

                if (gravity.LengthSquared() > 0)
                  entPosition -= gravity;

                if (_currentGraph.IsPositionValid(entPosition))
                {
                  Vector3I _;
                  var node = _currentGraph.WorldToLocal(entPosition);
                  if (_currentGraph.GetClosestValidNode(this, node, out _)) //, WorldMatrix.Up))
                  {
                    tgt = ent;
                    break;
                  }
                }
              }

              continue;
            }

            if (charTgt != null && charTgt.IsDead)
            {
              var charDef = charTgt.Definition as MyCharacterDefinition;
              if (!charDef.EnableSpawnInventoryAsContainer)
              { 
                var loot = charTgt.GetInventory() as MyInventory;
                if (loot?.ItemCount > 0)
                {
                  var firstItem = loot.GetItemAt(0);

                  if (!inv.CanItemsBeAdded(1, firstItem.Value.Type))
                  {
                    // inv too full, send bot to drop off current stock

                    var botLocal = _currentGraph.WorldToLocal(botPosition);
                    var invBlock = graph?.InventoryCache.GetClosestInventory(botLocal, this);
                    if (invBlock != null)
                    {
                      tgt = invBlock;
                      Target.SetInventory(invBlock);
                      isInventory = true;
                    }

                    break;
                  }

                  if (gravity.LengthSquared() > 0)
                    entPosition -= gravity;

                  if (_currentGraph.IsPositionValid(entPosition))
                  {
                    Vector3I _;
                    var node = _currentGraph.WorldToLocal(entPosition);
                    if (_currentGraph.GetClosestValidNode(this, node, out _)) //, WorldMatrix.Up))
                    {
                      tgt = ent;
                      break;
                    }
                  }
                }
              }
            }
          }
        }
      }

      if (tgt == null && isGridGraph && inv.ItemCount > 0)
      {
        // send bot to drop off current stock

        var botLocal = graph.WorldToLocal(botPosition);
        var invBlock = graph.InventoryCache.GetClosestInventory(botLocal, this);
        if (invBlock != null)
        {
          tgt = invBlock;
          Target.SetInventory(invBlock);
          isInventory = true;
        }
      }
        
      if (tgt == null)
      {
        // Allow for enemy targeting
        base.SetTargetInternal();
      }
      else
      {
        var onPatrol = PatrolMode && _patrolList?.Count > 0;

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
            tgt = character;
          }
        }

        if (onPatrol && Target.Override.HasValue)
        {
          _patrolIndex = Math.Max((short)-1, (short)(_patrolIndex - 1));
          Target.RemoveOverride(false);
        }

        if (Target.Entity != null && ReferenceEquals(Target.Entity, tgt))
        {
          return;
        }

        Target.SetTarget(Owner, tgt, isInventory);
        CleanPath();
      }
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool fistAttack, rifleAttack;
      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out fistAttack, out rifleAttack, distanceCheck);

      bool allowCharTgt = true;
      var charTgt = Target.Entity as IMyCharacter;
      if (charTgt != null)
      {
        var charDef = charTgt.Definition as MyCharacterDefinition;
        allowCharTgt = !charDef.EnableSpawnInventoryAsContainer && charTgt.IsDead;

        if (!allowCharTgt)
          charTgt = null;
      }

      if (Target.IsInventory || (Target.Entity != null && allowCharTgt))
      {
        var isInv = Target.IsInventory;
        //var isChar = charTgt != null;
        var directToFloater = isTgt && Target.IsFloater;
        bool ignoreRotation = false;

        var botPosition = BotInfo.CurrentBotPositionAdjusted;
        var actualPosition = Target.CurrentActualPosition;

        if (_currentGraph.IsGridGraph)
        {
          if (movement.Z != 0 || movement.Y != 0)
          {
            if (isInv || directToFloater)
            {
              var slim = isInv ? Target.Inventory : Target.Entity as IMySlimBlock;
              if (slim != null)
              {
                if (slim.FatBlock != null)
                {
                  distanceCheck = slim.FatBlock.PositionComp.LocalAABB.HalfExtents.AbsMax() + 4f;
                  distanceCheck *= distanceCheck;
                }
                else
                {
                  BoundingBoxD box;
                  slim.GetWorldBoundingBox(out box, true);
                  distanceCheck = (float)box.HalfExtents.AbsMax() + 4f;
                  distanceCheck *= distanceCheck;
                }
              }
              else
              {
                var cube = Target.Entity as IMyCubeBlock;
                if (cube != null)
                {
                  distanceCheck = cube.PositionComp.LocalAABB.HalfExtents.AbsMax() + 4f;
                  distanceCheck *= distanceCheck;
                }
                else
                {
                  distanceCheck = isInv ? 15 : directToFloater ? 8 : 10;
                }
              }

              if (isInv)
              {
                var distance = Vector3D.DistanceSquared(actualPosition, botPosition);
                if (distance <= distanceCheck)
                {
                  movement.Y = 0;
                  movement.Z = 0;
                }
              }
              else // if (directToBlock || directToFloater)
              {
                var distance = Vector3D.DistanceSquared(actualPosition, botPosition);
                if (distance <= distanceCheck)
                {
                  movement.Y = 0;
                  movement.Z = 0;
                }
              }
            }
          }
          else if (rotation.Y != 0 && (isInv || directToFloater))
          {
            var graph = _currentGraph as CubeGridMap;
            var localTgt = _currentGraph.WorldToLocal(actualPosition);
            var localBot = _currentGraph.WorldToLocal(botPosition);
            var dMan = Vector3I.DistanceManhattan(localBot, localTgt);

            if (dMan < 2)
            {
              var diff = localTgt - localBot;
              var upDir = graph.MainGrid.WorldMatrix.GetClosestDirection(Character.WorldMatrix.Up);
              var botUp = Base6Directions.GetIntVector(upDir);

              if (botUp.Dot(ref diff) != 0)
                rotation = Vector2.Zero;
            }
            else if (dMan < 3)
            {
              ignoreRotation = true;
            }
          }
        }
        else if (movement.Z != 0 || movement.Y != 0)
        {
          var distance = Vector3D.DistanceSquared(actualPosition, botPosition);
          var checkDistance = Target.IsFloater ? 8 : 10;
          if (distance <= checkDistance)
          {
            movement.Y = 0;
            movement.Z = 0;
          }
        }

        var notMoving = Vector3.IsZero(ref movement);
        var notRotating = ignoreRotation || Vector2.IsZero(ref rotation);

        if (notMoving && notRotating)
        {
          if (isInv)
          {
            var grid = _currentGraph as CubeGridMap;
            if (grid != null)
            {
              if (grid.InventoryCache.Locked)
                return;

              grid.InventoryCache.RemoveItemsFor(Target.Entity as IMySlimBlock, this);
              Target.RemoveInventory();
            }

            return;
          }

          var obj = Target.Entity as MyFloatingObject;
          if (obj != null)
          {
            var d = Vector3D.DistanceSquared(botPosition, Target.CurrentGoToPosition);
            if (d < 8)
            {
              var inv = Character.GetInventory() as MyInventory;
              if (inv != null && !inv.IsFull)
              {
                var item = obj.Item;
                var amount = MyFixedPoint.Min(item.Amount, inv.ComputeAmountThatFits(item.Content.GetId()));
                if (inv.AddItems(amount, item.Content))
                {
                  Target.RemoveTarget();

                  if (amount >= item.Amount)
                    obj.Close();
                  else
                    obj.Item.Amount -= amount;
                }
              }
            }

            return;
          }

          var bag = Target.Entity as MyInventoryBagEntity;
          if (bag != null)
          {
            var loot = bag.GetInventory();
            var inv = Character.GetInventory() as MyInventory;

            var items = loot?.GetItems();
            if (items?.Count > 0 && inv != null)
            {
              for (int i = items.Count - 1; i >= 0; i--)
              {
                var item = items[i];
                var amount = MyInventory.Transfer(loot, inv, item.ItemId);

                if (amount != item.Amount)
                {
                  // inventory too full, not able to move all items
                  break;
                }
              }
            }

            Target.RemoveTarget();
            return;
          }

          if (charTgt != null)
          {
            var loot = charTgt.GetInventory() as MyInventory;
            var inv = Character.GetInventory() as MyInventory;

            var items = loot?.GetItems();
            if (items?.Count > 0 && inv != null)
            {
              for (int i = items.Count - 1; i >= 0; i--)
              {
                var item = items[i];
                var amount = MyInventory.Transfer(loot, inv, item.ItemId);

                if (amount != item.Amount)
                {
                  // inventory too full, not able to move all items
                  break;
                }
              }
            }

            Target.RemoveTarget();
            return;
          }
        }
      }

      MoveToPoint(movement, rotation, roll);
    }
  }
}
