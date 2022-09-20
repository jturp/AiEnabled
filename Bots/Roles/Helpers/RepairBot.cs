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
    List<MyEntity> _threadOnlyEntList, _noThreadEntList;
    BuildBotToolInfo _toolInfo = new BuildBotToolInfo();
    ParallelTasks.Task _targetTask;
    Action _targetAction;
    bool _particlePacketSent;
    bool _cleaned;

    public enum BuildMode { None, Weld, Grind }
    public BuildMode CurrentBuildMode = BuildMode.Weld;
    public string FirstMissingItemForRepairs;

    public RepairBot(IMyCharacter bot, GridBase gridBase, long ownerId, AiSession.ControlInfo ctrlInfo, string toolType = null) : base(bot, 1, 1, gridBase, ownerId, ctrlInfo)
    {
      BotType = AiSession.BotType.Repair;
      Behavior = new WorkerBehavior(this);
      var toolSubtype = toolType ?? "Welder2Item";
      ToolDefinition = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), toolSubtype));

      _targetAction = new Action(SetTargetInternal);
      _blockDamagePerSecond = 0;
      _blockDamagePerAttack = 0; // he's a lover, not a fighter :)

      if (!AiSession.Instance.EntListStack.TryPop(out _threadOnlyEntList) || _threadOnlyEntList == null)
        _threadOnlyEntList = new List<MyEntity>();
      else
        _threadOnlyEntList.Clear();

      if (!AiSession.Instance.EntListStack.TryPop(out _noThreadEntList) || _noThreadEntList == null)
        _noThreadEntList = new List<MyEntity>();
      else
        _noThreadEntList.Clear();
    }

    internal override void CleanUp(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        if (_cleaned)
          return;

        _cleaned = true;

        if (!BugZapped && Character != null)
        {
          var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Weld, remove: true);

          if (MyAPIGateway.Session.Player != null)
            packet.Received(AiSession.Instance.Network);

          if (MyAPIGateway.Multiplayer.MultiplayerActive)
            AiSession.Instance.Network.RelayToClients(packet);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in RepairBot.CleanUp: {ex.ToString()}", MessageType.ERROR);
      }
      finally
      {
        base.CleanUp(cleanConfig, removeBot);
      }
    }

    internal override void Close(bool cleanConfig = false, bool removeBot = true)
    {
      try
      {
        if (AiSession.Instance?.Registered == true)
        {
          if (_threadOnlyEntList != null)
          {
            _threadOnlyEntList.Clear();
            AiSession.Instance.EntListStack.Push(_noThreadEntList);
          }

          if (_noThreadEntList != null)
          {
            _noThreadEntList.Clear();
            AiSession.Instance.EntListStack.Push(_noThreadEntList);
          }
        }
        else
        {
          _threadOnlyEntList?.Clear();
          _noThreadEntList?.Clear();

          _threadOnlyEntList = null;
          _noThreadEntList = null;
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
        AiSession.Instance.Logger.Log($"Exception in RepairBot.Close: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.Close(cleanConfig, removeBot);
      }
    }

    public override void AddWeapon()
    {
      if (ToolDefinition?.PhysicalItemId == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: ToolDefinition.PhysicalItemId was NULL!", MessageType.WARNING);
        return;
      }

      var inventory = Character?.GetInventory();
      if (inventory == null)
      {
        AiSession.Instance.Logger.Log($"{this.GetType().Name}.AddWeapon: WARNING: Inventory was NULL!", MessageType.WARNING);
        return;
      }

      UpdateWeaponInfo();
      base.AddWeapon();
    }

    public override void EquipWeapon()
    {
      UpdateWeaponInfo();
      base.EquipWeapon();
    }

    public void UpdateWeaponInfo()
    {
      _toolInfo.CheckMultiplier(ToolDefinition, out CurrentBuildMode);
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

      if (Target.IsSlimBlock && CurrentBuildMode != BuildMode.None)
      {
        var slim = Target.Entity as IMySlimBlock;
        if (slim != null)
        {
          if (CurrentBuildMode == BuildMode.Weld)
          {
            if (!slim.IsDestroyed && (slim.HasDeformation || slim.GetBlockHealth(_threadOnlyEntList) < 1) && CheckBotInventoryForItems(slim))
            {
              if (!AiSession.Instance.BlockRepairDelays.Contains(slim.CubeGrid.EntityId, slim.Position))
                return;
            }
          }
          else if (!slim.IsDestroyed)
          {
            Vector3? grindColor = null;
            var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
            for (int i = 0; i < modData.Count; i++)
            {
              var data = modData[i];
              if (data.OwnerIdentityId == Owner.IdentityId)
              {
                grindColor = data.RepairBotGrindColorMask;
                break;
              }
            }

            if (grindColor.HasValue)
            {
              var color = MyColorPickerConstants.HSVOffsetToHSV(slim.ColorMaskHSV) * new Vector3(360, 100, 100);
              if (Vector3.IsZero(grindColor.Value - color, 1E-2f))
                return;
            }
          }
        }
      }

      if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
        return;

      bool isInventory = false;
      bool returnNow = false;
      object tgt = null;
      var graph = _currentGraph as CubeGridMap;
      var isGridGraph = graph?.MainGrid?.MarkedForClose == false;
      var botPosition = BotInfo.CurrentBotPositionActual;

      if (CurrentBuildMode == BuildMode.Weld)
        tgt = GetRepairTarget(graph, ref isGridGraph, ref botPosition, out isInventory, out returnNow);
      else if (CurrentBuildMode == BuildMode.Grind)
        tgt = GetGrindTarget(_currentGraph, ref botPosition, out isInventory, out returnNow);

      if (returnNow)
        return;

      if (tgt == null)
      {
        if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
        {
          return;
        }

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
            {
              return;
            }

            _threadOnlyEntList.Clear();
            MyGamePruningStructure.GetAllEntitiesInOBB(ref _currentGraph.OBB, _threadOnlyEntList, MyEntityQueryType.Dynamic);
            _threadOnlyEntList.ShellSort(botPosition, true);

            var gravity = BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? BotInfo.CurrentGravityAtBotPosition_Nat : BotInfo.CurrentGravityAtBotPosition_Art;
            if (gravity.LengthSquared() > 0)
              gravity.Normalize();

            for (int i = _threadOnlyEntList.Count - 1; i >= 0; i--)
            {
              if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
              {
                break;
              }

              var ent = _threadOnlyEntList[i];
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
              if (gravity.LengthSquared() > 0)
              {
                floaterPos -= gravity;
              }

              if (_currentGraph.IsPositionValid(floaterPos))
              {
                Vector3I _;
                var node = _currentGraph.WorldToLocal(floaterPos);
                if (_currentGraph.GetClosestValidNode(this, node, out _)) //, WorldMatrix.Up))
                {
                  tgt = ent;
                  break;
                }
              }
            }
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
        var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Weld, remove: true);

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

    object GetGrindTarget(GridBase graph, ref Vector3D botPosition, out bool isInventory, out bool returnNow)
    {
      isInventory = false;
      returnNow = false;
      object tgt = null;

      if (((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
      {
        Vector3? grindColor = null;
        var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
        for (int i = 0; i < modData.Count; i++)
        {
          var data = modData[i];
          if (data.OwnerIdentityId == Owner.IdentityId)
          {
            grindColor = data.RepairBotGrindColorMask;
            break;
          }
        }

        if (grindColor == null)
          return null;

        var inv = Character.GetInventory() as MyInventory;
        if (inv == null)
          return null;

        var gridGraph = graph as CubeGridMap;

        if (((float)inv.CurrentVolume / (float)inv.MaxVolume) > 0.8f)
        {
          // inv too full, try to send bot to drop off current stock
          // if no inventory available, we'll let the block comps be spawned on the ground

          var botLocal = graph.WorldToLocal(botPosition);
          var invBlock = gridGraph?.InventoryCache.GetClosestInventory(botLocal, this);
          if (invBlock != null)
          {
            Target.SetInventory(invBlock);
            isInventory = true;
            return null;
          }
        }

        var colorVec = new Vector3(360, 100, 100);
        var botId = Character.EntityId;

        _threadOnlyEntList.Clear();
        MyGamePruningStructure.GetAllEntitiesInOBB(ref graph.OBB, _threadOnlyEntList, MyEntityQueryType.Both);
        var gravityNormalized = BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? BotInfo.CurrentGravityAtBotPosition_Nat : BotInfo.CurrentGravityAtBotPosition_Art;
        if (gravityNormalized.LengthSquared() > 0)
          gravityNormalized.Normalize();

        _cubes.Clear();

        for (int i = _threadOnlyEntList.Count - 1; i >= 0; i--)
        {
          if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
          {
            returnNow = true;
            break;
          }

          var connectedGrid = _threadOnlyEntList[i] as IMyCubeGrid;
          if (connectedGrid?.Physics == null || ((MyCubeGrid)connectedGrid).IsPreview || connectedGrid.MarkedForClose)
            continue;

          connectedGrid.GetBlocks(_cubes);
        }

        _cubes.ShellSort(botPosition);

        for (int j = 0; j < _cubes.Count; j++)
        {
          var slim = _cubes[j];
          if (slim?.CubeGrid == null || slim.IsDestroyed)
            continue;

          var color = MyColorPickerConstants.HSVOffsetToHSV(slim.ColorMaskHSV) * colorVec;
          if (!Vector3.IsZero(grindColor.Value - color, 1E-2f))
            continue;

          var slimPosition = slim.Position;
          if (slim.CubeGrid.EntityId != gridGraph?.MainGrid?.EntityId)
          {
            var slimWorld = slim.CubeGrid.GridIntegerToWorld(slimPosition);

            if (slim.CubeGrid.GridSizeEnum == MyCubeSize.Small)
            {
              if (gravityNormalized.LengthSquared() > 0)
                slimWorld -= gravityNormalized;
              else
                slimWorld += _currentGraph?.WorldMatrix.Up ?? WorldMatrix.Up;
            }

            slimPosition = graph.WorldToLocal(slimWorld);
          }

          var slimGridId = slim.CubeGrid.EntityId;

          if (AiSession.Instance.BlockRepairDelays.Contains(slimGridId, slimPosition) || gridGraph?.IsTileBeingRepaired(slimGridId, slimPosition, botId) == true)
          {
            continue;
          }

          Vector3I _;
          if (_currentGraph.GetClosestValidNode(this, slimPosition, out _, isSlimBlock: true))
          {
            tgt = slim;
            gridGraph?.AddRepairTile(slimGridId, slimPosition, botId, _repairedBlocks);
            break;
          }
        }
      }

      return tgt;
    }

    object GetRepairTarget(CubeGridMap graph, ref bool isGridGraph, ref Vector3D botPosition, out bool isInventory, out bool returnNow)
    {
      object tgt = null;
      isInventory = false;
      returnNow = false;

      if (((byte)MySessionComponentSafeZones.AllowedActions & 8) != 0)
      {
        if (isGridGraph && _pathCollection != null) // path collection is null until first target assignment
        {
          // check for damaged blocks on grid
          var mainGrid = graph.MainGrid as IMyCubeGrid;

          _threadOnlyEntList.Clear();
          MyGamePruningStructure.GetAllEntitiesInOBB(ref graph.OBB, _threadOnlyEntList);

          _cubes.Clear();
          _projectedGrids.Clear();

          for (int i = 0; i < _threadOnlyEntList.Count; i++)
          {
            if (_currentGraph == null || !_currentGraph.IsValid || _currentGraph.Dirty)
            {
              returnNow = true;
              return null;
            }

            var checkGrid = _threadOnlyEntList[i] as MyCubeGrid;
            var projector = checkGrid?.Projector as IMyProjector;
            if (projector != null && projector.BuildableBlocksCount > 0)
            {
              _projectedGrids[projector] = checkGrid;
              continue;
            }

            if (checkGrid?.Physics == null || checkGrid.IsPreview || checkGrid.MarkedForClose)
              continue;

            if (!checkGrid.IsSameConstructAs(mainGrid) && (mainGrid.Physics.LinearVelocity - checkGrid.Physics.LinearVelocity).LengthSquared() > 10)
              continue;

            var owner = checkGrid.BigOwners?.Count > 0 ? checkGrid.BigOwners[0] : checkGrid.SmallOwners?.Count > 0 ? checkGrid.SmallOwners[0] : -1L;
            var relation = MyIDModule.GetRelationPlayerPlayer(Owner.IdentityId, owner);

            if (relation == MyRelationsBetweenPlayers.Self || relation == MyRelationsBetweenPlayers.Allies)
            {
              ((IMyCubeGrid)checkGrid).GetBlocks(_cubes);
            }
          }

          Vector3? ignoreColor = null, grindColor = null;
          var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
          for (int i = 0; i < modData.Count; i++)
          {
            var data = modData[i];
            if (data.OwnerIdentityId == Owner.IdentityId)
            {
              ignoreColor = data.RepairBotIgnoreColorMask;
              grindColor = data.RepairBotGrindColorMask;
              break;
            }
          }

          var colorVec = new Vector3(360, 100, 100);
          var botId = Character.EntityId;

          var gravityNormalized = BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() > 0 ? BotInfo.CurrentGravityAtBotPosition_Nat : BotInfo.CurrentGravityAtBotPosition_Art;
          if (gravityNormalized.LengthSquared() > 0)
            gravityNormalized.Normalize();

          _cubes.ShellSort(botPosition);

          for (int j = 0; j < _cubes.Count; j++)
          {
            var slim = _cubes[j];
            if (slim?.CubeGrid == null || slim.IsDestroyed)
              continue;

            var color = MyColorPickerConstants.HSVOffsetToHSV(slim.ColorMaskHSV) * colorVec;
            if (ignoreColor.HasValue && Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
              continue;
            else if (grindColor.HasValue && Vector3.IsZero(grindColor.Value - color, 1E-2f))
              continue;

            var health = slim.GetBlockHealth(_threadOnlyEntList);
            if (!slim.HasDeformation && (health < 0 || health >= 1))
              continue;

            var node = slim.Position;
            if (slim.CubeGrid.EntityId != graph.MainGrid.EntityId)
            {
              var slimWorld = slim.CubeGrid.GridIntegerToWorld(node);

              if (slim.CubeGrid.GridSizeEnum == MyCubeSize.Small)
              {
                if (gravityNormalized.LengthSquared() > 0)
                  slimWorld -= gravityNormalized;
                else
                  slimWorld += _currentGraph?.WorldMatrix.Up ?? WorldMatrix.Up;
              }

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

          _cubes.Clear();
          //entList.Clear();
          //AiSession.Instance.EntListStack.Push(entList);

          if (tgt == null)
          {
            // check for projections to weld
            //entList.Clear();

            //MyGamePruningStructure.GetAllEntitiesInOBB(ref graph.OBB, entList, MyEntityQueryType.Both);

            //for (int i = 0; i < entList.Count; i++)
            //{
            //  var projGrid = entList[i] as MyCubeGrid;
            //  var projector = projGrid?.Projector as IMyProjector;

            //  if (projector?.BuildableBlocksCount > 0)
            //    _projectedGrids[projector] = projGrid;
            //}

            //entList.Clear();
            //AiSession.Instance.EntListStack.Push(entList);

            foreach (var kvp in _projectedGrids)
            {
              var projector = kvp.Key;
              if (projector?.CubeGrid == null || projector.CubeGrid.MarkedForClose)
                continue;

              if (projector.CubeGrid.EntityId != mainGrid.EntityId)
              {
                _cubes.Clear();
                projector.CubeGrid.GetBlocks(_cubes);
                _cubes.ShellSort(botPosition);

                for (int i = 0; i < _cubes.Count; i++)
                {
                  var slim = _cubes[i];
                  if (slim == null || slim.IsDestroyed)
                    continue;

                  var color = MyColorPickerConstants.HSVOffsetToHSV(slim.ColorMaskHSV) * colorVec;
                  if (ignoreColor.HasValue && Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
                    continue;

                  var health = slim.GetBlockHealth(_threadOnlyEntList);
                  if (!slim.HasDeformation && (health < 0 || health >= 1)) // this may be wrong!
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
                  {
                    returnNow = true;
                    return null;
                  }

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
                _cubes.ShellSort(botPosition);

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
                  {
                    returnNow = true;
                    return null;
                  }

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

      return tgt;
    }

    bool CheckBotInventoryForItems(IMySlimBlock block)
    {
      if (block == null)
      {
        return false;
      }

      if (MyAPIGateway.Session.CreativeMode)
        return true;

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

        var botPosition = BotInfo.CurrentBotPositionAdjusted;
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
          else if (rotation.Y != 0 && (isInv || directToBlock || directToFloater))
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

          var slim = Target.Entity as IMySlimBlock;
          if (slim != null)
          {
            if (CurrentBuildMode == BuildMode.Weld && ((byte)MySessionComponentSafeZones.AllowedActions & 8) != 0)
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
                  pkt = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Weld, terminal?.EntityId ?? 0L, slim.CubeGrid.EntityId, slim.Position, true);

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
            else if (CurrentBuildMode == BuildMode.Grind && ((byte)MySessionComponentSafeZones.AllowedActions & 16) != 0)
            {
              if (GrindBlockManually(slim))
              {
                ParticlePacket pkt;
                if (!_particlePacketSent && !BugZapped)
                {
                  _particlePacketSent = true;
                  var terminal = slim?.FatBlock as IMyTerminalBlock;
                  pkt = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Grind, terminal?.EntityId ?? 0L, slim.CubeGrid.EntityId, slim.Position, true);

                  if (MyAPIGateway.Session.Player != null)
                    pkt.Received(AiSession.Instance.Network);

                  if (MyAPIGateway.Multiplayer.MultiplayerActive)
                    AiSession.Instance.Network.RelayToClients(pkt);
                }
              }
            }
          }
        }
        else
        {
          if (CurrentBuildMode == BuildMode.Grind && Target.IsSlimBlock && Vector3D.DistanceSquared(actualPosition, botPosition) <= 10)
          {
            var slim = Target.Entity as IMySlimBlock;
            slim?.FixBones(0, float.MaxValue);
          }
          
          if (!notMoving && _particlePacketSent)
          {
            _particlePacketSent = false;
            var pType = CurrentBuildMode == BuildMode.Weld ? ParticleInfoBase.ParticleType.Weld : ParticleInfoBase.ParticleType.Grind;
            var packet = new ParticlePacket(Character.EntityId, pType, remove: true);

            if (MyAPIGateway.Session.Player != null)
              packet.Received(AiSession.Instance.Network);

            if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
              AiSession.Instance.Network.RelayToClients(packet);
          }
        }
      }
      else if (_particlePacketSent)
      {
        _particlePacketSent = false;
        var packet = new ParticlePacket(Character.EntityId, ParticleInfoBase.ParticleType.Weld, remove: true);

        if (MyAPIGateway.Session.Player != null)
          packet.Received(AiSession.Instance.Network);

        if (AiSession.Instance.IsServer && MyAPIGateway.Multiplayer.MultiplayerActive)
          AiSession.Instance.Network.RelayToClients(packet);
      }

      MoveToPoint(movement, rotation, roll);
    }

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

          if (_missingComps.Count == 0 && !block.HasDeformation && block.GetBlockHealth(_noThreadEntList) >= 1)
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
        Vector3? grindColor = null;
        var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
        for (int i = 0; i < modData.Count; i++)
        {
          var data = modData[i];
          if (data.OwnerIdentityId == Owner.IdentityId)
          {
            ignoreColor = data.RepairBotIgnoreColorMask;
            grindColor = data.RepairBotGrindColorMask;
            break;
          }
        }

        if (ignoreColor.HasValue || grindColor.HasValue)
        {
          var color = MyColorPickerConstants.HSVOffsetToHSV(block.ColorMaskHSV) * new Vector3(360, 100, 100);
          if (ignoreColor.HasValue && Vector3.IsZero(ignoreColor.Value - color, 1E-2f))
            return false;
          else if (grindColor.HasValue && Vector3.IsZero(grindColor.Value - color, 1E-2f))
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

      if (AiSession.Instance.ModSaveData.ObeyProjectionIntegrityForRepairs)
      {
        var realGrid = block.CubeGrid as MyCubeGrid;
        if (realGrid?.Projector == null)
        {
          _noThreadEntList.Clear();          
          var worldPosition = block.CubeGrid.GridIntegerToWorld(block.Position);
          var sphere = new BoundingSphereD(worldPosition, block.CubeGrid.GridSize * 0.5);
          MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _noThreadEntList);

          for (int i = 0; i < _noThreadEntList.Count; i++)
          {
            var projGrid = _noThreadEntList[i] as MyCubeGrid;
            if (projGrid?.Projector != null)
            {
              var projectedPosition = projGrid.WorldToGridInteger(worldPosition);
              var projectedBlock = projGrid.GetCubeBlock(projectedPosition) as IMySlimBlock;

              var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
              var weldIntegrityAmount = weldAmount / blockDef.IntegrityPointsPerSec;
              if (projectedBlock?.BlockDefinition.Id == blockDef.Id && block.BuildIntegrity + weldIntegrityAmount > projectedBlock.BuildIntegrity)
              {
                weldAmount = projectedBlock.BuildIntegrity - block.BuildIntegrity;
                break;
              }
            }
          }

          if (weldAmount <= 0)
            return false;
        }
      }

      block.IncreaseMountLevel(weldAmount, BotIdentityId, null, boneFixAmount);

      if (isProjection)
      {
        var projGrid = block.CubeGrid as MyCubeGrid;
        var realGrid = projector.CubeGrid as MyCubeGrid;

        if (projGrid != null && realGrid != null)
        {
          var localToWorld = projGrid.GridIntegerToWorld(block.Position);
          var worldToLocal = realGrid.WorldToGridInteger(localToWorld);

          var newBlock = realGrid.GetCubeBlock(worldToLocal) as IMySlimBlock;
          if (newBlock != null)
            Target.SetTarget(Owner, newBlock);
        }
      }

      return true;
    }

    bool GrindBlockManually(IMySlimBlock block)
    {
      var inv = Character?.GetInventory();
      if (inv == null || block == null)
        return false;

      block.DecreaseMountLevel(_toolInfo.GrindAmount, inv);
      block.MoveItemsFromConstructionStockpile(inv);

      if (block.IsDestroyed)
      {
        block.SpawnConstructionStockpile();
        block.CubeGrid.RazeBlock(block.Min);
      }

      return true;
    }
  }
}
