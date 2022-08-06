using System;
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

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace AiEnabled.Bots.Roles.Helpers
{
  public class RepairBot : FriendlyBotBase
  {
    // TODO: Create a pool for these collections and reuse them!
    Dictionary<IMyProjector, IMyCubeGrid> _projectedGrids = new Dictionary<IMyProjector, IMyCubeGrid>();
    Dictionary<string, int> _missingComps = new Dictionary<string, int>();
    HashSet<MyDefinitionId> _builtBlocks = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    Dictionary<long, HashSet<Vector3I>> _repairedBlocks = new Dictionary<long, HashSet<Vector3I>>();
    List<MyInventoryItem> _invItems = new List<MyInventoryItem>();
    List<IMySlimBlock> _cubes = new List<IMySlimBlock>();
    BuildBotToolInfo _toolInfo = new BuildBotToolInfo();
    ParallelTasks.Task _targetTask;
    Action _targetAction;

    public string FirstMissingItemForRepairs;

    public RepairBot(IMyCharacter bot, GridBase gridBase, long ownerId, string toolType = null) : base(bot, 1, 1, gridBase, ownerId)
    {
      BotType = AiSession.BotType.Repair;
      Behavior = new WorkerBehavior(this);
      var toolSubtype = toolType ?? "Welder2Item";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      _targetAction = new Action(SetTargetInternal);
      _blockDamagePerSecond = 0;
      _blockDamagePerAttack = 0; // he's a lover, not a fighter :)
    }

    internal override void Close(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        if (!BugZapped && Character != null)
        {
          var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Builder, remove: true);

          if (MyAPIGateway.Session.Player != null)
            packet.Received(AiSession.Instance.Network);

          if (MyAPIGateway.Multiplayer.MultiplayerActive)
            AiSession.Instance.Network.RelayToClients(packet);
        }

        _projectedGrids?.Clear();
        _missingComps?.Clear();
        _invItems?.Clear();
        _cubes?.Clear();
        _builtBlocks?.Clear();
        _repairedBlocks?.Clear();

        _projectedGrids = null;
        _missingComps = null;
        _toolInfo = null;
        _invItems = null;
        _cubes = null;
        _builtBlocks = null;
        _targetAction = null;
        _repairedBlocks = null;
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in RepairBot.Close: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        base.Close(cleanConfig, removeBot);
      }
    }

    public override void AddWeapon()
    {
      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      var welderDefinition = ToolDefinition?.PhysicalItemId ?? new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), "Welder2Item");
      _toolInfo.CheckMultiplier(welderDefinition);

      if (inventory.CanItemsBeAdded(1, welderDefinition))
      {
        var welder = (MyObjectBuilder_PhysicalGunObject)MyObjectBuilderSerializer.CreateNewObject(welderDefinition);
        inventory.AddItems(1, welder);

        var charController = Character as Sandbox.Game.Entities.IMyControllableEntity;
        if (charController.CanSwitchToWeapon(welderDefinition))
        {
          charController.SwitchToWeapon(welderDefinition);
          HasWeaponOrTool = true;
          SetShootInterval();
        }
        else
          AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Added welder but unable to switch to it!", MessageType.WARNING);
      }
      else
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING! Unable to add welder to inventory!", MessageType.WARNING);
    }

    void SetTargetInternal()
    {
      if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty || Target == null || !WantsTarget)
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
          _pathCollection?.CleanUp(true);
        }

        return;
      }

      if (Target.IsInventory)
      {
        return;
      }

      if (Target.IsSlimBlock)
      {
        var slim = Target.Entity as IMySlimBlock;
        if (slim != null && !slim.IsDestroyed && (slim.HasDeformation || slim.GetBlockHealth() < 1) && CheckBotInventoryForItems(slim))
        {
          if (!AiSession.Instance.BlockRepairDelays.Contains(slim.CubeGrid.EntityId, slim.Position))
            return;
        }
      }

      object tgt = null;
      var graph = _currentGraph as CubeGridMap;
      var isGridGraph = graph?.MainGrid?.MarkedForClose == false;
      bool isInventory = false;
      var botPosition = GetPosition();
      var botMatrix = WorldMatrix;

      if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
        return;

      if (((byte)MySessionComponentSafeZones.AllowedActions & 8) != 0)
      {
        if (isGridGraph && _pathCollection?.TempEntities != null) // path collection is null until first target assignment
        {
          // check for damaged blocks on grid
          var mainGrid = graph.MainGrid as IMyCubeGrid;

          var owner = mainGrid.BigOwners?.Count > 0 ? mainGrid.BigOwners[0] : mainGrid.SmallOwners?.Count > 0 ? mainGrid.SmallOwners[0] : -1L;
          var relation = MyIDModule.GetRelationPlayerPlayer(Owner.IdentityId, owner);

          if (relation == MyRelationsBetweenPlayers.Self || relation == MyRelationsBetweenPlayers.Allies)
          {
            Vector3? ignoreColor = null;
            var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
            for (int i = 0; i < modData.Count; i++)
            {
              var data = modData[i];
              if (data.OwnerIdentityId == Owner.IdentityId)
              {
                ignoreColor = data.RepairBotIgnoreColorMask;
                break;
              }
            }

            _projectedGrids.Clear();
            var colorVec = new Vector3(360, 100, 100);
            var botId = Character.EntityId;
            var gridList = graph.GridCollection;
            bool returnList = false;

            if (gridList == null || gridList.Count == 0)
            {
              if (!AiSession.Instance.GridGroupListStack.TryPop(out gridList) || gridList == null)
                gridList = new List<IMyCubeGrid>();
              else
                gridList.Clear();

              returnList = true;
              graph.MainGrid?.GetGridGroup(GridLinkTypeEnum.Mechanical)?.GetGrids(gridList);
            }

            for (int i = gridList.Count - 1; i >= 0; i--)
            {
              if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
                return;

              var connectedGrid = gridList[i];
              if (connectedGrid?.Physics == null || ((MyCubeGrid)connectedGrid).IsPreview || connectedGrid.MarkedForClose)
                continue;

              _cubes.Clear();
              connectedGrid.GetBlocks(_cubes);
              bool isMainGrid = connectedGrid.EntityId == graph.MainGrid.EntityId;

              for (int j = 0; j < _cubes.Count; j++)
              {
                var slim = _cubes[j];
                if (slim == null || slim.IsDestroyed)
                  continue;

                var color = MyColorPickerConstants.HSVOffsetToHSV(slim.ColorMaskHSV) * colorVec;
                if (ignoreColor.HasValue && Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
                  continue;

                var health = slim.GetBlockHealth();
                if (!slim.HasDeformation && (health < 0 || health >= 1))
                  continue;

                var node = slim.Position;
                if (!isMainGrid)
                {
                  var slimWorld = connectedGrid.GridIntegerToWorld(node);
                  node = mainGrid.WorldToGridInteger(slimWorld);
                }

                var slimGridId = slim.CubeGrid.EntityId;

                if (AiSession.Instance.BlockRepairDelays.Contains(slimGridId, node) || graph.IsTileBeingRepaired(slimGridId, node, botId))
                {
                  continue;
                }

                Vector3I _;
                if (_currentGraph.GetClosestValidNode(this, node, out _, isSlimBlock: true))
                {
                  if (CheckBotInventoryForItems(slim))
                  {
                    tgt = slim;
                    graph.AddRepairTile(slimGridId, node, botId, _repairedBlocks);
                    break;
                  }
                  else if (graph.InventoryCache.ContainsItemsFor(slim, _invItems, this))
                  {
                    var botLocal = mainGrid.WorldToGridInteger(botPosition);
                    var inv = graph.InventoryCache.GetClosestInventory(botLocal, this);
                    if (inv != null)
                    {
                      Target.SetInventory(inv);

                      tgt = slim;
                      graph.AddRepairTile(slimGridId, node, botId, _repairedBlocks);
                      isInventory = true;
                    }
                    break;
                  }
                }
              }

              if (tgt != null)
                break;
            }

            if (returnList)
            {
              gridList.Clear();
              AiSession.Instance.GridGroupListStack.Push(gridList);
            }

            if (tgt == null)
            {
              // check for projections to weld

              List<MyEntity> entList;
              if (!AiSession.Instance.EntListStack.TryPop(out entList))
                entList = new List<MyEntity>();
              else
                entList.Clear();

              MyGamePruningStructure.GetAllEntitiesInOBB(ref graph.OBB, entList, MyEntityQueryType.Both);

              for (int i = 0; i < entList.Count; i++)
              {
                var projGrid = entList[i] as MyCubeGrid;
                var projector = projGrid?.Projector as IMyProjector;

                if (projector?.BuildableBlocksCount > 0)
                  _projectedGrids[projector] = projGrid;
              }

              entList.Clear();
              AiSession.Instance.EntListStack.Push(entList);

              foreach (var kvp in _projectedGrids)
              {
                var projector = kvp.Key;
                if (projector?.CubeGrid == null || projector.CubeGrid.MarkedForClose)
                  continue;

                if (projector.CubeGrid.EntityId != mainGrid.EntityId)
                {
                  _cubes.Clear();
                  projector.CubeGrid.GetBlocks(_cubes);

                  for (int i = 0; i < _cubes.Count; i++)
                  {
                    var slim = _cubes[i];
                    if (slim == null || slim.IsDestroyed)
                      continue;

                    var color = MyColorPickerConstants.HSVOffsetToHSV(slim.ColorMaskHSV) * colorVec;
                    if (ignoreColor.HasValue && Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
                      continue;

                    var health = slim.GetBlockHealth();
                    if (!slim.HasDeformation && (health < 0 || health >= 1))
                      continue;

                    var projectedLocal = slim.Position;
                    var slimGridId = slim.CubeGrid.EntityId;

                    if (AiSession.Instance.BlockRepairDelays.Contains(slimGridId, projectedLocal) || graph.IsTileBeingRepaired(slimGridId, projectedLocal, botId))
                    {
                      continue;
                    }
                    
                    var projectedWorld = projector.CubeGrid.GridIntegerToWorld(projectedLocal);
                    var node = mainGrid.WorldToGridInteger(projectedWorld);

                    if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
                      return;

                    Vector3I closestNode;
                    if (_currentGraph.GetClosestValidNode(this, node, out closestNode, isSlimBlock: true))
                    {
                      if (CheckBotInventoryForItems(slim))
                      {
                        tgt = slim;
                        graph.AddRepairTile(slimGridId, projectedLocal, botId, _repairedBlocks);
                        break;
                      }
                      else if (graph.InventoryCache.ContainsItemsFor(slim, _invItems, this))
                      {
                        var botLocal = mainGrid.WorldToGridInteger(botPosition);
                        var inv = graph.InventoryCache.GetClosestInventory(botLocal, this);
                        if (inv != null)
                        {
                          Target.SetInventory(inv);

                          tgt = slim;
                          graph.AddRepairTile(slimGridId, node, botId, _repairedBlocks);
                          isInventory = true;
                        }
                        break;
                      }
                    }
                  }
                }

                if (tgt == null)
                {
                  _cubes.Clear();
                  var projectedGrid = kvp.Value;
                  projectedGrid.GetBlocks(_cubes);

                  for (int i = 0; i < _cubes.Count; i++)
                  {
                    var slim = _cubes[i];
                    if (slim == null || slim.IsDestroyed)
                      continue;

                    var buildResult = projector.CanBuild(slim, true);
                    if (buildResult != BuildCheckResult.OK)
                      continue;

                    var projectedLocal = slim.Position;
                    var slimGridId = slim.CubeGrid.EntityId;

                    if (graph.IsTileBeingRepaired(slimGridId, projectedLocal, botId))
                    {
                      continue;
                    }

                    var projectedWorld = projectedGrid.GridIntegerToWorld(projectedLocal);
                    var node = mainGrid.WorldToGridInteger(projectedWorld);

                    if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
                      return;

                    Vector3I closestNode;
                    if (_currentGraph.GetClosestValidNode(this, node, out closestNode, isSlimBlock: true))
                    {
                      if (CheckBotInventoryForItems(slim))
                      {
                        tgt = slim;
                        graph.AddRepairTile(slimGridId, projectedLocal, botId, _repairedBlocks);
                        break;
                      }
                      else if (graph.InventoryCache.ContainsItemsFor(slim, _invItems, this))
                      {
                        var botLocal = mainGrid.WorldToGridInteger(botPosition);
                        var inv = graph.InventoryCache.GetClosestInventory(botLocal, this);
                        if (inv != null)
                        {
                          Target.SetInventory(inv);

                          tgt = slim;
                          graph.AddRepairTile(slimGridId, node, botId, _repairedBlocks);
                          isInventory = true;
                        }
                        break;
                      }
                    }
                  }
                }

                if (tgt != null)
                  break;
              }
            }
          }
        }
      }

      if (tgt == null)
      {
        if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
          return;

        if (isGridGraph)
        {
          graph.RemoveRepairTiles(_repairedBlocks);
        }

        var inv = Character.GetInventory() as MyInventory;
        if (inv != null)
        {
          var invRatioOK = ((float)inv.CurrentVolume / (float)inv.MaxVolume) < 0.9f;

          if (invRatioOK)
          {
            var floatingObj = Target.Entity as MyFloatingObject;
            if (floatingObj != null && !floatingObj.MarkedForClose && floatingObj.Item.Content != null)
              return;

            List<MyEntity> entities;
            if (!AiSession.Instance.EntListStack.TryPop(out entities))
              entities = new List<MyEntity>();
            else
              entities.Clear();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref _currentGraph.OBB, entities, MyEntityQueryType.Dynamic);

            for (int i = entities.Count - 1; i >= 0; i--)
            {
              var ent = entities[i];
              if (ent == null || ent.MarkedForClose)
                continue;

              var floater = ent as MyFloatingObject;
              if (floater?.Physics == null || floater.IsPreview || floater.Item.Content == null)
                continue;

              if (!inv.CanItemsBeAdded(1, floater.ItemDefinition.Id))
              {
                // inv too full, send bot to drop off current stock

                var botLocal = graph.WorldToLocal(botPosition);
                var invBlock = graph.InventoryCache.GetClosestInventory(botLocal, this);
                if (invBlock != null)
                {
                  Target.SetInventory(invBlock);
                  isInventory = true;
                }

                break;
              }

              if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
                break;

              var floaterPos = floater.PositionComp.GetPosition();
              if (_currentGraph.IsPositionValid(floaterPos))
              {
                Vector3I _;
                var node = _currentGraph.WorldToLocal(floaterPos);
                if (_currentGraph.GetClosestValidNode(this, node, out _, botMatrix.Up))
                {
                  tgt = ent;
                  break;
                }
              }
            }

            entities.Clear();
            AiSession.Instance.EntListStack.Push(entities);
          }
          else if (isGridGraph)
          {
            // inv too full, send bot to drop off current stock

            var botLocal = graph.WorldToLocal(botPosition);
            var invBlock = graph.InventoryCache.GetClosestInventory(botLocal, this);
            if (invBlock != null)
            {
              Target.SetInventory(invBlock);
              isInventory = true;
            }
          }
        }
      }

      var onPatrol = PatrolMode && _patrolList?.Count > 0;

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

      if (Target.Entity != null && ReferenceEquals(Target.Entity, tgt))
      {
        return;
      }

      if (Target.IsSlimBlock && _particlePacketSent && !BugZapped)
      {
        _particlePacketSent = false;
        var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Builder, remove: true);

        if (MyAPIGateway.Session.Player != null)  
          packet.Received(AiSession.Instance.Network);

        if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
          AiSession.Instance.Network.RelayToClients(packet);
      }

      if (MyUtils.GetRandomInt(0, 10) > 6 && ReferenceEquals(tgt, character))
        Behavior.Speak("HelloHumanoid");

      Target.SetTarget(Owner, tgt, isInventory);
      _pathCollection?.CleanUp(true);
    }

    bool CheckBotInventoryForItems(IMySlimBlock block)
    {
      if (block == null)
      {
        return false;
      }

      var inv = Character.GetInventory();
      if (inv == null)
      {
        return false;
      }

      var gridGraph = _currentGraph as CubeGridMap;
      if (gridGraph == null)
      {
        return false;
      }

      Dictionary<string, int> missingComps;
      if (!AiSession.Instance.MissingCompsDictStack.TryPop(out missingComps) || missingComps == null)
      {
        missingComps = new Dictionary<string, int>();
      }

      bool valid = true;
      bool returnNow = false;

      missingComps.Clear();
      block.GetMissingComponents(missingComps);
      if (missingComps.Count == 0)
      {
        var myGrid = block.CubeGrid as MyCubeGrid;
        var projector = myGrid?.Projector as IMyProjector;
        if (projector?.CanBuild(block, true) == BuildCheckResult.OK)
        {
          if (!block.GetMissingComponentsProjected(missingComps, inv))
          {
            valid = false;
          }
          else if (missingComps.Count == 0)
          {
            returnNow = true;
          }
        }
        else
        {
          returnNow = true;
        }
      }

      if (valid && !returnNow)
      {
        _invItems.Clear();
        inv.GetItems(_invItems);

        valid = _invItems.Count > 0;
      }

      if (!valid || returnNow)
      {
        missingComps.Clear();
        AiSession.Instance.MissingCompsDictStack.Push(missingComps);
        return valid;
      }

      valid = false;

      foreach (var kvp in missingComps)
      {
        for (int i = 0; i < _invItems.Count; i++)
        {
          var item = _invItems[i];
          if (item.Type.SubtypeId == kvp.Key)
          {
            var amount = item.Amount;
            if (amount < 1)
            {
              VRage.Game.ModAPI.Ingame.MyItemInfo itemInfo;
              if (!AiSession.Instance.ComponentInfoDict.TryGetValue(item.Type, out itemInfo))
              {
                itemInfo = VRage.Game.ModAPI.Ingame.MyPhysicalInventoryItemExtensions_ModAPI.GetItemInfo(item.Type);
                AiSession.Instance.ComponentInfoDict[item.Type] = itemInfo;
              }

              if (itemInfo.IsComponent)
              {
                var def = (MyDefinitionId)item.Type;
                var obj = MyObjectBuilderSerializer.CreateNewObject(def.TypeId, def.SubtypeName) as MyObjectBuilder_PhysicalObject;
                inv.AddItems(1 - amount, obj);
              }
            }

            valid = true;
            break;
          }
        }

        if (valid)
          break;
      }

      missingComps.Clear();
      AiSession.Instance.MissingCompsDictStack.Push(missingComps);
      return valid;
    }

    bool _firstRun = true;
    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (Character?.Parent is IMyCockpit)
        return true;

      if (_firstRun || _tickCount % 100 == 0)
      {
        _firstRun = false;
        UpdateRelativeDampening();
      }

      if (!UseAPITargets && !Target.HasTarget)
        SetTarget();

      return true;
    }

    public override void SetTarget()
    {
      if (_targetTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during RepairBot.SetTarget task!\n");
        foreach (var ex in _targetTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

        AiSession.Instance.Logger.LogAll();
        MyAPIGateway.Utilities.ShowNotification($"Exception during RepairBot.SetTarget task!");
      }

      if (_targetTask.IsComplete)
      {
        _targetTask = MyAPIGateway.Parallel.Start(_targetAction);
      }

      // Testing only!
      //_targetAction?.Invoke();
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      bool fistAttack, rifleAttack;
      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, out fistAttack, out rifleAttack, distanceCheck);

      if (Target.IsInventory || (Target.Entity != null && !(Target.Entity is IMyCharacter)))
      {
        var isInv = Target.IsInventory;
        var directToBlock = isTgt && Target.IsSlimBlock;
        var directToFloater = !directToBlock && isTgt && Target.IsFloater;
        bool ignoreRotation = false;

        var botPosition = GetPosition();
        var actualPosition = Target.CurrentActualPosition;

        if (_currentGraph.IsGridGraph)
        {
          if (movement.Z != 0 || movement.Y != 0)
          {
            if (isInv || directToBlock || directToFloater)
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
                  distanceCheck = isInv ? 15 : directToFloater ? 6 : 10;
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
          else if (rotation.Y != 0 && (isInv || directToBlock))
          {
            var graph = _currentGraph as CubeGridMap;
            var localTgt = graph.WorldToLocal(actualPosition);
            var localBot = graph.WorldToLocal(botPosition);
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
          var checkDistance = Target.IsFloater ? 4 : 10;
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
          var obj = Target.Entity as MyFloatingObject;
          if (obj != null)
          {
            var d = Vector3D.DistanceSquared(botPosition, obj.PositionComp.WorldAABB.Center);
            if (d < 6)
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

          var slim = Target.Entity as IMySlimBlock;
          if (slim != null && ((byte)MySessionComponentSafeZones.AllowedActions & 8) != 0)
          {
            var myGrid = slim.CubeGrid as MyCubeGrid;
            var projector = myGrid?.Projector as IMyProjector;

            if (RepairBlockManually(slim, projector))
            {
              ParticlePacket pkt;
              if (!_particlePacketSent && !BugZapped)
              {
                _particlePacketSent = true;
                var terminal = slim?.FatBlock as IMyTerminalBlock;
                pkt = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Builder, terminal?.EntityId ?? 0L, slim.CubeGrid.EntityId, slim.Position, true);

                if (MyAPIGateway.Session.Player != null)
                  pkt.Received(AiSession.Instance.Network);

                if (MyAPIGateway.Multiplayer.MultiplayerActive)
                  AiSession.Instance.Network.RelayToClients(pkt);
              }
            }

            if (MyAPIGateway.Session?.SessionSettings?.EnableResearch == true && slim.BlockDefinition != null)
            {
              var blockDef = slim.BlockDefinition.Id;
              if (slim.BuildLevelRatio > 0.99f && _builtBlocks.Add(blockDef))
              {
                MyVisualScriptLogicProvider.PlayerResearchUnlock(Owner.IdentityId, blockDef);
              }
            }
          }
        }
        else if (!notMoving && _particlePacketSent)
        {
          _particlePacketSent = false;
          var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Builder, remove: true);

          if (MyAPIGateway.Session.Player != null)
            packet.Received(AiSession.Instance.Network);

          if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
            AiSession.Instance.Network.RelayToClients(packet);
        }
      }
      else if (_particlePacketSent)
      {
        _particlePacketSent = false;
        var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Builder, remove: true);

        if (MyAPIGateway.Session.Player != null)
          packet.Received(AiSession.Instance.Network);

        if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
          AiSession.Instance.Network.RelayToClients(packet);
      }

      MoveToPoint(movement, rotation, roll);
    }

    bool _particlePacketSent;
    bool RepairBlockManually(IMySlimBlock block, IMyProjector projector)
    {
      var inv = Character?.GetInventory();
      if (inv == null || block == null)
        return false;

      var isProjection = projector?.CanBuild(block, true) == BuildCheckResult.OK;
      if (!isProjection)
      {
        var gridGraph = _currentGraph as CubeGridMap;
        if (gridGraph != null)
        {
          _missingComps.Clear();
          block.GetMissingComponents(_missingComps);

          if (_missingComps.Count == 0 && !block.HasDeformation && block.GetBlockHealth() >= 1)
            return false;
        }
      }
      else if (projector != null)
      {
        projector.Build(block, Owner.IdentityId, Owner.Character.EntityId, false, Owner.IdentityId);

        var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
        var compDef = cubeDef.Components[0].Definition.Id;
        var obj = MyObjectBuilderSerializer.CreateNewObject(compDef.TypeId, compDef.SubtypeName) as MyObjectBuilder_PhysicalObject;
        inv.RemoveItemsOfType(1, obj);

        Vector3? ignoreColor = null;
        var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
        for (int i = 0; i < modData.Count; i++)
        {
          var data = modData[i];
          if (data.OwnerIdentityId == Owner.IdentityId)
          {
            ignoreColor = data.RepairBotIgnoreColorMask;
            break;
          }
        }

        if (ignoreColor.HasValue)
        {
          var color = MyColorPickerConstants.HSVOffsetToHSV(block.ColorMaskHSV) * new Vector3(360, 100, 100);
          if (Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
            return false;
        }
      }
      else
      {
        return false;
      }

      if (inv.ItemCount > 0)
        block.MoveItemsToConstructionStockpile(inv);

      var weldAmount = _toolInfo.WeldAmount;
      var boneFixAmount = _toolInfo.BoneFixAmount;

      block.IncreaseMountLevel(weldAmount, BotIdentityId, null, boneFixAmount);
      return true;
    }
  }
}
