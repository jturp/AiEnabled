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

namespace AiEnabled.Ai
{
  public static class PathFinding
  {
    public static void FindPath(Vector3I start, Vector3I goal, PathCollection collection, bool isIntendedGoal)
    {
      try
      {
        if (collection == null || collection.Dirty || collection.Locked)
          return;

        collection.Locked = true;

        var graph = collection.Graph;
        var bot = collection.Bot;
        if (graph == null || bot == null || !graph.Ready || bot.IsDead)
        {
          collection.Locked = false;
          return;
        }

        bool pathFound = RunAlgorithm(start, goal, collection);

        var currentMS = collection.PathTimer.Elapsed.TotalMilliseconds;
        if (collection.Dirty || currentMS > 10000)
        {
          if (currentMS > 10000)
            AiSession.Instance.Logger.Log($"{collection.Bot.Character.Name} - PathTimer exceeded 10000 ms", MessageType.WARNING);

          collection.Locked = false;
          return;
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
          if (graph.IsGridGraph && bot is RepairBot)
          {
            if (!bot.Target.IsSlimBlock && !bot.Target.IsInventory)
              bot._noPathCounter++;
            else
              bot.Target.RemoveTarget();

            graph.TempBlockedNodes[goal] = new byte();
          }
          else
          {
            bot._noPathCounter++;
          }
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
          collection.CleanUp(true);
        }
      }
    }

    static bool RunAlgorithm(Vector3I start, Vector3I goal, PathCollection collection)
    {
      var queue = collection.Queue;
      var cameFrom = collection.CameFrom;
      var costSoFar = collection.CostSoFar;
      var intermediatePoints = collection.IntermediatePoints;

      var bot = collection.Bot;
      var graph = collection.Graph;
      var isGridGraph = graph.IsGridGraph;
      var gridGraph = graph as CubeGridMap;
      var stackedStairs = gridGraph?.StackedStairsFound;
      var botPosition = bot.GetPosition();

      cameFrom.Clear();
      costSoFar.Clear();
      stackedStairs?.Clear();

      queue.Clear();
      queue.Enqueue(start, 0);

      cameFrom[start] = start;
      costSoFar[start] = 0;

      MyRelationsBetweenPlayers relation;
      collection.CheckDoors(out relation);

      //AiSession.Instance.Logger.ClearCached();
      //AiSession.Instance.Logger.Log($"Running FindPath for Start = {start} and End = {goal}");
      bool pathFound = false;
      while (queue.Count > 0)
      {
        var currentMS = collection.PathTimer.Elapsed.TotalMilliseconds;
        if (collection.Dirty || currentMS > 10000)
        {
          if (currentMS > 10000)
            AiSession.Instance.Logger.Log($"{bot.Character.Name} - PathTimer exceeded 10000 ms. Breaking out of RunAlgorithm", MessageType.WARNING);

          break;
        }

        if (pathFound)
        {
          break;
        }

        Vector3I current;
        if (!queue.TryDequeue(out current))
        {
          break;
        }

        if (current == goal)
        {
          pathFound = true;

          if (!isGridGraph)
          {
            break;
          }
        }

        Vector3I previous;
        if (!cameFrom.TryGetValue(current, out previous))
        {
          break;
        }

        Node currentNode;
        if (!graph.TryGetNodeForPosition(current, out currentNode))
        {
          break;
        }

        int currentCost;
        costSoFar.TryGetValue(current, out currentCost);
        currentCost += graph.MovementCost;

        bool checkDoors = true;
        if (isGridGraph)
        {
          IMyDoor door;
          if (currentNode?.Block != null)
          {
            door = currentNode.Block.FatBlock as IMyDoor;
            if (door != null)
            {
              bool isHangar = door is IMyAirtightHangarDoor;
              bool isOpen = door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open;

              if (!isOpen)
              {
                var blockDef = (MyCubeBlockDefinition)door.SlimBlock.BlockDefinition;
                if (door.SlimBlock.BuildLevelRatio < blockDef.CriticalIntegrityRatio)
                {
                  isOpen = true;
                }
                else
                {
                  currentCost += isHangar ? 9 : 2;
                }
              }

              if (bot.Owner == null)
              {
                if (!isOpen && (collection.DeniedDoors.ContainsKey(current) || gridGraph.BlockedDoors.ContainsKey(current)))
                {
                  currentCost += isHangar ? 20 : 10;
                }

                checkDoors = relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self;
              }
              else if (collection.DeniedDoors.ContainsKey(current))
              {
                continue;
              }
            }
          }
        }

        //AiSession.Instance.Logger.AddLine($" -> Checking neighbors for {current}");
        foreach (var next in graph.Neighbors(bot, previous, current, botPosition, checkDoors))
        {
          Node node;
          if (!graph.TryGetNodeForPosition(next, out node))
            continue;

          var newCost = currentCost;

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
            if (!bot.CanUseLadders)
              continue;
          }
          else if (node.IsGroundNode)
          {
            if (bot.WaterNodesOnly && !node.IsWaterNode)
              continue;

            if (bot.GroundNodesFirst && currentNode.IsAirNode)
              newCost  = Math.Max(0, newCost - 1);
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

            //stackFound = true; // not sure why I removed this xD
          }

          if (!stackFound && (!costSoFar.TryGetValue(next, out nextCost) || newCost < nextCost))
          {
            var isGoal = next == goal;
            int priority = isGoal ? -1 : newCost + Vector3I.DistanceManhattan(next, goal);

            queue.Enqueue(next, priority);
            costSoFar[next] = newCost;
            cameFrom[next] = current;

            //AiSession.Instance.Logger.AddLine($" ->-> Adding {next} to queue with priority of {priority}");

            if (isGoal)
            {
              //AiSession.Instance.Logger.AddLine($" -> Path found - exiting");

              break;
            }
          }
        }

        //AiSession.Instance.Logger.LogAll();
      }

