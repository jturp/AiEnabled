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
        var maxTime = AiSession.Instance.ModSaveData.MaxPathfindingTimeInSeconds;
        var maxTimeMS = (maxTime * 1000) / Math.Max(0.1f, Math.Min(1f, MyAPIGateway.Physics.ServerSimulationRatio));

        if (collection.Dirty || currentMS > maxTimeMS)
        {
          if (currentMS > maxTimeMS && bot?.Character?.Name != null)
            AiSession.Instance.Logger.Warning($"{bot.Character.Name} - PathTimer exceeded {maxTime} s pathing to {goal}");
          pathFound = false;
        }

        if (pathFound)
        {
          if (isIntendedGoal)
            bot._noPathCounter = 0;
          else
            bot._noPathCounter++;

          if (graph.IsGridGraph)
            ConstructPathForGridV2(start, goal, collection);
          else
            ConstructPathForVoxel(start, goal, collection);
        }
        else if (!collection.Dirty)
        {
          bool isInventory = false;
          IMySlimBlock invBlock = null;

          if (graph.IsGridGraph && bot is RepairBot)
          {
            if (bot.Target != null)
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

            if (bot.Target != null)
            {
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
          if (collection.Graph != null && collection.Bot != null && !collection.Bot.IsDead)
          {
            if (MyAPIGateway.Session?.Player != null)
              MyAPIGateway.Utilities.ShowNotification($"Exception in AiEnabled.FindPath: {ex.Message}", 10000);

            AiSession.Instance.Logger.Error($"Exception in AiEnabled.Pathfinder.FindPath: {ex.ToString()}\n");
          }

          collection.Locked = false;
          collection.Bot?.CleanPath();
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


     // AiSession.Instance.Logger.AddLine($"RunAlgorithm: Start = {start}, Goal = {goal}");

      bool pathFound = false;
      while (queue.Count > 0)
      {
        var currentMS = collection.PathTimer.Elapsed.TotalMilliseconds;
        if (collection.Dirty || currentMS > maxTimeMS)
        {
         // AiSession.Instance.Logger.AddLine($" -> Collection Dirty or Timeout");
          break;
        }

        if (pathFound)
        {
         // AiSession.Instance.Logger.AddLine($" -> Path found!");
          break;
        }

        Vector3I current;
        if (!queue.TryDequeue(out current))
        {
         // AiSession.Instance.Logger.AddLine($" -> Failed to dequeue from Queue");
          break;
        }

       // AiSession.Instance.Logger.AddLine($" -> Current = {current}");

        if (current == goal)
        {
          pathFound = true;

          if (!isGridGraph)
          {
           // AiSession.Instance.Logger.AddLine($" -> Path found!");
            break;
          }
        }

        Vector3I previous;
        if (!cameFrom.TryGetValue(current, out previous))
        {
         // AiSession.Instance.Logger.AddLine($" -> Failed to find previous in CameFrom for {current}");
          break;
        }

        Node currentNode;
        if (!graph.TryGetNodeForPosition(current, out currentNode))
        {
         // AiSession.Instance.Logger.AddLine($" -> Failed to find node for current, {current}");
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

       // AiSession.Instance.Logger.AddLine($" -> Checking Neighbors: Prev = {previous}, Cur = {current}");
        foreach (var next in graph.Neighbors(bot, previous, current, botPosition, checkDoors))
        {
         // AiSession.Instance.Logger.AddLine($"  -> Next = {next}");

          if (next == previous)
          {
           // AiSession.Instance.Logger.AddLine($"  -> Next == Previous");
            continue;
          }

          Node node;
          if (!graph.TryGetNodeForPosition(next, out node))
          {
           // AiSession.Instance.Logger.AddLine($"   -> No node found for next");
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

         // AiSession.Instance.Logger.AddLine($"   -> Able to use this node");

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

            // stackFound = true; // TODO: not sure why I removed this xD
          }

          if (!stackFound && (!costSoFar.TryGetValue(next, out nextCost) || newCost < nextCost))
          {
            var isGoal = next == goal;
            int priority = isGoal ? -1 : newCost + Vector3I.DistanceManhattan(next, goal);

           // AiSession.Instance.Logger.AddLine($"   -> Adding {next} to queue with pri {priority}");

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

    static void ConstructPathForGridV2(Vector3I start, Vector3I end, PathCollection collection)
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

          Vector3I localVec = cache[i];
          Vector3I gridLocalVector = localVec;
          MyCubeGrid grid = mainGrid;

          bool addOffsetStart = cache.Count <= i + 1;
          bool prevIsCatwalkExpansion = false, thisIsCatwalkExpansion = false, nextIsCatwalkExpansion = false;
          bool canSkip = false;

          Node n;
          IMySlimBlock block = null;
          UsableEntry thisEntry = null, nextEntry = null, prevEntry = null;

          if (gridGraph.TryGetNodeForPosition(gridLocalVector, out n))
          {
            block = n.Block ?? grid.GetCubeBlock(gridLocalVector);
            if (block != null)
            {
              var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
              var cell = blockDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(block, gridLocalVector) : Vector3I.Zero;
              var key = MyTuple.Create(blockDef.Id, cell);

              thisIsCatwalkExpansion = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(key.Item1)
                && key.Item1.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0;

              AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(key, out thisEntry);
            }

            canSkip = n.CanSkip;
          }

          Vector3D? offset = thisEntry?.GetOffset(block) ?? n?.Offset;
          Vector3I fromVec = addOffsetStart ? start : cache[i + 1]; // If no previous in list, then previous is start position
          Vector3I fromToLocal = localVec - fromVec;
          Vector3D fwdTravelDir = fromToLocal == Vector3I.Zero ? Vector3D.Zero : gridMatrix.GetDirectionVector(Base6Directions.GetDirection(fromToLocal));
          Vector3I toVec;

          IMySlimBlock thisBlock = block;
          IMySlimBlock prevBlock = (fromVec == start) ? thisBlock : gridGraph.GetBlockAtPosition(fromVec);
          IMySlimBlock nextBlock = null, afterNextBlock = null;

          if (prevBlock == null && gridGraph.TryGetNodeForPosition(fromVec, out n))
          {
            prevBlock = n?.Block;
          }

          if (prevBlock != null)
          {
            if (fromVec == start)
            {
              prevEntry = thisEntry;
              prevIsCatwalkExpansion = thisIsCatwalkExpansion;
            }
            else
            {
              var prevDef = (MyCubeBlockDefinition)prevBlock.BlockDefinition;
              var cell = prevDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(prevBlock, fromVec) : Vector3I.Zero;
              var key = MyTuple.Create(prevDef.Id, cell);

              prevIsCatwalkExpansion = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(key.Item1)
                && prevDef.Id.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0;

              AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(key, out prevEntry);
            }

            if (canSkip && prevEntry?.SpecialConsideration == true)
            {
              // Assumed to be the air space above a half stair or something, and the extra waypoint is not necessary
              continue;
            }
          }

          bool addOffsetForNextStair = false;
          IMySlimBlock nextStairBlock = null;

          if (i > 0)
          {
            next = cache[i - 1];
            nextBlock = gridGraph.GetBlockAtPosition(next);

            if (nextBlock == null && gridGraph.TryGetNodeForPosition(next, out n))
            {
              nextBlock = n?.Block;
            }

            if (nextBlock != null)
            {
              var nextDef =  (MyCubeBlockDefinition)nextBlock.BlockDefinition;
              var cell = nextDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(nextBlock, next) : Vector3I.Zero;
              var key = MyTuple.Create(nextDef.Id, cell);

              nextIsCatwalkExpansion = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(key.Item1)
                && nextDef.Id.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0;

              AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(key, out nextEntry);

              bool thisNotSpecial = thisEntry == null || !thisEntry.SpecialConsideration;
              var nextIsSpecial = nextEntry?.SpecialConsideration == true;

              if (nextIsSpecial)
              {
                if (canSkip)
                {
                  // Assumed to be the air space above a half stair or something, and the extra waypoint is not necessary
                  continue;
                }
                else if (thisNotSpecial && nextDef.Id.SubtypeName.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  // going up stairs, add offset

                  nextStairBlock = nextBlock;
                  addOffsetForNextStair = true;
                }
              }
            }

            if (i > 1)
            {
              var afterNext = cache[i - 2];
              toVec = afterNext;

              afterNextBlock = gridGraph.GetBlockAtPosition(afterNext);

              if (afterNextBlock == null && gridGraph.TryGetNodeForPosition(afterNext, out n))
              {
                afterNextBlock = n?.Block;
              }

              if (afterNextBlock != null)
              {
                var afterNextDef = (MyCubeBlockDefinition)afterNextBlock.BlockDefinition;
                var cell = afterNextDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(afterNextBlock, afterNext) : Vector3I.Zero;
                var key = MyTuple.Create(afterNextDef.Id, cell);

                var afterNextEntry = AiSession.Instance.BlockInfo.BlockDirInfo.GetValueOrDefault(key);
                bool thisNotSpecial = thisEntry == null || !thisEntry.SpecialConsideration;

                var vectorToAfterNext = afterNext - next;
                var dotAfterNext = Base6Directions.GetIntVector(afterNextBlock.Orientation.Up).Dot(ref vectorToAfterNext);

                if (!addOffsetForNextStair && thisNotSpecial && nextEntry == null && afterNextEntry != null && afterNextEntry.SpecialConsideration && dotAfterNext != 0
                  && afterNextDef.Id.SubtypeName.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  // going down stairs, add an offset

                  addOffsetForNextStair = true;
                  nextStairBlock = afterNextBlock;
                }
              }
            }
            else
              toVec = next;

            if (addOffsetForNextStair && nextStairBlock != null)
            {
              AddOffsetForStairComingUp(nextStairBlock, ref fromVec, ref localVec, ref toVec, path, gridGraph);
              continue;
            }
          }
          else
            toVec = localVec;

          bool isCatwalkExpansion = prevIsCatwalkExpansion || thisIsCatwalkExpansion || nextIsCatwalkExpansion;
          bool goingUpOrDownStair = false;

          if (nextIsCatwalkExpansion && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            var travelVec = toVec - fromVec;
            if (Base6Directions.GetIntVector(nextBlock.Orientation.Up).Dot(ref travelVec) != 0)
            {
              goingUpOrDownStair = true;
            }
          }

          if (isCatwalkExpansion)
          {
            if (addOffsetStart && cache.Count > 1)
            {
              if (prevIsCatwalkExpansion)
              {
                //AddOffsetForThisCatwalk(prevBlock, gridGraph, path, ref start, ref localVec, ref gridMatrix, ref gridSize, false);
                AddOffsetForThisCatwalk(prevBlock, gridGraph, path, fromVec, fromVec, start, ref gridMatrix, ref gridSize, false);
              }

              if (thisIsCatwalkExpansion && thisBlock != prevBlock)
              {
                AddOffsetForNextCatwalk(thisBlock, gridGraph, path, start, localVec, ref gridMatrix, ref gridSize);

                if (nextIsCatwalkExpansion)
                {
                  AddOffsetForNextCatwalk(nextBlock, gridGraph, path, localVec, next, ref gridMatrix, ref gridSize, goingUpOrDownStair);
                }

                continue;
              }
            }
            else if (thisIsCatwalkExpansion && cache.Count > 1)
            {
              AddOffsetForThisCatwalk(thisBlock, gridGraph, path, fromVec, localVec, next, ref gridMatrix, ref gridSize);

              if (nextIsCatwalkExpansion)
              {
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, localVec, next, ref gridMatrix, ref gridSize, goingUpOrDownStair);
              }

              continue;
            }
          }

          if (thisEntry != null)
          {
            if (thisEntry.SpecialConsideration)
            {
              HandleSpecialCase(fromVec, toVec, localVec, thisBlock, path, gridGraph);

              if (nextIsCatwalkExpansion)
              {
                AddOffsetForNextCatwalk(nextBlock, gridGraph, path, localVec, next, ref gridMatrix, ref gridSize, goingUpOrDownStair);
              }

              continue;
            }
            else if (!offset.HasValue)
            {
              offset = thisEntry.GetOffset(thisBlock);
            }
          }

          Vector3 ofs = (Vector3)(offset ?? Vector3D.Zero);
          AddTempNodeToPath(ref localVec, ref ofs, gridGraph, path);

          if (nextIsCatwalkExpansion)
          {
            AddOffsetForNextCatwalk(nextBlock, gridGraph, path, localVec, next, ref gridMatrix, ref gridSize, goingUpOrDownStair);
          }
        }

        lock (collection.PathToTarget)
          Interlocked.CompareExchange(ref collection.PathToTarget, path, collection.PathToTarget);
      }

      cache.Clear();
    }

    static void AddOffsetForStairComingUp(IMySlimBlock blockToConsider, ref Vector3I fromPosition, ref Vector3I thisPosition, ref Vector3I toPosition, MyQueue<Node> path, CubeGridMap gridGraph)
    {
      Matrix mWorld;
      if (blockToConsider.FatBlock != null)
        mWorld = blockToConsider.FatBlock.WorldMatrix;
      else
        blockToConsider.Orientation.GetMatrix(out mWorld);

      var toFromVec = toPosition - fromPosition;
      var upVec = Base6Directions.GetIntVector(blockToConsider.Orientation.Up);
      var dotUp = upVec.Dot(ref toFromVec);

      bool goingUpDown = dotUp != 0;
      bool stairsOnLeft = AiUtils.AreStairsOnLeftSide(blockToConsider);

      Vector3 leftRight = stairsOnLeft ? mWorld.Left : mWorld.Right;

      if (!goingUpDown)
        leftRight *= -1;

      Vector3 offset = leftRight * 0.25f * gridGraph.MainGrid.GridSize;

      AddTempNodeToPath(ref thisPosition, ref offset, gridGraph, path, false);
    }

    static void HandleSpecialCase(Vector3I from, Vector3I to, Vector3I current, IMySlimBlock block, MyQueue<Node> path, CubeGridMap gridGraph)
    {

      Matrix mWorld;
      if (block.FatBlock != null)
        mWorld = block.FatBlock.WorldMatrix;
      else
        block.Orientation.GetMatrix(out mWorld);

      MatrixI m = new MatrixI(block.Orientation);
      Vector3I travelVecTotal = to - from;
      var upDot = m.UpVector.Dot(ref travelVecTotal);

      var gridSize = block.CubeGrid.GridSize;
      var def = block.BlockDefinition.Id;
      var subtype = def.SubtypeName;

      bool isGCWE = AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(def);
      bool isLadder = isGCWE && subtype.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0;

      if (isLadder && upDot != 0)
      {
        // going up / down is the same regardless
        var offset = mWorld.Backward * 0.2f * gridSize;
        AddTempNodeToPath(ref current, ref offset, gridGraph, path);
      }
      else if (upDot > 0)
      {
        var dirVec = current - from;
        var dir = dirVec.RectangularLength() > 1 ? block.Orientation.Up : Base6Directions.GetDirection(dirVec); // make sure we don't try to get a direction with a weird vector (errors)
        var next = current + dirVec;

        if (isGCWE && from != current && dir == block.Orientation.Forward && gridGraph.IsOpenTile(next))
        {
          // needs to go around first
          HandleSpecialCase(from, next, current, block, path, gridGraph);

          var ofs = Vector3.Zero;
          var nextBlock = gridGraph.GetBlockAtPosition(next);

          if (nextBlock != null)
          {
            var nextDef = nextBlock.BlockDefinition as MyCubeBlockDefinition;
            var nextCell = nextDef?.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(nextBlock, next) : Vector3I.Zero;
            var key = MyTuple.Create(nextDef.Id, nextCell);

            UsableEntry usableEntry;
            if (AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(key, out usableEntry) && !usableEntry.SpecialConsideration)
            {
              ofs = usableEntry.GetOffset(nextBlock);
            }
          }

          AddTempNodeToPath(ref next, ref ofs, gridGraph, path, false);
          HandleSpecialGoingUp(ref current, ref next, block, path, gridGraph, ref isGCWE, ref def, ref mWorld);
        }
        else
        {
          // going up
          HandleSpecialGoingUp(ref current, ref from, block, path, gridGraph, ref isGCWE, ref def, ref mWorld);
        }
      }
      else if (upDot < 0)
      {
        // going down
        HandleSpecialGoingDown(ref current, ref from, block, path, gridGraph, ref isGCWE, ref def, ref mWorld);

        if (isGCWE && to != current)
        {
          var dirVec = to - current;
          var dir = dirVec.RectangularLength() > 1 ? block.Orientation.Up : Base6Directions.GetDirection(dirVec); // make sure we don't try to get a direction with a weird vector (errors)
          var next = current - dirVec;

          if (Base6Directions.GetOppositeDirection(dir) == block.Orientation.Forward && gridGraph.IsOpenTile(next))
          {
            // needs to go down and around
            var ofs = Vector3.Zero;
            var nextBlock = gridGraph.GetBlockAtPosition(next);

            if (nextBlock != null)
            {
              var nextDef = nextBlock.BlockDefinition as MyCubeBlockDefinition;
              var nextCell = nextDef?.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(nextBlock, next) : Vector3I.Zero;
              var key = MyTuple.Create(nextDef.Id, nextCell);

              UsableEntry usableEntry;
              if (AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(key, out usableEntry) && !usableEntry.SpecialConsideration)
              {
                ofs = usableEntry.GetOffset(nextBlock);
              }
            }

            AddTempNodeToPath(ref next, ref ofs, gridGraph, path, false);
            HandleSpecialCase(next, to, current, block, path, gridGraph);
          }
        }
      }
      else if (isGCWE)
      {
        if (isLadder)
        {
          var offset = mWorld.Forward * 0.2f * gridSize;
          AddTempNodeToPath(ref current, ref offset, gridGraph, path);
        }
        else if (subtype.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          Vector3I travelVec = to - current;

          if (travelVec.RectangularLength() > 1)
          {
            var max = Vector3I.One;
            var min = -max;

            Vector3I.Clamp(ref travelVec, ref min, ref max, out travelVec);

            if (travelVec.RectangularLength() > 1)
            {
              var worldNext = gridGraph.LocalToWorld(to);
              var worldLocal = gridGraph.LocalToWorld(current);
              var worldTravel = (Vector3)(worldNext - worldLocal);

              var intVec = Base6Directions.GetIntVector(mWorld.GetClosestDirection(ref worldTravel));
              var newVec = to - intVec;
              travelVec = to - newVec;
            }
          }

          var travelDir = Base6Directions.GetDirection(travelVec);

          bool isLeft = subtype.EndsWith("Left"); // Left = walkway is on left

          var lftRgt = isLeft ? mWorld.Left : mWorld.Right;
          var fwdBwd = (travelDir == block.Orientation.Forward) ? mWorld.Backward : mWorld.Forward;

          var offset = (fwdBwd + lftRgt) * 0.25f * gridSize;
          AddTempNodeToPath(ref current, ref offset, gridGraph, path);

          offset = (-fwdBwd + lftRgt) * 0.25f * gridSize;
          AddTempNodeToPath(ref current, ref offset, gridGraph, path);
        }
        else
        {
          AiSession.Instance.Logger.Error($"No pathing logic written for {def}");
        }
      }
      else if (subtype == "LargeRefineryIndustrial")
      {
        var blockCell = AiUtils.GetCellForPosition(block, current);
        if (blockCell == new Vector3I(1, 2, 0)) // bot is stopping at the top of the stairs, coming from a parallel point
        {
          var offset = (mWorld.Forward + mWorld.Right) * 0.25f * gridSize;
          AddTempNodeToPath(ref current, ref offset, gridGraph, path);
        }
        else if (upDot > 0)
        {
          if (blockCell == new Vector3I(1, 0, 0))
          {
            var offset = (mWorld.Forward + mWorld.Right) * 0.25f * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);

            offset = (mWorld.Backward + mWorld.Up + mWorld.Right * 0.25f) * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);
          }
          else if (blockCell == new Vector3I(1, 1, 0))
          {
            var offset = (mWorld.Backward * 0.75f + mWorld.Left * 0.25f) * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);

            offset = ((mWorld.Forward + mWorld.Left) * 0.25f + mWorld.Up) * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);
          }
          else // cell = 1, 2, 0
          {
            var offset = (mWorld.Forward + mWorld.Right) * 0.25f * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);
          }
        }
        else if (upDot < 0)
        {
          if (blockCell == new Vector3I(1, 0, 0))
          {
            var offset = (mWorld.Backward + mWorld.Up + mWorld.Right * 0.25f) * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);

            offset = (mWorld.Forward + mWorld.Right) * 0.25f * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);
          }
          else if (blockCell == new Vector3I(1, 1, 0))
          {
            var offset = ((mWorld.Forward + mWorld.Left) * 0.25f + mWorld.Up) * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);

            offset = (mWorld.Backward * 0.75f + mWorld.Left * 0.25f) * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);
          }
          else // cell = 1, 2, 0
          {
            var offset = (mWorld.Forward + mWorld.Right) * 0.25f * gridSize;
            AddTempNodeToPath(ref current, ref offset, gridGraph, path);
          }
        }
        else if (blockCell == new Vector3I(1, 0, 0)) // bot is stopping at the base of the stairs, coming from outside the block
        {
          var offset = (mWorld.Forward + mWorld.Right) * 0.25f * gridSize;
          AddTempNodeToPath(ref current, ref offset, gridGraph, path);
        }
        else // rest are just narrow walkways
        {
          var offset = mWorld.Forward * 0.25f * gridSize;
          AddTempNodeToPath(ref current, ref offset, gridGraph, path);
        }
      }
      else 
      {
        // TODO: NOT FINISHED
        AiSession.Instance.Logger.Error($"No pathing logic written for {def}");
      }
    }

    static void HandleSpecialGoingUp(ref Vector3I position, ref Vector3I prevPosition, IMySlimBlock block, MyQueue<Node> path, CubeGridMap gridGraph, 
      ref bool isGCWE, ref MyDefinitionId blockDef, ref Matrix m)
    {
      var queue = AiSession.Instance.NodeQueuePool.Get();

      HandleSpecialGoingDown(ref position, ref prevPosition, block, queue, gridGraph, ref isGCWE, ref blockDef, ref m, isGoingUp: true);

      for (int i = queue.Count - 1; i >= 0; i--)
      {
        path.Enqueue(queue[i]);
      }

      AiSession.Instance.NodeQueuePool.Return(ref queue);
    }

    static void HandleSpecialGoingDown(ref Vector3I position, ref Vector3I prevPosition, IMySlimBlock block, MyQueue<Node> path, CubeGridMap gridGraph,
      ref bool isGCWE, ref MyDefinitionId blockDef, ref Matrix m, bool isGoingUp = false)
    {
      var subtype = blockDef.SubtypeName;
      var gridSize = block.CubeGrid.GridSize;

      if (isGCWE) // catwalk expansion
      {
        bool isLeft = subtype.EndsWith("Left");

        if (subtype.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          bool isUTurn = subtype.IndexOf("UTurn", StringComparison.OrdinalIgnoreCase) >= 0;
          bool isCorner = subtype.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0;
          bool isSteep = subtype.IndexOf("Steep", StringComparison.OrdinalIgnoreCase) >= 0;
          bool isTall = ((MyCubeBlockDefinition)block.BlockDefinition).Size.AbsMax() > 1;

          if (isUTurn)
          {
            // left and right, tall and short

            if (isTall) // double block
            {
              var cell = AiUtils.GetCellForPosition(block, position);

              if (cell == Vector3.Zero)
              {
                return;
              }
              else if (isLeft)
              {
                var offset = (m.Up + m.Left * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down * 0.25f + m.Left * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down * 0.25f + m.Right * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down + m.Right * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
              else
              {
                var offset = (m.Up + m.Right * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down * 0.25f + m.Right * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down * 0.25f + m.Left * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down + m.Left * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
            }
            else // single block
            {
              if (isLeft)
              {
                var offset = (m.Up * 0.75f + m.Left * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Up * 0.35f + m.Left * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Up * 0.35f + m.Right * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Right * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
              else
              {
                var offset = (m.Up * 0.75f + m.Right * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Up * 0.35f + m.Right * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Up * 0.35f + m.Left * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Left * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
            }
          }
          else if (isCorner)
          {
            // left and right, tall and short
            if (isTall)
            {
              var cell = AiUtils.GetCellForPosition(block, position);

              if (cell == Vector3.Zero)
              {
                return;
              }
              else if (isLeft)
              {
                var offset = (m.Up + m.Left * 0.75f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down * 0.25f + m.Right * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down + m.Right * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
              else // is right
              {
                var offset = (m.Up+ m.Right * 0.75f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down * 0.25f + m.Left * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Down + m.Left * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
            }
            else // short
            {
              if (isLeft)
              {
                var offset = (m.Up * 0.75f + m.Left * 0.75f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Up * 0.35f + m.Right * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Right * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
              else
              {
                var offset = (m.Up * 0.75f + m.Right * 0.75f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Up * 0.35f + m.Left * 0.25f + m.Backward * 0.25f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

                offset = (m.Left * 0.25f + m.Forward * 0.75f) * gridSize;
                AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
              }
            }
          }
          else if (isSteep)
          {
            // base and tip

            // TODO: Need to figure out if they are going up / down or just passing through, and adjust offsets accordingly!

            if (subtype.IndexOf("Base", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              var lftRgt = isLeft ? m.Right : m.Left;

              var offset = (m.Up * 0.6f + lftRgt * 0.25f) * gridSize;
              AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

              offset = (m.Down * 0.25f + lftRgt * 0.25f + m.Forward * 0.25f) * gridSize;
              AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
            }
            else // tip
            {
              var lftRgt = isLeft ? m.Right : m.Left;

              var offset = (m.Up * 0.7f + lftRgt * 0.25f + m.Backward * 0.7f) * gridSize;
              AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

              offset = (m.Down * 0.25f + lftRgt * 0.25f + m.Forward * 0.1f) * gridSize;
              AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
            }
          }
          else // regular half types - left and right
          {
            // TODO: Need to figure out if they are going up / down or just passing through, and adjust offsets accordingly!
            var lftRgt = isLeft ? m.Right : m.Left;

            var offset = (m.Up + lftRgt * 0.25f + m.Backward * 0.75f) * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

            offset = (lftRgt * 0.25f + m.Forward * 0.75f) * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
          }
        }
        else
        {
          // TODO: NOT FINISHED!
          AiSession.Instance.Logger.Error($"No pathing logic written for {blockDef}");
        }
      }
      else // vanilla
      {
        if (subtype.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          Vector3 leftRight = AiUtils.AreStairsOnLeftSide(block) ? m.Left : m.Right;
          var offset = (m.Up * 0.85f + leftRight * 0.3f + m.Forward * 0.5f) * gridSize;
          AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

          offset = (m.Down * 0.15f + leftRight * 0.3f + m.Backward * 0.5f) * gridSize;
          AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
        }
        else if (block.FatBlock is IMyRefinery && subtype == "LargeRefineryIndustrial")
        {
          var cell = AiUtils.GetCellForPosition(block, position);

          if (cell == new Vector3I(1, 0, 0)) // Bottom of refinery stairs
          {
            var offset = (m.Backward * 0.8f + m.Up + m.Right * 0.325f) * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

            offset = (m.Forward * 0.5f + m.Right * 0.25f) * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
          }
          else if (cell == new Vector3I(1, 1, 0)) // Middle of refinery stairs
          {
            var prevCell = AiUtils.GetCellForPosition(block, prevPosition);
            var checkVector = isGoingUp ? new Vector3I(1, 1, 1) : new Vector3I(1, 2, 0); // 111 is landing between stairs, 120 is top of stairs

            if (prevCell != checkVector)
            {
              // Keeps it from adding the middle points twice when walking up / down the stairs.
              // Will only add the points when coming from the proper location.
              return;
            }

            var offset = ((m.Forward + m.Left) * 0.25f + m.Up) * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);

            offset = (m.Backward * 0.8f + m.Left * 0.325f) * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
          }
          else if (cell == new Vector3I(1, 2, 0)) // Top of refinery stairs
          {
            var offset = (m.Forward + m.Right) * 0.25f * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
          }
          else // rest are just narrow walkways
          {
            var offset = m.Forward * 0.25f * gridSize;
            AddTempNodeToPath(ref position, ref offset, gridGraph, path, false);
          }
        }
        else
        {
          // TODO: NOT FINISHED!
          AiSession.Instance.Logger.Error($"No pathing logic written for {blockDef}");
        }
      }
    }

    static void AddTempNodeToPath(ref Vector3I position, ref Vector3 offset, CubeGridMap gridGraph, MyQueue<Node> path, bool canSkip = true)
    {
      Node node;
      gridGraph.TryGetNodeForPosition(position, out node);

      TempNode tempNode = AiSession.Instance.TempNodePool.Get();
      tempNode.Update(node, offset);
      tempNode.CanBeSkipped = canSkip;
      AddNodeToPath(path, tempNode);
    }


    static void AddOffsetForThisCatwalk(IMySlimBlock thisBlock, CubeGridMap gridGraph, MyQueue<Node> path,
      Vector3I fromVec, Vector3I currentVec, Vector3I nextVec, ref MatrixD gridMatrix, ref float gridSize, bool addCenterPoint = true)
    {
      if (currentVec == nextVec)
        return;

      var blockDef = (MyCubeBlockDefinition)thisBlock.BlockDefinition;
      var subtype = blockDef.Id.SubtypeName;

      var cell = blockDef.Size.AbsMax() > 1 ? AiUtils.GetCellForPosition(thisBlock, currentVec) : Vector3I.Zero;
      var key = MyTuple.Create(blockDef.Id, cell);
      var ofs = Vector3.Zero;

      UsableEntry usableEntry;
      if (AiSession.Instance.BlockInfo.BlockDirInfo.TryGetValue(key, out usableEntry) && !usableEntry.SpecialConsideration)
      {
        ofs = usableEntry.GetOffset(thisBlock);
        AddTempNodeToPath(ref currentVec, ref ofs, gridGraph, path, false);
        return;
      }
      else if (subtype.StartsWith("GCMHalfWidthCatwalkBranchingWithStairs")
        || subtype.StartsWith("GCMHalfWidthCatwalkStraightWithStairs")
        || subtype.StartsWith("GCMHalfWidthCatwalkWallWithStairs"))
      {
        HandleSpecialCase(fromVec, nextVec, currentVec, thisBlock, path, gridGraph);
        return;
      }

      var travelVec = nextVec - currentVec;
      if (travelVec.RectangularLength() > 1)
      {
        var max = Vector3I.One;
        var min = -max;

        Vector3I.Clamp(ref travelVec, ref min, ref max, out travelVec);

        if (travelVec.RectangularLength() > 1)
        {
          Matrix m;
          if (thisBlock.FatBlock != null)
          {
            m = thisBlock.FatBlock.WorldMatrix;
          }
          else
          {
            thisBlock.Orientation.GetMatrix(out m);
          }

          var worldNext = gridGraph.LocalToWorld(nextVec);
          var worldLocal = gridGraph.LocalToWorld(currentVec);
          var worldTravel = (Vector3)(worldNext - worldLocal);

          var intVec = Base6Directions.GetIntVector(m.GetClosestDirection(ref worldTravel));
          nextVec = currentVec + intVec;
          travelVec = nextVec - currentVec;
        }
      }

      // add an offset to align with entrance of next block as needed
      var blockFwdVector = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
      var blockLeftVector = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
      var gridTravelDir = Base6Directions.GetDirection(travelVec);

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

      if (addCenterPoint)
      {
        AddTempNodeToPath(ref currentVec, ref Vector3.Zero, gridGraph, path, false);
      }

      ofs = (Vector3)(offset ?? Vector3D.Zero);
      AddTempNodeToPath(ref currentVec, ref ofs, gridGraph, path, false);
    }

    static void AddOffsetForNextCatwalk(IMySlimBlock nextBlock, CubeGridMap gridGraph, MyQueue<Node> path,
      Vector3I localVec, Vector3I next, ref MatrixD gridMatrix, ref float gridSize, bool goingUpDown = false)
    {
      if (localVec == next)
        return;

      var travelVec = next - localVec;
      if (travelVec.RectangularLength() > 1)
      {
        var max = Vector3I.One;
        var min = -max;

        Vector3I.Clamp(ref travelVec, ref min, ref max, out travelVec);

        if (travelVec.RectangularLength() > 1)
        {
          Matrix m;
          if (nextBlock.FatBlock != null)
          {
            m = nextBlock.FatBlock.WorldMatrix;
          }
          else
          {
            nextBlock.Orientation.GetMatrix(out m);
          }

          var worldNext = gridGraph.LocalToWorld(next);
          var worldLocal = gridGraph.LocalToWorld(localVec);
          var worldTravel = (Vector3)(worldNext - worldLocal);

          var intVec = Base6Directions.GetIntVector(m.GetClosestDirection(ref worldTravel));
          localVec = next - intVec;
          travelVec = next - localVec;
        }
      }

      // add an offset to align with entrance of next block as needed
      var subtype = nextBlock.BlockDefinition.Id.SubtypeName;
      var blockFwdVector = gridMatrix.GetDirectionVector(nextBlock.Orientation.Forward);
      var blockLeftVector = gridMatrix.GetDirectionVector(nextBlock.Orientation.Left);
      var gridTravelDir = Base6Directions.GetDirection(travelVec);
      Vector3D? offset = null;

      if (subtype.StartsWith("GCMHalfWidthCatwalkBranchingWithStairs")
        || subtype.StartsWith("GCMHalfWidthCatwalkStraightWithStairs")
        || subtype.StartsWith("GCMHalfWidthCatwalkWallWithStairs"))
      {
        var isLeft = subtype.EndsWith("Left");
        Vector3D lftRgt;

        if (goingUpDown)
        {
          // align with stairs to travel up / down
          lftRgt = isLeft ? -blockLeftVector : blockLeftVector;
        }
        else
        {
          // align with walkway beside stairs to go around
          lftRgt = isLeft ? blockLeftVector : -blockLeftVector;
        }

        if (gridTravelDir == nextBlock.Orientation.Forward)
        {
          offset = (-blockFwdVector + lftRgt) * gridSize * 0.5;
        }
        else
        {
          offset = (blockFwdVector + lftRgt) * gridSize * 0.5;
        }
      }
      else if (subtype.EndsWith("CatwalkHalfLeft"))
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
        var ofs = (Vector3)offset.Value;
        AddTempNodeToPath(ref next, ref ofs, gridGraph, path, canSkip: false);
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
      //Node prev = node;
      //if (path.Count > 1)
      //{
      //  prev = path[path.Count - 1];
      //  diff = node.Position - prev.Position;
      //}

      //AiSession.Instance.Logger.Log($" Prev: {prev.Position} | Node: {node.Position} | {node.NodeType} | Diff = {diff}");

      path.Enqueue(node);
    }
  }
}