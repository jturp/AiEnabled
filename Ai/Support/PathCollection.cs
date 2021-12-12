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

namespace AiEnabled.Ai.Support
{
  public class PathCollection
  {
    List<IMyUseObject> _useObjList = new List<IMyUseObject>();
    List<Vector3I> _temp = new List<Vector3I>();
    Vector3D[] _corners = new Vector3D[8];
    double? _distanceToWaypointSquared = null;

    //public struct PathNode
    //{
    //  public Node Node;
    //  public Vector3D? Offset;

    //  public PathNode(Node n, Vector3D? offset = null)
    //  {
    //    Node = n;
    //    Offset = offset;
    //  }
    //}

    /// <summary>
    /// The number of ticks since the path collection was last cleared
    /// </summary>
    public uint IdlePathTimer;

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
    /// This graph needs to be set BEFORE calling FindPath
    /// Ensure this has the proper grid info or it won't work!
    /// </summary>
    public GridBase Graph;

    /// <summary>
    /// The bot that is navigating to the target.
    /// Set this BEFORE calling FindPath!
    /// </summary>
    public BotBase Bot;

    /// <summary>
    /// True if there are any waypoints left to move to
    /// </summary>
    public bool HasPath => PathToTarget.Count > 0;

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
    /// This tracks the movement cost for each checked position
    /// It uses a fixed cost per tile along with a heuristic and keeps only the smallest value for any given tile
    /// </summary>
    public Dictionary<Vector3I, int> CostSoFar = new Dictionary<Vector3I, int>(Vector3I.Comparer);

    /// <summary>
    /// The priority queue is used to sort and return the cell with the highest priority (lowest number)
    /// </summary>
    public SimplePriorityQueue<Vector3I> Queue = new SimplePriorityQueue<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// Whenever the algorithm detects that two Half Stair blocks are stacked on top of one another,
    /// it will find a middle point to place between them to make them traversable
    /// Item1 = From
    /// Item2 = To
    /// Item3 = Point to insert between them
    /// </summary>
    public List<MyTuple<Vector3I, Vector3I, Vector3I>> IntermediatePoints = new List<MyTuple<Vector3I, Vector3I, Vector3I>>();

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
      if (Bot.HasWeaponOrTool && (Bot._isShooting || Bot._sideNode.HasValue))
      {
        _distanceToWaypointSquared = null;
        return false;
      }