      //AiSession.Instance.Logger.LogAll();
      return pathFound;
    }

    static void ConstructPathForVoxel(Vector3I start, Vector3I end, PathCollection collection)
    {
      var cameFrom = collection.CameFrom;
      //var openTiles = collection.Graph.OpenTileDict;
      var graph = collection.Graph;
      var path = collection.TempPath;
      var cache = collection.Cache;
      var optimizedCache = collection.Graph.OptimizedCache;

      cache.Clear();
      Vector3I current = end;
      while (current != start)
      {
        if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > 10000)
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
          if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > 10000)
          {
            break;
          }

          var localVec = cache[i];

          Node pathNode;
          if (graph.TryGetNodeForPosition(localVec, out pathNode))
          {
            path.Enqueue(pathNode);
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
      var optimizedCache = collection.Graph.OptimizedCache;
      var intermediatePoints = collection.IntermediatePoints;

      cache.Clear();
      Vector3I previous = end;
      Vector3I current = end;
      Vector3I next = end;
      while (current != start)
      {
        if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > 10000)
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
        if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > 10000)
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
      var tileDict = gridGraph.OpenTileDict;
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
          if (collection.Dirty || collection.PathTimer.Elapsed.TotalMilliseconds > 10000)
          {
            break;
          }

          var localVec = cache[i];
          MyCubeGrid grid = mainGrid;
          var gridLocalVector = localVec;

          Node n;
          if (tileDict.TryGetValue(localVec, out n) && n != null)
          {
            if (n.IsGridNodePlanetTile)
            {
              path.Enqueue(n);
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
            }
          }

          Node pathNode;
          if (optimizedCache.TryGetValue(localVec, out pathNode))
          {
            path.Enqueue(pathNode);
            continue;
          }

          Vector3D? offset = null;
          bool thisIsHalfStair = false, thisisHalfPanelSlope = false, prevIsHalfStair = false, prevIsHalfPanelSlope = false;
          bool addOffsetStart = cache.Count <= i + 1;

          IMySlimBlock thisBlock = grid.GetCubeBlock(gridLocalVector);
          if (thisBlock != null)
          {
            var thisDef = thisBlock.BlockDefinition.Id;
            thisIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(thisDef);
            thisisHalfPanelSlope = !thisIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(thisDef);
          }

