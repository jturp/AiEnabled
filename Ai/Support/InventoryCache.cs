﻿using AiEnabled.Bots;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Parallel;
using AiEnabled.Support;
using AiEnabled.Utilities;

using ParallelTasks;

using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

using VRageMath;

using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace AiEnabled.Ai.Support
{
  public class InventoryCache
  {
    internal class InventoryAddRemove
    {
      public IMyInventory Inventory;
      public MyObjectBuilder_PhysicalObject Item;
      public uint ItemId;
      public MyFixedPoint Amount;
      public bool Add;

      public void Set(IMyInventory inv, MyObjectBuilder_PhysicalObject obj, MyFixedPoint amount, bool add)
      {
        Inventory = inv;
        Item = obj;
        Amount = amount;
        Add = add;
      }

      public void Set(IMyInventory inv, uint itemId, MyFixedPoint amount, bool add)
      {
        Inventory = inv;
        ItemId = itemId;
        Amount = amount;
        Add = add;
      }

      public void Clear()
      {
        Inventory = null;
        Item = null;
        ItemId = 0;
        Amount = -1;
      }
    }

    public ConcurrentDictionary<string, float> ItemCounts = new ConcurrentDictionary<string, float>(); // component subtype to count
    public ConcurrentDictionary<IMyInventory, List<MyInventoryItem>> Inventories = new ConcurrentDictionary<IMyInventory, List<MyInventoryItem>>();
    public MyCubeGrid Grid;

    public bool Locked { get; private set; }
    public int AccessibleInventoryCount => _inventoryPositions?.Count ?? 0;

    ConcurrentDictionary<IMyInventory, List<MyInventoryItem>> _inventories = new ConcurrentDictionary<IMyInventory, List<MyInventoryItem>>();
    ConcurrentDictionary<Vector3I, IMyTerminalBlock> _inventoryPositions = new ConcurrentDictionary<Vector3I, IMyTerminalBlock>(Vector3I.Comparer);
    Dictionary<string, float> _itemCounts = new Dictionary<string, float>(); // component subtype to count
    HashSet<Vector3I> _terminals = new HashSet<Vector3I>(Vector3I.Comparer);
    internal bool _needsUpdate = true;
    bool _refreshInvList;
    bool _closed;
    ParallelTasks.Task _repairTask, _updateTask;
    Action<WorkData> _workAction, _workCallBack;
    RepairWorkData _workData;
    List<InventoryAddRemove> _inventoryItemsToAddRemove = new List<InventoryAddRemove>();
    Stack<InventoryAddRemove> _invItemPool = new Stack<InventoryAddRemove>();

    AiEPool<List<MyInventoryItem>> _invItemListStack = new AiEPool<List<MyInventoryItem>>
    (
      defaultCapacity: 100,
      clear: (x) => x.Clear(),
      activator: () => new List<MyInventoryItem>(),
      deactivator: (x) => { x.Clear(); x = null; }
    );

    public InventoryCache(MyCubeGrid grid)
    {
      Grid = grid;
      _workAction = new Action<WorkData>(RemoveItemsInternal);
      _workCallBack = new Action<WorkData>(RemoveItemsComplete);
      _workData = AiSession.Instance.RepairWorkPool.Get();

      if (Grid != null)
      {
        Grid.OnFatBlockAdded -= Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockRemoved -= Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockAdded += Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockRemoved += Grid_OnFatBlockAddRemove;
      }
    }

    public InventoryCache()
    {
      _workAction = new Action<WorkData>(RemoveItemsInternal);
      _workCallBack = new Action<WorkData>(RemoveItemsComplete);
    }

    public void SetGrid(MyCubeGrid grid)
    {
      if (Grid != null)
      {
        Grid.OnFatBlockAdded -= Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockRemoved -= Grid_OnFatBlockAddRemove;
      }

      Grid = grid;

      if (Grid != null)
      {
        Grid.OnFatBlockAdded -= Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockRemoved -= Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockAdded += Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockRemoved += Grid_OnFatBlockAddRemove;
      }

      if (_workData == null)
        _workData = AiSession.Instance.RepairWorkPool.Get();

      ItemCounts.Clear();
      Inventories.Clear();

      _itemCounts.Clear();
      _inventories.Clear();
      _inventoryPositions.Clear();
      _itemCounts.Clear();
      _terminals.Clear();
    }

    private void Grid_OnFatBlockAddRemove(MyCubeBlock cube)
    {
      if (cube.InventoryCount > 0)
      {
        _needsUpdate = true;
        _inventoryRefresh = true;
      }
    }

    public void Close()
    {
      if (Grid != null)
      {
        Grid.OnFatBlockAdded -= Grid_OnFatBlockAddRemove;
        Grid.OnFatBlockRemoved -= Grid_OnFatBlockAddRemove;
      }

      _closed = true;

      if (_workData != null)
        AiSession.Instance.RepairWorkPool?.Return(ref _workData);

      ItemCounts?.Clear();
      ItemCounts = null;

      if (Inventories != null)
      {
        foreach (var list in Inventories.Values)
          list?.Clear();

        Inventories?.Clear();
        Inventories = null;
      }

      if (_inventories != null)
      {
        foreach (var list in _inventories.Values)
          list?.Clear();

        _inventories?.Clear();
        _inventories = null;
      }

      _invItemListStack?.Clean();
      _invItemListStack = null;

      _inventoryItemsToAddRemove?.Clear();
      _inventoryItemsToAddRemove = null;

      _inventoryPositions?.Clear();
      _inventoryPositions = null;

      _invItemPool?.Clear();
      _invItemPool = null;

      _terminals?.Clear();
      _terminals = null;

      _itemCounts?.Clear();
      _itemCounts = null;

      _workAction = null;
      _workCallBack = null;
    }

    public IMySlimBlock GetClosestInventory(Vector3I localBot, BotBase bot)
    {
      var range = int.MaxValue;
      var rBot = bot as RepairBot;
      IMySlimBlock inv = null;

      Vector3? grindColor = null;
      var colorVec = new Vector3(360, 100, 100);

      if (bot?.Owner != null && AiSession.Instance?.ModSaveData?.PlayerHelperData != null)
      {
        var modData = AiSession.Instance.ModSaveData.PlayerHelperData;
        for (int i = 0; i < modData.Count; i++)
        {
          var data = modData[i];
          if (data.OwnerIdentityId == bot.Owner.IdentityId)
          {
            grindColor = data.RepairBotGrindColorMask;
            break;
          }
        }
      }

      var gridGraph = AiSession.Instance.GridGraphDict.GetValueOrDefault(Grid.EntityId, null);

      foreach (var kvp in _inventoryPositions)
      {
        var block = kvp.Value.SlimBlock;
        if (block == null)
          continue;

        var funcBlock = kvp.Value as IMyFunctionalBlock;
        if (funcBlock != null && !funcBlock.Enabled)
          continue;

        var connector = kvp.Value as IMyShipConnector;
        if (connector != null)
        {
          if (connector.ThrowOut || connector.IsConnected || gridGraph == null || !gridGraph.IsValid || !gridGraph.Ready)
            continue;

          MatrixI m = new MatrixI(connector.Orientation);

          var left = connector.Position + m.LeftVector;
          var right = connector.Position + m.RightVector;
          var fwd = connector.Position + m.ForwardVector;
          var bwd = connector.Position + m.BackwardVector;
          var up = connector.Position + m.UpVector;

          bool isValid = (gridGraph.IsOpenTile(left) && !gridGraph.IsObstacle(left, bot, false))
            || (gridGraph.IsOpenTile(right) && !gridGraph.IsObstacle(right, bot, false))
            || (gridGraph.IsOpenTile(fwd) && !gridGraph.IsObstacle(fwd, bot, false))
            || (gridGraph.IsOpenTile(bwd) && !gridGraph.IsObstacle(bwd, bot, false))
            || (gridGraph.IsOpenTile(up) && !gridGraph.IsObstacle(up, bot, false));

          if (!isValid)
            continue;
        }

        if (grindColor != null)
        {
          var color = MyColorPickerConstants.HSVOffsetToHSV(block.ColorMaskHSV) * colorVec;
          if (Vector3.IsZero(grindColor.Value - color, 1E-2f))
            continue;
        }

        Vector3I node;
        if (!bot._currentGraph.GetClosestValidNode(bot, kvp.Key, out node, isSlimBlock: true))
        {
          bool valid = false;

          foreach (var dir in AiSession.Instance.CardinalDirections)
          {
            var pos = kvp.Key + dir;
            if (bot._currentGraph.IsOpenTile(pos))
            {
              valid = true;
              break;
            }
          }

          if (!valid)
          {
            IMyTerminalBlock _;
            _inventoryPositions.TryRemove(kvp.Key, out _);
          }

          continue;
        }

        if (!kvp.Value.IsFunctional)
          continue;

        if (block.IsBlockUnbuilt())
          continue;

        if (rBot?.CurrentBuildMode == RepairBot.BuildMode.Grind && (block.FatBlock is IMyProductionBlock || block.FatBlock is IMyShipToolBase))
          continue;

        var dist = Vector3I.DistanceManhattan(node, localBot);

        if (dist < range)
        {
          range = dist;
          inv = kvp.Value.SlimBlock;
        }
      }

      return inv;
    }

    public bool ContainsItem(MyDefinitionId itemDef)
    {
      return Inventories.Count > 0 && !itemDef.TypeId.IsNull && ItemCounts.GetValueOrDefault(itemDef.ToString(), 0) > 0;
    }

    public bool TryMoveItem(MyDefinitionId itemDef, float numToMove, MyInventoryBase destination)
    {
      float num;
      if (Inventories == null || Inventories.Count == 0 || itemDef.TypeId.IsNull || !ItemCounts.TryGetValue(itemDef.SubtypeName, out num) || num < numToMove)
        return false;

      bool found = false;

      foreach (var invKvp in Inventories)
      {
        var inv = invKvp.Key;
        var invList = invKvp.Value;
        for (int i = 0; i < invList.Count; i++)
        {
          var invItem = invList[i];
          if (invItem.Type == (MyItemType)itemDef)
          {

            float amount = (float)invItem.Amount;
            if (amount < numToMove)
              continue;

            var fixedPoint = (MyFixedPoint)numToMove;

            inv.RemoveItems(invItem.ItemId, fixedPoint);

            var toAdd = MyObjectBuilderSerializer.CreateNewObject(itemDef);
            destination.AddItems(fixedPoint, toAdd);

            found = true;
            break;
          }
        }

        if (found)
          break;
      }

      return found;
    }

    public bool ContainsItemsFor(IMySlimBlock block, List<MyInventoryItem> botItems, BotBase bot)
    {
      var rBot = bot as RepairBot;
      if (rBot != null && !rBot.FirstMissingItemAssigned)
      {
        rBot.FirstMissingItemForRepairs = null;
        rBot.FirstMissingItemBlock = null;
      }
      else if (Inventories.Count == 0)
        return false;

      bool valid = true;
      Dictionary<string, int> missingComps = AiSession.Instance.MissingCompsDictPool.Get();
      missingComps.Clear();
      block.GetMissingComponents(missingComps);

      if (missingComps.Count == 0)
      {
        var myGrid = block.CubeGrid as MyCubeGrid;
        var projector = myGrid?.Projector as IMyProjector;
        if (projector?.CanBuild(block, true) == BuildCheckResult.OK)
        {
          block.GetMissingComponentsProjected(missingComps, null);
          if (missingComps.Count == 0)
            valid = false;
        }
        else
          valid = false;
      }

      if (!valid)
      {
        AiSession.Instance.MissingCompsDictPool?.Return(ref missingComps);
        return false;
      }

      if (Inventories.Count == 0)
      {
        if (rBot != null && !rBot.FirstMissingItemAssigned)
        {
          var firstComp = missingComps.First().Key;
          var def = new MyDefinitionId(typeof(MyObjectBuilder_Component), firstComp);

          MyDefinitionBase compDef;
          if (AiSession.Instance.AllGameDefinitions.TryGetValue(def, out compDef) && compDef != null)
          {
            var terminal = block.FatBlock as IMyTerminalBlock;
            rBot.FirstMissingItemBlock = terminal?.CustomName ?? block.BlockDefinition.DisplayNameText;
            rBot.FirstMissingItemForRepairs = compDef.DisplayNameText;
            rBot.FirstMissingItemAssigned = true;
          }
        }

        AiSession.Instance.MissingCompsDictPool?.Return(ref missingComps);
        return false;
      }

      bool atLeastOne = false;
      foreach (var kvp in missingComps)
      {
        float needed = kvp.Value;

        for (int i = 0; i < botItems.Count; i++)
        {
          var item = botItems[i];
          if (item.Type.SubtypeId == kvp.Key)
            needed -= (float)item.Amount;
        }

        float num;
        ItemCounts.TryGetValue(kvp.Key, out num);
        if (num < needed)
        {
          if (rBot != null && !rBot.FirstMissingItemAssigned)
          {
            var def = new MyDefinitionId(typeof(MyObjectBuilder_Component), kvp.Key);

            MyDefinitionBase compDef;
            if (AiSession.Instance.AllGameDefinitions.TryGetValue(def, out compDef) && compDef != null)
            {
              var terminal = block.FatBlock as IMyTerminalBlock;
              rBot.FirstMissingItemBlock = terminal?.CustomName ?? block.BlockDefinition.DisplayNameText;
              rBot.FirstMissingItemForRepairs = compDef.DisplayNameText;
              rBot.FirstMissingItemAssigned = true;
            }
          }

          valid = atLeastOne || num > 0;
          break;
        }

        atLeastOne = true;
      }

      AiSession.Instance.MissingCompsDictPool?.Return(ref missingComps);
      return valid;
    }

    public bool ShouldKeepItem(MyInventoryItem item)
    {
      var def = item.Type;

      if (AiSession.Instance?.Registered == true && AiSession.Instance.ModSaveData?.InventoryItemsToKeep?.Count > 0)
      {
        for (int i = 0; i < AiSession.Instance.ModSaveData.InventoryItemsToKeep.Count; i++)
        {
          var keeper = AiSession.Instance.ModSaveData.InventoryItemsToKeep[i];
          if (def.SubtypeId.StartsWith(keeper, StringComparison.OrdinalIgnoreCase) || keeper.StartsWith(def.SubtypeId, StringComparison.OrdinalIgnoreCase))
            return true;
        }
      }

      return false;
    }

    public bool ShouldKeepTool(MyInventoryItem item, List<MyInventoryItem> botItems)
    {
      var subtype = item.Type.SubtypeId;

      bool isWelder = subtype.IndexOf("Welder") >= 0;
      bool isGrinder = !isWelder && subtype.IndexOf("Grinder") >= 0;

      if (!isWelder && !isGrinder)
        return false;

      var tier = GetToolTier(subtype);

      int num = 0;
      for (int i = 0; i < botItems.Count; i++)
      {
        var botItem = botItems[i];
        var itemSubtype = botItem.Type.SubtypeId;
        if (itemSubtype == subtype)
          num++;

        if (num > 1)
          return false;

        if ((isWelder && botItem.Type.SubtypeId.IndexOf("Welder") >= 0)
          || (isGrinder && botItem.Type.SubtypeId.IndexOf("Grinder") >= 0))
        {
          var toolTier = GetToolTier(itemSubtype);
          if (toolTier > tier)
            return false;
        }
      }

      return true;
    }

    int GetToolTier(string subtype)
    {
      int priority;
      if (subtype.IndexOf("2") >= 0)
        priority = 2;
      else if (subtype.IndexOf("3") >= 0)
        priority = 3;
      else if (subtype.IndexOf("4") >= 0)
        priority = 4;
      else
        priority = 1;

      return priority;
    }

    public bool ShouldSendToUnload(BotBase bot)
    {
      if (bot == null || bot.IsDead)
        return false;

      List<MyInventoryItem> invItems = _invItemListStack.Get();

      var rBot = bot as RepairBot;
      var botInv = bot.Character.GetInventory();
      botInv.GetItems(invItems);

      var tool = bot.ToolDefinition?.PhysicalItemId.SubtypeName;
      var grindMode = rBot == null || rBot.CurrentBuildMode == BotBase.BuildMode.Grind;

      bool sendToUnload = false;

      for (int i = invItems.Count - 1; i >= 0; i--)
      {
        var item = invItems[i];

        VRage.Game.ModAPI.Ingame.MyItemInfo itemInfo;
        if (!AiSession.Instance.ItemInfoDict.TryGetValue(item.Type, out itemInfo))
        {
          itemInfo = VRage.Game.ModAPI.Ingame.MyPhysicalInventoryItemExtensions_ModAPI.GetItemInfo(item.Type);
          AiSession.Instance.ItemInfoDict[item.Type] = itemInfo;
        }

        if (itemInfo.IsTool && rBot != null && (item.Type.SubtypeId == tool || ShouldKeepTool(item, invItems)))
          continue;

        if (ShouldKeepItem(item))
          continue;

        if (grindMode || itemInfo.IsOre || itemInfo.IsIngot || itemInfo.IsComponent)
        {
          sendToUnload = true;
          break;
        }
      }

      _invItemListStack.Return(ref invItems);
      return sendToUnload;
    }

    void RemoveItemsInternal(WorkData workData)
    {
      var data = workData as RepairWorkData;
      var block = data.Block;
      var bot = data.Bot?.Character;
      var botInv = bot?.GetInventory();
      var rBot = data.Bot as RepairBot;

      if (bot == null || bot.MarkedForClose || botInv == null)
        return;

      List<MyInventoryItem> invItems = _invItemListStack.Get();

      botInv.GetItems(invItems);
      var tool = data.Bot.ToolDefinition?.PhysicalItemId.SubtypeName;
      var grindMode = rBot == null || rBot.CurrentBuildMode == BotBase.BuildMode.Grind;
      _inventoryItemsToAddRemove.Clear();

      for (int i = invItems.Count - 1; i >= 0; i--)
      {
        var item = invItems[i];

        VRage.Game.ModAPI.Ingame.MyItemInfo itemInfo;
        if (!AiSession.Instance.ItemInfoDict.TryGetValue(item.Type, out itemInfo))
        {
          itemInfo = VRage.Game.ModAPI.Ingame.MyPhysicalInventoryItemExtensions_ModAPI.GetItemInfo(item.Type);
          AiSession.Instance.ItemInfoDict[item.Type] = itemInfo;
        }

        if (itemInfo.IsTool && rBot != null && (item.Type.SubtypeId == tool || ShouldKeepTool(item, invItems)))
          continue;

        if (ShouldKeepItem(item))
          continue;

        if (grindMode || itemInfo.IsOre || itemInfo.IsIngot || itemInfo.IsComponent)
        {
          var itemAmount = item.Amount;
          foreach (var invKvp in Inventories)
          {
            var inv = invKvp.Key;
            var myInv = inv as MyInventory;

            if (myInv == null || myInv.IsFull)
              continue;

            var invOwner = inv.Owner as IMyTerminalBlock;
            if (invOwner == null)
              continue;

            if (grindMode && (invOwner is IMyProductionBlock || invOwner is IMyShipToolBase))
              continue;

            List<MyItemType> acceptedItems;
            if (!AiSession.Instance.AcceptedItemDict.TryGetValue(invOwner.BlockDefinition, out acceptedItems))
            {
              acceptedItems = new List<MyItemType>();
              inv.GetAcceptedItems(acceptedItems);
            }

            if (!acceptedItems.Contains(item.Type))
              continue;

            var amountToFit = myInv.ComputeAmountThatFits(item.Type);
            if (amountToFit > 0)
            {
              var amount = MyFixedPoint.Min(amountToFit, itemAmount);

              MyObjectBuilder_PhysicalObject ob;
              if (!AiSession.Instance.ItemOBDict.TryGetValue(item.Type, out ob))
              {
                ob = MyObjectBuilderSerializer.CreateNewObject((MyDefinitionId)item.Type) as MyObjectBuilder_PhysicalObject;
                AiSession.Instance.ItemOBDict[item.Type] = ob;
              }

              botInv.RemoveItemsAt(i, amount);

              var invAR = _invItemPool.Count > 0 ? _invItemPool.Pop() : new InventoryAddRemove();
              invAR.Set(inv, ob, amount, true);
              _inventoryItemsToAddRemove.Add(invAR);
              //inv.AddItems(amount, ob);

              if (amount >= itemAmount)
              {
                invItems.RemoveAtFast(i);
                break;
              }

              itemAmount -= amount;
            }
          }
        }
      }

      if (grindMode || botInv.IsFull || block == null || block.IsDestroyed)
      {
        _invItemListStack.Return(ref invItems);
        return;
      }

      Dictionary<string, int> missingComps = AiSession.Instance.MissingCompsDictPool.Get();

      bool valid = true;

      missingComps.Clear();
      block.GetMissingComponents(missingComps);

      if (missingComps.Count == 0)
      {
        var myGrid = block.CubeGrid as MyCubeGrid;
        var projector = myGrid?.Projector as IMyProjector;
        if (projector?.CanBuild(block, true) == BuildCheckResult.OK)
        {
          block.GetMissingComponentsProjected(missingComps, null);
          if (missingComps.Count == 0)
            valid = false;
        }
        else
          valid = false;
      }

      if (!valid)
      {
        AiSession.Instance.MissingCompsDictPool?.Return(ref missingComps);
        _invItemListStack.Return(ref invItems);
        return;
      }

      var myBotInv = botInv as MyInventory;

      foreach (var kvp in missingComps)
      {
        float needed = kvp.Value;

        for (int i = 0; i < invItems.Count; i++)
        {
          var item = invItems[i];
          if (item.Type.SubtypeId == kvp.Key)
            needed -= (float)item.Amount;
        }

        needed = (float)Math.Ceiling(needed);
        bool isFull = false;

        foreach (var invKvp in Inventories)
        {
          if (isFull || needed <= 0)
            break;

          var inv = invKvp.Key;
          var invList = invKvp.Value;
          for (int i = 0; i < invList.Count; i++)
          {
            if (isFull || needed <= 0)
              break;

            var invItem = invList[i];
            if (invItem.Type.TypeId != "MyObjectBuilder_Component" || invItem.Type.SubtypeId != kvp.Key)
              continue;

            float amount = (float)invItem.Amount;
            var left = amount - needed;
            if (left > 0)
              amount -= left;

            MyObjectBuilder_Component comp;
            if (!AiSession.Instance.ComponentBuilderDict.TryGetValue(kvp.Key, out comp))
            {
              comp = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Component>(kvp.Key);
              AiSession.Instance.ComponentBuilderDict[kvp.Key] = comp;
            }

            var fixedPoint = (MyFixedPoint)amount;
            var maxFit = myBotInv.ComputeAmountThatFits(comp.GetId());

            if (maxFit < fixedPoint)
            {
              fixedPoint = maxFit;
              amount = (float)maxFit;
              isFull = true;
            }
            else if (kvp.Key == "SteelPlate" && amount <= 1)
            {
              var leftOver = maxFit - fixedPoint;
              var added = MyFixedPoint.Min(5, leftOver);
              fixedPoint += added;
              amount += (float)added;
            }

            if (amount > 0)
            {
              //inv.RemoveItems(invItem.ItemId, fixedPoint);
              var invAR = _invItemPool.Count > 0 ? _invItemPool.Pop() : new InventoryAddRemove();
              invAR.Set(inv, invItem.ItemId, fixedPoint, false);
              _inventoryItemsToAddRemove.Add(invAR);

              myBotInv.AddItems(fixedPoint, comp);
              needed -= amount;
            }
          }
        }
      }

      AiSession.Instance.MissingCompsDictPool?.Return(ref missingComps);
      _invItemListStack.Return(ref invItems);
    }

    void RemoveItemsComplete(WorkData workData)
    {
      try
      {
        for (int i = 0; i < _inventoryItemsToAddRemove.Count; i++)
        {
          var invAR = _inventoryItemsToAddRemove[i];
          var inv = invAR.Inventory;
          var amount = invAR.Amount;

          if (invAR.Add)
          {
            if (invAR.Item != null)
              inv?.AddItems(amount, invAR.Item);
          }
          else
          {
            inv?.RemoveItems(invAR.ItemId, amount);
          }

          invAR.Clear();
          _invItemPool.Push(invAR);
        }

        _needsUpdate = true;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in InventoryCache.RemoveItemsComplete: {ex}");
      }
    }

    public void RemoveItemsFor(IMySlimBlock block, BotBase bot)
    {
      if (_repairTask.IsComplete)
      {
        if (_workData == null)
          _workData = AiSession.Instance.RepairWorkPool.Get();

        _workData.Bot = bot;
        _workData.Block = block;

        _repairTask = MyAPIGateway.Parallel.Start(_workAction, _workCallBack, _workData);

        // testing only
        //RemoveItemsInternal(_workData);
        //RemoveItemsComplete(_workData);
      }
    }

    bool _inventoryRefresh;
    public void Update(bool refreshInventories)
    {
      _inventoryRefresh |= refreshInventories;

      if (!_updateTask.IsComplete || (!_needsUpdate && !_inventoryRefresh))
        return;

      _needsUpdate = false;

      if (_inventoryRefresh)
        _refreshInvList = true;

      _inventoryRefresh = false;
      _updateTask = MyAPIGateway.Parallel.Start(CheckGrids);
    }

    void CheckGrids()
    {
      Locked = true;

      _itemCounts.Clear();

      List<IMyCubeGrid> gridList = AiSession.Instance.GridGroupListPool.Get();

      Grid.GetGridGroup(GridLinkTypeEnum.Mechanical)?.GetGrids(gridList);

      for (int i = gridList.Count - 1; i >= 0; i--)
      {
        var grid = gridList[i];
        if (grid?.Physics != null && !grid.MarkedForClose && grid.GridSizeEnum == MyCubeSize.Large)
          CheckInventories(grid);
      }

      AiSession.Instance.GridGroupListPool?.Return(ref gridList);

      _refreshInvList = false;

      foreach (var kvp in _inventories)
      {
        List<MyInventoryItem> items;
        if (!Inventories.TryGetValue(kvp.Key, out items))
        {
          items = new List<MyInventoryItem>();
          if (!Inventories.TryAdd(kvp.Key, items))
            continue;
        }

        items.Clear();
        foreach (var item in kvp.Value)
        {
          if (item.Type.TypeId != "MyObjectBuilder_Component" && item.Type.TypeId != "MyObjectBuilder_AmmoMagazine")
          {
            continue;
          }

          float num;
          _itemCounts.TryGetValue(item.Type.SubtypeId, out num);
          num += (float)item.Amount;

          _itemCounts[item.Type.SubtypeId] = num;
          items.Add(item);
        }
      }

      ItemCounts.Clear();
      foreach (var kvp in _itemCounts)
      {
        ItemCounts[kvp.Key] = kvp.Value;
      }

      Locked = false;
    }

    void CheckInventories(IMyCubeGrid grid)
    {
      List<IMySlimBlock> blockList = AiSession.Instance.SlimListPool.Get();
      grid.GetBlocks(blockList);
      for (int i = 0; i < blockList.Count; i++)
      {
        var terminal = blockList[i]?.FatBlock as IMyTerminalBlock;
        if (terminal == null || !terminal.HasInventory)
          continue;

        if (!(terminal is IMyCargoContainer) && !(terminal is IMyShipConnector) && !(terminal is IMyShipToolBase) && !(terminal is IMyAssembler))
          continue;

        if (terminal.CustomName.IndexOf("[AiExclude]", StringComparison.OrdinalIgnoreCase) >= 0 || terminal.CustomData.IndexOf("[AiExclude]", StringComparison.OrdinalIgnoreCase) >= 0)
          continue;

        for (int j = 0; j < terminal.InventoryCount; j++)
        {
          var inv = terminal.GetInventory(j);
          if (inv.MaxVolume == 0)
            return;
        }

        bool allowInvPosition = grid.EntityId == Grid.EntityId
          && (terminal is IMyShipConnector || (terminal is IMyCargoContainer
          && (terminal.BlockDefinition.SubtypeName.IndexOf("container", StringComparison.OrdinalIgnoreCase) >= 0)
          || terminal.BlockDefinition.SubtypeName.IndexOf("freight", StringComparison.OrdinalIgnoreCase) >= 0));

        bool hookEvents = _terminals.Add(terminal.Position);
        if (hookEvents)
        {
          if (allowInvPosition)
            _inventoryPositions.TryAdd(terminal.Position, terminal);

          terminal.OnClosing += Terminal_OnClosing;
        }
        else if (_refreshInvList && allowInvPosition)
          _inventoryPositions.TryAdd(terminal.Position, terminal);

        for (int j = 0; j < terminal.InventoryCount; j++)
        {
          var inv = terminal.GetInventory(j);
          if (inv == null || (terminal is IMyAssembler && j == 0))
            continue;

          if (hookEvents)
          {
            ((MyInventory)inv).ContentsChanged += InventoryCache_ContentsChanged;
          }

          List<MyInventoryItem> items;
          if (!_inventories.TryGetValue(inv, out items))
          {
            items = new List<MyInventoryItem>();
            if (!_inventories.TryAdd(inv, items))
              continue;
          }

          items.Clear();
          inv.GetItems(items);

          // TODO: Why is this here? Remove??
        }
      }

      AiSession.Instance.SlimListPool?.Return(ref blockList);
    }

    private void InventoryCache_ContentsChanged(MyInventoryBase obj)
    {
      _needsUpdate = true;
    }

    private void Terminal_OnClosing(VRage.ModAPI.IMyEntity obj)
    {
      try
      {
        var terminal = obj as IMyTerminalBlock;
        if (terminal == null)
          return;

        terminal.OnClosing -= Terminal_OnClosing;

        for (int i = 0; i < terminal.InventoryCount; i++)
        {
          var inv = terminal.GetInventory(i);
          if (inv == null)
            continue;

          ((MyInventory)inv).ContentsChanged -= InventoryCache_ContentsChanged;

          if (_closed)
            continue;

          List<MyInventoryItem> items;
          if (_inventories.TryRemove(inv, out items))
          {
            items?.Clear();
            items = null;
          }

          if (Inventories.TryRemove(inv, out items))
          {
            items?.Clear();
            items = null;
          }
        }

        if (_closed)
          return;

        IMyTerminalBlock _;
        _inventoryPositions.TryRemove(terminal.Position, out _);
        _terminals.Remove(terminal.Position);
        _needsUpdate = true;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in {GetType().FullName}: {ex}");
      }
    }
  }
}