      if (NextNode != null)
      {
        if (Bot.Target.HasTarget)
        {
          Vector3D gotoPos, actualPos;
          Bot.Target.GetTargetPosition(out gotoPos, out actualPos);
          var localTarget = Graph.WorldToLocal(gotoPos);

          if (localTarget == NextNode.Position)
          {
            _distanceToWaypointSquared = null;
            return false;
          }
        }

        var next = NextNode.SurfacePosition ?? Graph.LocalToWorld(NextNode.Position);
        var dSquared = Vector3D.DistanceSquared(Bot.Position, next);

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

    /// <summary>
    /// For grids, will assign the next clear point up to 5 points ahead to NextNode
    /// For voxel, will perform area checks for any new obstacles (ie dynamic grids) and assign the next clear point to NextNode
    /// </summary>
    /// <param name="current">The bot's current world position</param>
    /// <param name="onLadder">If the bot is currently climbing a ladder</param>
    /// <param name="nextIsLadder">If the next point is a ladder that the bot needs to climb</param>
    /// <param name="ladderUseObj">The use object for the ladder</param>
    /// <param name="useNow">If the bot is going DOWN the ladder, use it now before falling off the ledge</param>
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
        Vector3D gotoPosition, actualTarget;
        if (!Bot.Target.GetTargetPosition(out gotoPosition, out actualTarget))
        {
          NextNode = PathToTarget.Dequeue();
          nextIsAirNode = NextNode.IsAirNode;
          return;
        }

        var localCurrent = Graph.WorldToLocal(current);
        var localTarget = Graph.WorldToLocal(gotoPosition);

        if (Graph.ObstacleNodes.ContainsKey(localCurrent) || Graph.ObstacleNodes.ContainsKey(localTarget))
        {
          NextNode = PathToTarget.Dequeue();
          nextIsAirNode = NextNode.IsAirNode;
          return;
        }

        if (!Graph.IsGridGraph)
        {
          GetNextVoxelNode(current, gotoPosition, isTransition, out findNewPath, out nextIsAirNode);
          return;
        }

        var result = PathToTarget.Peek(); // TODO: Need to check that each node's edge isn't blocked ?
        var botMatrixT = MatrixD.Transpose(Bot.WorldMatrix);
        var allowedDiff = 1.5;

        var gridGraph = Graph as CubeGridMap;
        var localResult = result.Position;

        var worldCurrentNode = Graph.LocalToWorld(localCurrent); // use these for relative height check
        var worldResultNode = Graph.LocalToWorld(localResult); // use these for relative height check
        Vector3D worldCurrent, worldResult;

        Node testNode;
        if (Graph.OpenTileDict.TryGetValue(localCurrent, out testNode) && testNode?.SurfacePosition != null)
          worldCurrent = testNode.SurfacePosition.Value;
        else
          worldCurrent = worldCurrentNode;

        if (Graph.OpenTileDict.TryGetValue(localResult, out testNode) && testNode?.SurfacePosition != null)
          worldResult = testNode.SurfacePosition.Value;
        else
          worldResult = worldResultNode;

        if (Bot._canUseLadders)
        {
          var slim = gridGraph.Grid.GetCubeBlock(localResult) as IMySlimBlock;
          var cube = slim?.FatBlock as MyCubeBlock;
          nextIsLadder = cube != null && AiSession.Instance.LadderBlockDefinitions.Contains(cube.BlockDefinition.Id);

          if (!onLadder && nextIsLadder && Bot._ticksSinceLastDismount > 180)
          {
            var curSlim = gridGraph.Grid.GetCubeBlock(localCurrent) as IMySlimBlock;
            if (curSlim?.FatBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(curSlim.BlockDefinition.Id))
            {
              cube = curSlim.FatBlock as MyCubeBlock;
              IMyUseObject useObj = null;
              bool voxelBlocked, charBlocked;

              bool blocked = IsLadderBlocked(cube, out voxelBlocked, out charBlocked);
              if (voxelBlocked || (blocked && !charBlocked))
              {
                bool firstWasVoxelBlocked = voxelBlocked;
                var nextSlim = gridGraph.Grid.GetCubeBlock(result.Position) as IMySlimBlock;
                if (nextSlim?.FatBlock != null && AiSession.Instance.LadderBlockDefinitions.Contains(nextSlim.BlockDefinition.Id))
                {
                  var nextCube = nextSlim.FatBlock as MyCubeBlock;
                  if (!IsLadderBlocked(nextCube, out voxelBlocked, out charBlocked))
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
                      nextSlim = gridGraph.Grid.GetCubeBlock(afterNext) as IMySlimBlock;
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
                      if (!Graph.OpenTileDict.TryGetValue(localCurrent, out next))
                      {
                        var grid = gridGraph.Grid;
                        var block = grid.GetCubeBlock(localCurrent) as IMySlimBlock;
                        next = new Node(localCurrent, grid, block, Bot.Position - Graph.LocalToWorld(localCurrent));
                      }

                      NextNode = next;
                      return;
                    }
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
                Bot._waitForStuckTimer = false;
                Bot._afterNextIsLadder = false;

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
            else
            {
              var vector = worldResultNode - worldCurrentNode;
              var localVector = Vector3D.Rotate(vector, botMatrixT);
              var checkY = Math.Abs(localVector.Y) < allowedDiff ? 0 : Math.Sign(localVector.Y);

              if (checkY < 0)
              {
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
        var worldTarget = Graph.LocalToWorld(localTarget);
        var transToTgt = Vector3D.Rotate(worldTarget - worldCurrentNode, botMatrixT);
        var isFlying = Bot._botState.IsFlying;

        // Can we go straight to the target? Only attempted if the target is eye level with the bot and bot isn't flying
        if (!isTransition && Bot.HasLineOfSight && !isFlying && Math.Abs(transToTgt.Y) < allowedDiff)
        {
          _temp.Clear();
          gridGraph.Grid.RayCastCells(worldCurrent, worldTarget, _temp);

          bool goDirect = true;
          for (int i = 0; i < _temp.Count; i++)
          {
            var point = _temp[i];
            Node n;

            if (Graph.ObstacleNodes.ContainsKey(point))
            {
              NextNode = result;
              nextIsAirNode = result.IsAirNode;
              return;
            }

            var worldPoint = Graph.LocalToWorld(point);
            if (gridGraph.Grid.CubeExists(point) || !Graph.IsPositionUsable(Bot, worldPoint, out n))
            {
              goDirect = false;
              break;
            }

            var tgtPos = (_temp.Count > i + 1) ? _temp[i + 1] : localTarget;
            var vectorTo = tgtPos - point;

            if (n.BlockedEdges != null && n.BlockedEdges.Contains(vectorTo))
            {
              goDirect = false;
              break;
            }

            Node nNext;
            if (Graph.OpenTileDict.TryGetValue(tgtPos, out nNext) && nNext.BlockedEdges != null)
            {
              if (nNext.BlockedEdges.Contains(-vectorTo))
              {
                goDirect = false;
                break;
              }
            }
          }

          if (goDirect)
          {
            Vector3D? hit = null;

            if (Graph.Planet != null)
            {
              var line = new LineD(worldCurrent, gotoPosition);

              using (Graph.Planet.Pin())
              {
                Graph.Planet.RootVoxel.GetIntersectionWithLine(ref line, out hit);
              }
            }

            if (!hit.HasValue)
            {
              CleanUp(true);
              Node next;
              if (!Graph.OpenTileDict.TryGetValue(localTarget, out next))
              {
                var grid = gridGraph.Grid;
                var block = grid.GetCubeBlock(localTarget) as IMySlimBlock;
                next = new Node(localTarget, grid, block, gotoPosition - worldTarget);
              }

              NextNode = next;
              nextIsAirNode = next.IsAirNode;
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
          if (Graph.ObstacleNodes.ContainsKey(next.Position))
            break;

          var worldNext = next.SurfacePosition ?? Graph.LocalToWorld(next.Position);
          var vector = worldNext - worldCurrentNode;
          var localVector = Vector3D.Rotate(vector, botMatrixT);
          var checkY = Math.Abs(localVector.Y) < allowedDiff ? 0 : Math.Sign(localVector.Y);

          if (i < 2 && checkY < 0 && Bot._canUseLadders)
          {
            IMySlimBlock slim = gridGraph.Grid.GetCubeBlock(next.Position);
            if (slim != null && AiSession.Instance.LadderBlockDefinitions.Contains(slim.BlockDefinition.Id))
            {
              var cube = slim.FatBlock as MyCubeBlock;
              ladderUseObj = GetBlockUseObject(cube);
              afterNextIsLadder = ladderUseObj != null;
              useNow = afterNextIsLadder && checkY < 0;
              break;
            }
          }

          if (checkY != 0)
            break;

          if (PathToTarget.Count > 1 && Bot._canUseLadders)
          {
            var afterNext = PathToTarget[1].Position;
            IMySlimBlock slim = gridGraph.Grid.GetCubeBlock(afterNext);
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

          _temp.Clear();
          gridGraph.Grid.RayCastCells(worldCurrent, worldNext, _temp);

          bool goDirect = true;
          for (int j = 0; j < _temp.Count; j++)
          {
            var point = _temp[j];
            if (Graph.ObstacleNodes.ContainsKey(point))
              break;

            Node n;
            IMySlimBlock slim = gridGraph.Grid.GetCubeBlock(point);
            if (slim != null || !Graph.IsPositionUsable(Bot, Graph.LocalToWorld(point), out n))
            {
              if (slim != null && Bot._botState.IsRunning && AiSession.Instance.HalfStairBlockDefinitions.Contains(slim.BlockDefinition.Id))
              {
                Bot.Character.SwitchWalk();
              }

              goDirect = false;
              break;
            }

            var tgtPos = (_temp.Count > j + 1) ? _temp[j + 1] : next.Position;
            var vectorTo = tgtPos - point;

            if (n.BlockedEdges != null)
            {
              if (n.BlockedEdges.Contains(vectorTo))
              {
                goDirect = false;
                break;
              }
            }

            Node nNext;
            if (Graph.OpenTileDict.TryGetValue(tgtPos, out nNext) && nNext.BlockedEdges != null)
            {
              if (nNext.BlockedEdges.Contains(-vectorTo))
              {
                goDirect = false;
                break;
              }
            }
          }

          if (Graph.Planet != null)
          {
            if (!curPoint.HasValue)
              curPoint = worldCurrent;

            if (!prevPoint.HasValue)
              prevPoint = worldCurrent;

            using (Graph.Planet.Pin())
            {
              Vector3D? hit = null;

              if (!Vector3D.IsZero(curPoint.Value - worldNext))
              {
                var testLine = new LineD(curPoint.Value, worldNext);
                if (Graph.Planet.RootVoxel.GetIntersectionWithLine(ref testLine, out hit))
                {
                  // having to do this here instead of during map init because GetIntersectionWithLine isn't thread safe :(

                  gridGraph.AddToObstacles(prevPoint.Value, curPoint.Value, worldNext);
                  findNewPath = true;
                  PathToTarget.Clear();
                  break;
                }
              }

              if (!Vector3D.IsZero(worldCurrent - worldNext))
              {
                var line = new LineD(worldCurrent, worldNext);
                if (Graph.Planet.RootVoxel.GetIntersectionWithLine(ref line, out hit))
                {
                  break;
                }
              }
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

      var localCurrent = Graph.WorldToLocal(current);
      Graph.OpenTileDict.TryGetValue(localCurrent, out testNode);
      var worldCurrentNode = Graph.LocalToWorld(localCurrent);
      var worldCurrent = testNode?.SurfacePosition ?? worldCurrentNode;

      var localTarget = Graph.WorldToLocal(actualTarget);
      Graph.OpenTileDict.TryGetValue(localTarget, out testNode);
      var worldTarget = testNode?.SurfacePosition ?? Graph.LocalToWorld(localTarget);

      var botMatrixT = MatrixD.Transpose(Bot.WorldMatrix);
      var transToTgt = Vector3D.Rotate(worldTarget - worldCurrentNode, botMatrixT);
      var allowedDiff = Graph.CellSize * 0.5f;

      var startTransformed = Vector3D.Transform(worldCurrent, voxelGrid.MatrixNormalizedInv);
      var endTransformed = Vector3D.Transform(worldTarget, voxelGrid.MatrixNormalizedInv);

      // Can we go straight to the target? Only attempted if the target is eye level with the bot
      if (!isTransition && Bot.HasLineOfSight && Math.Abs(transToTgt.Y) < allowedDiff)
      {          
        _temp.Clear();
        MyCubeGrid.RayCastStaticCells(startTransformed, endTransformed, _temp, Graph.CellSize, Graph.BoundingBox.HalfExtents);
        bool goDirectToTarget = true;
        foreach (var point in _temp)
        {
          if (Graph.OpenTileDict.ContainsKey(point) && !Graph.Obstacles.ContainsKey(point)
            && !Graph.TempBlockedNodes.ContainsKey(point) && !Graph.ObstacleNodes.ContainsKey(point))
            continue;

          goDirectToTarget = false;
          break;
        }

        if (goDirectToTarget)
        {
          Vector3D? hit = null;

          if (voxelGrid.Planet != null)
          {
            var line = new LineD(worldCurrent, worldTarget);

            using (voxelGrid.Planet.Pin())
              voxelGrid.Planet.RootVoxel.GetIntersectionWithLine(ref line, out hit);
          }

          if (!hit.HasValue)
          {
            CleanUp(true);
            Node next;
            if (!voxelGrid.OpenTileDict.TryGetValue(localTarget, out next))
            {
              var offset = actualTarget - Graph.LocalToWorld(localTarget);
              next = new Node(localTarget, null, null, offset);
            }

            NextNode = next;
            nextIsAirNode = next.IsAirNode;
            return;
          }
        }
      }

      //Graph.OpenTileDict.TryGetValue(result.Node, out testNode);
      //var worldResult = testNode?.SurfacePosition ?? Graph.LocalToWorld(result.Node) + (result.Offset ?? Vector3D.Zero);

      Vector3D? curPoint = null;
      Vector3D? curPointNode = null;
      Vector3D? prevPoint = null;

      // Nope, but can we skip ANY?
      for (int i = 0; i < 10; i++)
      {
        if (i >= PathToTarget.Count)
          break;

        var nextNode = PathToTarget.Peek();
        //var nextNode = Graph.OpenTileDict.GetValueOrDefault(next.Node, null);
        var nextNodeWorld = nextNode.SurfacePosition ?? Graph.LocalToWorld(nextNode.Position);
        //var worldNext = nextNode.SurfacePosition ?? nextNodeWorld;

        var vector = nextNodeWorld - worldCurrentNode;
        var localVector = Vector3D.Rotate(vector, botMatrixT);

        if (Math.Abs(localVector.Y) > allowedDiff && (nextNode == null || !nextNode.IsAirNode))
          break;

        if (voxelGrid.Planet != null)
        {
          if (!curPoint.HasValue)
          {
            curPointNode = worldCurrentNode;
            curPoint = worldCurrent;
          }

          if (!prevPoint.HasValue)
            prevPoint = worldCurrentNode;

          Vector3D? hit;
          var line = new LineD(worldCurrent, nextNodeWorld);
          var testLine = new LineD(curPoint.Value, nextNodeWorld);

          using (voxelGrid.Planet.Pin())
          {
            if (voxelGrid.Planet.RootVoxel.GetIntersectionWithLine(ref testLine, out hit))
            {
              voxelGrid.AddToObstacles(prevPoint.Value, curPointNode.Value, nextNodeWorld);
              findNewPath = true;
              PathToTarget.Clear();
              break;
            }
            else if (voxelGrid.Planet.RootVoxel.GetIntersectionWithLine(ref line, out hit))
            {
              break;
            }
          }
        }

        _temp.Clear();
        var nextTransformed = Vector3D.Transform(nextNodeWorld, voxelGrid.MatrixNormalizedInv);
        MyCubeGrid.RayCastStaticCells(startTransformed, nextTransformed, _temp, Graph.CellSize, Graph.BoundingBox.HalfExtents);

        bool keepGoing = true;
        foreach (var point in _temp)
        {
          if (Graph.OpenTileDict.ContainsKey(point) && !Graph.Obstacles.ContainsKey(point)
            && !Graph.TempBlockedNodes.ContainsKey(point) && !Graph.ObstacleNodes.ContainsKey(point))
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
          box.GetCorners(_corners, 0);
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
      if (includelast)
        LastNode = null;
      else
        LastNode = NextNode;

      NextNode = null;
      _distanceToWaypointSquared = null;
    }

    /// <summary>
    /// Clears all collections
    /// </summary>
    public void CleanUp(bool includePath = false)
    {
      try
      {
        if (Locked)
        {
          Dirty = true;
          return;
        }

        _useObjList.Clear();
        _temp.Clear();
        IdlePathTimer = 0;
        IntermediatePoints.Clear();
        CameFrom.Clear();
        CostSoFar.Clear();
        Queue.Clear();
        Cache.Clear();
        DeniedDoors.Clear();

        if (includePath)
        {
          lock (PathToTarget)
            PathToTarget.Clear();

          NextNode = null;
          LastNode = null;
          _distanceToWaypointSquared = null;
        }

        Dirty = false;
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in {GetType().FullName}: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }
    }

    public void Close()
    {
      Locked = false;

      CleanUp(true);
      
      _useObjList = null;
      _temp = null;
      PathToTarget = null;
      IntermediatePoints = null;
      CameFrom = null;
      CostSoFar = null;
      Queue = null;
      Cache = null;
    }

    public void CheckDoors(out MyRelationsBetweenPlayers relation)
    {
      DeniedDoors.Clear();
      relation = MyRelationsBetweenPlayers.Neutral;

      var gridGraph = Graph as CubeGridMap;
      if (gridGraph?.Grid == null || gridGraph.Dirty || Bot?.Character?.ControllerInfo == null)
        return;

      var grid = gridGraph.Grid;
      var botIdentityId = Bot.Character.ControllerInfo.ControllingIdentityId;
      var gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : 0;

      relation = MyIDModule.GetRelationPlayerPlayer(botIdentityId, gridOwner);
      if (relation == MyRelationsBetweenPlayers.Enemies || (Bot.Owner == null && relation == MyRelationsBetweenPlayers.Neutral))
      {
        foreach (var kvp in gridGraph.BlockedDoors)
        {
          var slim = kvp.Value.SlimBlock;
          if (slim.BuildLevelRatio < ((MyCubeBlockDefinition)slim.BlockDefinition).CriticalIntegrityRatio)
          {
            gridGraph.Door_EnabledChanged(kvp.Value);
          }
          else
          {
            DeniedDoors[kvp.Key] = kvp.Value;
          }
        }

        foreach (var node in gridGraph.OpenTileDict.Values)
        {
          if (node?.Block == null || node.IsGridNodePlanetTile)
            continue;

          var door = node.Block.FatBlock as IMyDoor;
          if (door == null || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
            continue;

          var localDoor = grid.WorldToGridInteger(door.GetPosition());

          if (DeniedDoors.ContainsKey(localDoor))
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

            foreach (var kvp in faceDict)
            {
              var cell = kvp.Key;
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

    #region DebugOnly
    HashSet<Vector3I> _tempDebug = new HashSet<Vector3I>(); // for debug only
    public void DrawFreeSpace(Vector3D start, Vector3D goal)
    {
      try
      {
        if (Graph?.Ready != true || MyAPIGateway.Session?.LocalHumanPlayer == null)
          return;

        _tempDebug.Clear();
        var next = NextNode;
        var current = Graph.WorldToLocal(start);
        var end = Graph.WorldToLocal(goal);

        Vector3I? transition = null;
        if (Bot != null && Bot._transitionPoint != null)
          transition = Bot._transitionPoint.Position;

        var rotation = Quaternion.CreateFromRotationMatrix(Graph.WorldMatrix);

        var owner = Bot?.Owner?.Character;
        if (owner != null)
        {
          var ownerNode = Graph.WorldToLocal(owner.WorldMatrix.Translation);
          MyAPIGateway.Utilities.ShowNotification($"Owner: {ownerNode}", 16);
          
          var camPosition = MyAPIGateway.Session.Camera.Position;
          var localCamera = Graph.WorldToLocal(camPosition);
          MyAPIGateway.Utilities.ShowNotification($"Camera: {localCamera}", 16);
        }

        var botTile = Graph.OpenTileDict.GetValueOrDefault(current, null);
        MyAPIGateway.Utilities.ShowNotification($"Start: {current} | Next: {next?.Position.ToString() ?? "[NULL]"} | Transition: {transition?.ToString() ?? "[NULL]"} | Goal: {end}", 16);
        MyAPIGateway.Utilities.ShowNotification($"Obstacle count: {Graph.ObstacleNodes.Count}", 16);

        MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe;
        MyOrientedBoundingBoxD obb;
        Vector3D vec;
        Vector4 color = Color.Purple;

        foreach (var node in Graph.ObstacleNodes.Keys)
        {
          _tempDebug.Add(node);
          obb = new MyOrientedBoundingBoxD(Graph.LocalToWorld(node), Vector3D.One * 0.2, rotation);
          DrawOBB(obb, Color.Firebrick, raster);
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
          if (point != current && point != end && !_tempDebug.Contains(point) && !Graph.Obstacles.ContainsKey(point) && Graph.OpenTileDict.TryGetValue(point, out tileNode))
          {
            vec = Graph.LocalToWorld(point);
            var nodeColor = tileNode.IsAirNode ? Color.Blue : Color.Brown;
            obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);

            DrawOBB(obb, nodeColor, raster);
          }

          iter.MoveNext();
        }

        raster = MySimpleObjectRasterizer.Solid;

        foreach (var point in Graph.TempBlockedNodes.Keys)
        {
          if (Graph.OpenTileDict.ContainsKey(point))
          {
            _tempDebug.Add(point);
            vec = Graph.LocalToWorld(point);
            obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);

            DrawOBB(obb, Color.Red, MySimpleObjectRasterizer.Solid);
          }
        }

        if (HasPath)
        {
          lock (PathToTarget)
          {
            foreach (var item in PathToTarget)
            {
              _tempDebug.Add(item.Position);
              var worldItem = item.SurfacePosition ?? Graph.LocalToWorld(item.Position);

              obb = new MyOrientedBoundingBoxD(worldItem, Vector3D.One * 0.1, rotation);
              DrawOBB(obb, Color.Yellow, raster);
            }
          }
        }

        if (Bot._sideNode.HasValue)
        {
          obb = new MyOrientedBoundingBoxD(Bot._sideNode.Value, Vector3D.One * 0.1, rotation);
          DrawOBB(obb, Color.Bisque, raster);

          color = Color.Tan.ToVector4();
          MySimpleObjectDraw.DrawLine(start, Bot._sideNode.Value, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        color = Color.Purple.ToVector4();
        vec = Graph.LocalToWorld(end);
        MySimpleObjectDraw.DrawLine(start, vec, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

        obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
        DrawOBB(obb, Color.Red, raster);

        var dir = Graph.WorldMatrix.GetClosestDirection(vec - Graph.OBB.Center);
        var normal = Graph.WorldMatrix.GetDirectionVector(dir);
        var center = Graph.OBB.Center;

        color = Color.Green.ToVector4();
        MySimpleObjectDraw.DrawLine(center, vec, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        MySimpleObjectDraw.DrawLine(center, center + normal * 50, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

        color = Color.Orange.ToVector4();
        if (next != null)
        {
          vec = next.SurfacePosition ?? Graph.LocalToWorld(next.Position);
          obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
          DrawOBB(obb, Color.Purple, raster);

          var line = new LineD(start, vec);
          MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        if (transition.HasValue)
        {
          vec = Graph.LocalToWorld(transition.Value);
          obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
          DrawOBB(obb, Color.HotPink, raster);

          var line = new LineD(start, vec);
          MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("Square"), ref color, 0.05f);
        }

        vec = Graph.LocalToWorld(current);
        obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);
        DrawOBB(obb, Color.Green, raster);

        color = Color.LightGreen;
        var to = center + Graph.WorldMatrix.Up * 50;
        MySimpleObjectDraw.DrawLine(center, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

        color = Color.LightBlue;
        to = center + Graph.WorldMatrix.Forward * 50;
        MySimpleObjectDraw.DrawLine(center, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

        color = Color.PaleVioletRed;
        to = center + Graph.WorldMatrix.Right * 50;
        MySimpleObjectDraw.DrawLine(center, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

        DrawOBB(Graph.OBB, Color.Orange * 0.5f, MySimpleObjectRasterizer.Solid);

        if (Bot?._nextGraph != null)
        {
          DrawOBB(Bot._nextGraph.OBB, Color.DarkRed * 0.5f, MySimpleObjectRasterizer.Solid);

          color = Color.LightGreen;
          center = Bot._nextGraph.OBB.Center;
          to = center + Bot._nextGraph.WorldMatrix.Up * 50;
          MySimpleObjectDraw.DrawLine(center, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

          color = Color.LightBlue;
          to = center + Bot._nextGraph.WorldMatrix.Forward * 50;
          MySimpleObjectDraw.DrawLine(center, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

          color = Color.PaleVioletRed;
          to = center + Bot._nextGraph.WorldMatrix.Right * 50;
          MySimpleObjectDraw.DrawLine(center, to, MyStringId.GetOrCompute("Square"), ref color, 0.05f);

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
          center = Bot._currentGraph.LocalToWorld(box.Center);

          var vector = Bot._nextGraph.OBB.Center - Bot._currentGraph.OBB.Center;
          var forward = Bot._currentGraph.WorldMatrix.GetClosestDirection(vector);
          var upDir = forward == Base6Directions.Direction.Up ? Base6Directions.Direction.Backward : Base6Directions.Direction.Up;
          var upVector = forward == Base6Directions.Direction.Up ? Bot._currentGraph.WorldMatrix.Backward : Bot._currentGraph.WorldMatrix.Up;
          var fwdVector = Bot._currentGraph.WorldMatrix.GetDirectionVector(forward);

          var matrix = Matrix.CreateWorld(Vector3D.Zero, fwdVector, upVector);
          MatrixI m = new MatrixI(forward, upDir);
          Vector3I.TransformNormal(ref localExt, ref m, out localExt);
          var halfExt = Vector3I.Abs(localExt) * Bot._currentGraph.CellSize;

          MyAPIGateway.Utilities.ShowNotification($"LocalExt = {localExt}, HalfExt = {Vector3.Round(halfExt, 1)}", 16);

          var quat = Quaternion.CreateFromRotationMatrix(matrix);
          var newOBB = new MyOrientedBoundingBoxD(center, halfExt, quat);

          DrawOBB(newOBB, Color.AntiqueWhite * 0.5f, MySimpleObjectRasterizer.Solid);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.ShowMessage($"Exception in DrawDebug: {ex.Message}");
      }
    }

    public static void DrawOBB(MyOrientedBoundingBoxD obb, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.01f, BlendTypeEnum blendType = BlendTypeEnum.Standard)
    {
      var material = MyStringId.GetOrCompute("Square");
      var box = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);
      var wm = MatrixD.CreateFromQuaternion(obb.Orientation);
      wm.Translation = obb.Center;
      MySimpleObjectDraw.DrawTransparentBox(ref wm, ref box, ref color, raster, 1, thickness, material, material, blendType: blendType);
    }

    public static void DrawAABB(BoundingBoxD bb, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.01f)
    {
      var material = MyStringId.GetOrCompute("Square");
      var box = new BoundingBoxD(-bb.HalfExtents, bb.HalfExtents);
      var wm = MatrixD.CreateTranslation(bb.Center);
      MySimpleObjectDraw.DrawTransparentBox(ref wm, ref box, ref color, raster, 1, thickness, material, material);
    }

    #endregion
  }
}
