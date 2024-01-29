using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;

using VRage.Game.ModAPI;

using VRageMath;
using AiEnabled.Ai.Support;
using AiEnabled.Support;
using AiEnabled.Utilities;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Ai.Support.PriorityQ;
using VRage.ModAPI;
using System.Threading;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage;
using VRage.Collections;
using VRage.Game;
using AiEnabled.Bots;

namespace AiEnabled.Ai
{
  public static class PathFinding
  {
    public static void FindPath(Vector3I start, Vector3I goal, PathCollection collection, bool isIntendedGoal)
    {
      try
      {
        if (collection == null || collection.Dirty || collection.Locked)
        {
          return;
        }

        collection.Locked = true;

        var graph = collection.Graph;
        var bot = collection.Bot;
        if (graph == null || bot == null || !graph.Ready || graph.Dirty || graph.Remake || bot.IsDead)
        {
          collection.Locked = false;
          return;
        }

        bool groundNodesFirst = AiSession.Instance.ModSaveData.EnforceGroundPathingFirst && !collection.Bot.RequiresJetpack;
        bool pathFound = RunAlgorithm(start, goal, collection, groundNodesFirst);

        if (groundNodesFirst && !pathFound)
        {
          collection.PathTimer.Reset();
          pathFound = RunAlgorithm(start, goal, collection, false);
        }

        var currentMS = collection.PathTimer.Elapsed.TotalMilliseconds;
        var maxTimeMS = AiSession.Instance.ModSaveData.MaxPathfindingTimeInSeconds * 1000;
        if (collection.Dirty || currentMS > maxTimeMS)
        {
          if (currentMS > maxTimeMS)
            AiSession.Instance.Logger.Log($"{collection.Bot.Character.Name} - PathTimer exceeded {maxTimeMS} ms pathing to {goal}", MessageType.WARNING);
          pathFound = false;
        }

        if (pathFound)
        {
          if (isIntendedGoal)
            bot._noPathCounter = 0;
          else
            bot._noPathCounter++;

          if (graph.IsGridGraph)
            ConstructPathForGrid(start, goal, collection);
          else
            ConstructPathForVoxel(start, goal, collection);
        }
        else if (!collection.Dirty)
        {
          bool isInventory = false;
          IMySlimBlock invBlock = null;

          if (graph.IsGridGraph && bot is RepairBot)
          {
            isInventory = bot.Target.IsInventory;
            invBlock = bot.Target.Inventory;

            if (!isInventory && !bot.Target.IsSlimBlock)
            {
              bot._noPathCounter++;
            }
            else
            {
              bot.Target.RemoveTarget();
            }
          }
          else
          {
            bot._noPathCounter++;
          }

          if (!isInventory)
          {
            if (graph.IsTemporaryBlock(goal))
            {
              if (!graph.TempBlockedNodes.ContainsKey(goal))
              {
                //AiSession.Instance.Logger.Log($"{graph}: adding {goal} to temp obstacles from Pathfinding");
                graph.TempBlockedNodes[goal] = new byte();
              }
            }
            else if (!collection.Obstacles.ContainsKey(goal))
            {
              //AiSession.Instance.Logger.Log($"{graph}: adding {goal} to permanent obstacles from Pathfinding");
              collection.Obstacles[goal] = new byte();
            }

            var cube = bot.Target.Entity as IMyCubeBlock;
            var slim = (cube?.SlimBlock) ?? bot.Target.Entity as IMySlimBlock;

            if (slim?.CubeGrid != null && graph.IsPositionValid(slim.CubeGrid.GridIntegerToWorld(slim.Position)))
            {
              var gridGraph = graph as CubeGridMap;

              var gridSize = slim.CubeGrid.GridSizeEnum;
              if (gridSize == MyCubeSize.Small)
              {
                bot.Target.RemoveTarget();
                collection.AddBlockedObstacle(slim);
              }
              else if (gridGraph != null && slim.CubeGrid.IsSameConstructAs(gridGraph.MainGrid))
              {
                bot.Target.RemoveTarget();
                collection.AddBlockedObstacle(slim);
              }
            }
          }
          else
          {
            //AiSession.Instance.Logger.Log($"Bot failed to find path to inventory: Start = {start}, Goal = {goal}, InvPos = {invBlock.Position}");

            if (!graph.TempBlockedNodes.ContainsKey(goal))
            {
              //AiSession.Instance.Logger.Log($" -> adding {goal} (inv) to temp obstacles from Pathfinding");
              graph.TempBlockedNodes[goal] = new byte();
            }
          }

          bot._transitionPoint = null;
          bot.NeedsTransition = false;
        }

        collection.Locked = false;
      }
      catch (Exception ex)
      {
        if (collection != null)
        {
          if (collection.Graph != null && collection.Bot != null)
          {
            if (MyAPIGateway.Session?.Player != null)
              MyAPIGateway.Utilities.ShowNotification($"Exception in AiEnabled.FindPath: {ex.Message}", 10000);

            AiSession.Instance.Logger.Log($"Exception in AiEnabled.Pathfinder.FindPath: {ex.Message}\n{ex.StackTrace}\n", MessageType.ERROR);
          }

          collection.Locked = false;
          collection.Bot.CleanPath();
        }
      }
    }

    static bool RunAlgorithm(Vector3I start, Vector3I goal, PathCollection collection, bool groundNodesFirst)
    {
      var queue = collection.Queue;
      var cameFrom = collection.CameFrom;
      var costSoFar = collection.CostSoFar;
      var intermediatePoints = collection.IntermediatePoints;

      var bot = collection.Bot;
      var graph = collection.Graph;
      var isGridGraph = graph.IsGridGraph;
      var gridGraph = graph as CubeGridMap;
      var stackedStairs = collection.StackedStairsFound;
      var botPosition = bot.BotInfo.CurrentBotPositionActual;
      var maxTimeMS = AiSession.Instance.ModSaveData.MaxPathfindingTimeInSeconds * 1000;
      var canUseDoors = bot.CanOpenDoors;

      cameFrom.Clear();
      costSoFar.Clear();
      stackedStairs?.Clear();

      queue.Clear();
      queue.Enqueue(start, 0);

      cameFrom[start] = start;
      costSoFar[start] = 0;

      MyRelationsBetweenPlayers relation;
      collection.CheckDoors(out relation);


      //AiSession.Instance.Logger.AddLine($"RunAlgorithm: Start = {start}, Goal = {goal}");

      bool pathFound = false;
      while (queue.Count > 0)
      {
        var currentMS = collection.PathTimer.Elapsed.TotalMilliseconds;
        if (collection.Dirty || currentMS > maxTimeMS)
        {
          //AiSession.Instance.Logger.AddLine($" -> Collection Dirty or Timeout");
          break;
        }

        if (pathFound)
        {
          //AiSession.Instance.Logger.AddLine($" -> Path found!");
          break;
        }

        Vector3I current;
        if (!queue.TryDequeue(out current))
        {
          //AiSession.Instance.Logger.AddLine($" -> Failed to dequeue from Queue");
          break;
        }

        //AiSession.Instance.Logger.AddLine($" -> Current = {current}");

        if (current == goal)
        {
          pathFound = true;

          if (!isGridGraph)
          {
            //AiSession.Instance.Logger.AddLine($" -> Path found!");
            break;
          }
        }

        Vector3I previous;
        if (!cameFrom.TryGetValue(current, out previous))
        {
          //AiSession.Instance.Logger.AddLine($" -> Failed to find previous in CameFrom for {current}");
          break;
        }

        Node currentNode;
        if (!graph.TryGetNodeForPosition(current, out currentNode))
        {
          //AiSession.Instance.Logger.AddLine($" -> Failed to find node for current, {current}");
          break;
        }

        int currentCost;
        costSoFar.TryGetValue(current, out currentCost);
        currentCost += currentNode.MovementCost;

        bool checkDoors = canUseDoors && (!(bot is NeutralBotBase) || AiSession.Instance.ModSaveData.AllowNeutralsToOpenDoors);
        if (isGridGraph)
        {
          var addedCost = currentNode.AddedMovementCost;

          if (addedCost > 0 && AiSession.Instance.ModSaveData.IncreaseNodeWeightsNearWeapons)
          {
            var relToBot = gridGraph.GetRelationshipTo(bot);
            if (relToBot == MyRelationsBetweenPlayers.Enemies || relToBot == MyRelationsBetweenPlayers.Neutral)
              currentCost += addedCost;
          }

          IMyDoor door;
          if (currentNode?.Block != null)
          {
            door = currentNode.Block.FatBlock as IMyDoor;
            if (door != null)
            {
              bool isHangar = door is IMyAirtightHangarDoor || door.BlockDefinition.SubtypeName.Contains("Gate");
              bool isOpen = door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open;

              if (door.SlimBlock.IsBlockUnbuilt())
              {
                isOpen = true;
                var doorType = isHangar ? "Hangar" : "Door";
                currentCost -= AiSession.Instance.MovementCostData.MovementCostDict[doorType];
              }
              else if (!checkDoors)
              {
                continue;
              }

              if (bot.Owner == null)
              {
                if (!isOpen && (collection.DeniedDoors.ContainsKey(current) || gridGraph.BlockedDoors.ContainsKey(current)))
                {
                  currentCost += isHangar ? 40 : 20;
                }

                checkDoors &= (relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self);
              }
              else if (collection.DeniedDoors.ContainsKey(current) || (!isOpen && gridGraph.BlockedDoors.ContainsKey(current)))
              {
                continue;
              }
            }
          }
        }

        currentCost = Math.Max(1, currentCost);

        //AiSession.Instance.Logger.AddLine($" -> Checking Neighbors: Prev = {previous}, Cur = {current}");
        foreach (var next in graph.Neighbors(bot, previous, current, botPosition, checkDoors))
        {
          //AiSession.Instance.Logger.AddLine($"  -> Next = {next}");
          Node node;
          if (!graph.TryGetNodeForPosition(next, out node))
          {
            //AiSession.Instance.Logger.AddLine($"   -> No node found for next");
            continue;
          }

          var newCost = currentCost;

          if (groundNodesFirst && !node.IsGroundNode)
          {
            continue;
          }

          if (node.IsSpaceNode(graph))
          {
            if (!bot.CanUseSpaceNodes)
              continue;
          }
          else if (node.IsAirNode)
          {
            if (!bot.CanUseAirNodes)
              continue;

            if (bot.GroundNodesFirst && !currentNode.IsAirNode)
              newCost += 5;
          }
          else if (node.IsLadder)
          {
            if (!bot.CanUseLadders && !bot.BotInfo.IsFlying)
              continue;
          }
          else if (node.IsGroundNode)
          {
            if (bot.WaterNodesOnly && !node.IsWaterNode)
              continue;

            if (bot.GroundNodesFirst && currentNode.IsAirNode)
              newCost = Math.Max(0, newCost - 1);
          }
          else if (node.IsWaterNode)
          {
            if (!bot.CanUseWaterNodes)
              continue;

            if (!bot.WaterNodesOnly)
              newCost += 5;
          }
          else if (bot.WaterNodesOnly)
            continue;

          if (isGridGraph && !canUseDoors)
          {
            var door = (node.Block?.FatBlock as IMyDoor) ?? gridGraph.GetBlockAtPosition(node.Position)?.FatBlock as IMyDoor;

            if (door != null && door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Open && !door.SlimBlock.IsBlockUnbuilt())
              continue;
          }

          //AiSession.Instance.Logger.AddLine($"   -> Able to use this node");

          int nextCost;
          bool stackFound = false;

          if (isGridGraph && stackedStairs?.Count > 0) // we encountered a stacked half stair which the algo won't allow us to traverse
          {
            lock (stackedStairs)
            {
              MyTuple<Vector3I, Vector3I, Vector3I> tuple;
              if (stackedStairs.TryDequeue(out tuple))
                intermediatePoints.Add(tuple);
            }

            //stackFound = true; // TODO: not sure why I removed this xD
          }

          if (!stackFound && (!costSoFar.TryGetValue(next, out nextCost) || newCost < nextCost))
          {
            var isGoal = next == goal;
            int priority = isGoal ? -1 : newCost + Vector3I.DistanceManhattan(next, goal);

            //AiSession.Instance.Logger.AddLine($"   -> Adding {next} to queue with pri {priority}");

            queue.Enqueue(next, priority);
            costSoFar[next] = newCost;
            cameFrom[next] = current;

            if (isGoal)
            {
              break;
            }
          }
        }
      }

      //AiSession.Instance.Logger.AddLine($" -> PathFound = {pathFound}");
      //AiSession.Instance.Logger.LogAll();
      return pathFound;
    }

