using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Bots;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;
using AiEnabled.Ai.Support.PriorityQ;
using System.Diagnostics;
using VRage.ModAPI;
using Sandbox.Definitions;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using AiEnabled.Utilities;

namespace AiEnabled.Ai.Support
{
  public class PathCollection
  {
    List<IMyUseObject> _useObjList = new List<IMyUseObject>();
    List<Vector3I> _temp = new List<Vector3I>();
    readonly Vector3D[] _corners = new Vector3D[8];
    double? _distanceToWaypointSquared = null;

    /// <summary>
    /// Keeps track of the elapsed time since FindPath was called
    /// </summary>
    public Stopwatch PathTimer = new Stopwatch();

    /// <summary>
    /// If true, the pathfinding algorithm is currently using the collection
    /// </summary>
    public bool Locked;

    /// <summary>
    /// If true, the path will be updated the next time the bot needs to move toward its target
    /// </summary>
    public bool Dirty;

    /// <summary>
    /// The bot's current map graph
    /// </summary>
    public GridBase Graph => Bot?._currentGraph ?? null;

    /// <summary>
    /// The bot that is navigating to the target.
    /// Set this BEFORE calling FindPath!
    /// </summary>
    public BotBase Bot;

    /// <summary>
    /// True if there are any waypoints left to move to
    /// </summary>
    public bool HasPath => PathToTarget.Count > 0 || NextNode != null;

    /// <summary>
    /// True if the next node isn't null
    /// </summary>
    public bool HasNode => NextNode != null;

    /// <summary>
    /// The next waypoint, if there is one
    /// </summary>
    public Node NextNode { get; protected set; }

    /// <summary>
    /// The previous waypoint, if there was one
    /// </summary>
    public Node LastNode { get; protected set; }

    /// <summary>
    /// After running FindPath, this will contain the nodes to get there
    /// </summary>
    public MyQueue<Node> PathToTarget = new MyQueue<Node>();

    /// <summary>
    /// Use this queue for constructing the path in PathFinding
    /// </summary>
    public MyQueue<Node> TempPath = new MyQueue<Node>();

    /// <summary>
    /// This maps each point to its previous one
    /// </summary>
    public Dictionary<Vector3I, Vector3I> CameFrom = new Dictionary<Vector3I, Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// This tracks the movement cost for each checked position.
    /// It uses a fixed cost per tile along with a heuristic and keeps only the smallest value for any given tile
    /// </summary>
    public Dictionary<Vector3I, int> CostSoFar = new Dictionary<Vector3I, int>(Vector3I.Comparer);

    /// <summary>
    /// Used for permanently blocked nodes
    /// </summary>
    internal ConcurrentDictionary<Vector3I, byte> Obstacles = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);

