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
  public class RepairBot : BotBase
  {
    // TODO: Create a pool for these collections and reuse them!
    Dictionary<IMyProjector, IMyCubeGrid> _projectedGrids = new Dictionary<IMyProjector, IMyCubeGrid>();
    Dictionary<string, int> _missingComps = new Dictionary<string, int>();
    HashSet<MyDefinitionId> _builtBlocks = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
    List<MyInventoryItem> _invItems = new List<MyInventoryItem>();
    List<IMySlimBlock> _cubes = new List<IMySlimBlock>();
    BuildBotToolInfo _toolInfo = new BuildBotToolInfo();

    public RepairBot(IMyCharacter bot, GridBase gridBase, long ownerId, string toolType = null) : base(bot, 1, 1, gridBase)
    {
      Owner = AiSession.Instance.Players[ownerId];
      Behavior = new WorkerBehavior(bot);
      ToolSubtype = toolType ?? "Welder2Item";
      _targetAction = new Action(SetTargetInternal);

      _followDistanceSqd = 25;

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
      CanUseSeats = true;
      CanUseLadders = true;
      WantsTarget = true;

      _blockDamagePerSecond = 0;
      _blockDamagePerAttack = 0; // he's a lover, not a fighter :)

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

    internal override void Close(bool cleanConfig = false)
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

        _projectedGrids = null;
        _missingComps = null;
        _toolInfo = null;
        _invItems = null;
        _cubes = null;
        _builtBlocks = null;
        _targetAction = null;
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in RepairBot.Close: {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        base.Close(cleanConfig);
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

      var welderDefinition = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), ToolSubtype);
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

    internal override bool IsInRangeOfTarget() => true;

    void SetTargetInternal()
    {
      if (_currentGraph == null || Target == null || !WantsTarget)
        return;

      var character = Owner?.Character;
      if (character == null)
        return;

      if (Target.IsSlimBlock)
      {
        var slim = Target.Entity as IMySlimBlock;
        if (slim != null && !slim.IsDestroyed && slim.GetBlockHealth() < 1 && CheckBotInventoryForItems(slim))
        {
          if (!AiSession.Instance.BlockRepairDelays.Contains(slim.CubeGrid.EntityId, slim.Position))
            return;
        }
      }

      object tgt = null;
      var graph = _currentGraph as CubeGridMap;
      var isGridGraph = graph?.Grid?.MarkedForClose == false;
      bool isInventory = false;
      var botPosition = Position;
      var botMatrix = WorldMatrix;

      if (((byte)MySessionComponentSafeZones.AllowedActions & 8) != 0)
      {
        if (isGridGraph && _pathCollection?.TempEntities != null) // path collection is null until first target assignment
        {
          // check for damaged blocks on grid
          var grid = graph.Grid as IMyCubeGrid;

          var owner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : -1L;
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
            _cubes.Clear();
            grid.GetBlocks(_cubes);
            var gridSizeHalf = grid.GridSize * 0.5;
            var gridUp = grid.WorldMatrix.GetClosestDirection(botMatrix.Up);
            var upVec = grid.WorldMatrix.GetDirectionVector(gridUp);
            var upAddVec = upVec * gridSizeHalf;
            var colorVec = new Vector3(360, 100, 100);
            var botId = Character.EntityId;

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

              var node = slim.Position;
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
                  graph.AddRepairTile(slimGridId, node, botId);
                  break;
                }
                else if (graph.InventoryCache.ContainsItemsFor(slim, _invItems))
                {
                  var botLocal = grid.WorldToGridInteger(botPosition);
                  var inv = graph.InventoryCache.GetClosestInventory(botLocal, this);
                  if (inv != null)
                  {
                    Target.SetInventory(inv);

                    tgt = slim;
                    graph.AddRepairTile(slimGridId, node, botId);
                    isInventory = true;
                  }
                  break;
                }
              }
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

                if (projector.CubeGrid.EntityId != grid.EntityId)
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
                    var node = grid.WorldToGridInteger(projectedWorld);

                    Vector3I closestNode;
                    if (_currentGraph.GetClosestValidNode(this, node, out closestNode, isSlimBlock: true))
                    {
                      if (CheckBotInventoryForItems(slim))
                      {
                        tgt = slim;
                        graph.AddRepairTile(slimGridId, projectedLocal, botId);
                        break;
                      }
                      else if (graph.InventoryCache.ContainsItemsFor(slim, _invItems))
                      {
                        var botLocal = grid.WorldToGridInteger(botPosition);
                        var inv = graph.InventoryCache.GetClosestInventory(botLocal, this);
                        if (inv != null)
                        {
                          Target.SetInventory(inv);

                          tgt = slim;
                          graph.AddRepairTile(slimGridId, projectedLocal, botId);
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
                    var node = grid.WorldToGridInteger(projectedWorld);

                    Vector3I closestNode;
                    if (_currentGraph.GetClosestValidNode(this, node, out closestNode, isSlimBlock: true))
                    {
                      if (CheckBotInventoryForItems(slim))
                      {
                        tgt = slim;
                        graph.AddRepairTile(slimGridId, projectedLocal, botId);
                        break;
                      }
                      else if (graph.InventoryCache.ContainsItemsFor(slim, _invItems))
                      {
                        var botLocal = grid.WorldToGridInteger(botPosition);
                        var inv = graph.InventoryCache.GetClosestInventory(botLocal, this);
                        if (inv != null)
                        {
                          Target.SetInventory(inv);

                          tgt = slim;
                          graph.AddRepairTile(slimGridId, projectedLocal, botId);
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
        if (isGridGraph)
        {
          graph.RemoveRepairTiles(Character.EntityId);
        }

        var floatingObj = Target.Entity as MyFloatingObject;
        if (floatingObj != null && !floatingObj.MarkedForClose && floatingObj.Item.Content != null)
          return;

        var inv = Character.GetInventory() as MyInventory;
        if (inv != null)
        {
          if (inv.IsFull)
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
          else
          {
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
        }
      }
      

      if (tgt == null)
      {
        tgt = character;
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

      _missingComps.Clear();
      block.GetMissingComponents(_missingComps);
      if (_missingComps.Count == 0)
      {
        var myGrid = block.CubeGrid as MyCubeGrid;
        var projector = myGrid?.Projector as IMyProjector;
        if (projector?.CanBuild(block, true) == BuildCheckResult.OK)
        {
          if (!block.GetMissingComponentsProjected(_missingComps, inv))
            return false;

          if (_missingComps.Count == 0)
            return true;
        }
        else
          return true;
      }

      _invItems.Clear();
      inv.GetItems(_invItems);

      if (_invItems.Count == 0)
      {
        return false;
      }

      foreach (var kvp in _missingComps)
      {
        for (int i = 0; i < _invItems.Count; i++)
        {
          var item = _invItems[i];
          if (item.Type.SubtypeId == kvp.Key)
          {
            var amount =  item.Amount;
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
            return true;
          }
        }
      }

      return false;
    }

    int _ticks;
    bool _firstRun = true;
    internal override bool Update()
    {
      if (!base.Update())
        return false;

      if (Character?.Parent is IMyCockpit)
        return true;

      ++_ticks;

      if (_firstRun || _ticks % 100 == 0)
      {
        _firstRun = false;
        UpdateRelativeDampening();
      }

      if (!UseAPITargets && !Target.HasTarget)
        SetTarget();

      return true;
    }

    ParallelTasks.Task _targetTask;
    Action _targetAction;
    public override void SetTarget()
    {
      if (_targetTask.IsComplete)
      {
        _targetTask = MyAPIGateway.Parallel.Start(_targetAction);
      }
    }

    internal override void MoveToPoint(Vector3D point, bool isTgt = false, double distanceCheck = 1)
    {
      Vector3 movement;
      Vector2 rotation;
      float roll;
      GetMovementAndRotation(isTgt, point, out movement, out rotation, out roll, distanceCheck);

      var isInv = Target.IsInventory;
      var directToBlock = isTgt && Target.IsSlimBlock;
      var directToFloater = !directToBlock && isTgt && Target.IsFloater;
      bool ignoreRotation = false;

      var botPosition = Position;
      var actualPosition = Target.CurrentActualPosition;

      if (_currentGraph.IsGridGraph)
      {
        if (movement.Z != 0 || movement.Y != 0)
        {
          if (isInv)
          {
            var distance = Vector3D.DistanceSquared(actualPosition, botPosition);
            if (distance <= 15)
            {
              movement.Y = 0;
              movement.Z = 0;
            }
          }
          else if (directToBlock || directToFloater)
          {
            var distance = Vector3D.DistanceSquared(actualPosition, botPosition);
            var checkDistance = directToFloater ? 4 : 10;
            if (distance <= checkDistance)
            {
              movement.Y = 0;
              movement.Z = 0;
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
            var upDir = graph.Grid.WorldMatrix.GetClosestDirection(Character.WorldMatrix.Up);
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
          if (d < 4)
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
        _missingComps.Clear();
        block.GetMissingComponents(_missingComps);

        if (_missingComps.Count == 0 && !block.HasDeformation && block.GetBlockHealth() >= 1)
          return false;
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

      block.IncreaseMountLevel(weldAmount, Character.ControllerInfo.ControllingIdentityId, null, boneFixAmount);

      return true;
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
      GetMovementAndRotation(Target.Entity != null, actualPosition, out movement, out rotation, out roll);
      Character.MoveAndRotate(movement, rotation, roll);
    }

    public void GetMovementAndRotation(bool isTarget, Vector3D waypoint, out Vector3 movement, out Vector2 rotation, out float roll, double distanceCheck = 5)
    {
      var botPosition = Position;
      var botMatrix = WorldMatrix;

      var vecToWP = waypoint - botPosition;
      var flatDistanceCheck = (isTarget && Target.IsFriendly()) ? _followDistanceSqd : distanceCheck;
      var relVectorBot = Vector3D.TransformNormal(vecToWP, MatrixD.Transpose(botMatrix));
      roll = 0;

      float interference;
      var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(botPosition, out interference);
      var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(botPosition, interference);
      var totalGrav = nGrav + aGrav;
      if (totalGrav.LengthSquared() > 0)
      {
        var positionAbove = botPosition - totalGrav * 5;
        var vecToAbove = positionAbove - botPosition;
        var projFwd = VectorUtils.Project(vecToAbove, botMatrix.Forward);
        var rejection = vecToAbove - projFwd;
        var deviationAngle = VectorUtils.GetAngleBetween(vecToAbove, rejection);
        if (Math.Abs(deviationAngle) > MathHelper.ToRadians(2))
        {
          roll = (float)deviationAngle * Math.Sign(botMatrix.Left.Dot(totalGrav));
        }
      }

      if (_botState.IsOnLadder)
      {
        movement = (relVectorBot.Y > 0 ? Vector3.Forward : Vector3.Backward) * 0.5f;
        rotation = Vector2.Zero;
        return;
      }

      var projUp = VectorUtils.Project(vecToWP, botMatrix.Up);
      var reject = vecToWP - projUp;
      var angle = VectorUtils.GetAngleBetween(botMatrix.Forward, reject);
      var twoDeg = MathHelperD.ToRadians(2);

      if (!WaitForStuckTimer && relVectorBot.Z < 0 && Math.Abs(angle) < twoDeg)
      {
        rotation = Vector2.Zero;
      }
      else
      {
        float xRot = 0;
        if (totalGrav.LengthSquared() > 0)
        {
          var angleFwd = MathHelperD.PiOver2 - VectorUtils.GetAngleBetween(botMatrix.Forward, totalGrav);
          if (Math.Abs(angleFwd) > twoDeg)
          {
            xRot = (float)angleFwd;
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

            return;
          }
        }
      }

      var flattenedVector = new Vector3D(relVectorBot.X, 0, relVectorBot.Z);
      var flattenedLengthSquared = flattenedVector.LengthSquared();

      if (PathFinderActive)
      {
        if (flattenedLengthSquared > flatDistanceCheck || Math.Abs(relVectorBot.Y) > distanceCheck)
          movement = Vector3.Forward * 0.5f;
        else
          movement = Vector3.Zero;
      }
      else if (flattenedLengthSquared > flatDistanceCheck && _ticksSinceFoundTarget > 240)
        movement = Vector3.Forward * 0.5f;
      else
        movement = Vector3.Zero;

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

    bool FireWeapon()
    {
      var gun = Character.EquippedTool as IMyHandheldGunObject<MyDeviceBase>;
      if (gun == null)
        return false;

      if (MyAPIGateway.Multiplayer.MultiplayerActive)
      {
        var packet = new WeaponFirePacket(Character.EntityId, 0L, 0, 0, null, TicksBetweenProjectiles, 100, false, true, false);
        AiSession.Instance.Network.RelayToClients(packet);
      }

      AiSession.Instance.StartWeaponFire(Character.EntityId, 0L, 0, 0, null, TicksBetweenProjectiles, 100, false, true, false);
      return true;
    }
  }
}