          Vector3I fromVec = addOffsetStart ? start : cache[i + 1]; // If no previous in list, then previous is start position
          Vector3I fromToLocal = localVec - fromVec;
          Vector3D fwdTravelDir = gridMatrix.GetDirectionVector(Base6Directions.GetDirection(fromToLocal));
          IMySlimBlock prevBlock = gridGraph.GetBlockAtPosition(fromVec); // grid.GetCubeBlock(fromVec);

          if (prevBlock == null && tileDict.TryGetValue(fromVec, out n) && n?.Block != null)
          {
            var blockGrid = n.Block.CubeGrid;
            var worldFrom = mainGrid.GridIntegerToWorld(fromVec);
            var localFrom = blockGrid.WorldToGridInteger(worldFrom);
            prevBlock = blockGrid.GetCubeBlock(localFrom);
          }

          if (prevBlock != null)
          {
            var prevDef = prevBlock.BlockDefinition.Id;
            prevIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(prevDef);
            prevIsHalfPanelSlope = !prevIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(prevDef);
          }

          if (!thisIsHalfStair && !thisisHalfPanelSlope && thisBlock != null)
          {
            var cubeBlockDef = thisBlock.BlockDefinition as MyCubeBlockDefinition;
            var cubeDef = cubeBlockDef.Id;
            bool isDeadBody = cubeDef.SubtypeName.StartsWith("DeadBody");
            bool isDeco = !isDeadBody && AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(cubeDef);
            bool isHalfWall = !isDeco && AiSession.Instance.HalfWallDefinitions.ContainsItem(cubeDef);
            bool isHalfBlock = !isHalfWall && ((cubeDef.SubtypeName.StartsWith("Large") && cubeDef.SubtypeName.EndsWith("HalfArmorBlock"))
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

            if (isDeadBody)
            {
              offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * 0.3;

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
              continue;
            }

            if (cubeDef.SubtypeName == "LargeBlockOffsetDoor")
            {
              offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.3;

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
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
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.3;
              }
              else if (subtype.EndsWith("CounterCorner"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * -0.3
                  + gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * 0.3;
              }
              else if (subtype.StartsWith("LargeBlockCouch"))
              {
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward) * gridSize * 0.3;

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
                offset = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left) * gridSize * -0.3;
              }

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
              continue;
            }