    /// <summary>
    /// Used for temporarily blocked nodes (ie bot got stuck on the corner of a block, try another path but this one may still be valid).
    /// If same position is added more than five times without a successful pass, will be considered a permanent obstacle.
    /// </summary>
    internal ConcurrentDictionary<Vector3I, byte> TempObstacles = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);

    /// <summary>
    /// Used for potential block targets that reside outside the current map area.
    /// Key = block.CubeGrid.EntityId, Value = block
    /// </summary>
    internal ConcurrentDictionary<long, List<KeyValuePair<IMySlimBlock, Vector3D>>> BlockObstacles = new ConcurrentDictionary<long, List<KeyValuePair<IMySlimBlock, Vector3D>>>();

    /// <summary>
    /// The priority queue is used to sort and return the cell with the highest priority (lowest number)
    /// </summary>
    public SimplePriorityQueue<Vector3I> Queue = new SimplePriorityQueue<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// Whenever the algorithm detects that two Half Stair blocks are stacked on top of one another,
    /// it will find a middle point to place between them to make them traversable.
    /// Item1 = From, Item2 = To, Item3 = Point to insert between them
    /// </summary>
    public List<MyTuple<Vector3I, Vector3I, Vector3I>> IntermediatePoints = new List<MyTuple<Vector3I, Vector3I, Vector3I>>();

    /// <summary>
    /// Used to populate <see cref="IntermediatePoints"/>.
    /// Item1 = stair coming from, Item2 = stair going to, Item3 = position to insert between them.
    /// </summary>
    internal Queue<MyTuple<Vector3I, Vector3I, Vector3I>> StackedStairsFound { get; private set; } = new Queue<MyTuple<Vector3I, Vector3I, Vector3I>>();

    /// <summary>
    /// This cache is used to construct the path from goal back to the starting point
    /// before being converted into world coords and placed into a path queue
    /// </summary>
    public List<Vector3I> Cache = new List<Vector3I>();

    /// <summary>
    /// Used for checking blocked nodes
    /// </summary>
    public List<MyEntity> TempEntities = new List<MyEntity>();

    /// <summary>
    /// Contains doors that the bot does not have access to
    /// </summary>
    public ConcurrentDictionary<Vector3I, IMyDoor> DeniedDoors = new ConcurrentDictionary<Vector3I, IMyDoor>();

    /// <summary>
    /// Checks to see if the Bot has moved too far away from the current waypoint
    /// </summary>
    /// <returns>True if the Bot has moved too far away (needs a new path), otherwise False</returns>
    public bool UpdateDistanceToNextNode()
    {
      if (Bot.HasWeaponOrTool && (Bot.IsShooting || Bot._sideNode.HasValue))
      {
        _distanceToWaypointSquared = null;
        return false;
      }

      if (NextNode != null)
      {
        var curGraph = Graph;

        if (Bot.Target.PositionsValid)
        {
          Vector3D gotoPos = Bot.Target.CurrentGoToPosition;
          var localTarget = curGraph.WorldToLocal(gotoPos);

          if (localTarget == NextNode.Position)
          {
            _distanceToWaypointSquared = null;
            return false;
          }
        }

        var next = curGraph.LocalToWorld(NextNode.Position) + NextNode.Offset;
        var dSquared = Vector3D.DistanceSquared(Bot.BotInfo.CurrentBotPositionAdjusted, next);

        if (_distanceToWaypointSquared.HasValue)
        {
          if (dSquared < _distanceToWaypointSquared.Value)
          {
            _distanceToWaypointSquared = dSquared;
            return false;
          }

          var delta = dSquared - _distanceToWaypointSquared.Value;
          return delta > 10;
        }

        _distanceToWaypointSquared = dSquared;
      }

      return false;
    }

    public bool CheckIfBlockedObstacle(IMySlimBlock slim)
    {
      List<KeyValuePair<IMySlimBlock, Vector3D>> kvpList;
      if (BlockObstacles != null && BlockObstacles.TryGetValue(slim.CubeGrid.EntityId, out kvpList))
      {
        for (int i = kvpList.Count - 1; i >= 0; i--)
        {
          var kvp = kvpList[i];
          if (kvp.Key == null || kvp.Key.IsDestroyed || kvp.Key.CubeGrid.GetCubeBlock(kvp.Key.Position) != kvp.Key)
          {
            kvpList.RemoveAtFast(i);
          }
          else if (kvp.Key == slim)
          {
            return Vector3D.IsZero(kvp.Value - slim.CubeGrid.GridIntegerToWorld(slim.Position), 1);
          }
        }
      }

      return false;
    }

    public void AddBlockedObstacle(IMySlimBlock slim, Vector3D? slimWorld = null)
    {
      if (BlockObstacles == null || Graph == null || Graph.Dirty || Graph.Remake)
        return;

      List<KeyValuePair<IMySlimBlock, Vector3D>> kvpList;
      if (!BlockObstacles.TryGetValue(slim.CubeGrid.EntityId, out kvpList))
      {
        kvpList = new List<KeyValuePair<IMySlimBlock, Vector3D>>();
        BlockObstacles[slim.CubeGrid.EntityId] = kvpList;
      }

      if (slimWorld == null)
        slimWorld = slim.CubeGrid.GridIntegerToWorld(slim.Position);

      bool found = false;
      for (int i = kvpList.Count - 1; i >= 0; i--)
      {
        var kvp = kvpList[i];
        if (kvp.Key == null || kvp.Key.IsDestroyed || kvp.Key.CubeGrid.GetCubeBlock(kvp.Key.Position) != kvp.Key)
        {
          kvpList.RemoveAtFast(i);
        }
        else if (kvp.Key == slim)
        {
          found = true;

          if (!Vector3D.IsZero(kvp.Value - slimWorld.Value, 1))
          {
            kvpList[i] = new KeyValuePair<IMySlimBlock, Vector3D>(kvp.Key, slimWorld.Value);
            //AiSession.Instance.Logger.Log($"Updated block obstacle for {slim.BlockDefinition.DisplayNameText} [{slim.Position}]");
          }

          break;
        }
      }

      if (!found)
      {
        //AiSession.Instance.Logger.Log($"Added block obstacle for {slim.BlockDefinition.DisplayNameText} [{slim.Position}]");
        kvpList.Add(new KeyValuePair<IMySlimBlock, Vector3D>(slim, slimWorld.Value));
      }
    }

    public void ClearObstacles(bool includeBlocks = false)
    {
      Obstacles?.Clear();
      DeniedDoors?.Clear();

      if (includeBlocks)
        BlockObstacles?.Clear();
    }

    public void OnGridBaseClosing()
    {
      var thisGraph = Graph;
      if (thisGraph != null)
      {
        thisGraph.OnGridBaseClosing -= OnGridBaseClosing;
        thisGraph.OnPositionsRemoved -= ClearObstacles;
      }  
    }

    /// <summary>
    /// For grids, will assign the next clear point up to 5 points ahead to NextNode
    /// For voxel, will perform area checks for any new obstacles (ie dynamic grids) and assign the next clear point to NextNode
    /// </summary>
    /// <param name="current">The bot's current world position</param>
    /// <param name="onLadder">If the bot is currently climbing a ladder</param>
    /// <param name="nextIsLadder">If the next point is a ladder that the bot needs to climb</param>
    /// <param name="ladderUseObj">The use object for the ladder</param>
    /// <param name="useNow">If the bot is going DOWN the ladder, use it now before falling off the ledge</param>
    /// <param name="afterNextIsLadder">If the point after next is a ladder that the bot needs to climb</param>
    /// <param name="findNewPath">If this is true then something went awry during the pathing and the bot needs to recalculate the path</param>\
    /// <param name="isTransition">If the final point is a transition point to another map area or not</param>
    /// <param name="nextIsAirNode">If the next point is an Air Node</param>
    /// <returns></returns>
    public void GetNextNode(Vector3D current, bool onLadder, bool isTransition,
      out bool nextIsLadder, out bool afterNextIsLadder, out IMyUseObject ladderUseObj, out bool useNow, out bool findNewPath, out bool nextIsAirNode)
    {
      _distanceToWaypointSquared = null;
      ladderUseObj = null;
      nextIsLadder = afterNextIsLadder = false;
      useNow = findNewPath = false;
      nextIsAirNode = false;

      if (PathToTarget.Count == 0)
      {
        return;
      }

      lock (PathToTarget)
      {
        if (!Bot.Target.PositionsValid)
        {
          NextNode = PathToTarget.Dequeue();
          nextIsAirNode = NextNode.IsAirNode;
          return;
        }

        var curGraph = Graph;
        var gotoPosition = Bot.Target.CurrentGoToPosition;
        var localCurrent = curGraph.WorldToLocal(current);
        var localTarget = curGraph.WorldToLocal(gotoPosition);

        if (curGraph.ObstacleNodes.ContainsKey(localCurrent) || curGraph.ObstacleNodes.ContainsKey(localTarget))
        {
          NextNode = PathToTarget.Dequeue();
          nextIsAirNode = NextNode.IsAirNode;
          return;
        }

        if (!curGraph.IsGridGraph)
        {
          GetNextVoxelNode(current, gotoPosition, isTransition, out findNewPath, out nextIsAirNode);
          return;
        }

        var result = PathToTarget.Peek(); // TODO: Need to check that each node's edge isn't blocked ?
        var botMatrix = Bot.WorldMatrix;
        var botMatrixT = MatrixD.Transpose(botMatrix);
        var allowedDiff = 1.5;

        var gridGraph = curGraph as CubeGridMap;
        var localResult = result.Position;

        if (onLadder && localCurrent == localResult)
        {
          // the bot likely just hopped on a ladder, which puts it at the next point automatically
          // move next point one further to avoid having current and next both be the same ladder

          PathToTarget.Dequeue();
          result = PathToTarget.Peek();
          localResult = result.Position;
        }

        var worldCurrentNode = gridGraph.LocalToWorld(localCurrent); // use these for relative height check
        var worldResultNode = gridGraph.LocalToWorld(localResult); // use these for relative height check
        Vector3D worldCurrent, worldResult;

        Node testNode;
        if (gridGraph.TryGetNodeForPosition(localCurrent, out testNode))
          worldCurrent = gridGraph.LocalToWorld(testNode.Position) + testNode.Offset;
        else
          worldCurrent = current;

        if (gridGraph.TryGetNodeForPosition(localResult, out testNode))
          worldResult = gridGraph.LocalToWorld(testNode.Position) + testNode.Offset;
        else
          worldResult = worldResultNode;

        if (Bot.CanUseLadders)
        {
          var curSlim = gridGraph.GetBlockAtPosition(localCurrent);
          var nextSlim = gridGraph.GetBlockAtPosition(localResult);

          var curIsLadder = curSlim?.FatBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(curSlim.BlockDefinition.Id);
          nextIsLadder = nextSlim?.FatBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(nextSlim.BlockDefinition.Id);

          if (curIsLadder && !nextIsLadder)
          {
            var upDir = gridGraph.WorldMatrix.GetClosestDirection(botMatrix.Up);
            var upVec = Base6Directions.GetIntVector(upDir);
            var localVector = localResult - localCurrent;
            var vector = worldResultNode - worldCurrentNode;

            if (upVec.Dot(ref localVector) <= 0 || vector.Dot(botMatrix.Up) <= 0)
            {
              curIsLadder = false;
            }
          }

          if (!onLadder && (nextIsLadder || curIsLadder) && Bot._ticksSinceLastDismount > 180)
          {
            if (curIsLadder)
            {
              var cube = curSlim.FatBlock as MyCubeBlock;
              IMyUseObject useObj = null;
              bool voxelBlocked, charBlocked;

              bool blocked = IsLadderBlocked(cube, out voxelBlocked, out charBlocked);
              if (voxelBlocked || (blocked && !charBlocked))
              {
                bool firstWasVoxelBlocked = voxelBlocked;
                var nextCube = nextSlim?.FatBlock as MyCubeBlock;
                if (nextCube != null && !IsLadderBlocked(nextCube, out voxelBlocked, out charBlocked))
                {
                  result = PathToTarget.Dequeue();
                  useObj = GetBlockUseObject(nextCube);
                }
                else
                {
                  bool okay = false;
                  if (PathToTarget.Count > 1 && firstWasVoxelBlocked && !charBlocked)
                  {
                    var afterNext = PathToTarget[1].Position;
                    nextSlim = gridGraph.GetBlockAtPosition(afterNext);
                    if (nextSlim?.FatBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(nextSlim.BlockDefinition.Id))
                    {
                      nextCube = nextSlim.FatBlock as MyCubeBlock;
                      if (!IsLadderBlocked(nextCube, out voxelBlocked, out charBlocked))
                      {
                        okay = true;
                        PathToTarget.Dequeue();
                        result = PathToTarget.Dequeue();
                        useObj = GetBlockUseObject(nextCube);
                      }
                    }
                  }

                  if (!okay)
                  {
                    Node next;
                    gridGraph.TryGetNodeForPosition(localCurrent, out next);
                    NextNode = next;
                    return;
                  }
                }
              }
              else if (!blocked)
              {
                useObj = GetBlockUseObject(cube);
              }

              if (useObj != null)
              {
                Bot._stuckTimer = 0;
                Bot._stuckCounter = 0;
                Bot._stuckTimerReset = 0;
                Bot.WaitForStuckTimer = false;

                nextIsLadder = false;
                afterNextIsLadder = false;

                try
                {
                  useObj.Use(UseActionEnum.Manipulate, Bot.Character);
                }
                catch (NullReferenceException)
                {
                  // sometimes the game still throws an error trying to display the "ladder is blocked" notification
                }
              }

              NextNode = result;
              nextIsAirNode = result.IsAirNode;
              return;
            }
            else // nextIsLadder
            {
              var vector = worldResultNode - worldCurrentNode;
              var localVector = Vector3D.Rotate(vector, botMatrixT);
              var checkY = Math.Abs(localVector.Y) < allowedDiff ? 0 : Math.Sign(localVector.Y);

              if (checkY < 0)
              {
                var cube = nextSlim?.FatBlock as MyCubeBlock;
                ladderUseObj = GetBlockUseObject(cube);
                useNow = ladderUseObj != null;
                NextNode = result;

                return;
              }
            }
          }
        }

        // We've already set result to .Peek() so we just discard the first entry here
        PathToTarget.Dequeue();

        if (onLadder || PathToTarget.Count == 0)
        {
          if (onLadder && PathToTarget.Count > 0 && Vector3D.DistanceSquared(current, worldResult) < 0.3)
            result = PathToTarget.Dequeue();

          NextNode = result;
          nextIsAirNode = result.IsAirNode;
          return;
        }

        // nextIsLadder = false;
        var worldTarget = gridGraph.LocalToWorld(localTarget);
        var transToTgt = Vector3D.Rotate(worldTarget - worldCurrentNode, botMatrixT);
        var isFlying = Bot.BotInfo.IsFlying;

        // Can we go straight to the target? Only attempted if the target is eye level with the bot and bot isn't flying
        if (!isTransition && Bot.HasLineOfSight && !isFlying && Math.Abs(transToTgt.Y) < allowedDiff)
        {
          _temp.Clear();
          gridGraph.MainGrid.RayCastCells(worldCurrent, worldTarget, _temp);

          bool goDirect = true;
          bool currentIsAir = result.IsAirNode;
          for (int i = 0; i < _temp.Count; i++)
          {
            var point = _temp[i];
            Node n;

            if (gridGraph.ObstacleNodes.ContainsKey(point))
            {
              NextNode = result;
              nextIsAirNode = result.IsAirNode;
              return;
            }

            var worldPoint = gridGraph.LocalToWorld(point);
            if (gridGraph.MainGrid.CubeExists(point) || !gridGraph.IsPositionUsable(Bot, worldPoint, out n) || (!currentIsAir && n.IsAirNode))
            {
              goDirect = false;
              break;
            }

            var tgtPos = (_temp.Count > i + 1) ? _temp[i + 1] : localTarget;
            var vectorTo = tgtPos - point;

            if (n.IsBlocked(vectorTo))
            {
              goDirect = false;
              break;
            }

            Node nNext;
            if (gridGraph.TryGetNodeForPosition(tgtPos, out nNext) && nNext.IsBlocked(-vectorTo))
            {
              goDirect = false;
              break;
            }
          }

          if (goDirect)
          {
            Vector3D? hit = null;

            //if (gridGraph.RootVoxel != null)
            //{
            //  var line = new LineD(worldCurrent, gotoPosition);

            //  using (gridGraph.RootVoxel.Pin())
            //  {
            //    gridGraph.RootVoxel.RootVoxel.GetIntersectionWithLine(ref line, out hit);
            //  }
            //}

            if (!hit.HasValue)
            {
              CleanUp(true);
              Node next;
              gridGraph.TryGetNodeForPosition(localTarget, out next);
              nextIsAirNode = next?.IsAirNode ?? false;
              NextNode = next;
              return;
            }
          }
        }

        Vector3D? curPoint = null;
        Vector3D? prevPoint = null;
        var tgtIsOwner = Bot.Owner?.Character != null && Bot.Target.Entity == Bot.Owner.Character;
        var stopShort = tgtIsOwner && isFlying;

        // Nope, but can we skip ANY?
        for (int i = 0; i < 3; i++)
        {
          var maxCount = stopShort ? PathToTarget.Count - 1 : PathToTarget.Count; 
          if (i >= maxCount)
            break;

          var next = PathToTarget.Peek();
          if (gridGraph.ObstacleNodes.ContainsKey(next.Position) || (!result.IsAirNode && next.IsAirNode))
            break;

          var worldNext = gridGraph.LocalToWorld(next.Position) + next.Offset;
          var vector = worldNext - worldCurrentNode;
          var localVector = Vector3D.Rotate(vector, botMatrixT);
          var checkY = Math.Abs(localVector.Y) < allowedDiff ? 0 : Math.Sign(localVector.Y);

          if (i < 2 && checkY < 0 && Bot.CanUseLadders)
          {
            IMySlimBlock slim = gridGraph.GetBlockAtPosition(next.Position);
            if (slim != null && AiSession.Instance.LadderBlockDefinitions.Contains(slim.BlockDefinition.Id))
            {
              var cube = slim.FatBlock as MyCubeBlock;
              ladderUseObj = GetBlockUseObject(cube);
              afterNextIsLadder = ladderUseObj != null;
              useNow = afterNextIsLadder && checkY < 0;

              if (useNow)
                nextIsLadder = false;

              break;
            }
          }

          //if (gridGraph.RootVoxel != null)
          //{
          //  if (!curPoint.HasValue)
          //    curPoint = worldCurrent;

          //  if (!prevPoint.HasValue)
          //    prevPoint = worldCurrent;

          //  using (gridGraph.RootVoxel.Pin())
          //  {
          //    Vector3D? hit = null;

          //    if (!Vector3D.IsZero(curPoint.Value - worldNext))
          //    {
          //      var testLine = new LineD(curPoint.Value, worldNext);
          //      if (gridGraph.RootVoxel.RootVoxel.GetIntersectionWithLine(ref testLine, out hit))
          //      {
          //        // having to do this here instead of during map init because GetIntersectionWithLine isn't thread safe :(

          //        gridGraph.AddToObstacles(prevPoint.Value, curPoint.Value, worldNext);
          //        findNewPath = true;

          //        ReturnTempNodes(PathToTarget);
          //        PathToTarget.Clear();
          //        break;
          //      }
          //    }

          //    if (!Vector3D.IsZero(worldCurrent - worldNext))
          //    {
          //      var line = new LineD(worldCurrent, worldNext);
          //      if (gridGraph.RootVoxel.RootVoxel.GetIntersectionWithLine(ref line, out hit))
          //      {
          //        break;
          //      }
          //    }
          //  }
          //}

          if (checkY != 0)
            break;

          if (PathToTarget.Count > 1 && Bot.CanUseLadders)
          {
            var afterNext = PathToTarget[1].Position;
            IMySlimBlock slim = gridGraph.GetBlockAtPosition(afterNext);
            if (slim != null && AiSession.Instance.LadderBlockDefinitions.Contains(slim.BlockDefinition.Id))
            {
              var worldAfter = gridGraph.LocalToWorld(afterNext);
              vector = worldAfter - worldCurrentNode;
              localVector = Vector3D.Rotate(vector, botMatrixT);
              checkY = Math.Abs(localVector.Y) < allowedDiff ? 0 : Math.Sign(localVector.Y);

              if (checkY < 0)
                break;
            }
          }

          var cellVector = next.Position - localCurrent;
          if (cellVector.RectangularLength() > 1)
          {
            if (cellVector.X != 0)
            {
              var checkPoint = localCurrent + new Vector3I(cellVector.X, 0, 0);
              if (gridGraph.DoesBlockExist(checkPoint))
              {
                break;
              }
            }

            if (cellVector.Y != 0)
            {
              var checkPoint = localCurrent + new Vector3I(0, cellVector.Y, 0);
              if (gridGraph.DoesBlockExist(checkPoint))
              {
                break;
              }
            }

            if (cellVector.Z != 0)
            {
              var checkPoint = localCurrent + new Vector3I(0, 0, cellVector.Z);
              if (gridGraph.DoesBlockExist(checkPoint))
              {
                break;
              }
            }
          }

          _temp.Clear();
          gridGraph.MainGrid.RayCastCells(worldCurrent, worldNext, _temp);

          bool goDirect = true;
          for (int j = 0; j < _temp.Count; j++)
          {
            var point = _temp[j];
            if (gridGraph.ObstacleNodes.ContainsKey(point))
            {
              goDirect = false;
              break;
            }

            Node n;
            IMySlimBlock slim = gridGraph.GetBlockAtPosition(point);
            if (slim != null || !gridGraph.IsPositionUsable(Bot, gridGraph.LocalToWorld(point), out n) || (!result.IsAirNode && n.IsAirNode))
            {
              if (slim != null && Bot.BotInfo.IsRunning && AiSession.Instance.HalfStairBlockDefinitions.Contains(slim.BlockDefinition.Id))
              {
                Bot.Character.SwitchWalk();
              }

              goDirect = false;
              break;
            }

            var tgtPos = (_temp.Count > j + 1) ? _temp[j + 1] : next.Position;
            var vectorTo = tgtPos - point;

            if (n.IsBlocked(vectorTo))
            {
              goDirect = false;
              break;
            }

            Node nNext;
            if (gridGraph.TryGetNodeForPosition(tgtPos, out nNext) && nNext.IsBlocked(-vectorTo))
            {
              goDirect = false;
              break;
            }
          }

          if (!goDirect)
            break;

          prevPoint = curPoint;
          curPoint = worldNext;
          result = PathToTarget.Dequeue();
        }

        NextNode = result;
        nextIsAirNode = result.IsAirNode;
      }
    }

    void GetNextVoxelNode(Vector3D current, Vector3D actualTarget, bool isTransition, out bool findNewPath, out bool nextIsAirNode)
    {
      var voxelGrid = Graph as VoxelGridMap;
      var result = PathToTarget.Dequeue();
      findNewPath = false;
      nextIsAirNode = false;

      Node testNode;
      if (voxelGrid == null)
      {
        NextNode = result;
        nextIsAirNode = result.IsAirNode;
        return;
      }

      Vector3D worldCurrent, worldCurrentNode, worldTarget;
      var localCurrent = voxelGrid.WorldToLocal(current);
      var localTarget = voxelGrid.WorldToLocal(actualTarget);

      if (voxelGrid.TryGetNodeForPosition(localCurrent, out testNode))
      {
        worldCurrentNode = voxelGrid.LocalToWorld(testNode.Position);
        worldCurrent = worldCurrentNode + testNode.Offset;
      }
      else
      {
        worldCurrentNode = voxelGrid.LocalToWorld(localCurrent);
        worldCurrent = current;
      }

      if (voxelGrid.TryGetNodeForPosition(localTarget, out testNode))
      {
        worldTarget = voxelGrid.LocalToWorld(testNode.Position) + testNode.Offset;
      }
      else
      {
        worldTarget = voxelGrid.LocalToWorld(localTarget);
      }

      var botMatrixT = MatrixD.Transpose(Bot.WorldMatrix);
      var transToTgt = Vector3D.Rotate(worldTarget - worldCurrentNode, botMatrixT);
      var allowedDiff = voxelGrid.CellSize * 0.5f;

      var startTransformed = Vector3D.Transform(worldCurrent, voxelGrid.MatrixNormalizedInv);
      var endTransformed = Vector3D.Transform(worldTarget, voxelGrid.MatrixNormalizedInv);

      // Can we go straight to the target? Only attempted if the target is eye level with the bot
      if (!isTransition && Bot.HasLineOfSight && Math.Abs(transToTgt.Y) < allowedDiff)
      {          
        _temp.Clear();
        MyCubeGrid.RayCastStaticCells(startTransformed, endTransformed, _temp, voxelGrid.CellSize, voxelGrid.BoundingBox.HalfExtents);
        bool goDirectToTarget = true;
        for (int p = 0; p < _temp.Count; p++)
        {
          var point = _temp[p];
          if (!Obstacles.ContainsKey(point) && voxelGrid.IsOpenTile(point) && !voxelGrid.TempBlockedNodes.ContainsKey(point) && !voxelGrid.ObstacleNodes.ContainsKey(point))
            continue;

          goDirectToTarget = false;
          break;
        }

        if (goDirectToTarget)
        {
          Vector3D? hit = null;

          //if (voxelGrid.RootVoxel != null)
          //{
          //  var line = new LineD(worldCurrent, worldTarget);

          //  using (voxelGrid.RootVoxel.Pin())
          //    voxelGrid.RootVoxel.RootVoxel.GetIntersectionWithLine(ref line, out hit);
          //}

          if (!hit.HasValue)
          {
            CleanUp(true);
            Node next;
            voxelGrid.TryGetNodeForPosition(localTarget, out next);
            nextIsAirNode = next?.IsAirNode ?? false;
            NextNode = next;
            return;
          }
        }
      }

      //voxelGrid.OpenTileDict.TryGetValue(result.Node, out testNode);
      //var worldResult = testNode?.SurfacePosition ?? voxelGrid.LocalToWorld(result.Node) + (result.Offset ?? Vector3D.Zero);

      Vector3D? curPoint = null;
      Vector3D? curPointNode = null;
      Vector3D? prevPoint = null;

      // Nope, but can we skip ANY?
      for (int i = 0; i < 10; i++)
      {
        if (i >= PathToTarget.Count)
          break;

        var nextNode = PathToTarget.Peek();
        var nextNodeWorld = voxelGrid.LocalToWorld(nextNode.Position) + nextNode.Offset;

        var vector = nextNodeWorld - worldCurrentNode;
        var localVector = Vector3D.Rotate(vector, botMatrixT);

        if (Math.Abs(localVector.Y) > allowedDiff && (nextNode == null || !nextNode.IsAirNode))
          break;

        //if (voxelGrid.RootVoxel != null)
        //{
        //  if (!curPoint.HasValue)
        //  {
        //    curPointNode = worldCurrentNode;
        //    curPoint = worldCurrent;
        //  }

        //  if (!prevPoint.HasValue)
        //    prevPoint = worldCurrentNode;

        //  Vector3D? hit;
        //  var line = new LineD(worldCurrent, nextNodeWorld);
        //  var testLine = new LineD(curPoint.Value, nextNodeWorld);

        //  using (voxelGrid.RootVoxel.Pin())
        //  {
        //    if (voxelGrid.RootVoxel.RootVoxel.GetIntersectionWithLine(ref testLine, out hit))
        //    {
        //      voxelGrid.AddToObstacles(prevPoint.Value, curPointNode.Value, nextNodeWorld);
        //      findNewPath = true;

        //      ReturnTempNodes(PathToTarget);
        //      PathToTarget.Clear();
        //      break;
        //    }
        //    else if (voxelGrid.RootVoxel.RootVoxel.GetIntersectionWithLine(ref line, out hit))
        //    {
        //      break;
        //    }
        //  }
        //}

        _temp.Clear();
        var nextTransformed = Vector3D.Transform(nextNodeWorld, voxelGrid.MatrixNormalizedInv);
        MyCubeGrid.RayCastStaticCells(startTransformed, nextTransformed, _temp, voxelGrid.CellSize, voxelGrid.BoundingBox.HalfExtents);

        bool keepGoing = true;
        for (int p = 0; p < _temp.Count; p++)
        {
          var point = _temp[p];
          if (!Obstacles.ContainsKey(point) && voxelGrid.IsOpenTile(point) && !voxelGrid.TempBlockedNodes.ContainsKey(point) && !voxelGrid.ObstacleNodes.ContainsKey(point))
            continue;

          keepGoing = false;
          break;
        }

        if (!keepGoing)
          break;

        prevPoint = curPointNode;
        curPoint = nextNodeWorld;
        curPointNode = nextNodeWorld;
        result = PathToTarget.Dequeue();
      }

      _temp.Clear();
      NextNode = result;
      nextIsAirNode = result.IsAirNode;
    }

    /// <summary>
    /// Gets the use object for the block if it isn't null
    /// </summary>
    /// <param name="block"></param>
    public IMyUseObject GetBlockUseObject(MyCubeBlock block)
    {
      var blockUseComp = block?.Components?.Get<MyUseObjectsComponentBase>();
      if (blockUseComp != null)
      {
        _useObjList.Clear();
        blockUseComp.GetInteractiveObjects(_useObjList);

        if (_useObjList.Count > 0)
          return _useObjList[0];
      }

      return null;
    }

    /// <summary>
    /// Checks to see if something is blocking the ladder from being used
    /// </summary>
    /// <param name="ladder"></param>
    public bool IsLadderBlocked(MyCubeBlock ladder, out bool blockedByVoxel, out bool characterOnLadder, bool goingDown = false)
    {
      blockedByVoxel = false;
      characterOnLadder = false;
      bool blockedByChar = false;

      var center = ladder.PositionComp.WorldAABB.Center;
      var orienation = Quaternion.CreateFromRotationMatrix(ladder.WorldMatrix);
      var box = new MyOrientedBoundingBoxD(center, ladder.PositionComp.LocalAABB.HalfExtents, orienation);
      //DrawOBB(box, Color.Yellow);

      TempEntities.Clear();
      MyGamePruningStructure.GetAllEntitiesInOBB(ref box, TempEntities);

      for (int i = 0; i < TempEntities.Count; i++)
      {
        var ent = TempEntities[i];
        orienation = Quaternion.CreateFromRotationMatrix(ent.WorldMatrix);
        var entBox = new MyOrientedBoundingBoxD(ent.PositionComp.WorldAABB.Center, ent.PositionComp.LocalAABB.HalfExtents, orienation);

        if (!box.Intersects(ref entBox))
          continue;

        var ch = ent as IMyCharacter;
        if (ch != null)
        {
          
          if (ch.EntityId != Bot?.Character?.EntityId)
          {
            var name = string.IsNullOrWhiteSpace(ch.DisplayName) ? ch.Name : ch.DisplayName;
            blockedByChar = true;

            var state = ch.CurrentMovementState.GetMode();
            characterOnLadder = state == MyCharacterMovement.Ladder;

            if (characterOnLadder)
            {
              return true;
            }
          }

          continue;
        }

        var grid = ent as MyCubeGrid;
        if (grid != null)
        {
          if (grid != ladder.CubeGrid)
          {
            return true;
          }

          continue;
        }

        var voxel = ent as MyVoxelBase;
        if (voxel != null)
        {
          var voxBox = box;
          voxBox.HalfExtent *= 0.8;
          voxBox.GetCorners(_corners, 0);

          for (int j = 0; j < _corners.Length; j++)
          {
            if (GridBase.PointInsideVoxel(_corners[j], voxel))
            {
              blockedByVoxel = true;
              return true;
            }
          }
        }
      }

      return blockedByChar && !goingDown;
    }

    public void ClearNode(bool includelast = false)
    {
      var temp = LastNode as TempNode;
      if (temp != null)
        AiSession.Instance.TempNodeStack.Push(temp);

      if (includelast)
      {
        LastNode = null;
      }
      else
      {
        if (LastNode != null)
        {
          byte _;
          if (TempObstacles.ContainsKey(LastNode.Position))
            TempObstacles.TryRemove(LastNode.Position, out _);
        }

        LastNode = NextNode;
      }

      NextNode = null;
      _distanceToWaypointSquared = null;
    }

    /// <summary>
    /// Clears all collections
    /// </summary>
    public void CleanUp(bool includePath = false, bool includeObstacles = false)
    {
      try
      {
        if (Locked)
        {
          Dirty = true;
          return;
        }

        _useObjList.Clear();
        IntermediatePoints.Clear();
        CameFrom.Clear();
        CostSoFar.Clear();
        Queue.Clear();
        Cache.Clear();

        if (includeObstacles)
        {
          DeniedDoors.Clear();
          Obstacles?.Clear();
        }

        if (includePath)
        {
          lock (PathToTarget)
          {
            ReturnTempNodes(PathToTarget);
            PathToTarget.Clear();
          }

          lock (TempPath)
          {
            ReturnTempNodes(TempPath);
            TempPath.Clear();
          }

          var temp = NextNode as TempNode;
          if (temp != null)
            AiSession.Instance.TempNodeStack.Push(temp);

          temp = LastNode as TempNode;
          if (temp != null)
            AiSession.Instance.TempNodeStack.Push(temp);

          NextNode = null;
          LastNode = null;
          _distanceToWaypointSquared = null;
        }

        Dirty = false;
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in {GetType().FullName}: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public void Close()
    {
      Locked = false;

      ClearObstacles();
      OnGridBaseClosing();
      CleanUp(true);
      
      _useObjList = null;
      _temp = null;
      PathToTarget = null;
      TempPath = null;
      IntermediatePoints = null;
      CameFrom = null;
      CostSoFar = null;
      Queue = null;
      Cache = null;
      Obstacles = null;
      BlockObstacles = null;
    }

    public void CheckDoors(out MyRelationsBetweenPlayers relation)
    {
      DeniedDoors.Clear();
      relation = MyRelationsBetweenPlayers.Neutral;

      var gridGraph = Graph as CubeGridMap;
      if (gridGraph?.MainGrid == null || gridGraph.Dirty || Bot?.Character?.ControllerInfo == null)
        return;

      var grid = gridGraph.MainGrid;
      var botIdentityId = Bot.BotIdentityId;
      var gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0L;

      relation = MyIDModule.GetRelationPlayerPlayer(botIdentityId, gridOwner);
      if (relation == MyRelationsBetweenPlayers.Enemies || (Bot.Owner == null && relation == MyRelationsBetweenPlayers.Neutral))
      {
        foreach (var kvp in gridGraph.BlockedDoors)
        {
          var slim = kvp.Value.SlimBlock;
          if (slim.IsBlockUnbuilt())
          {
            gridGraph.Door_EnabledChanged(kvp.Value);
          }
          else
          {
            DeniedDoors[kvp.Key] = kvp.Value;
          }
        }

        foreach (var kvp in gridGraph.AllDoors)
        {
          var door = kvp.Value;
          var localDoor = kvp.Key;
          if (door == null || door.MarkedForClose || grid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(door.Position)) != localDoor)
          {
            gridGraph.AllDoors.TryRemove(localDoor, out door);
            continue;
          }

          if (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open || DeniedDoors.ContainsKey(localDoor))
            continue;

          if (door is IMyAirtightHangarDoor)
          {
            DeniedDoors[localDoor] = door;

            var cubeDef = door.SlimBlock.BlockDefinition as MyCubeBlockDefinition;
            var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

            Matrix matrix = new Matrix
            {
              Forward = Base6Directions.GetVector(door.Orientation.Forward),
              Left = Base6Directions.GetVector(door.Orientation.Left),
              Up = Base6Directions.GetVector(door.Orientation.Up)
            };

            matrix.TransposeRotationInPlace();

            Vector3I center = cubeDef.Center;
            Vector3I.TransformNormal(ref center, ref matrix, out center);
            var adjustedPosition = door.Position - center;

            foreach (var kvpFD in faceDict)
            {
              var cell = kvpFD.Key;
              Vector3I.TransformNormal(ref cell, ref matrix, out cell);
              var position = adjustedPosition + cell;

              DeniedDoors[position] = door;
            }
          }
          else if (Bot.Owner == null || !((MyDoorBase)door).AnyoneCanUse)
          {
            DeniedDoors[localDoor] = door;
          }
        }
      }
    }

    public void ReturnTempNodes(MyQueue<Node> queue)
    {
      if (queue?.Count > 0)
      {
        for (int i = 0; i < queue.Count; i++)
        {
          var pn = queue[i] as TempNode;
          if (pn != null)
            AiSession.Instance.TempNodeStack.Push(pn);
        }
      }
    }

    #region DebugOnly
    readonly HashSet<Vector3I> _tempDebug = new HashSet<Vector3I>(); // for debug only
    public void DrawFreeSpace(Vector3D start, Vector3D goal)
    {
      try
      {
        var curGraph = Graph;
        if (curGraph == null || !curGraph.Ready || MyAPIGateway.Session?.LocalHumanPlayer == null)
          return;

        AiSession.Instance.GridsToDraw.Add(curGraph.Key);

        _tempDebug.Clear();
        var next = NextNode;
        var current = curGraph.WorldToLocal(start);
        var end = curGraph.WorldToLocal(goal);

        var transition = Bot?._transitionPoint;
        var rotation = Quaternion.CreateFromRotationMatrix(curGraph.WorldMatrix);

        MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe;
        MyOrientedBoundingBoxD obb;
        Vector3D vec;
        Vector4 color = Color.Purple;

        if (AiSession.Instance.DrawDebug2)
        {
          MyAPIGateway.Utilities.ShowNotification($"Start: {current} | Next: {next?.Position.ToString() ?? "[NULL]"} | Transition: {transition?.ToString() ?? "[NULL]"} | Goal: {end}", 16);

          foreach (var localVec in curGraph.ObstacleNodes.Keys)
          {
            _tempDebug.Add(localVec);
            obb = new MyOrientedBoundingBoxD(curGraph.LocalToWorld(localVec), Vector3D.One * 0.2, rotation);
            AiUtils.DrawOBB(obb, Color.Firebrick, raster);
          }

          var boxVec = new Vector3I(5, 2, 5);
          var localBox = new BoundingBoxI(-boxVec, boxVec);
          localBox = localBox.Translate(current);

          var iter = new Vector3I_RangeIterator(ref localBox.Min, ref localBox.Max);
          raster = MySimpleObjectRasterizer.Wireframe;

          while (iter.IsValid())
          {
            var point = iter.Current;
            Node tileNode;
            if (point != current && point != end && !_tempDebug.Contains(point) && !Obstacles.ContainsKey(point) && curGraph.TryGetNodeForPosition(point, out tileNode))
            {
              vec = curGraph.LocalToWorld(point);
              var nodeColor = tileNode.IsAirNode ? Color.Blue : Color.Brown;
              obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);

              AiUtils.DrawOBB(obb, nodeColor, raster);
            }

            iter.MoveNext();
          }
        }

        raster = MySimpleObjectRasterizer.Solid;

        if (HasPath)
        {
          lock (PathToTarget)
          {
            foreach (var item in PathToTarget)
            {
              _tempDebug.Add(item.Position);
              var worldItem = curGraph.LocalToWorld(item.Position) + item.Offset;

              obb = new MyOrientedBoundingBoxD(worldItem, Vector3D.One * 0.1, rotation);
              AiUtils.DrawOBB(obb, Color.Yellow, raster);
            }
          }
        }

        if (Bot._sideNode.HasValue)
        {
          obb = new MyOrientedBoundingBoxD(Bot._sideNode.Value, Vector3D.One * 0.1, rotation);
          AiUtils.DrawOBB(obb, Color.Bisque, raster);

          color = Color.Tan.ToVector4();
          MySimpleObjectDraw.DrawLine(start, Bot._sideNode.Value, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        color = Color.Purple.ToVector4();

        Node node;
        if (curGraph.TryGetNodeForPosition(end, out node))
          vec = curGraph.LocalToWorld(node.Position) + node.Offset;
        else
          vec = curGraph.LocalToWorld(end);
  
        MySimpleObjectDraw.DrawLine(start, vec, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
        AiUtils.DrawOBB(obb, Color.Red, raster);

        var dir = curGraph.WorldMatrix.GetClosestDirection(vec - curGraph.OBB.Center);
        var normal = curGraph.WorldMatrix.GetDirectionVector(dir);

        color = Color.Orange.ToVector4();
        if (next != null)
        {
          vec = curGraph.LocalToWorld(next.Position) + next.Offset;
          obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
          AiUtils.DrawOBB(obb, Color.Purple, raster);

          var line = new LineD(start, vec);
          MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        if (transition != null)
        {
          vec = curGraph.LocalToWorld(transition.Position) + transition.Offset;
          obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
          AiUtils.DrawOBB(obb, Color.HotPink, raster);

          var line = new LineD(start, vec);
          MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        if (curGraph.TryGetNodeForPosition(current, out node))
          vec = curGraph.LocalToWorld(node.Position) + node.Offset;
        else
          vec = curGraph.LocalToWorld(current);
  
        obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
        AiUtils.DrawOBB(obb, Color.Green, raster);

        if (AiSession.Instance.DrawDebug2 && Bot?._nextGraph != null)
        {
          AiUtils.DrawOBB(Bot._nextGraph.OBB, Color.DarkRed * 0.5f, MySimpleObjectRasterizer.Solid);

          var corners = new Vector3D[8];
          BoundingBoxI box = BoundingBoxI.CreateInvalid();
          Bot._currentGraph.OBB.GetCorners(corners, 0);
          for (int j = 0; j < corners.Length; j++)
          {
            var localCorner = Bot._currentGraph.WorldToLocal(corners[j]);
            box.Include(ref localCorner);
          }

          Bot._nextGraph.OBB.GetCorners(corners, 0);
          BoundingBoxI otherBox = BoundingBoxI.CreateInvalid();
          for (int j = 0; j < corners.Length; j++)
          {
            var localCorner = Bot._currentGraph.WorldToLocal(corners[j]);
            otherBox.Include(ref localCorner);
          }

          box.IntersectWith(ref otherBox);
          var localExt = box.HalfExtents;
          var center = Bot._currentGraph.LocalToWorld(box.Center);
          MatrixD matrix;
          Vector3 halfExt;

          var vector = Bot._nextGraph.OBB.Center - Bot._currentGraph.OBB.Center;
          var forward = Bot._currentGraph.WorldMatrix.GetClosestDirection(vector);

          if (forward == Base6Directions.Direction.Up || forward == Base6Directions.Direction.Down)
          {
            matrix = Bot._currentGraph.WorldMatrix;
            halfExt = localExt * Bot._currentGraph.CellSize;
          }
          else
          {
            var fwdVector = Bot._currentGraph.WorldMatrix.GetDirectionVector(forward);
            matrix = Matrix.CreateWorld(Vector3D.Zero, fwdVector, Bot._currentGraph.WorldMatrix.Up);
            MatrixI m = new MatrixI(forward, Base6Directions.Direction.Up);
            Vector3I.TransformNormal(ref localExt, ref m, out localExt);
            halfExt = Vector3I.Abs(localExt) * Bot._currentGraph.CellSize;
          }

          //MyAPIGateway.Utilities.ShowNotification($"LocalExt = {localExt}, HalfExt = {Vector3.Round(halfExt, 1)}", 16);

          var quat = Quaternion.CreateFromRotationMatrix(matrix);
          var newOBB = new MyOrientedBoundingBoxD(center, halfExt, quat);

          AiUtils.DrawOBB(newOBB, Color.AntiqueWhite * 0.5f, MySimpleObjectRasterizer.Solid);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.ShowMessage($"Exception in DrawDebug: {ex.Message}");
      }
    }

    #endregion
  }
}
