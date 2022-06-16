using AiEnabled.Bots;
using AiEnabled.Parallel;
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
using System.Text;
using System.Threading.Tasks;

using VRage;
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
    public ConcurrentDictionary<string, float> ItemCounts = new ConcurrentDictionary<string, float>(); // component subtype to count
    public ConcurrentDictionary<IMyInventory, List<MyInventoryItem>> Inventories = new ConcurrentDictionary<IMyInventory, List<MyInventoryItem>>();
    public MyCubeGrid Grid;

    public bool Locked { get; private set; }
    public int AccessibleInventoryCount => _inventoryPositions?.Count ?? 0;

    ConcurrentDictionary<IMyInventory, List<MyInventoryItem>> _inventories = new ConcurrentDictionary<IMyInventory, List<MyInventoryItem>>();
    ConcurrentDictionary<Vector3I, IMyTerminalBlock> _inventoryPositions = new ConcurrentDictionary<Vector3I, IMyTerminalBlock>(Vector3I.Comparer);
    Dictionary<string, float> _itemCounts = new Dictionary<string, float>(); // component subtype to count
    HashSet<Vector3I> _terminals = new HashSet<Vector3I>(Vector3I.Comparer);
    List<MyInventoryItem> _invItems = new List<MyInventoryItem>();
    ParallelTasks.Task _task;
    internal bool _needsUpdate = true;
    bool _refreshInvList;
    bool _closed;
    ParallelTasks.Task _repairTask;
    Action<WorkData> _workAction, _workCallBack;
    RepairWorkData _workData;

    public InventoryCache(MyCubeGrid grid)
    {
      Grid = grid;
      _workAction = new Action<WorkData>(RemoveItemsInternal);
      _workCallBack = new Action<WorkData>(RemoveItemsComplete);

      if (!AiSession.Instance.RepairWorkStack.TryPop(out _workData))
        _workData = new RepairWorkData();
    }

    public void SetGrid(MyCubeGrid grid)
    {
      Grid = grid;

      ItemCounts.Clear();
      Inventories.Clear();

      _itemCounts.Clear();
      _inventories.Clear();
      _inventoryPositions.Clear();
      _itemCounts.Clear();
      _terminals.Clear();
      _invItems.Clear();
    }

    public void Close()
    {
      _closed = true;

      if (_workData != null)
        AiSession.Instance.RepairWorkStack.Push(_workData);

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

      _inventoryPositions?.Clear();
      _inventoryPositions = null;

      _terminals?.Clear();
      _terminals = null;

      _itemCounts?.Clear();
      _itemCounts = null;

      _invItems?.Clear();
      _invItems = null;

      _workAction = null;
      _workCallBack = null;
    }

    public IMySlimBlock GetClosestInventory(Vector3I localBot, BotBase bot)
    {
      var range = int.MaxValue;
      IMySlimBlock inv = null;

      foreach (var kvp in _inventoryPositions)
      {
        Vector3I node;
        if (!bot._currentGraph.GetClosestValidNode(bot, kvp.Key, out node, isSlimBlock: true))
        {
          IMyTerminalBlock _;
          _inventoryPositions.TryRemove(kvp.Key, out _);
          continue;
        }

        if (!kvp.Value.IsFunctional)
          continue;

        var block = kvp.Value.SlimBlock;
        if (block.IsBlockUnbuilt())
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

    public bool ContainsItemsFor(IMySlimBlock block, List<MyInventoryItem> botItems)
    {
      if (Inventories.Count == 0)
      {
        return false;
      }

      Dictionary<string, int> missingComps;
      if (!AiSession.Instance.MissingCompsDictStack.TryPop(out missingComps) || missingComps == null)
      {
        missingComps = new Dictionary<string, int>();
      }

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
        missingComps.Clear();
        AiSession.Instance.MissingCompsDictStack.Push(missingComps);
        return false;
      }

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
          valid = false;
          break;
        }
      }

      missingComps.Clear();
      AiSession.Instance.MissingCompsDictStack.Push(missingComps);
      return valid;
    }

    void RemoveItemsInternal(WorkData workData)
    {
      var data = workData as RepairWorkData;
      var block = data.Block;
      var bot = data.Bot?.Character;

      var botInv = bot?.GetInventory();
      if (bot == null || bot.MarkedForClose || botInv == null)
        return;

      _invItems.Clear();
      botInv.GetItems(_invItems);
      var tool = data.Bot.ToolDefinition?.PhysicalItemId.SubtypeName;

      for (int i = _invItems.Count - 1; i >= 0; i--)
      {
        var item = _invItems[i];

        VRage.Game.ModAPI.Ingame.MyItemInfo itemInfo;
        if (!AiSession.Instance.ComponentInfoDict.TryGetValue(item.Type, out itemInfo))
        {
          itemInfo = VRage.Game.ModAPI.Ingame.MyPhysicalInventoryItemExtensions_ModAPI.GetItemInfo(item.Type);
          AiSession.Instance.ComponentInfoDict[item.Type] = itemInfo;
        }

        if (itemInfo.IsOre || itemInfo.IsIngot || itemInfo.IsComponent
          || (itemInfo.IsTool && item.Type.SubtypeId != tool))
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
              inv.AddItems(amount, ob);

              if (amount >= itemAmount)
              {
                _invItems.RemoveAtFast(i);
                break;
              }

              itemAmount -= amount;
            }
          }
        }
      }

      if (botInv.IsFull || block == null || block.IsDestroyed)
      {
        return;
      }

      Dictionary<string, int> missingComps;
      if (!AiSession.Instance.MissingCompsDictStack.TryPop(out missingComps) || missingComps == null)
      {
        missingComps = new Dictionary<string, int>();
      }

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
        missingComps.Clear();
        AiSession.Instance.MissingCompsDictStack.Push(missingComps);
        return;
      }

      var myBotInv = botInv as MyInventory;

      foreach (var kvp in missingComps)
      {
        float needed = kvp.Value;

        for (int i = 0; i < _invItems.Count; i++)
        {
          var item = _invItems[i];
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
              inv.RemoveItems(invItem.ItemId, fixedPoint);
              myBotInv.AddItems(fixedPoint, comp);
              needed -= amount;
            }
          }
        }
      }

      missingComps.Clear();
      AiSession.Instance.MissingCompsDictStack.Push(missingComps);
    }

    void RemoveItemsComplete(WorkData workData)
    {
      _needsUpdate = true;
    }

    public void RemoveItemsFor(IMySlimBlock block, BotBase bot)
    {
      if (_repairTask.IsComplete)
      {
        if (_workData == null && !AiSession.Instance.RepairWorkStack.TryPop(out _workData))
          _workData = new RepairWorkData();

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

      if (!_task.IsComplete || (!_needsUpdate && !_inventoryRefresh))
        return;

      _needsUpdate = false;

      if (_inventoryRefresh)
        _refreshInvList = true;

      _inventoryRefresh = false;
      _task = MyAPIGateway.Parallel.Start(CheckGrids);
    }

    void CheckGrids()
    {
      Locked = true;

      _itemCounts.Clear();

      List<IMyCubeGrid> gridList;
      if (!AiSession.Instance.GridGroupListStack.TryPop(out gridList))
        gridList = new List<IMyCubeGrid>();
      else
        gridList.Clear();

      Grid.GetGridGroup(GridLinkTypeEnum.Mechanical)?.GetGrids(gridList);

      for (int i = gridList.Count - 1; i >= 0; i--)
      {
        var grid = gridList[i];
        if (grid?.Physics != null && !grid.MarkedForClose && grid.GridSizeEnum == MyCubeSize.Large)
          CheckInventories(grid);
      }

      gridList.Clear();
      AiSession.Instance.GridGroupListStack.Push(gridList);

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
          if (item.Type.TypeId != "MyObjectBuilder_Component")
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
      List<IMySlimBlock> blockList;
      if (!AiSession.Instance.SlimListStack.TryPop(out blockList))
        blockList = new List<IMySlimBlock>();
      else
        blockList.Clear();

      grid.GetBlocks(blockList);
      for (int i = 0; i < blockList.Count; i++)
      {
        var terminal = blockList[i]?.FatBlock as IMyTerminalBlock;
        if (terminal == null || !terminal.HasInventory)
          continue;

        if (!(terminal is IMyCargoContainer) && !(terminal is IMyShipConnector) && !(terminal is IMyShipToolBase) && !(terminal is IMyAssembler))
          continue;

        for (int j = 0; j < terminal.InventoryCount; j++)
        {
          var inv = terminal.GetInventory(j);
          if (inv.MaxVolume == 0)
            return;
        }

        bool hookEvents = _terminals.Add(terminal.Position);
        if (hookEvents)
        {
          if (grid.EntityId == Grid.EntityId && (terminal is IMyCargoContainer || terminal is IMyShipConnector))
            _inventoryPositions.TryAdd(terminal.Position, terminal);

          terminal.OnClosing += Terminal_OnClosing;
        }
        else if (_refreshInvList)
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
        }
      }

      blockList.Clear();
      AiSession.Instance.SlimListStack.Push(blockList);
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
        AiSession.Instance.Logger.Log($"Error in {GetType().FullName}: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }
  }
}