            if (isHalfBlock)
            {
              offset = -downTravelDir * gridSize * 0.25;

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
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
                var blockBelow = gridGraph.GetBlockAtPosition(positionBelow); // grid.GetCubeBlock(positionBelow) as IMySlimBlock;

                if (blockBelow == null && tileDict.TryGetValue(positionBelow, out n) && n?.Block != null)
                {
                  var blockGrid = n.Block.CubeGrid;
                  var worldFrom = mainGrid.GridIntegerToWorld(positionBelow);
                  var localFrom = blockGrid.WorldToGridInteger(worldFrom);
                  blockBelow = blockGrid.GetCubeBlock(localFrom);
                }

                if (blockBelow != null && AiSession.Instance.RampBlockDefinitions.Contains(blockBelow.BlockDefinition.Id))
                {
                  offset = downTravelDir * gridSize * 0.25f;

                  //pathNode = tileDict[localVec];
                  gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                  pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                  optimizedCache[localVec] = pathNode;
                  path.Enqueue(pathNode);
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

                //pathNode = tileDict[localVec];
                gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                optimizedCache[localVec] = pathNode;
                path.Enqueue(pathNode);
                continue;
              }
              else if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Tip"))
              {
                if (botDownDir == thisBlock.Orientation.Forward)
                {
                  offset = -downTravelDir * gridSize * 0.5 - gridMatrix.GetDirectionVector(thisBlock.Orientation.Up) * gridSize * 0.25;

                  //pathNode = tileDict[localVec];
                  gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                  pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                  optimizedCache[localVec] = pathNode;
                  path.Enqueue(pathNode);
                  continue;
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

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
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

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
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

              //var tempNode = tileDict[localVec];

              Node tempNode;
              gridGraph.TryGetNodeForPosition(localVec, out tempNode);

              TempNode node;
              if (!AiSession.Instance.NodeStack.TryPop(out node))
                node = new TempNode();

              if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
              {                
                var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                {
                  if (offset.HasValue)
                    offset = offset.Value - downTravelDir * 0.5f;
                  else
                    offset = -downTravelDir * 0.5f;
                }  
              }

              node.Update(tempNode.Position, offset ?? Vector3.Zero, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
              path.Enqueue(node);
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

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
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

              //pathNode = tileDict[localVec];
              gridGraph.TryGetNodeForPosition(localVec, out pathNode);
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

              optimizedCache[localVec] = pathNode;
              path.Enqueue(pathNode);
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
                  var blockBelow = gridGraph.GetBlockAtPosition(positionBelow); // grid.GetCubeBlock(positionBelow) as IMySlimBlock;

                  if (blockBelow == null && tileDict.TryGetValue(positionBelow, out n) && n?.Block != null)
                  {
                    var blockGrid = n.Block.CubeGrid;
                    var worldFrom = mainGrid.GridIntegerToWorld(positionBelow);
                    var localFrom = blockGrid.WorldToGridInteger(worldFrom);
                    blockBelow = blockGrid.GetCubeBlock(localFrom);
                  }

                  if (blockBelow != null && AiSession.Instance.RampBlockDefinitions.Contains(blockBelow.BlockDefinition.Id))
                  {
                    offset = downTravelDir * gridSize * 0.25f;

                    //pathNode = tileDict[localVec];
                    gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                    pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                    optimizedCache[localVec] = pathNode;
                    path.Enqueue(pathNode);
                    continue;
                  }
                }
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

              //var tempNode = tileDict[localVec];

              Node tempNode;
              gridGraph.TryGetNodeForPosition(localVec, out tempNode);

              TempNode node;
              if (!AiSession.Instance.NodeStack.TryPop(out node))
                node = new TempNode();

              if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
              {
                var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                {
                  if (offset.HasValue)
                    offset = offset.Value - downTravelDir * 0.5f;
                  else
                    offset = -downTravelDir * 0.5f;
                }
              }

              node.Update(tempNode.Position, offset ?? Vector3.Zero, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
              path.Enqueue(node);
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

                //pathNode = tileDict[localVec];
                gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                optimizedCache[localVec] = pathNode;
                path.Enqueue(pathNode);
                continue;
              }
            }
          }
          else if (thisBlock == null)
          {
            var positionBelow = localVec + intVecDown;
            var blockBelowThis = gridGraph.GetBlockAtPosition(positionBelow); // grid.GetCubeBlock(positionBelow) as IMySlimBlock;

            if (blockBelowThis == null && tileDict.TryGetValue(positionBelow, out n) && n?.Block != null)
            {
              var blockGrid = n.Block.CubeGrid;
              var worldFrom = mainGrid.GridIntegerToWorld(positionBelow);
              var localFrom = blockGrid.WorldToGridInteger(worldFrom);
              blockBelowThis = blockGrid.GetCubeBlock(localFrom);
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
                if (blockBelowThis.Orientation.Up == botDownDir)
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
                else
                  offset = downTravelDir * gridSize * 0.5f;

                //var tempNode = tileDict[localVec];

                Node tempNode;
                gridGraph.TryGetNodeForPosition(localVec, out tempNode);


                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, offset ?? Vector3.Zero, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
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

                //var tempNode = tileDict[localVec];

                Node tempNode;
                gridGraph.TryGetNodeForPosition(localVec, out tempNode);

                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, offset ?? Vector3.Zero, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
                continue;
              }
              else if (isPanelHalfSlope)
              {
                if (Base6Directions.GetIntVector(blockBelowThis.Orientation.Left).Dot(ref intVecDown) == 0)
                {
                  // moving this down would be redundant, so skip it
                  continue;
                }

                //var tempNode = tileDict[localVec];

                Node tempNode;
                gridGraph.TryGetNodeForPosition(localVec, out tempNode);

                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, offset ?? Vector3.Zero, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
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

                  //pathNode = tileDict[localVec];
                  gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                  pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                  optimizedCache[localVec] = pathNode;
                  path.Enqueue(pathNode);
                  continue;
                }
              }
            }
          }

          Vector3I toVec;
          IMySlimBlock nextBlock = null, afterNextBlock = null;
          bool nextIsHalfStair = false, afterNextIsHalfStair = false;
          bool nextIsHalfPanelSlope = false, afterNextIsHalfPanelSlope = false;

          if (i > 0)
          {
            next = cache[i - 1];
            nextBlock = gridGraph.GetBlockAtPosition(next); // grid.GetCubeBlock(next);

            if (nextBlock == null && tileDict.TryGetValue(next, out n) && n?.Block != null)
            {
              var blockGrid = n.Block.CubeGrid;
              var worldFrom = mainGrid.GridIntegerToWorld(next);
              var localFrom = blockGrid.WorldToGridInteger(worldFrom);
              nextBlock = blockGrid.GetCubeBlock(localFrom);
            }

            if (nextBlock != null)
            {
              var nextDef = nextBlock.BlockDefinition.Id;
              nextIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(nextDef);
              nextIsHalfPanelSlope = !nextIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(nextDef);

              if (!thisIsHalfStair && !thisisHalfPanelSlope && !nextIsHalfStair && !nextIsHalfPanelSlope)
              {
                var nextIsLadder = AiSession.Instance.LadderBlockDefinitions.Contains(nextDef);
                if (nextIsLadder)
                {
                  var thisIsLadder = thisBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(thisBlock.BlockDefinition.Id);
                  if (thisIsLadder)
                  {
                    var blockBwd = -gridMatrix.GetDirectionVector(thisBlock.Orientation.Forward);
                    offset = blockBwd * gridSize * 0.3;

                    //pathNode = tileDict[localVec];
                    gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                    pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                    optimizedCache[localVec] = pathNode;
                    path.Enqueue(pathNode);
                    continue;
                  }
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
                    else
                    {
                      add = false;
                    }

                    if (add)
                    {

                      //pathNode = tileDict[localVec];
                      gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                      pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                      optimizedCache[localVec] = pathNode;
                      path.Enqueue(pathNode);
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

                      //pathNode = tileDict[localVec];
                      gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                      pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                      optimizedCache[localVec] = pathNode;
                      path.Enqueue(pathNode);
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


                    //pathNode = tileDict[localVec];
                    gridGraph.TryGetNodeForPosition(localVec, out pathNode);
                    pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

                    optimizedCache[localVec] = pathNode;
                    path.Enqueue(pathNode);
                    continue;
                  }
                }
              }
            }

            if (i > 1)
            {
              var afterNext = cache[i - 2];
              toVec = afterNext;

              afterNextBlock = gridGraph.GetBlockAtPosition(afterNext); // grid.GetCubeBlock(afterNext);

              if (afterNextBlock == null && tileDict.TryGetValue(afterNext, out n) && n?.Block != null)
              {
                // afterNextBlock = n.block; // I think this is all I need for these ???

                var blockGrid = n.Block.CubeGrid;
                var worldFrom = mainGrid.GridIntegerToWorld(afterNext);
                var localFrom = blockGrid.WorldToGridInteger(worldFrom);
                afterNextBlock = blockGrid.GetCubeBlock(localFrom);
              }

              if (afterNextBlock != null)
              {
                var afterNextDef = afterNextBlock.BlockDefinition.Id;
                afterNextIsHalfStair = AiSession.Instance.HalfStairBlockDefinitions.Contains(afterNextDef);
                afterNextIsHalfPanelSlope = !afterNextIsHalfStair && AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(afterNextDef);
              }
            }
            else
              toVec = next;
          }
          else
            toVec = localVec;

          if (prevIsHalfStair || thisIsHalfStair || nextIsHalfStair || afterNextIsHalfStair)
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

                //var tempNode = tileDict[start];
                Node tempNode;
                gridGraph.TryGetNodeForPosition(start, out tempNode);

                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, extra, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
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

                var dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Right"))
                  dir = -dir;

                var extra = dir * gridSize * 0.25;

                //var extra = fwdTravelDir * gridSize * 0.5;
                //var tempNode = tileDict[start];
                Node tempNode;
                gridGraph.TryGetNodeForPosition(start, out tempNode);

                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, extra, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
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

            //var tempNode2 = tileDict[localVec];

            Node tempNode2;
            gridGraph.TryGetNodeForPosition(localVec, out tempNode2);

            TempNode node2;
            if (!AiSession.Instance.NodeStack.TryPop(out node2))
              node2 = new TempNode();

            if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
            {
              var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
              if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
              {
                if (offset.HasValue)
                  offset = offset.Value - downTravelDir * 0.5f;
                else
                  offset = -downTravelDir * 0.5f;
              }
            }

            node2.Update(tempNode2.Position, offset ?? Vector3.Zero, tempNode2.NodeType, tempNode2.BlockedMask, tempNode2.Grid, tempNode2.Block);
            path.Enqueue(node2);
          }
          else if (prevIsHalfPanelSlope || thisisHalfPanelSlope || nextIsHalfPanelSlope || afterNextIsHalfPanelSlope)
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

                //var tempNode = tileDict[start];
                Node tempNode;
                gridGraph.TryGetNodeForPosition(start, out tempNode);

                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, extra, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
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

                var dir = gridMatrix.GetDirectionVector(thisBlock.Orientation.Left);
                if (!thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Left"))
                  dir = -dir;

                var extra = dir * gridSize * 0.25; // + fwdTravelDir * gridSize * 0.5;

                //var tempNode = tileDict[start];
                Node tempNode;
                gridGraph.TryGetNodeForPosition(start, out tempNode);

                TempNode node;
                if (!AiSession.Instance.NodeStack.TryPop(out node))
                  node = new TempNode();

                if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
                {
                  var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
                  if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
                  {
                    if (offset.HasValue)
                      offset = offset.Value - downTravelDir * 0.5f;
                    else
                      offset = -downTravelDir * 0.5f;
                  }
                }

                node.Update(tempNode.Position, extra, tempNode.NodeType, tempNode.BlockedMask, tempNode.Grid, tempNode.Block);
                path.Enqueue(node);
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

            //var tempNode2 = tileDict[localVec];

            Node tempNode2;
            gridGraph.TryGetNodeForPosition(localVec, out tempNode2);

            TempNode node2;
            if (!AiSession.Instance.NodeStack.TryPop(out node2))
              node2 = new TempNode();

            if (gridGraph.RootVoxel != null && !gridGraph.RootVoxel.MarkedForClose)
            {
              var worldPosition = gridGraph.LocalToWorld(localVec) + (offset ?? Vector3.Zero) + gridGraph.WorldMatrix.Down * 0.5;
              if (GridBase.PointInsideVoxel(worldPosition, gridGraph.RootVoxel))
              {
                if (offset.HasValue)
                  offset = offset.Value - downTravelDir * 0.5f;
                else
                  offset = -downTravelDir * 0.5f;
              }
            }

            node2.Update(tempNode2.Position, offset ?? Vector3.Zero, tempNode2.NodeType, tempNode2.BlockedMask, tempNode2.Grid, tempNode2.Block);
            path.Enqueue(node2);
          }
          else
          {
            //pathNode = tileDict[localVec];
            gridGraph.TryGetNodeForPosition(localVec, out pathNode);

            if (offset.HasValue)
              pathNode.Offset = (Vector3)(offset ?? Vector3.Zero);

            optimizedCache[localVec] = pathNode;
            path.Enqueue(pathNode);
          }
        }

        lock (collection.PathToTarget)
          Interlocked.CompareExchange(ref collection.PathToTarget, path, collection.PathToTarget);
      }

      cache.Clear();
    }

    static bool AreStairsOnLeftSide(IMySlimBlock stairBlock)
    {
      // Mirrored has stairs on Left side
      return stairBlock != null && AiSession.Instance.HalfStairMirroredDefinitions.Contains(stairBlock.BlockDefinition.Id);
    }
  }
}