    static void ConstructPathForVoxel(Vector3I start, Vector3I end, PathCollection collection)
    {
      var cameFrom = collection.CameFrom;
      var graph = collection.Graph;
      var path = collection.TempPath;
      var cache = collection.Cache;
      var maxTimeMS = AiSession.Instance.ModSaveData.MaxPathfindingTimeInSeconds * 1000;

      cache.Clear();
      Vector3I current = end;
      while (current != start)
      {
        if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > maxTimeMS)
        {
          return;
        }

        cache.Add(current);

        if (!cameFrom.TryGetValue(current, out current))
        {
          return;
        }
      }

      lock (path)
      {
        path.Clear();

        for (int i = cache.Count - 1; i >= 0; i--)
        {
          if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > maxTimeMS)
          {
            break;
          }

          var localVec = cache[i];

          Node pathNode;
          if (graph.TryGetNodeForPosition(localVec, out pathNode))
          {
            AddNodeToPath(path, pathNode);
          }
        }

        lock (collection.PathToTarget)
          Interlocked.CompareExchange(ref collection.PathToTarget, path, collection.PathToTarget);
      }

      cache.Clear();
    }

    static void ConstructPathForGrid(Vector3I start, Vector3I end, PathCollection collection)
    {
      var cameFrom = collection.CameFrom;
      var path = collection.TempPath;
      var cache = collection.Cache;
      var intermediatePoints = collection.IntermediatePoints;
      var maxTimeMS = AiSession.Instance.ModSaveData.MaxPathfindingTimeInSeconds * 1000;

      cache.Clear();
      Vector3I previous = end;
      Vector3I current = end;
      Vector3I next = end;
      while (current != start)
      {
        if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > maxTimeMS)
        {
          cache.Clear();
          return;
        }

        cache.Add(current);

        if (!cameFrom.TryGetValue(current, out next))
        {
          cache.Clear();
          return;
        }

        for (int i = intermediatePoints.Count - 1; i >= 0; i--)
        {
          // tuple is: 1=From, 2=To, 3=InsertBetween
          var tuple = intermediatePoints[i];

          // we're going backwards, so we'll see the TO position first
          if (current == tuple.Item2 && next == tuple.Item1)
          {
            if (tuple.Item3 != previous) // if this is true then we're already heading there, ie we aren't going down again
            {
              cache.Add(tuple.Item3);
              cache.Add(tuple.Item2);
            }

            intermediatePoints.RemoveAtFast(i);
            break;
          }
        }

        previous = current;
        current = next;
      }

      // last check for needed intermediate between start and first point
      // only happens when you start on a half stair.
      for (int i = intermediatePoints.Count - 1; i >= 0; i--)
      {
        if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > maxTimeMS)
        {
          cache.Clear();
          return;
        }

        // tuple is: 1=From, 2=To, 3=InsertBetween
        var tuple = intermediatePoints[i];

        // we're going backwards, so we'll see the TO position first
        if (current == tuple.Item2 && (next == tuple.Item1 || next == current))
        {
          if (tuple.Item3 != previous) // if this is true then we're already heading there, ie we aren't going down again
          {
            cache.Add(tuple.Item3);
            cache.Add(tuple.Item2);
          }

          intermediatePoints.RemoveAtFast(i);
          break;
        }
      }

      var gridGraph = collection.Graph as CubeGridMap;
      var mainGrid = gridGraph.MainGrid;
      var gridMatrix = mainGrid.WorldMatrix;
      var gridSize = mainGrid.GridSize;
      var botMatrix = collection.Bot.WorldMatrix;

      var botMatrixTransposed = MatrixD.Transpose(botMatrix);
      var allowedDiff = gridSize * 0.5f;
      var botDownDir = gridMatrix.GetClosestDirection(botMatrix.Down);
      var downTravelDir = gridMatrix.GetDirectionVector(botDownDir);
      var intVecDown = Base6Directions.GetIntVector(botDownDir);

      lock (path)
      {
        path.Clear();
        intermediatePoints.Clear();

        for (int i = cache.Count - 1; i >= 0; i--)
        {
          if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > maxTimeMS)
          {
            break;
          }

          var localVec = cache[i];
          MyCubeGrid grid = mainGrid;
          var gridLocalVector = localVec;

          Node n;
          if (gridGraph.TryGetNodeForPosition(localVec, out n) && n != null)
          {
            if (n.IsGridNodePlanetTile)
            {
              AddNodeToPath(path, n);
              continue;
            }

            if (n.IsGridNodeUnderGround)
              continue;

            var block = n.Block;
            if (block != null)
            {
              grid = block.CubeGrid as MyCubeGrid;

              if (grid.EntityId != mainGrid.EntityId)
              {
                var worldVec = mainGrid.GridIntegerToWorld(localVec);
                gridLocalVector = grid.WorldToGridInteger(worldVec);
              }

              if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(block.BlockDefinition.Id))
              {
                var positionBelow = localVec + intVecDown;

                Node nBelow;
                if (gridGraph.TryGetNodeForPosition(positionBelow, out nBelow) && nBelow?.Block != null)
                {
                  var blockBelowDef = nBelow.Block.BlockDefinition.Id;
                  if (AiSession.Instance.SlopeBlockDefinitions.Contains(blockBelowDef)
                    && !(nBelow.Block.FatBlock is IMyTextPanel)
                    && !AiSession.Instance.HalfStairBlockDefinitions.Contains(blockBelowDef)
                    && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(blockBelowDef))
                  {
                    // railing over stair / slope, can skip
                    // can't skip over half stair / slope bc of how alignment is adjusted
                    continue;
                  }
                }
              }
            }
          }

          bool thisIsHalfStair = false, thisisHalfPanelSlope = false, thisIsRailing = false, thisIsCatwalkExpansion = false;
          bool prevIsHalfStair = false, prevIsHalfPanelSlope = false, prevIsRailing = false, prevIsCatwalkExpansion = false;
          bool nextIsHalfStair = false, afterNextIsHalfStair = false, nextIsCatwalkExpansion = false;
          bool nextIsHalfPanelSlope = false, afterNextIsHalfPanelSlope = false;
          bool nextIsHalfBlock = false, nextIsRailing = false, afterNextIsRailing = false;
          bool addOffsetStart = cache.Count <= i + 1;

          Vector3D? offset = null;
          Vector3I fromVec = addOffsetStart ? start : cache[i + 1]; // If no previous in list, then previous is start position
          Vector3I fromToLocal = localVec - fromVec;
          Vector3D fwdTravelDir = fromToLocal == Vector3I.Zero ? Vector3D.Zero : gridMatrix.GetDirectionVector(Base6Directions.GetDirection(fromToLocal));
          Vector3I toVec;

          IMySlimBlock prevBlock = gridGraph.GetBlockAtPosition(fromVec);
          IMySlimBlock thisBlock = grid.GetCubeBlock(gridLocalVector);
          IMySlimBlock nextBlock = null, afterNextBlock = null;

          if (prevBlock == null && gridGraph.TryGetNodeForPosition(fromVec, out n) && n?.Block != null)
          {
            prevBlock = n.Block;
          }

          if (prevBlock != null)
          {
            var prevDef = prevBlock.BlockDefinition.Id;
            prevIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(prevDef);
            prevIsHalfPanelSlope = !prevIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(prevDef);
            prevIsRailing = !prevIsHalfPanelSlope && AiSession.Instance.RailingBlockDefinitions.ContainsItem(prevDef);
            prevIsCatwalkExpansion = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(prevDef)
              && prevDef.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0;
          }

          if (thisBlock == null && gridGraph.TryGetNodeForPosition(gridLocalVector, out n) && n?.Block != null)
          {
            thisBlock = n.Block;
          }

          bool thisBlockNull = thisBlock == null || !((MyCubeBlockDefinition)thisBlock.BlockDefinition).HasPhysics;
          bool thisBlockValid = thisBlock != null && ((MyCubeBlockDefinition)thisBlock.BlockDefinition).HasPhysics;

          Node thisNode;
          if (thisBlockValid && gridGraph.TryGetNodeForPosition(gridLocalVector, out thisNode) && thisNode?.IsGroundNode == true)
          {
            var positionBelow = localVec + intVecDown;
            if (gridGraph.TryGetNodeForPosition(positionBelow, out n) && n?.Block != null && AiSession.Instance.SlopeBlockDefinitions.Contains(n.Block.BlockDefinition.Id))
            {
              if (CanIgnoreBlock(thisBlock, gridGraph))
              {
                thisBlockNull = true;
                thisBlockValid = false;
              }
            }
          }

          bool nextBlockNull = true;
          bool afterNextBlockNull = true;
          bool nextBlockValid = false;
          bool afterNextBlockValid = false;

          if (thisBlockValid)
          {
            var thisDef = thisBlock.BlockDefinition.Id;
            thisIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(thisDef);
            thisisHalfPanelSlope = !thisIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(thisDef);
            thisIsRailing = !thisisHalfPanelSlope && !thisIsHalfStair && AiSession.Instance.RailingBlockDefinitions.ContainsItem(thisDef);
            thisIsCatwalkExpansion = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(thisDef)
              && thisDef.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0;
          }

          if (i > 0)
          {
            next = cache[i - 1];
            nextBlock = gridGraph.GetBlockAtPosition(next);

            if (nextBlock == null && gridGraph.TryGetNodeForPosition(next, out n) && n?.Block != null)
            {
              nextBlock = n.Block;
            }

            nextBlockNull = nextBlock == null || !((MyCubeBlockDefinition)nextBlock.BlockDefinition).HasPhysics;
            nextBlockValid = nextBlock != null && ((MyCubeBlockDefinition)nextBlock.BlockDefinition).HasPhysics;

            if (nextBlockValid)
            {
              var nextDef = nextBlock.BlockDefinition.Id;
              nextIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(nextDef);
              nextIsHalfPanelSlope = !nextIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(nextDef);
              nextIsRailing = !nextIsHalfPanelSlope && AiSession.Instance.RailingBlockDefinitions.ContainsItem(nextDef);
              nextIsHalfBlock = !nextIsRailing && (nextDef.SubtypeName.EndsWith("HalfArmorBlock")
                || (nextDef.SubtypeName.StartsWith("AQD_LG") && nextDef.SubtypeName.EndsWith($"Concrete_Half_Block")));
              nextIsCatwalkExpansion = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(nextDef)
                && nextDef.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (i > 1)
            {
              var afterNext = cache[i - 2];
              toVec = afterNext;

              afterNextBlock = gridGraph.GetBlockAtPosition(afterNext);

              if (afterNextBlock == null && gridGraph.TryGetNodeForPosition(afterNext, out n) && n?.Block != null)
              {
                afterNextBlock = n.Block;
              }

              afterNextBlockNull = afterNextBlock == null || !((MyCubeBlockDefinition)afterNextBlock.BlockDefinition).HasPhysics;
              afterNextBlockValid = afterNextBlock != null && ((MyCubeBlockDefinition)afterNextBlock.BlockDefinition).HasPhysics;

              if (afterNextBlockValid)
              {
                var afterNextDef = afterNextBlock.BlockDefinition.Id;
                afterNextIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(afterNextDef);
                afterNextIsHalfPanelSlope = !afterNextIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(afterNextDef);
                afterNextIsRailing = !afterNextIsHalfPanelSlope && AiSession.Instance.RailingBlockDefinitions.ContainsItem(afterNextDef);
              }
            }
            else
              toVec = next;
          }
          else
            toVec = localVec;

          bool halfStairCheck = prevIsHalfStair || thisIsHalfStair || nextIsHalfStair || afterNextIsHalfStair;
          bool halfSlopeCheck = prevIsHalfPanelSlope || thisisHalfPanelSlope || nextIsHalfPanelSlope || afterNextIsHalfPanelSlope;
          bool isCatwalkExpansion = prevIsCatwalkExpansion || thisIsCatwalkExpansion || nextIsCatwalkExpansion;

          if (addOffsetStart && cache.Count > 1)
          {
            if (prevIsCatwalkExpansion)
            {
              AddOffsetForThisCatwalk(prevBlock, gridGraph, path, ref start, ref localVec, ref gridMatrix, ref gridSize, false);
            }

            if (thisIsCatwalkExpansion)
            {
              AddOffsetForNextCatwalk(thisBlock, gridGraph, path, ref start, ref localVec, ref gridMatrix, ref gridSize);
            }
          }

          if (!thisIsHalfStair && !thisisHalfPanelSlope && thisBlockValid)
          {
            var cubeBlockDef = thisBlock.BlockDefinition as MyCubeBlockDefinition;
            var cubeDef = cubeBlockDef.Id;
            bool isDeadBody = !thisIsCatwalkExpansion && cubeDef.SubtypeName.StartsWith("DeadBody");
            bool isHalfCatwalk = !isDeadBody && cubeDef.SubtypeName.StartsWith("CatwalkHalf");
            bool isDeco = !isHalfCatwalk && AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(cubeDef);
            bool isHalfWall = !isDeco && AiSession.Instance.HalfWallDefinitions.ContainsItem(cubeDef);
            bool isHalfBlock = !isHalfWall && (cubeDef.SubtypeName.EndsWith("HalfArmorBlock")
              || (cubeDef.SubtypeName.StartsWith("AQD_LG") && cubeDef.SubtypeName.EndsWith($"Concrete_Half_Block")));
            bool isFreight = !isHalfBlock && AiSession.Instance.FreightBlockDefinitions.ContainsItem(cubeDef);
            bool isRamp = !isFreight && AiSession.Instance.RampBlockDefinitions.Contains(cubeDef);
            bool isBeam = !isRamp && AiSession.Instance.BeamBlockDefinitions.ContainsItem(cubeDef);
            bool isHalfPanel = !isBeam && AiSession.Instance.ArmorPanelHalfDefinitions.ContainsItem(cubeDef);
            bool isPanelSlope = !isHalfPanel && AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeDef);
            bool isHalfSlope = !isPanelSlope && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef);
            bool isSlope = !isHalfSlope
              && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef)
              && !AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef);

            if (thisIsCatwalkExpansion && cache.Count > 1)
            {
              AddOffsetForThisCatwalk(thisBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              if (!halfSlopeCheck && !halfStairCheck)
                continue;
            }

            if (isDeadBody)
            {
              offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * 0.3;

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }

            if (isHalfCatwalk)
            {
              offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.25;

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }

            if (cubeDef.SubtypeName == "LargeBlockOffsetDoor")
            {
              offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.3;

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }

            if (isDeco)
            {
              var subtype = cubeDef.SubtypeName;
              if (subtype.StartsWith("LargeBlockDesk"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.3;

                if (subtype.EndsWith("Corner"))
                {
                  offset = offset.Value + gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.3;
                }
              }
              else if (subtype.EndsWith("Planter") || subtype.EndsWith("Kitchen") || subtype.EndsWith("Counter"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.1;
              }
              else if (subtype.EndsWith("CounterCorner"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.1
                  + gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.1;
              }
              else if (subtype.StartsWith("LargeBlockCouch"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * 0.1;

                if (subtype.EndsWith("Corner"))
                {
                  offset = offset.Value - gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.3;
                }
              }
              else if (subtype == "Shower")
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * 0.3
                  + gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.3;
              }
              else if (subtype.EndsWith("Toilet"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.3;
              }
              else if (subtype == "Jukebox" || subtype == "AtmBlock" || subtype == "FoodDispenser" || subtype == "VendingMachine")
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * -0.2;
              }
              else if (subtype == "TrussFloorHalf")
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.25;
              }
              else if (subtype == "LargeCrate")
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.3;
              }
              else if (subtype.EndsWith("Barrel"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.25
                  + gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.25;
              }
              else if (subtype.EndsWith("HalfBed"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * -0.25;
              }
              else if (subtype.EndsWith("HalfBedOffset"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.25;
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }

            if (isHalfBlock)
            {
              if (thisBlock.Orientation.Forward == botDownDir)
              {
                offset = -downTravelDir * gridSize * 0.25;
              }
              else if (Base6Directions.GetIntVector(thisBlock.Orientation.Forward).Dot(ref intVecDown) == 0)
              {
                offset = -gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * 0.25;
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }

            if (isHalfSlope)
            {
              var upIsDown = thisBlock.Orientation.Up == botDownDir;
              if (upIsDown ||
                ((thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("HalfSlopeArmorBlock") || thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Concrete_Half_Block_Slope"))
                && Base6Directions.GetOppositeDirection(botDownDir) == thisBlock.Orientation.Forward))
              {
                if (upIsDown && gridGraph.ExemptNodesUpper.Contains(localVec))
                  continue;

                var positionBelow = localVec + intVecDown;
                var blockBelow = gridGraph.GetBlockAtPosition(positionBelow);

                if (blockBelow == null && gridGraph.TryGetNodeForPosition(positionBelow, out n) && n?.Block != null)
                {
                  var blockGrid = n.Block.CubeGrid;
                  var worldFrom = mainGrid.GridIntegerToWorld(positionBelow);
                  var localFrom = blockGrid.WorldToGridInteger(worldFrom);
                  blockBelow = blockGrid.GetCubeBlock(localFrom);
                }

                if (blockBelow != null && AiSession.Instance.RampBlockDefinitions.Contains(blockBelow.BlockDefinition.Id))
                {
                  offset = downTravelDir * gridSize * 0.25f;

                  Node node;
                  gridGraph.TryGetNodeForPosition(localVec, out node);

                  TempNode tempNode = AiSession.Instance.TempNodePool.Get();
                  tempNode.Update(node, offset ?? Vector3D.Zero);
                  AddNodeToPath(path, tempNode);

                  if (nextIsCatwalkExpansion)
                    AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                  continue;
                }
              }
              else if (gridGraph.ExemptNodesUpper.Contains(localVec))
              {
                var blockUp = gridMatrix.GetDirectionVector(thisBlock.Orientation.Up);
                if (Base6Directions.GetIntVector(thisBlock.Orientation.Forward) == intVecDown)
                {
                  offset = -downTravelDir * gridSize * 0.25 - blockUp * gridSize * 0.25;
                }
                else
                {
                  offset = downTravelDir * gridSize * 0.25 + blockUp * gridSize * 0.5;
                }

                Node node;
                gridGraph.TryGetNodeForPosition(localVec, out node);

                TempNode tempNode = AiSession.Instance.TempNodePool.Get();
                tempNode.Update(node, offset ?? Vector3D.Zero);
                AddNodeToPath(path, tempNode);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
              else if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Tip"))
              {
                if (botDownDir == thisBlock.Orientation.Forward)
                {
                  offset = -downTravelDir * gridSize * 0.5 - gridMatrix.GetDirectionVector(thisBlock.Orientation.Up) * gridSize * 0.25;

                  Node node;
                  gridGraph.TryGetNodeForPosition(localVec, out node);

                  TempNode tempNode = AiSession.Instance.TempNodePool.Get();
                  tempNode.Update(node, offset ?? Vector3D.Zero);
                  AddNodeToPath(path, tempNode);

                  if (nextIsCatwalkExpansion)
                    AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                  continue;
                }
                else if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip"))
                {
                  if (botDownDir == thisBlock.Orientation.Left || Base6Directions.GetOppositeDirection(botDownDir) == thisBlock.Orientation.Left)
                  {
                    offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Up) * gridSize * 0.25;

                    Node node;
                    gridGraph.TryGetNodeForPosition(localVec, out node);

                    TempNode tempNode = AiSession.Instance.TempNodePool.Get();
                    tempNode.Update(node, offset ?? Vector3D.Zero);
                    AddNodeToPath(path, tempNode);

                    if (nextIsCatwalkExpansion)
                      AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                    continue;
                  }
                }
              }
            }
            else if (isBeam)
            {
              var blockSubtype = cubeDef.SubtypeName;
              if (blockSubtype.EndsWith("End") || blockSubtype.EndsWith("Block") || blockSubtype.EndsWith("Junction"))
              {
                offset = -downTravelDir * gridSize * 0.5;
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (isHalfPanel)
            {
              var blockFwd = thisBlock.Orientation.Forward;
              var blockUp = thisBlock.Orientation.Up;
              var blockLeft = thisBlock.Orientation.Left;

              if (cubeDef.SubtypeName.StartsWith("LargeArmorHalf"))
              {
                if (Base6Directions.GetIntVector(blockFwd).Dot(ref intVecDown) != 0)
                {
                  offset = gridMatrix.GetDirectionVector(blockUp) * gridSize * 0.4 - gridMatrix.GetDirectionVector(blockLeft) * gridSize * 0.5;
                }
              }
              else if (Base6Directions.GetIntVector(blockUp).Dot(ref intVecDown) != 0)
              {
                offset = gridMatrix.GetDirectionVector(blockLeft) * gridSize * 0.4 - gridMatrix.GetDirectionVector(blockFwd) * gridSize * 0.5;
              }
              else if (Base6Directions.GetIntVector(blockFwd).Dot(ref intVecDown) == 0)
              {
                offset = gridMatrix.GetDirectionVector(blockUp) * gridSize * 0.4 - gridMatrix.GetDirectionVector(blockFwd) * gridSize * 0.5;
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (isPanelSlope)
            {
              var blockSubtype = cubeDef.SubtypeName;
              if (blockSubtype.StartsWith("LargeArmor2x1SlopedPanel"))
              {
                var worldPrev = grid.GridIntegerToWorld(fromVec);
                var vecToNext = grid.GridIntegerToWorld(localVec) - worldPrev;

                Vector3D.Rotate(ref vecToNext, ref botMatrixTransposed, out vecToNext);
                var yCheck = Math.Abs(vecToNext.Y) < allowedDiff ? 0 : Math.Sign(vecToNext.Y);
                var blockFwdVec = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
                var upIsUp = Base6Directions.GetOppositeDirection(thisBlock.Orientation.Up) == botDownDir;

                if (blockSubtype.StartsWith("LargeArmor2x1SlopedPanelTip"))
                {
                  if (yCheck < 0) // going down
                  {
                    offset = -downTravelDir * gridSize * 0.5;
                  }
                  else // going up
                  {
                    if (upIsUp)
                      offset = -downTravelDir * gridSize * 0.25 - blockFwdVec * gridSize * 0.25;
                    else
                      offset = -downTravelDir * gridSize * 0.25;
                  }
                }
                else if (yCheck < 0) // going down
                {
                  if (upIsUp)
                    offset = blockFwdVec * gridSize * 0.5;
                  else
                    offset = -downTravelDir * gridSize * 0.5;
                }
                else // going up
                {
                  offset = -downTravelDir * gridSize * 0.5;
                }
              }
              else
              {
                offset = -downTravelDir * gridSize * 0.5f;
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();

              if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
              {
                var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                {
                  if (offset.HasValue)
                    offset = offset.Value - downTravelDir * 0.5f;
                  else
                    offset = -downTravelDir * 0.5f;
                }
              }

              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (isFreight)
            {
              var blockFwd = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);

              if (cubeDef.SubtypeName.EndsWith("1"))
              {
                var leftVec = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                offset = (blockFwd + leftVec) * gridSize * 0.4;
              }
              else
                offset = blockFwd * gridSize * 0.4;

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (isHalfWall)
            {
              var dotUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up).Dot(ref intVecDown);

              if (dotUp != 0)
              {
                if (cubeDef.SubtypeName == "LargeCoverWallHalf")
                {
                  var leftVec = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                  offset = -leftVec * gridSize * 0.5f;
                }
                else
                {
                  var fwdVec = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);

                  if (cubeDef.SubtypeName.EndsWith("Right")) // half rail right if true
                    fwdVec = -fwdVec;

                  offset = fwdVec * gridSize * 0.5f;
                }
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (isRamp || isSlope)
            {
              if (isRamp)
              {
                bool isBlockTip = thisBlock.Position == localVec;
                var dotUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up).Dot(ref intVecDown);
                var dotFwd = Base6Directions.GetIntVector(thisBlock.Orientation.Forward).Dot(ref intVecDown);

                if (dotUp < 0)
                {
                  if (!isBlockTip)
                    continue;

                  offset = -downTravelDir * gridSize * 0.25f;
                }
                else if (dotFwd < 0)
                {
                  if (isBlockTip)
                  {
                    if (localVec != end)
                      continue;

                    var blockUp = gridMatrix.GetDirectionVector(thisBlock.Orientation.Up);
                    var addVec = downTravelDir * gridSize * 0.25f + blockUp * 0.25f;
                    if (fwdTravelDir.Dot(downTravelDir) > 0) // going down the ramp
                      offset = addVec;
                    else // going up the ramp
                      offset = -addVec;
                  }
                  else
                    offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Up) * gridSize * 0.75f;
                }
                else if (dotUp > 0 && isBlockTip) // block is upside down
                {
                  var positionBelow = localVec + intVecDown;
                  var blockBelow = gridGraph.GetBlockAtPosition(positionBelow);

                  if (blockBelow == null && gridGraph.TryGetNodeForPosition(positionBelow, out n) && n?.Block != null)
                  {
                    var blockGrid = n.Block.CubeGrid;
                    var worldFrom = mainGrid.GridIntegerToWorld(positionBelow);
                    var localFrom = blockGrid.WorldToGridInteger(worldFrom);
                    blockBelow = blockGrid.GetCubeBlock(localFrom);
                  }

                  if (blockBelow != null && AiSession.Instance.RampBlockDefinitions.Contains(blockBelow.BlockDefinition.Id))
                  {
                    offset = downTravelDir * gridSize * 0.25f;

                    Node node2;
                    gridGraph.TryGetNodeForPosition(localVec, out node2);

                    TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                    tempNode2.Update(node2, offset ?? Vector3D.Zero);
                    AddNodeToPath(path, tempNode2);

                    if (nextIsCatwalkExpansion)
                      AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                    continue;
                  }
                }
              }
              else if (thisBlock.FatBlock is IMyTextPanel)
              {
                var botUpDir = Base6Directions.GetOppositeDirection(botDownDir);
                Vector3D blockFwd;

                if (botUpDir == thisBlock.Orientation.Forward)
                  blockFwd = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
                else if (botUpDir == thisBlock.Orientation.Left)
                  blockFwd = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                else
                  continue; // upside down or sideways

                offset = blockFwd * gridSize * 0.5f;
              }
              else if (thisBlock.BlockDefinition.Context.ModName == "PassageIntersections" && thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large"))
              {
                // offset already 
                Node node2;
                gridGraph.TryGetNodeForPosition(localVec, out node2);

                TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                tempNode2.Update(node2, node2.Offset);
                AddNodeToPath(path, tempNode2);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
              else if (Base6Directions.GetIntVector(thisBlock.Orientation.Up).Dot(ref intVecDown) > 0
                || Base6Directions.GetIntVector(thisBlock.Orientation.Forward).Dot(ref intVecDown) < 0)
              {
                // upside down slope
                continue;
              }
              else if (Base6Directions.GetIntVector(thisBlock.Orientation.Left).Dot(ref intVecDown) != 0)
              {
                var blockFwd = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
                var blockDwn = -gridMatrix.GetDirectionVector(thisBlock.Orientation.Up);

                offset = blockFwd * gridSize * 0.5f + blockDwn * gridSize * 0.5f;
              }
              else if (Base6Directions.GetIntVector(thisBlock.Orientation.Forward).Dot(ref intVecDown) != 0)
              {
                var blockUp = gridMatrix.GetDirectionVector(thisBlock.Orientation.Up);
                if (cubeDef.SubtypeName.EndsWith("Tip"))
                  offset = -blockUp * gridSize * 0.25;
                else
                  offset = blockUp * gridSize * 0.5;
              }
              else
              {
                offset = -downTravelDir * gridSize * 0.5f;
              }

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
              {
                var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                {
                  if (offset.HasValue)
                    offset = offset.Value - downTravelDir * 0.5f;
                  else
                    offset = -downTravelDir * 0.5f;
                }
              }

              tempNode.Update(node, offset ?? Vector3D.Zero);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (AiSession.Instance.AngledWindowDefinitions.ContainsItem(cubeDef))
            {
              bool isWindow1 = cubeDef.SubtypeName == "Window1x1Slope" || cubeDef.SubtypeName == "LargeWindowEdge";
              bool isWindow2 = !isWindow1 && cubeDef.SubtypeName == "Window1x2Slope";

              if (isWindow1 || isWindow2)
              {
                if (isWindow1)
                {
                  offset = -downTravelDir * gridSize * 0.5f;
                }
                else
                {
                  bool isBlockTip = thisBlock.Position == localVec;
                  var dotUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up).Dot(ref intVecDown);
                  var dotFwd = Base6Directions.GetIntVector(thisBlock.Orientation.Forward).Dot(ref intVecDown);

                  if (dotUp != 0)
                  {
                    if (dotUp > 0)
                    {
                      if (isBlockTip)
                        offset = -downTravelDir * gridSize * 0.75f;
                    }
                    else if (!isBlockTip)
                      offset = -downTravelDir * gridSize * 0.75f;
                  }
                  else if (dotFwd != 0)
                  {
                    if (dotFwd > 0)
                    {
                      if (isBlockTip)
                        continue;
                    }
                    else if (!isBlockTip)
                      continue;
                  }
                }

                Node node2;
                gridGraph.TryGetNodeForPosition(localVec, out node2);

                TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                tempNode2.Update(node2, offset ?? Vector3D.Zero);
                AddNodeToPath(path, tempNode2);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
            }
          }
          else if (thisBlockNull)
          {
            var positionBelow = localVec + intVecDown;
            var blockBelowThis = gridGraph.GetBlockAtPosition(positionBelow); // grid.GetCubeBlock(positionBelow) as IMySlimBlock;

            if (blockBelowThis == null && gridGraph.TryGetNodeForPosition(positionBelow, out n) && n?.Block != null)
            {
              blockBelowThis = n.Block;
            }

            if (blockBelowThis != null)
            {
              var belowDef = blockBelowThis.BlockDefinition.Id;
              bool isRamp = AiSession.Instance.RampBlockDefinitions.Contains(belowDef);
              bool isPanelSlope = !isRamp && AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(belowDef);
              bool isPanelHalfSlope = !isPanelSlope && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(belowDef);
              bool isHalfSlope = !isPanelHalfSlope && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(belowDef);
              bool isSlope = !isHalfSlope && !isPanelHalfSlope
                && AiSession.Instance.SlopeBlockDefinitions.Contains(belowDef)
                && !AiSession.Instance.HalfStairBlockDefinitions.Contains(belowDef);

              if (isRamp || isSlope)
              {
                if (isSlope && blockBelowThis.FatBlock is IMyTextPanel)
                {
                  var botUpDir = Base6Directions.GetOppositeDirection(botDownDir);
                  if (botUpDir == blockBelowThis.Orientation.Forward || botUpDir == blockBelowThis.Orientation.Left)
                    offset = downTravelDir * gridSize * 0.5f;
                  else
                    offset = null;
                }
                else if (blockBelowThis.Orientation.Up == botDownDir)
                {
                  offset = null;
                }
                else if (isRamp)
                {
                  bool isBlockTip = blockBelowThis.Position == localVec;
                  var dotUp = Base6Directions.GetIntVector(blockBelowThis.Orientation.Up).Dot(ref intVecDown);
                  var dotFwd = Base6Directions.GetIntVector(blockBelowThis.Orientation.Forward).Dot(ref intVecDown);

                  if (dotUp < 0)
                  {
                    var multi = isBlockTip ? 0.5f : 0.25f;
                    offset = downTravelDir * gridSize * multi;
                  }
                  else if (dotFwd < 0)
                  {
                    var blockDownDir = gridMatrix.GetDirectionVector(blockBelowThis.Orientation.Up);
                    offset = downTravelDir * gridSize * 0.25f - blockDownDir * gridSize * 0.5f;
                  }
                }
                else if (Base6Directions.GetIntVector(blockBelowThis.Orientation.Left).Dot(ref intVecDown) != 0)
                {
                  var blockFwd = gridMatrix.GetDirectionVector(blockBelowThis.Orientation.Forward);
                  var blockDwn = -gridMatrix.GetDirectionVector(blockBelowThis.Orientation.Up);

                  offset = (blockFwd * gridSize * 0.25f) + (blockDwn * gridSize * 0.25f);
                }
                else if (blockBelowThis.Orientation.Forward == Base6Directions.GetOppositeDirection(botDownDir) || blockBelowThis.Orientation.Up == botDownDir)
                {
                  offset = null;
                }
                else
                  offset = downTravelDir * gridSize * 0.5f;

                Node node;
                gridGraph.TryGetNodeForPosition(localVec, out node);

                TempNode tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, offset ?? Vector3D.Zero);
                AddNodeToPath(path, tempNode);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
              else if (isHalfSlope)
              {
                // think we can skip this one
                continue;
              }
              else if (isPanelSlope)
              {
                if (Base6Directions.GetIntVector(blockBelowThis.Orientation.Left).Dot(ref intVecDown) == 0)
                {
                  // moving this down would be redundant, so skip it
                  continue;
                }

                Node node;
                gridGraph.TryGetNodeForPosition(localVec, out node);

                TempNode tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, offset ?? Vector3D.Zero);
                AddNodeToPath(path, tempNode);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
              else if (isPanelHalfSlope)
              {
                if (Base6Directions.GetIntVector(blockBelowThis.Orientation.Left).Dot(ref intVecDown) == 0)
                {
                  // moving this down would be redundant, so skip it
                  continue;
                }

                Node node;
                gridGraph.TryGetNodeForPosition(localVec, out node);

                TempNode tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, offset ?? Vector3D.Zero);
                AddNodeToPath(path, tempNode);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
              else if (AiSession.Instance.AngledWindowDefinitions.ContainsItem(belowDef))
              {
                bool isWindow1 = belowDef.SubtypeName == "Window1x1Slope" || belowDef.SubtypeName == "LargeWindowEdge";
                bool isWindow2 = !isWindow1 && belowDef.SubtypeName == "Window1x2Slope";

                if (isWindow1 || isWindow2)
                {
                  if (isWindow1)
                  {
                    offset = downTravelDir * gridSize * 0.5f;
                  }
                  else
                  {
                    bool isBlockTip = blockBelowThis.Position == localVec + intVecDown;
                    var dotUp = Base6Directions.GetIntVector(blockBelowThis.Orientation.Up).Dot(ref intVecDown);
                    var dotFwd = Base6Directions.GetIntVector(blockBelowThis.Orientation.Forward).Dot(ref intVecDown);

                    if (dotUp != 0)
                    {
                      if (dotUp > 0)
                      {
                        if (!isBlockTip)
                          continue;
                        else
                          offset = downTravelDir * gridSize * 0.25f;
                      }
                      else if (isBlockTip)
                        continue;
                      else
                        offset = downTravelDir * gridSize * 0.25f;
                    }
                    else if (dotFwd != 0)
                    {
                      var blockDownDir = gridMatrix.GetDirectionVector(blockBelowThis.Orientation.Up);
                      var addVec = blockDownDir * gridSize * 0.25f * Math.Sign(dotFwd);
                      offset = downTravelDir * gridSize * 0.25f + addVec;
                    }
                  }

                  Node node2;
                  gridGraph.TryGetNodeForPosition(localVec, out node2);

                  TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                  tempNode2.Update(node2, offset ?? Vector3D.Zero);
                  AddNodeToPath(path, tempNode2);

                  if (nextIsCatwalkExpansion)
                    AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                  continue;
                }
              }
            }
          }

          if (nextBlockValid)
          {
            var nextDef = nextBlock.BlockDefinition.Id;

            if (thisBlock == null && nextIsHalfBlock && Base6Directions.GetIntVector(nextBlock.Orientation.Forward).Dot(ref intVecDown) == 0)
            {
              offset = -gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * 0.25;

              Node node;
              gridGraph.TryGetNodeForPosition(localVec, out node);

              TempNode tempNode = AiSession.Instance.TempNodePool.Get();
              tempNode.Update(node, offset.Value);
              AddNodeToPath(path, tempNode);

              if (nextIsCatwalkExpansion)
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

              continue;
            }
            else if (!thisIsHalfStair && !thisisHalfPanelSlope && !nextIsHalfStair && !nextIsHalfPanelSlope)
            {
              var thisIsLadder = thisBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(thisBlock.BlockDefinition.Id);
              if (thisIsLadder)
              {
                var blockBwd = -gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
                offset = blockBwd * gridSize * 0.3;

                Node node2;
                gridGraph.TryGetNodeForPosition(localVec, out node2);

                TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                tempNode2.Update(node2, offset.Value);
                AddNodeToPath(path, tempNode2);

                if (nextIsCatwalkExpansion)
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                continue;
              }
              else if (AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(nextDef))
              {
                var vecTo = next - current;
                var subtype = nextDef.SubtypeName;
                if (Base6Directions.GetIntVector(nextBlock.Orientation.Left).Dot(ref vecTo) != 0)
                {
                  bool add = true;
                  if (subtype.StartsWith("LargeBlockDesk"))
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * -0.3;

                    if (subtype.EndsWith("Corner"))
                    {
                      offset = offset.Value + gridMatrix.GetDirectionVector(nextBlock.Orientation.Left) * gridSize * 0.3;
                    }
                  }
                  else if (subtype.EndsWith("Planter") || subtype.EndsWith("Kitchen") || subtype.IndexOf("Counter") >= 0 || subtype == "LargeBlockLockers")
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * -0.3;
                  }
                  else if (subtype.StartsWith("LargeBlockCouch"))
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * 0.3;
                  }
                  else if (subtype == "TrussFloorHalf")
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * -0.25;
                  }
                  else if (subtype == "LargeCrate")
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * -0.3;
                  }
                  else if (subtype.EndsWith("Barrel"))
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * -0.25;
                  }
                  else
                  {
                    add = false;
                  }

                  if (add)
                  {
                    Node node2;
                    gridGraph.TryGetNodeForPosition(localVec, out node2);

                    TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                    tempNode2.Update(node2, offset ?? Vector3D.Zero);
                    AddNodeToPath(path, tempNode2);

                    if (nextIsCatwalkExpansion)
                      AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                    continue;
                  }
                }
                else if (Base6Directions.GetIntVector(nextBlock.Orientation.Forward).Dot(ref vecTo) != 0)
                {
                  bool add = true;
                  if (subtype.EndsWith("Toilet"))
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left) * gridSize * 0.3;
                  }
                  else if (subtype == "LargeBlockCouchCorner" || subtype == "Jukebox" || subtype == "AtmBlock" || subtype == "VendingMachine" || subtype == "FoodDispenser")
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left) * gridSize * -0.3;
                  }
                  else if (subtype.EndsWith("CounterCorner"))
                  {
                    offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left) * gridSize * 0.3;
                  }
                  else
                  {
                    add = false;
                  }

                  if (add)
                  {
                    Node node2;
                    gridGraph.TryGetNodeForPosition(localVec, out node2);

                    TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                    tempNode2.Update(node2, offset ?? Vector3D.Zero);
                    AddNodeToPath(path, tempNode2);

                    if (nextIsCatwalkExpansion)
                      AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                    continue;
                  }
                }
              }
              else if (nextDef.SubtypeName == "LargeBlockOffsetDoor")
              {
                var vecTo = next - current;
                if (Base6Directions.GetIntVector(nextBlock.Orientation.Left).Dot(ref vecTo) != 0)
                {
                  offset = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward) * gridSize * -0.3;

                  Node node2;
                  gridGraph.TryGetNodeForPosition(localVec, out node2);

                  TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
                  tempNode2.Update(node2, offset ?? Vector3D.Zero);
                  AddNodeToPath(path, tempNode2);

                  if (nextIsCatwalkExpansion)
                    AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);

                  continue;
                }
              }
            }
          }

          if (halfStairCheck)
          {
            // Adjust the position to be center of the actual stairs. Assumes the stairs are placed properly!

            var worldPrev = grid.GridIntegerToWorld(fromVec);
            var vecToNext = grid.GridIntegerToWorld(toVec) - worldPrev;

            Vector3D.Rotate(ref vecToNext, ref botMatrixTransposed, out vecToNext);
            var yCheck = Math.Abs(vecToNext.Y) < allowedDiff ? 0 : Math.Sign(vecToNext.Y);

            if (yCheck < 0) // going down
            {
              if (addOffsetStart && (thisIsHalfStair || nextIsHalfStair))
              {
                // addOffsetStart is only true if the Start position is just before a stair
                // add an extra point with offset to the current position so we are aligned before trying to move down the half stair

                var useBlock = nextIsHalfStair ? nextBlock : thisBlock;
                var dir = gridMatrix.GetDirectionVector(useBlock.Orientation.Left);

                if (!AreStairsOnLeftSide(useBlock))
                  dir = -dir;

                Vector3D extra;
                if (nextIsHalfStair)
                  extra = dir * gridSize * 0.25 + fwdTravelDir * gridSize * 0.5;
                else
                  extra = fwdTravelDir * gridSize * 0.5;

                Node node;
                gridGraph.TryGetNodeForPosition(start, out node);

                TempNode tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose) // TODO: is this right? using offset here instead of extra
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, extra);
                AddNodeToPath(path, tempNode);
              }

              if (!thisIsHalfStair && afterNextIsHalfStair)
              {
                // approaching the stairs
                // set this point to align with entrance

                var dir = gridMatrix.GetDirectionVector(afterNextBlock.Orientation.Left);
                if (!AreStairsOnLeftSide(afterNextBlock))
                  dir = -dir;

                offset = dir * gridSize * 0.25;
              }
              else if ((thisIsHalfStair && nextIsHalfStair) || (!prevIsHalfStair && !thisIsHalfStair && nextIsHalfStair))
              {
                // standing at the top of the stairs (grid position is above the actual stair we're walking down)
                // set this point to be down and forward one block so the bot has to move all the way down the stairs to proceed

                var dir = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left);
                if (!AreStairsOnLeftSide(nextBlock))
                  dir = -dir;

                offset = (dir * gridSize * 0.25) + (downTravelDir * gridSize) + (fwdTravelDir * gridSize);
              }
              else if ((prevIsHalfStair && thisIsHalfStair) || (!prevIsHalfStair && thisIsHalfStair && !nextIsHalfStair))
              {
                // we came down to the lower block position (actual stair position)
                // we can skip this one because we've already pushed the previous point down and forward

                continue;
              }
            }
            else if (yCheck > 0) // going up
            {
              if (addOffsetStart && thisIsHalfStair)
              {
                // addOffsetStart is only true if the Start position is just before a stair
                // add an extra point with offset to the current position so we are aligned before trying to move up the half stair

                Vector3D dir, extra;
                Node node;
                TempNode tempNode;

                if (prevIsHalfStair && prevBlock?.Position == start)
                {
                  // starting in the first block space
                  // set this point to be forward of the stair so we move out from beside it where the bot tends to get stuck

                  var cube = (MyCubeBlock)prevBlock.FatBlock;
                  var localCenter = cube.PositionComp.LocalAABB.Center;
                  var worldCenter = Vector3D.Transform(localCenter, cube.WorldMatrix);
                  var botToBlockCenter = botMatrix.Translation - cube.PositionComp.WorldAABB.Center;
                  var localToBlockCenter = worldCenter - cube.PositionComp.WorldAABB.Center;

                  Vector3I insertStart, insertPos;
                  if (botToBlockCenter.Dot(localToBlockCenter) < 0)
                  {
                    // bot is on other side of stair, move to front of stair first
                    insertStart = start;
                  }
                  else
                  {
                    // bot on stair, move up stair instead
                    insertStart = localVec;
                  }

                  if (gridGraph.GetValidPositionForStackedStairs(insertStart, out insertPos))
                  {
                    gridGraph.TryGetNodeForPosition(insertPos, out node);

                    tempNode = AiSession.Instance.TempNodePool.Get();

                    dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                    if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Right"))
                      dir = -dir;

                    extra = dir * gridSize * 0.25;

                    tempNode.Update(node, extra);
                    AddNodeToPath(path, tempNode);
                  }
                }

                dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Right"))
                  dir = -dir;

                extra = dir * gridSize * 0.25;

                gridGraph.TryGetNodeForPosition(start, out node);

                tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose) // TODO: is this right? using offset here instead of extra
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, extra);
                AddNodeToPath(path, tempNode);
              }

              if (!thisIsHalfStair && nextIsHalfStair)
              {
                // approaching the stairs
                // set this point to align with entrance

                var dir = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left);
                if (!AreStairsOnLeftSide(nextBlock))
                  dir = -dir;

                offset = dir * gridSize * 0.25;
              }
              else if ((thisIsHalfStair && nextIsHalfStair) || (!prevIsHalfStair && thisIsHalfStair && !nextIsHalfStair))
              {
                // standing at the bottom of the stairs (actual block position)
                // set this point to be up and forward one block so the bot has to move all the way up the stairs to proceed

                var botUpDir = gridMatrix.GetClosestDirection(botMatrix.Up);
                var UpTravelDir = gridMatrix.GetDirectionVector(botUpDir);

                var dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                if (!AreStairsOnLeftSide(thisBlock))
                  dir = -dir;

                offset = (dir * gridSize * 0.25) + (UpTravelDir * gridSize) + (fwdTravelDir * gridSize);
              }
              else if ((prevIsHalfStair && thisIsHalfStair) || (prevIsHalfStair && !thisIsHalfStair && !nextIsHalfStair))
              {
                // stairs are stacked on top of one another
                // we can skip this one because we've already pushed the previous point up and forward

                continue;
              }
            }

            Node node2;
            gridGraph.TryGetNodeForPosition(localVec, out node2);

            TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();

            if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
            {
              var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
              if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
              {
                if (offset.HasValue)
                  offset = offset.Value - downTravelDir * 0.5f;
                else
                  offset = -downTravelDir * 0.5f;
              }
            }

            tempNode2.Update(node2, offset ?? Vector3D.Zero);
            AddNodeToPath(path, tempNode2);
          }
          else if (halfSlopeCheck)
          {
            // Adjust the position to be center of the actual slope. Assumes the slope is placed properly!

            var worldPrev = grid.GridIntegerToWorld(fromVec);
            var vecToNext = grid.GridIntegerToWorld(toVec) - worldPrev;

            Vector3D.Rotate(ref vecToNext, ref botMatrixTransposed, out vecToNext);
            var yCheck = Math.Abs(vecToNext.Y) < allowedDiff ? 0 : Math.Sign(vecToNext.Y);

            if (yCheck < 0) // going down
            {
              if (addOffsetStart && (thisisHalfPanelSlope || nextIsHalfPanelSlope))
              {
                // addOffsetStart is only true if the Start position is just before a slope
                // add an extra point with offset to the current position so we are aligned before trying to move down the half slope

                var useBlock = nextIsHalfPanelSlope ? nextBlock : thisBlock;
                var dir = gridMatrix.GetDirectionVector(useBlock.Orientation.Left);

                if (!useBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                Vector3D extra;
                if (nextIsHalfPanelSlope)
                  extra = dir * gridSize * 0.25 + fwdTravelDir * gridSize * 0.5;
                else
                  extra = fwdTravelDir * gridSize * 0.5;

                Node node;
                gridGraph.TryGetNodeForPosition(start, out node);

                TempNode tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose) // TODO: is this right? using offset here instead of extra
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, extra);
                AddNodeToPath(path, tempNode);
              }

              if (!thisisHalfPanelSlope && afterNextIsHalfPanelSlope)
              {
                // approaching the slope
                // set this point to align with entrance

                var dir = gridMatrix.GetDirectionVector(afterNextBlock.Orientation.Left);
                if (!afterNextBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                offset = dir * gridSize * 0.25;
              }
              else if ((thisisHalfPanelSlope && nextIsHalfPanelSlope) || (!prevIsHalfPanelSlope && !thisisHalfPanelSlope && nextIsHalfPanelSlope))
              {
                // standing at the top of the slope (grid position is above the actual slope we're walking down)
                // set this point to be down and forward one block so the bot has to move all the way down the slope to proceed

                var dir = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left);
                if (!nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                offset = (dir * gridSize * 0.25) + (downTravelDir * gridSize) + (fwdTravelDir * gridSize);
              }
              else if ((prevIsHalfPanelSlope && thisisHalfPanelSlope) || (!prevIsHalfPanelSlope && thisisHalfPanelSlope && !nextIsHalfPanelSlope))
              {
                // we came down to the lower block position (actual slope position)
                // we can skip this one because we've already pushed the previous point down and forward

                continue;
              }
            }
            else if (yCheck > 0) // going up
            {
              if (addOffsetStart && thisisHalfPanelSlope)
              {
                // addOffsetStart is only true if the Start position is just before a slope
                // add an extra point with offset to the current position so we are aligned before trying to move up the half slope

                Vector3D dir, extra;
                Node node;
                TempNode tempNode;

                if (prevIsHalfPanelSlope && prevBlock?.Position == start)
                {
                  // starting in the first block space
                  // set this point to be forward of the stair so we move out from beside it where the bot tends to get stuck

                  var cube = (MyCubeBlock)prevBlock.FatBlock;
                  var localCenter = cube.PositionComp.LocalAABB.Center;
                  var worldCenter = Vector3D.Transform(localCenter, cube.WorldMatrix);
                  var botToBlockCenter = botMatrix.Translation - cube.PositionComp.WorldAABB.Center;
                  var localToBlockCenter = worldCenter - cube.PositionComp.WorldAABB.Center;

                  Vector3I insertStart, insertPos;
                  if (botToBlockCenter.Dot(localToBlockCenter) < 0)
                  {
                    // bot is on other side of stair, move to front of stair first
                    insertStart = start;
                  }
                  else
                  {
                    // bot on stair, move up stair instead
                    insertStart = localVec;
                  }

                  if (gridGraph.GetValidPositionForStackedStairs(insertStart, out insertPos))
                  {
                    gridGraph.TryGetNodeForPosition(insertPos, out node);

                    tempNode = AiSession.Instance.TempNodePool.Get();

                    dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                    if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Right"))
                      dir = -dir;

                    extra = dir * gridSize * 0.25;

                    tempNode.Update(node, extra);
                    AddNodeToPath(path, tempNode);
                  }
                }

                dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                if (!thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                extra = dir * gridSize * 0.25; // + fwdTravelDir * gridSize * 0.5;

                gridGraph.TryGetNodeForPosition(start, out node);

                tempNode = AiSession.Instance.TempNodePool.Get();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose) // TODO: is this right? Using offset here instead of extra
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                tempNode.Update(node, extra);
                AddNodeToPath(path, tempNode);
              }

              if (!thisisHalfPanelSlope && nextIsHalfPanelSlope)
              {
                // approaching the slope
                // set this point to align with entrance

                var dir = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left);
                if (!nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                offset = dir * gridSize * 0.25;
              }
              else if ((thisisHalfPanelSlope && nextIsHalfPanelSlope) || (!prevIsHalfPanelSlope && thisisHalfPanelSlope && !nextIsHalfPanelSlope))
              {
                // standing at the bottom of the slope (actual block position)
                // set this point to be up and forward one block so the bot has to move all the way up the slope to proceed

                var botUpDir = gridMatrix.GetClosestDirection(botMatrix.Up);
                var UpTravelDir = gridMatrix.GetDirectionVector(botUpDir);

                var dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                if (!thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                offset = (dir * gridSize * 0.25) + (UpTravelDir * gridSize) + (fwdTravelDir * gridSize);
              }
              else if ((prevIsHalfPanelSlope && thisisHalfPanelSlope) || (prevIsHalfPanelSlope && !thisisHalfPanelSlope && !nextIsHalfPanelSlope))
              {
                // stairs are stacked on top of one another
                // we can skip this one because we've already pushed the previous point up and forward

                continue;
              }
            }

            Node node2;
            gridGraph.TryGetNodeForPosition(localVec, out node2);

            TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();

            if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
            {
              var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3D.Zero) + gridGraph.WorldMatrix.Down * 0.5;
              if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
              {
                if (offset.HasValue)
                  offset = offset.Value - downTravelDir * 0.5f;
                else
                  offset = -downTravelDir * 0.5f;
              }
            }

            tempNode2.Update(node2, offset ?? Vector3D.Zero);
            AddNodeToPath(path, tempNode2);

            if (nextIsCatwalkExpansion)
              AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);
          }
          else
          {
            Node node2;
            gridGraph.TryGetNodeForPosition(localVec, out node2);

            TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
            tempNode2.Update(node2, offset ?? Vector3D.Zero);
            AddNodeToPath(path, tempNode2);

            if (nextIsCatwalkExpansion)
            {
              AddOffsetForNextCatwalk(nextBlock, gridGraph, path, ref localVec, ref next, ref gridMatrix, ref gridSize);
            }
          }
        }

        lock (collection.PathToTarget)
          Interlocked.CompareExchange(ref collection.PathToTarget, path, collection.PathToTarget);
      }

      cache.Clear();
    }

    static bool CanIgnoreBlock(IMySlimBlock block, CubeGridMap gridGraph)
    {
      if (gridGraph?.MainGrid == null || block == null || !((MyCubeBlockDefinition)block.BlockDefinition).HasPhysics)
        return true;

      var cubeDef = block.BlockDefinition;
      var upDir = gridGraph.MainGrid.WorldMatrix.GetClosestDirection(gridGraph.WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(upDir);

      if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeDef.Id))
      {
        return Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref upVec) <= 0;
      }
      else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id))
      {
        Base6Directions.Direction sideWithPane;
        if (cubeDef.Id.SubtypeName == "LargeWindowSquare")
          sideWithPane = block.Orientation.Forward;
        else
          sideWithPane = Base6Directions.GetOppositeDirection(block.Orientation.Left);

        return Base6Directions.GetIntVector(sideWithPane).Dot(ref upVec) >= 0;
      }
      else if (AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(cubeDef.Id)
        || AiSession.Instance.ArmorPanelHalfDefinitions.ContainsItem(cubeDef.Id))
      {
        return Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref upVec) <= 0;
      }
      else if ((block.FatBlock is IMyTextPanel && !AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id))
        || (block.FatBlock is IMyLightingBlock && cubeDef.Id.SubtypeName == "LargeLightPanel"))
      {
        return Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref upVec) >= 0;
      }
      else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeDef.Id))
      {
        return !gridGraph.CheckCatwalkForRails(block, -upVec);
      }

      return false;
    }

    static bool AreStairsOnLeftSide(IMySlimBlock stairBlock)
    {
      if (stairBlock != null && !stairBlock.IsDestroyed)
      {
        // Mirrored has stairs on Left side
        if (AiSession.Instance.HalfStairMirroredDefinitions.Contains(stairBlock.BlockDefinition.Id))
          return true;

        // Grated Catwalk Expansion
        if (AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(stairBlock.BlockDefinition.Id)
          && stairBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
          return true;
      }

      return false;
    }

    static void AddOffsetForThisCatwalk(IMySlimBlock thisBlock, CubeGridMap gridGraph, MyQueue<Node> path,
      ref Vector3I localVec, ref Vector3I next, ref MatrixD gridMatrix, ref float gridSize, bool addCenterPoint = true)
    {
      if (localVec == next)
        return;

      var subtype = thisBlock.BlockDefinition.Id.SubtypeName;
      var blockFwdVector = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
      var blockLeftVector = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
      var gridTravelDir = Base6Directions.GetDirection(next - localVec);
      Vector3D? offset = null;

      if (subtype.EndsWith("CatwalkHalfLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = Vector3D.Zero;
        }
        else
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("CatwalkHalfRight"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = Vector3D.Zero;
        }
        else
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfWidthCrossoverLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.6;
        }
        else
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfWidthCrossoverRight"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.6;
        }
        else
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("CatwalkHalfTJunction") || subtype.EndsWith("HalfWidthTJunctionBalcony") || subtype.EndsWith("HalfWidthBalcony"))
      {
        var fwdTravelDir = gridMatrix.GetDirectionVector(gridTravelDir);
        offset = -blockLeftVector * gridSize * 0.3 + fwdTravelDir * gridSize * 0.6;
      }
      else if (subtype.EndsWith("HalfMixedCrossroad"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward || Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          var fwdTravelDir = gridMatrix.GetDirectionVector(gridTravelDir);
          offset = -blockLeftVector * gridSize * 0.3 + fwdTravelDir * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("MixedTJunctionRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("MixedTJunctionLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfWallLeft") || subtype.EndsWith("HalfWallRight")
        || subtype.EndsWith("DiagonalBaseLeft") || subtype.EndsWith("DiagonalBaseRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          if (subtype.EndsWith("Left"))
          {
            offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
          }
          else
          {
            offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
          }
        }
      }
      else if (subtype.EndsWith("HalfWidthCornerA"))
      {
        offset = -blockLeftVector * gridSize * 0.5 + blockFwdVector * gridSize * 0.5;
      }
      else if (subtype.EndsWith("HalfWidthCornerB"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.6;
        }
        else // can only go forward or right
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("RoundCatwalkMixedCornerLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("RoundCatwalkMixedCornerRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfWidthCatwalk") || subtype.EndsWith("SlopeBaseCatwalkRight")
        || subtype.EndsWith("SlopeBaseCatwalkWallRight") || subtype.EndsWith("SlopeBaseCatwalkDiagonalWallRight")
        || subtype.EndsWith("SlopeBaseCatwalkStraightRight") || subtype.EndsWith("SlopeBaseCatwalkAcuteCornerRight"))
      {
        addCenterPoint = false;
        offset = -blockLeftVector * gridSize * 0.3;
      }
      else if (subtype.EndsWith("HalfWidthCatwalkWall") || subtype.EndsWith("HalfWidthCatwalkWallOffset")
        || subtype.EndsWith("HalfWidthCatwalkTJunctionBaseLeft") || subtype.EndsWith("SlopeBaseCatwalkLeft")
        || subtype.EndsWith("SlopeBaseCatwalkWallLeft") || subtype.EndsWith("SlopeBaseCatwalkDiagonalWallLeft")
        || subtype.EndsWith("SlopeBaseCatwalkStraightLeft") || subtype.EndsWith("SlopeBaseCatwalkAcuteCornerLeft"))
      {
        addCenterPoint = false;
        offset = blockLeftVector * gridSize * 0.3;
      }
      else if (subtype.EndsWith("HalfWidthDiagonalCatwalk") || subtype.EndsWith("HalfWidthDiagonalCatwalkCrossroad"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left || gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.4 + blockFwdVector * gridSize * 0.4;
        }
        else // right or backward
        {
          offset = -blockLeftVector * gridSize * 0.4 - blockFwdVector * gridSize * 0.4;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkDiagonalWall") || subtype.EndsWith("HalfSlopeInvertedCatwalkHalfCornerA"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkMixedCornerRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkMixedTJunctionRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkMixedTJunctionLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkHalfCornerA"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
        else // backwards, can only go left and backward
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkHalfCornerB"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.6;
        }
        else // right, can only for forward or right
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedCornerLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedCornerRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedTJunctionLeft"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedTJunctionRight"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkCurvedWallLeft"))
      {
        var oppDir = Base6Directions.GetOppositeDirection(gridTravelDir);
        if (oppDir == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
        else if (oppDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkCurvedWallRight"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkFullCurvedWall") || subtype.EndsWith("HalfStadiumCatwalkHalfWidthBalcony"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkDiagonalCornerLeft"))
      {
        var oppDir = Base6Directions.GetOppositeDirection(gridTravelDir);
        if (oppDir == thisBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
        else if (oppDir == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkDiagonalCornerRight"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.6;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkStraight"))
      {
        if (gridTravelDir == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.6;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == thisBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.6;
        }
      }

      Node node;
      gridGraph.TryGetNodeForPosition(localVec, out node);

      if (addCenterPoint)
      {
        TempNode tempNode2 = AiSession.Instance.TempNodePool.Get();
        tempNode2.Update(node, Vector3D.Zero);
        AddNodeToPath(path, tempNode2);
      }

      TempNode tempNode = AiSession.Instance.TempNodePool.Get();
      tempNode.Update(node, offset ?? Vector3D.Zero);
      AddNodeToPath(path, tempNode);
    }

    static void AddOffsetForNextCatwalk(IMySlimBlock nextBlock, CubeGridMap gridGraph, MyQueue<Node> path,
      ref Vector3I localVec, ref Vector3I next, ref MatrixD gridMatrix, ref float gridSize)
    {
      if (localVec == next)
        return;

      // add an offset to align with entrance of next block as needed
      var subtype = nextBlock.BlockDefinition.Id.SubtypeName;
      var blockFwdVector = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward);
      var blockLeftVector = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left);
      var gridTravelDir = Base6Directions.GetDirection(next - localVec);
      Vector3D? offset = null;

      if (subtype.EndsWith("CatwalkHalfLeft"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("CatwalkHalfRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfWidthCrossoverLeft"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
        else
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfWidthCrossoverRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
        else
        {
          offset = -blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("CatwalkHalfTJunction") || subtype.EndsWith("HalfWidthTJunctionBalcony")
        || subtype.EndsWith("HalfWidthBalcony") || subtype.EndsWith("HalfMixedCrossroad"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("MixedTJunctionRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("MixedTJunctionLeft"))
      {
        if (gridTravelDir == Base6Directions.GetOppositeDirection(nextBlock.Orientation.Left))
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("CatwalkHalfWallLeft") || subtype.EndsWith("CatwalkHalfWallRight")
        || subtype.EndsWith("DiagonalBaseLeft") || subtype.EndsWith("DiagonalBaseRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          if (subtype.EndsWith("Left"))
          {
            offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
          }
          else
          {
            offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
          }
        }
      }
      else if (subtype.EndsWith("HalfWidthCornerA"))
      {
        offset = blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
      }
      else if (subtype.EndsWith("HalfWidthCornerB"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
        else // travelDir == Backwards, can only enter from two directions
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("RoundCatwalkMixedCornerLeft"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("RoundCatwalkMixedCornerRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
      }
      //else if (subtype.EndsWith("HalfWidthCatwalk") || subtype.EndsWith("SlopeBaseCatwalkRight")
      //  || subtype.EndsWith("SlopeBaseCatwalkWallRight") || subtype.EndsWith("SlopeBaseCatwalkDiagonalWallRight")
      //  || subtype.EndsWith("SlopeBaseCatwalkStraightRight") || subtype.EndsWith("SlopeBaseCatwalkAcuteCornerRight"))
      //{
      //  // skip?
      //}
      //else if (subtype.EndsWith("HalfWidthCatwalkWall") || subtype.EndsWith("HalfWidthCatwalkWallOffset")
      //  || subtype.EndsWith("HalfWidthCatwalkTJunctionBaseLeft") || subtype.EndsWith("SlopeBaseCatwalkLeft")
      //  || subtype.EndsWith("SlopeBaseCatwalkWallLeft") || subtype.EndsWith("SlopeBaseCatwalkDiagonalWallLeft")
      //  || subtype.EndsWith("SlopeBaseCatwalkStraightLeft") || subtype.EndsWith("SlopeBaseCatwalkAcuteCornerLeft"))
      //{
      //  // skip?
      //}
      else if (subtype.EndsWith("HalfWidthDiagonalCatwalk") || subtype.EndsWith("HalfWidthDiagonalCatwalkCrossroad"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left || gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
        else // right or backward
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkDiagonalWall") || subtype.EndsWith("HalfSlopeInvertedCatwalkHalfCornerA"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkMixedCornerRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkMixedTJunctionRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedCatwalkMixedTJunctionLeft"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkHalfCornerA"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
        else // right
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkHalfCornerB"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
        else // backward
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedCornerLeft"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedCornerRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedTJunctionLeft"))
      {
        if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfSlopeInvertedRoundedCatwalkMixedTJunctionRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkCurvedWallLeft"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
        else if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkCurvedWallRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = blockLeftVector * gridSize * 0.3 + blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkFullCurvedWall") || subtype.EndsWith("HalfStadiumCatwalkHalfWidthBalcony"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkDiagonalCornerLeft"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward || gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockLeftVector * gridSize * 0.3 - blockFwdVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkDiagonalCornerRight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Forward || Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }
      else if (subtype.EndsWith("HalfStadiumCatwalkStraight"))
      {
        if (gridTravelDir == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 - blockLeftVector * gridSize * 0.3;
        }
        else if (Base6Directions.GetOppositeDirection(gridTravelDir) == nextBlock.Orientation.Left)
        {
          offset = -blockFwdVector * gridSize * 0.3 + blockLeftVector * gridSize * 0.3;
        }
      }

      if (offset.HasValue)
      {
        Node node;
        gridGraph.TryGetNodeForPosition(next, out node);

        TempNode tempNode = AiSession.Instance.TempNodePool.Get();
        tempNode.Update(node, offset.Value);
        AddNodeToPath(path, tempNode);
      }
    }

    static void AddNodeToPath(MyQueue<Node> path, Node node)
    {
      //if (path.Count == 0)
      //  AiSession.Instance.Logger.Log($"~~~~ Path Start ~~~~");

      //if (node.Position == Vector3I.Zero && node.NodeType == NodeType.None)
      //{
      //  AiSession.Instance.Logger.Log($" !! Node is empty !!");
      //}

      //Vector3I diff = Vector3I.Zero;
      //if (path.Count > 1)
      //{
      //  var prev = path[path.Count - 1];
      //  diff = node.Position - prev.Position;
      //}

      //AiSession.Instance.Logger.Log($" {node.Position} | {node.NodeType} | Diff = {diff}");

      path.Enqueue(node);
    }
  }
}