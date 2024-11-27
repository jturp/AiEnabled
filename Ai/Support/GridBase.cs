using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Bots.Roles;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;

using VRageMath;

namespace AiEnabled.Ai.Support
{
  public abstract class GridBase : IEqualityComparer<GridBase>
  {
    //internal readonly byte MovementCost = 1;
    internal byte LastActiveTicks;
    internal MyOrientedBoundingBoxD OBB;
    internal BoundingBoxI BoundingBox;
    internal bool GraphLocked, GraphHasTunnel;
    byte _refreshTimer;

    /// <summary>
    /// Holds all nodes that are currently occupied by unconnected grids. Used to determine if a blocked path is temporarily or permanently blocked
    /// </summary>
    internal ConcurrentDictionary<Vector3I, KeyValuePair<IMyCubeGrid, bool>> ObstacleNodes = new ConcurrentDictionary<Vector3I, KeyValuePair<IMyCubeGrid, bool>>(Vector3I.Comparer);

    /// <summary>
    /// Only used in the method to update <see cref="ObstacleNodes"/>
    /// </summary>
    internal ConcurrentDictionary<Vector3I, KeyValuePair<IMyCubeGrid, bool>> ObstacleNodesTemp = new ConcurrentDictionary<Vector3I, KeyValuePair<IMyCubeGrid, bool>>(Vector3I.Comparer);

    /// <summary>
    /// Used for temporarily blocked nodes
    /// </summary>
    internal ConcurrentDictionary<Vector3I, byte> TempBlockedNodes = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);

    ///// <summary>
    ///// Used for permanently blocked nodes
    ///// </summary>
    //internal ConcurrentDictionary<Vector3I, byte> Obstacles = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);

    /// <summary>
    /// The dictionary key for this map
    /// </summary>
    public ulong Key;

    /// <summary>
    /// The matrix to use for alignment purposes
    /// </summary>
    public MatrixD WorldMatrix;

    /// <summary>
    /// True if this holds information about a grid,
    /// False if it holds information about a voxel area
    /// </summary>
    public bool IsGridGraph;

    /// <summary>
    /// True if there is at least one bot using this graph, otherwise False
    /// </summary>
    public bool IsActive;

    /// <summary>
    /// True if the graph hasn't been closed, otherwise False
    /// </summary>
    public bool IsValid = true;

    /// <summary>
    /// The planet this graph on, if it's not in space.
    /// </summary>
    public MyVoxelBase RootVoxel { get; internal set; }
    
    /// <summary>
    /// Will be true once the grid has been processed
    /// </summary>
    public bool Ready { get; protected set; }

    /// <summary>
    /// This is true if tiles need to be updated
    /// </summary>
    public bool Dirty { get; protected set; }

    /// <summary>
    /// This is true if the graph needs to be remade entirely (vs just Dirty)
    /// </summary>
    public bool Remake { get; protected set; }

    /// <summary>
    /// Whether or not any voxel changes have occurred
    /// </summary>
    public bool NeedsVoxelUpdate;

    /// <summary>
    /// If true, blocks have been added or removed the main grid
    /// </summary>
    public bool NeedsBlockUpdate;

    /// <summary>
    /// If true, temp obstacles will be cleared before the next pathfinding attempt
    /// </summary>
    public bool NeedsTempCleared;

    /// <summary>
    /// Determines whether the given position is still within this graph's limits
    /// </summary>
    /// <param name="position">The position to check</param>
    /// <returns></returns>
    public abstract bool IsPositionValid(Vector3D position);

    /// <summary>
    /// Determines whether the given position is an available tile position
    /// </summary>
    /// <param name="position">The world position to check</param>
    /// <returns></returns>
    public virtual bool IsPositionAvailable(Vector3D position, BotBase bot)
    {
      var node = WorldToLocal(position);
      if (IsOpenTile(node) && !TempBlockedNodes.ContainsKey(node) && !ObstacleNodes.ContainsKey(node))
      {
        return bot?._pathCollection == null || !bot._pathCollection.Obstacles.ContainsKey(node);
      }

      return false;
    }

    /// <summary>
    /// Determines whether the given position is an available tile position
    /// </summary>
    /// <param name="position">The local position to check</param>
    /// <param name="bot">The bot to check availability for</param>
    /// <returns>true if the <paramref name="position"/> is available (is open tile and not blocked in any way)</returns>
    public virtual bool IsPositionAvailable(Vector3I position, BotBase bot)
    {
      if (IsOpenTile(position) && !TempBlockedNodes.ContainsKey(position) && !ObstacleNodes.ContainsKey(position))
      {
        return bot?._pathCollection == null || !bot._pathCollection.Obstacles.ContainsKey(position);
      }

      return false;
    }

    /// <summary>
    /// Determines if a position is within the transition area for a map
    /// </summary>
    /// <param name="position">The position to check</param>
    /// <returns>true if position is close to the edge of a map, otherwise false</returns>
    public virtual bool IsInBufferZone(Vector3D position)
    {
      var excludeOBB = new MyOrientedBoundingBoxD(OBB.Center, OBB.HalfExtent - 3, OBB.Orientation);
      return OBB.Contains(ref position) && !excludeOBB.Contains(ref position);
    }

    /// <summary>
    /// Determines if something is in the temporary blocked nodes collection
    /// </summary>
    /// <param name="position">the grid local position to test for</param>
    /// <returns>true if temporarily blocked, otherwise false</returns>
    public virtual bool IsTemporaryBlock(Vector3I position) => ObstacleNodes.ContainsKey(position);

    /// <summary>
    /// Gets the <see cref="Node"/> associated with a given local position
    /// </summary>
    /// <param name="position">the intended <see cref="Node"/> position</param>
    /// <param name="node">the <see cref="Node"/> associated with the <paramref name="position"/>, if there is one</param>
    /// <returns>true if the position is a valid tile space, otherwise false</returns>
    public abstract bool TryGetNodeForPosition(Vector3I position, out Node node);

    /// <summary>
    /// Determines if the supplied position is a valid tile
    /// </summary>
    /// <param name="position"></param>
    /// <returns>true if the <paramref name="position"/> is a valid tile, otherwise false</returns>
    public abstract bool IsOpenTile(Vector3I position);

    /// <summary>
    /// Determines if the supplied position is blocked in some way
    /// </summary>
    /// <param name="position">the <see cref="Node"/> position</param>
    /// <param name="bot">the <see cref="BotBase"/> requesting the info</param>
    /// <param name="includeTemp">whether temporary blockages should be considered (ie a parked vehicle in the way)</param>
    /// <returns></returns>
    public abstract bool IsObstacle(Vector3I position, BotBase bot, bool includeTemp);

    /// <summary>
    /// Gets the <see cref="Node"/> associated with a grid position, or the default if there isn't one
    /// </summary>
    /// <param name="position">the grid local position to get the Node for</param>
    /// <param name="defaultValue">the default if there isn't an associated node</param>
    /// <returns></returns>
    public abstract Node GetValueOrDefault(Vector3I position, Node defaultValue);

    /// <summary>
    /// Determines whether the given position is usable by the bot
    /// </summary>
    /// <param name="bot">The bot to test usability for</param>
    /// <param name="worldPosition">The world position to check</param>
    /// <returns></returns>
    public virtual bool IsPositionUsable(BotBase bot, Vector3D worldPosition)
    {
      if (Dirty || !Ready)
        return false;

      if (bot == null || bot.IsDead)
        return false;

      Node node;
      var localPosition = WorldToLocal(worldPosition);
      if (!IsObstacle(localPosition, bot, true) && TryGetNodeForPosition(localPosition, out node))
      {
        var worldPoint = LocalToWorld(node.Position) + node.Offset;
        if (!PointInsideVoxel(worldPoint, RootVoxel))
        {
          bool isWaterNode = node.IsWaterNode;

          if (bot.WaterNodesOnly)
            return isWaterNode;

          return (!node.IsAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this));
        }
      }

      return false;
    }

    /// <summary>
    /// Determines whether the given position is usable by the bot
    /// </summary>
    /// <param name="bot">The bot to test usability for</param>
    /// <param name="worldPosition">The world position to check</param>
    /// <param name="node">If the position is a valid tile, this will contain the tile information</param>
    /// <returns>true if <paramref name="worldPosition"/> is available AND if <paramref name="bot"/> can use the node type (air, water, etc)</returns>
    public virtual bool IsPositionUsable(BotBase bot, Vector3D worldPosition, out Node node)
    {
      node = null;

      if (bot == null || bot.IsDead)
        return false;

      var localPosition = WorldToLocal(worldPosition);
      if (!IsObstacle(localPosition, bot, true) && TryGetNodeForPosition(localPosition, out node))
      {
        var worldPoint = LocalToWorld(node.Position) + node.Offset;
        if (!PointInsideVoxel(worldPoint, RootVoxel))
        {
          bool isWaterNode = node.IsWaterNode;

          if (bot.WaterNodesOnly)
            return isWaterNode;

          return (!node.IsAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this));
        }
      }

      return false;
    }

    /// <summary>
    /// Determines whether the given position is usable by the bot
    /// </summary>
    /// <param name="bot">The bot to test usability for</param>
    /// <param name="localPosition">The node position to check</param>
    /// <returns>true if <paramref name="localPosition"/> is available AND if <paramref name="bot"/> can use the node type (air, water, etc)</returns>
    public virtual bool IsPositionUsable(BotBase bot, Vector3I localPosition)
    {
      Node node;
      if (!IsObstacle(localPosition, bot, true) && TryGetNodeForPosition(localPosition, out node))
      {
        var worldPoint = LocalToWorld(node.Position) + node.Offset;
        if (!PointInsideVoxel(worldPoint, RootVoxel))
        {
          bool isWaterNode = node.IsWaterNode;

          if (bot.WaterNodesOnly)
            return isWaterNode;

          return (!node.IsAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this));
        }
      }

      return false;
    }

    public Node GetBufferZoneTargetPositionFromPrunik(ref MyOrientedBoundingBoxD nextObb, ref Base6Directions.Direction forward, ref Vector3D goal, BotBase bot)
    {
      Node resultNode = null;
      Node backupNode = null;

      Vector3D[] corners;
      if (!AiSession.Instance.CornerArrayStack.TryPop(out corners))
        corners = new Vector3D[8];

      BoundingBoxI box = BoundingBoxI.CreateInvalid();
      OBB.GetCorners(corners, 0);
      for (int j = 0; j < corners.Length; j++)
      {
        var localCorner = WorldToLocal(corners[j]);
        box.Include(ref localCorner);
      }

      nextObb.GetCorners(corners, 0);
      BoundingBoxI otherBox = BoundingBoxI.CreateInvalid();
      for (int j = 0; j < corners.Length; j++)
      {
        var localCorner = WorldToLocal(corners[j]);
        otherBox.Include(ref localCorner);
      }

      AiSession.Instance.CornerArrayStack.Push(corners);

      box.IntersectWith(ref otherBox);
      if (!box.IsValid)
      {
        return null;
      }

      var absMin = box.HalfExtents.AbsMin();
      if (absMin > 1)
        box.Inflate(-1);
      else if (absMin == 0)
        box.Inflate(1);

      var vectorToNext = nextObb.Center - OBB.Center;
      forward = WorldMatrix.GetClosestDirection(vectorToNext);

      MatrixD matrix;
      BoundingBoxI localBox;
      var nextGraph = bot._nextGraph;

      if (forward == Base6Directions.Direction.Up || forward == Base6Directions.Direction.Down)
      {
        matrix = WorldMatrix;
        localBox = box;
      }
      else
      {
        var localExt = box.HalfExtents;
        var center = LocalToWorld(box.Center);
        var fwdVector = WorldMatrix.GetDirectionVector(forward);
        matrix = MatrixD.CreateWorld(center, fwdVector, WorldMatrix.Up);
        var m = new MatrixI(forward, Base6Directions.Direction.Up);

        Vector3I.TransformNormal(ref localExt, ref m, out localExt);
        localExt = Vector3I.Abs(localExt);
        localBox = new BoundingBoxI(-localExt, localExt);
      }

      bool goalUnderGround = false;
      bool startUnderGound = false;
      bool checkTunnelPoints = false;
      Vector3D gravity;

      if (GraphHasTunnel && RootVoxel != null && !RootVoxel.MarkedForClose)
      {
        float _;
        gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(goal, out _);

        if (gravity.LengthSquared() > 0)
          gravity.Normalize();
        else
          gravity = WorldMatrix.Down;

        Vector3D upVector = -gravity;
        Vector3D? surfacePoint;

        var planet = RootVoxel as MyPlanet;
        if (planet != null)
          surfacePoint = planet.GetClosestSurfacePointGlobal(ref goal) + upVector;
        else
          surfacePoint = GetClosestSurfacePointFast(bot, goal + upVector * 20, upVector);

        if (surfacePoint.HasValue)
        {
          var surfaceValue = surfacePoint.Value;

          while (PointInsideVoxel(surfaceValue, RootVoxel) && OBB.Contains(ref surfaceValue))
            surfaceValue -= gravity;

          var goalPoint = goal;

          while (PointInsideVoxel(goalPoint, RootVoxel) && OBB.Contains(ref goalPoint))
            goalPoint -= gravity;

          var line = new LineD(surfaceValue, goalPoint);
          var maxCount = Math.Floor(line.Length) - 3;

          for (int i = 0; i < maxCount; i++)
          {
            var point = surfaceValue + line.Direction * i;
            if (PointInsideVoxel(point, RootVoxel))
            {
              goalUnderGround = true;
              break;
            }
          }
        }

        if (goalUnderGround)
        {
          checkTunnelPoints = true;
        }
        else
        {
          var botPosition = bot.BotInfo.CurrentBotPositionActual;
          if (planet != null)
            surfacePoint = planet.GetClosestSurfacePointGlobal(ref botPosition) + upVector;
          else
            surfacePoint = GetClosestSurfacePointFast(bot, botPosition + upVector * 20, upVector);

          if (surfacePoint.HasValue)
          {
            var surfaceValue = surfacePoint.Value;

            while (PointInsideVoxel(surfaceValue, RootVoxel) && OBB.Contains(ref surfaceValue))
              surfaceValue -= gravity;

            var goalPoint = botPosition;

            while (PointInsideVoxel(goalPoint, RootVoxel) && OBB.Contains(ref goalPoint))
              goalPoint -= gravity;

            var line = new LineD(surfaceValue, goalPoint);
            var maxCount = Math.Floor(line.Length) - 3;

            for (int i = 0; i < maxCount; i++)
            {
              var point = surfaceValue + line.Direction * i;
              if (PointInsideVoxel(point, RootVoxel))
              {
                startUnderGound = true;
                break;
              }
            }

            checkTunnelPoints = startUnderGound;
          }
        }        
      }

      var cellSize = CellSize;

      var minX = localBox.Min.X;
      var minY = localBox.Min.Y;
      var minZ = localBox.Min.Z;

      var maxX = localBox.Max.X;
      var maxY = localBox.Max.Y;
      var maxZ = localBox.Max.Z;

      double distanceTunnel = double.MaxValue;
      double distanceRegular = double.MaxValue;
      bool pointFound = false;

      for (int y = minY; y <= maxY; y++)
      {
        for (int z = minZ; z <= maxZ; z++)
        {
          for (int x = minX; x <= maxX; x++)
          {
            var localPoint = new Vector3I(x, y, z);

            Vector3D worldPoint = localPoint * cellSize;
            Vector3D.Transform(ref worldPoint, ref matrix, out worldPoint);

            if (OBB.Contains(ref worldPoint) && nextObb.Contains(ref worldPoint))
            {
              Node node;
              if (IsPositionUsable(bot, worldPoint, out node))
              {
                var distance = Vector3D.DistanceSquared(worldPoint, goal);

                if (checkTunnelPoints && node.IsTunnelNode)
                {
                  if (distance < distanceTunnel)
                  {
                    distanceTunnel = distance;
                    resultNode = node;
                  }
                }
                else if (distance < distanceRegular)
                {
                  distanceRegular = distance;
                  backupNode = node;
                }
              }
            }
          }

          if (pointFound)
            break;
        }

        if (pointFound)
          break;
      }

      var result = resultNode ?? backupNode;
      //var extents = localBox.Max - localBox.Min + 1;
      //var volume = extents.X * extents.Y * extents.Z;

      return result;
    }

    /// <summary>
    /// Clears all temporary blockages from the map
    /// </summary>
    public abstract void ClearTempObstacles();

    /// <summary>
    /// Adds a blocked edge to known obstacles
    /// </summary>
    /// <param name="prevNode">Previous <see cref="Node"/> position</param>
    /// <param name="curNode">Current <see cref="Node"/> position</param>
    /// <param name="nextNode">Next <see cref="Node"/> position</param>
    public virtual bool AddToObstacles(Vector3I prevNode, Vector3I curNode, Vector3I nextNode, bool asTemp)
    {
      var prev = prevNode; // WorldToLocal(prevNode);
      var curr = curNode; // WorldToLocal(curNode);
      var next = nextNode; // WorldToLocal(nextNode);
      bool addObstacle = true;

      Node node;
      if (TryGetNodeForPosition(prev, out node))
      {
        var dirPN = next - prev;
        var min = -Vector3I.One;
        Vector3I.Clamp(ref dirPN, ref min, ref Vector3I.One, out dirPN);

        if (asTemp)
        {
          if (node.SetBlockedTemp(dirPN))
            addObstacle = false;
        }
        else if (node.SetBlocked(dirPN))
          addObstacle = false;

      }

      if (addObstacle && TryGetNodeForPosition(curr, out node))
      {
        var dirCN = next - curr;
        var min = -Vector3I.One;
        Vector3I.Clamp(ref dirCN, ref min, ref Vector3I.One, out dirCN);

        if (asTemp)
        {
          if (node.SetBlockedTemp(dirCN))
            addObstacle = false;
        }
        else if (node.SetBlocked(dirCN))
          addObstacle = false;
      }

      return addObstacle;
    }

    public virtual void Refresh()
    {
      ++_refreshTimer;
      if (_refreshTimer > 4)
      {
        _refreshTimer = 0;
        Init();
      }
    }

    /// <summary>
    /// How big the grid squares are for this map
    /// </summary>
    public virtual float CellSize { get; internal set; } = 2.5f;

    /// <summary>
    /// Converts local position to world position
    /// </summary>
    /// <param name="localVector">The local position to transform</param>
    /// <returns></returns>
    public abstract Vector3D LocalToWorld(Vector3I localVector);

    /// <summary>
    /// Converts world position to local position
    /// </summary>
    /// <param name="worldVector">The world position to transform</param>
    /// <returns></returns>
    public abstract Vector3I WorldToLocal(Vector3D worldVector);

    /// <summary>
    /// Gets the node's blocked edges
    /// </summary>
    /// <param name="nodeBase">The <see cref="Node"/> the edges are requested for</param>
    /// <returns></returns>
    public abstract void GetBlockedNodeEdges(Node nodeBase);

    /// <summary>
    /// Gets all valid neighbors (open tiles) to a given node position
    /// </summary>
    /// <param name="bot">the <see cref="BotBase"/> requesting the neighbors</param>
    /// <param name="previousNode">the previous <see cref="Node"/> position</param>
    /// <param name="currentNode">the current <see cref="Node"/> position</param>
    /// <param name="worldPosition">the <paramref name="bot"/>'s current world position</param>
    /// <param name="checkDoors">whether doors should be considered (ie <see cref="CubeGridMap"/></param>
    /// <param name="currentIsObstacle">whether the <paramref name="currentNode"/> is known to be blocked</param>
    /// <param name="isSlimBlock">whether the <paramref name="currentNode"/> is known to be a slim block (ie repair target) </param>
    /// <param name="up">if supplied, only lateral positions will be considered valid</param>
    /// <returns></returns>
    public abstract IEnumerable<Vector3I> Neighbors(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, Vector3D? up = null, bool checkRepairInfo = false);

    public abstract bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, bool checkRepairInfo = false);

    public abstract bool GetClosestValidNode(BotBase bot, Vector3I testNode, out Vector3I node, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true);

    public abstract bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I node);

    public abstract bool InBounds(Vector3I node);

    public abstract Vector3D? GetBufferZoneTargetPosition(Vector3D fromPosition, Vector3D toPosition, bool getEdgePoint = false);

    public abstract Vector3D? GetBufferZoneTargetPositionCentered(Vector3D fromPosition, Vector3D toPosition, Vector3D sideNormal, bool getEdgePoint = false);

    public abstract bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node);

    public abstract void UpdateTempObstacles();

    public abstract Node GetReturnHomePoint(BotBase bot);

    public abstract IMySlimBlock GetBlockAtPosition(Vector3I localPosition, bool checkOtherGrids = false);

    public virtual IMySlimBlock GetBlockAtPosition(Vector3D worldPosition, bool checkOtherGrids = false)
    {
      var localPosition = WorldToLocal(worldPosition);
      return GetBlockAtPosition(localPosition, checkOtherGrids);
    }

    public virtual void TeleportNearby(BotBase bot)
    {
      var botPostion = bot.BotInfo.CurrentBotPositionActual;
      var graph = bot._currentGraph;

      if (graph == null || !graph.Ready)
        return;

      if (!graph.IsPositionValid(botPostion) && bot.Owner == null)
      {
        if (bot.ConfineToMap && bot.BotInfo.CurrentGravityAtBotPosition_Nat.LengthSquared() == 0 && bot.BotInfo.CurrentGravityAtBotPosition_Art.LengthSquared() == 0)
        {
          AiSession.Instance.Logger.Log($"{this}.TeleportNearby: {bot.Character.Name} found outside of map bounds (pos was {graph.WorldToLocal(botPostion)})", MessageType.WARNING);
          bot.Character.Kill();
        }
        else
        {
          var directionCenterToBot = graph.WorldMatrix.GetClosestDirection(bot.BotInfo.CurrentBotPositionActual - graph.OBB.Center);
          var normal = graph.WorldMatrix.GetDirectionVector(directionCenterToBot);

          double distanceToEdge;
          if (!graph.GetEdgeDistanceInDirection(normal, out distanceToEdge))
            distanceToEdge = VoxelGridMap.DefaultHalfSize * 0.75;

          var newGraphPosition = graph.OBB.Center + (normal * distanceToEdge) + (normal * VoxelGridMap.DefaultHalfSize) - (normal * VoxelGridMap.DefaultCellSize * 3);
          
          if (Vector3D.DistanceSquared(newGraphPosition, botPostion) > VoxelGridMap.DefaultHalfSize * VoxelGridMap.DefaultHalfSize * 0.9)
            newGraphPosition = botPostion;

          var map = AiSession.Instance.GetVoxelGraph(newGraphPosition, graph.WorldMatrix);
          if (map != null)
          {
            bot._nextGraph = map;
            bot.SwitchGraph();
          }
        }

        return;
      }

      var botLocal = graph.WorldToLocal(botPostion);
      int distanceCheck = 20;

      Vector3I? center = null;
      Vector3I? centerLOS = null;

      List<IHitInfo> hitList = AiSession.Instance.HitListPool.Get();

      for (int i = 1; i <= distanceCheck; i++)
      {
        var testPoint = botLocal + Vector3I.Up * i;
        if (IsPositionUsable(bot, testPoint))
        {
          if (!center.HasValue)
            center = testPoint;

          if (CheckLineOfSight(botLocal, testPoint, bot, hitList))
          {
            centerLOS = testPoint;
            break;
          }
        }

        testPoint = botLocal + Vector3I.Down * i;
        if (IsPositionUsable(bot, testPoint))
        {
          if (!center.HasValue)
            center = testPoint;

          if (CheckLineOfSight(botLocal, testPoint, bot, hitList))
          {
            centerLOS = testPoint;
            break;
          }
        }

        testPoint = botLocal + Vector3I.Left * i;
        if (IsPositionUsable(bot, testPoint))
        {
          if (!center.HasValue)
            center = testPoint;

          if (CheckLineOfSight(botLocal, testPoint, bot, hitList))
          {
            centerLOS = testPoint;
            break;
          }
        }

        testPoint = botLocal + Vector3I.Right * i;
        if (IsPositionUsable(bot, testPoint))
        {
          if (!center.HasValue)
            center = testPoint;

          if (CheckLineOfSight(botLocal, testPoint, bot, hitList))
          {
            centerLOS = testPoint;
            break;
          }
        }

        testPoint = botLocal + Vector3I.Forward * i;
        if (IsPositionUsable(bot, testPoint))
        {
          if (!center.HasValue)
            center = testPoint;

          if (CheckLineOfSight(botLocal, testPoint, bot, hitList))
          {
            centerLOS = testPoint;
            break;
          }
        }

        testPoint = botLocal + Vector3I.Backward * i;
        if (IsPositionUsable(bot, testPoint))
        {
          if (!center.HasValue)
            center = testPoint;

          if (CheckLineOfSight(botLocal, testPoint, bot, hitList))
          {
            centerLOS = testPoint;
            break;
          }
        }
      }

      AiSession.Instance.HitListPool?.Return(ref hitList);

      if (centerLOS.HasValue)
        center = centerLOS;

      if (!center.HasValue)
      {
        var node = GetReturnHomePoint(bot);
        if (node != null)
          center = node.Position;
      }

      if (center.HasValue)
      {
        Vector3D worldPoint;
        Vector3 velocity;
        Node node;
        if (graph.TryGetNodeForPosition(center.Value, out node))
        {
          worldPoint = graph.LocalToWorld(center.Value) + node.Offset + WorldMatrix.Down * 0.5;
          velocity = (node.IsGroundNode || !bot.CanUseAirNodes || bot is CreatureBot) ? (Vector3)WorldMatrix.Down * 0.5f : Vector3.Zero;
        }
        else
        {
          worldPoint = graph.LocalToWorld(center.Value) + WorldMatrix.Down * 0.5;
          velocity = (!bot.CanUseAirNodes || bot is CreatureBot) ? (Vector3)WorldMatrix.Down * 0.5f : Vector3.Zero;
        }

        var gridGraph = bot._currentGraph as CubeGridMap;
        if (gridGraph?.MainGrid?.Physics != null && !gridGraph.MainGrid.IsStatic)
          velocity += gridGraph.MainGrid.Physics.LinearVelocity;

        var matrix = WorldMatrix;
        matrix.Translation = worldPoint;

        AiSession.Instance.Logger.Log($"{this}.TeleportNearby: {bot.Character.Name} moved from {graph.WorldToLocal(botPostion)} to {center.Value}");

        bot.Character.SetWorldMatrix(matrix);
        bot.Character.Physics.SetSpeeds(velocity, Vector3.Zero);
        bot.CleanPath();
      }
      else
      {
        AiSession.Instance.Logger.Log($"{this}.TeleportNearby: Unable to find placement for {bot.Character.Name} (pos was {graph.WorldToLocal(botPostion)})", MessageType.WARNING);

        if (bot.Owner == null)
          bot.Character.Kill();
      }
    }

    bool CheckLineOfSight(Vector3I start, Vector3I end, BotBase bot, List<IHitInfo> hitList)
    {
      var graph = bot?._currentGraph;
      if (graph == null)
        return false;

      var wStart = graph.LocalToWorld(start);
      var wEnd = graph.LocalToWorld(end);

      hitList.Clear();
      MyAPIGateway.Physics.CastRay(wStart, wEnd, hitList);

      for (int i = 0; i < hitList.Count; i++)
      {
        var ent = hitList[i] as IMyCharacter;
        if (ent == null)
          return false;
      }

      return true;
    }

    internal virtual void SetReady()
    {
      //MyAPIGateway.Utilities.ShowMessage("AiEnabled", "GridBase.SetReady()");
      GraphLocked = false;
      Ready = true;
    }

    internal abstract void Init();

    internal bool GetEdgeDistanceInDirection(Vector3D normal, out double distance)
    {
      Vector3D[] corners;
      if (!AiSession.Instance.CornerArrayStack.TryPop(out corners))
        corners = new Vector3D[8];

      var ray = new RayD(OBB.Center, normal);
      var result = OBB.GetDistanceToEdgeInDirection(ray, corners, out distance);

      AiSession.Instance.CornerArrayStack.Push(corners);
      return result;
    }

    internal bool GetEdgeDistanceInDirection(Vector3D normal, MyOrientedBoundingBoxD box, out double distance)
    {
      Vector3D[] corners;
      if (!AiSession.Instance.CornerArrayStack.TryPop(out corners))
        corners = new Vector3D[8];

      var ray = new RayD(box.Center, normal);
      var result = box.GetDistanceToEdgeInDirection(ray, corners, out distance);

      AiSession.Instance.CornerArrayStack.Push(corners);
      return result;
    }

    internal Vector3D? GetClosestSurfacePointFast(BotBase bot, Vector3D worldPosition, Vector3D upVec)
    {
      if (RootVoxel == null || RootVoxel.MarkedForClose)
      {
        return worldPosition;
      }

      var localPoint = WorldToLocal(worldPosition);

      if (bot != null && IsPositionUsable(bot, localPoint))
        return worldPosition;

      if (RootVoxel is MyPlanet)
      {
        float _;
        Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
        if (gravity.LengthSquared() > 0)
          upVec = Vector3D.Normalize(-gravity);
      }

      var closestSurfacePoint = LocalToWorld(localPoint);
      var addVec = upVec * CellSize;

      if (PointInsideVoxel(closestSurfacePoint, RootVoxel))
      {
        closestSurfacePoint += addVec;

        while (PointInsideVoxel(closestSurfacePoint, RootVoxel))
        {
          if (!OBB.Contains(ref closestSurfacePoint))
            return null;

          closestSurfacePoint += addVec;
        }
      }
      else
      {
        closestSurfacePoint -= addVec;

        while (!PointInsideVoxel(closestSurfacePoint, RootVoxel))
        {
          if (!OBB.Contains(ref closestSurfacePoint))
            return null;

          closestSurfacePoint -= addVec;
        }

        if (PointInsideVoxel(closestSurfacePoint, RootVoxel))
          closestSurfacePoint += addVec;
      }

      Node node;
      localPoint = WorldToLocal(closestSurfacePoint);

      if (TryGetNodeForPosition(localPoint, out node))
      {
        closestSurfacePoint = LocalToWorld(node.Position) + node.Offset;
      }

      return closestSurfacePoint;
    }

    internal bool GetClosestSurfacePointFast(Vector3D worldPosition, Vector3D upVec, out Node node)
    {
      node = null;
      if (RootVoxel == null || RootVoxel.MarkedForClose)
      {
        return false;
      }

      var localPoint = WorldToLocal(worldPosition);

      if (RootVoxel is MyPlanet)
      {
        float _;
        Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
        if (gravity.LengthSquared() > 0)
          upVec = Vector3D.Normalize(-gravity);
      }

      var closestSurfacePoint = LocalToWorld(localPoint);
      var addVec = upVec * CellSize;

      if (PointInsideVoxel(closestSurfacePoint, RootVoxel))
      {
        closestSurfacePoint += addVec;

        while (PointInsideVoxel(closestSurfacePoint, RootVoxel))
        {
          if (!OBB.Contains(ref closestSurfacePoint))
            return false;

          closestSurfacePoint += addVec;
        }
      }
      else
      {
        closestSurfacePoint -= addVec;

        while (!PointInsideVoxel(closestSurfacePoint, RootVoxel))
        {
          if (!OBB.Contains(ref closestSurfacePoint))
            return false;

          closestSurfacePoint -= addVec;
        }

        if (PointInsideVoxel(closestSurfacePoint, RootVoxel))
          closestSurfacePoint += addVec;
      }

      localPoint = WorldToLocal(closestSurfacePoint);
      return TryGetNodeForPosition(localPoint, out node) && node.IsGroundNode;
    }

    public static IMyCubeGrid GetLargestGridForMap(IMyCubeGrid initialGrid)
    {
      try
      {
        if (initialGrid == null)
          return null;

        var biggest = initialGrid.GridSizeEnum == MyCubeSize.Small ? null : initialGrid;
        var gridLink = initialGrid.GetGridGroup(GridLinkTypeEnum.Mechanical);
        if (gridLink == null)
          return biggest;

        List<IMyCubeGrid> gridList = AiSession.Instance.GridGroupListPool.Get();
        gridLink.GetGrids(gridList);

        for (int i = 0; i < gridList.Count; i++)
        {
          var g = gridList[i];
          if (g == null || g.MarkedForClose || g.Closed || g.GridSizeEnum == MyCubeSize.Small)
            continue;

          if (biggest == null || biggest.GridSizeEnum == MyCubeSize.Small || g.WorldAABB.Volume > biggest.WorldAABB.Volume || (g.IsStatic && !biggest.IsStatic))
            biggest = g;
        }

        AiSession.Instance.GridGroupListPool?.Return(ref gridList);

        return (biggest?.GridSize > 1) ? biggest : null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotFactory.GetLargestGridForMap: {ex}");
        return null;
      }
    }

    public static Vector3D GetClosestSurfacePointFast(Vector3D worldPosition, Vector3D upVec, GridBase map, out bool pointOnGround)
    {
      pointOnGround = true;
      var voxel = map?.RootVoxel;

      if (voxel == null || voxel.MarkedForClose)
      {
        return worldPosition;
      }

      if (voxel is MyPlanet)
      {
        float _;
        Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
        if (gravity.LengthSquared() > 0)
          upVec = Vector3D.Normalize(-gravity);
      }

      var pointAbove = worldPosition + upVec;
      var pointBelow = worldPosition - upVec;
      int count = 0;

      while (Vector3D.DistanceSquared(pointAbove, pointBelow) > 0.1)
      {
        count++;
        if (count > 500)
        {
          break;
        }

        var mid = (pointAbove + pointBelow) * 0.5;
        if (PointInsideVoxel(mid, voxel))
        {
          pointBelow = mid;
        }
        else
        {
          pointAbove = mid;
        }
      }

      if (PointInsideVoxel(pointAbove, voxel))
      {
        pointOnGround = false;
        return pointBelow;
      }

      return pointAbove;
    }

    public static Vector3D GetClosestSurfacePointFast(Vector3D worldPosition, Vector3D upVec, MyVoxelBase voxel, out bool pointOnGround)
    {
      pointOnGround = true;

      if (voxel == null || voxel.MarkedForClose)
      {
        return worldPosition;
      }

      if (voxel is MyPlanet)
      {
        float _;
        Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
        if (gravity.LengthSquared() > 0)
          upVec = Vector3D.Normalize(-gravity);
      }

      var pointAbove = worldPosition + upVec;
      var pointBelow = worldPosition - upVec;
      int count = 0;

      while (Vector3D.DistanceSquared(pointAbove, pointBelow) > 0.1)
      {
        count++;
        if (count > 500)
        {
          break;
        }

        var mid = (pointAbove + pointBelow) * 0.5;
        if (PointInsideVoxel(mid, voxel))
        {
          pointBelow = mid;
        }
        else
        {
          pointAbove = mid;
        }
      }

      if (PointInsideVoxel(pointAbove, voxel))
      {
        pointOnGround = false;
        return pointBelow;
      }

      return pointAbove;
    }

    public static Vector3D? GetClosestSurfacePointAboveGround(ref Vector3D worldPosition, Vector3D? up = null, MyVoxelBase voxel = null, bool checkGrids = false)
    {
      if (voxel == null)
      {
        List<MyVoxelBase> vList = AiSession.Instance.VoxelMapListPool.Get();

        var sphere = new BoundingSphereD(worldPosition, 1);
        MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, vList);

        var distance = double.MaxValue;

        for (int j = 0; j < vList.Count; j++)
        {
          var vb = vList[j];
          if (vb == vb.RootVoxel)
          {
            var dist = Vector3D.DistanceSquared(vb.PositionComp.GetPosition(), worldPosition);
            if (dist < distance)
            {
              voxel = vb;
              distance = dist;
            }
          }
        }

        AiSession.Instance.VoxelMapListPool?.Return(ref vList);

        if (voxel == null)
          return null;
      }

      Vector3D? result = worldPosition;

      var planet = voxel as MyPlanet;
      if (planet != null)
        result = planet.GetClosestSurfacePointGlobal(ref worldPosition);

      if (up == null || up.Value == Vector3D.Zero)
      {
        float _;
        var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
        if (gravity.LengthSquared() > 0)
          up = Vector3D.Normalize(-gravity);
        else
          up = Vector3D.Normalize(worldPosition - voxel.PositionComp.GetPosition());

        if (up == Vector3D.Zero)
          return null;
      }

      while (PointInsideVoxel(result.Value, voxel))
        result += (Vector3D?)(up.Value * 2);

      if (checkGrids)
      {
        var entList = AiSession.Instance.EntListPool.Get();
        var sphere = new BoundingSphereD(result.Value, 5);
        var upVec = up.Value;

        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

        for (int i = 0; i < entList.Count; i++)
        {
          var grid = entList[i] as MyCubeGrid;
          if (grid != null)
          {
            var gridUp = grid.WorldMatrix.GetClosestDirection(upVec);
            var gridUpInt = Base6Directions.GetIntVector(gridUp);

            var localResult = grid.WorldToGridInteger(result.Value);
            bool needsAdjust = false;

            while (grid.CubeExists(localResult))
            {
              needsAdjust = true;
              bool found = false;

              foreach (var dirVec in AiSession.Instance.CardinalDirections)
              {
                var testVec = dirVec;
                if (gridUpInt.Dot(ref testVec) >= 0)
                {
                  var nextResult = localResult + dirVec;
                  if (!grid.CubeExists(nextResult))
                  {
                    found = true;
                    localResult = nextResult;
                    break;
                  }
                }
              }

              if (!found)
                localResult = grid.WorldToGridInteger(result.Value + upVec * grid.GridSize);
            }

            if (needsAdjust)
              result = grid.GridIntegerToWorld(localResult);
          }
        }

        AiSession.Instance.EntListPool.Return(ref entList);
      }

      return result;
    }

    public static bool GetClosestPointAboveGround(ref Vector3D worldPosition, ref Vector3D up, out MyVoxelBase voxel, int testPoints = 20)
    {
      List<MyVoxelBase> vList = AiSession.Instance.VoxelMapListPool.Get();

      var sphere = new BoundingSphereD(worldPosition, 1);
      MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, vList);

      voxel = null;
      var distance = double.MaxValue;

      for (int j = 0; j < vList.Count; j++)
      {
        var vb = vList[j];
        if (vb == vb.RootVoxel)
        {
          var dist = Vector3D.DistanceSquared(vb.PositionComp.GetPosition(), worldPosition);
          if (dist < distance)
          {
            voxel = vb;
            distance = dist;
          }
        }
      }

      vList.Clear();
      AiSession.Instance.VoxelMapListPool?.Return(ref vList);

      float interference;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out interference);
      if (gravity.LengthSquared() == 0)
        gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(worldPosition, interference);

      if (gravity.LengthSquared() > 0)
        up = Vector3D.Normalize(-gravity);

      if (up != Vector3D.Zero)
      {
        int num = 0;
        while (num < testPoints && PointInsideVoxel(worldPosition, voxel))
        {
          num++;
          worldPosition += up * 2;
        }

        return num < testPoints;
      }

      return false;
    }

    public static bool PointInsideVoxel(Vector3D pos, MyVoxelBase voxel)
    {
      if (voxel == null || voxel.MarkedForClose)
        return false;

      var planet = voxel as MyPlanet;
      if (planet != null)
      {
        var maxRadius = planet.MaximumRadius;
        if (Vector3D.DistanceSquared(planet.PositionComp.GetPosition(), pos) > maxRadius * maxRadius)
          return false;
      }

      MyStorageData tmpStorage;
      if (!AiSession.StorageStack.TryPop(out tmpStorage))
        tmpStorage = new MyStorageData();

      var voxelMatrix = voxel.PositionComp.WorldMatrixInvScaled;

      Vector3D result;
      Vector3D.Transform(ref pos, ref voxelMatrix, out result);
      var r = result + (Vector3D)(voxel.Size / 2);
      var v1 = Vector3D.Floor(r);
      Vector3D.Fract(ref r, out r);
      var v2 = v1 + voxel.StorageMin;
      var v3 = v2 + 1;

      if (v2 != Vector3I.MaxValue && v3 != Vector3I.MinValue)
      {
        tmpStorage.Resize(v2, v3);
        using (voxel.Pin())
          voxel.Storage.ReadRange(tmpStorage, MyStorageDataTypeFlags.Content, 0, v2, v3);
      }

      var num1 = tmpStorage.Content(0, 0, 0);
      var num2 = tmpStorage.Content(1, 0, 0);
      var num3 = tmpStorage.Content(0, 1, 0);
      var num4 = tmpStorage.Content(1, 1, 0);
      var num5 = tmpStorage.Content(0, 0, 1);
      var num6 = tmpStorage.Content(1, 0, 1);
      var num7 = tmpStorage.Content(0, 1, 1);
      var num8 = tmpStorage.Content(1, 1, 1);
      var num9 = num1 + (num2 - num1) * r.X;
      var num10 = num3 + (num4 - num3) * r.X;
      var num11 = num5 + (num6 - num5) * r.X;
      var num12 = num7 + (num8 - num7) * r.X;
      var num13 = num9 + (num10 - num9) * r.Y;
      var num14 = num11 + (num12 - num11) * r.Y;

      AiSession.StorageStack.Push(tmpStorage);
      return num13 + (num14 - num13) * r.Z >= sbyte.MaxValue;
    }

    public static bool LineIntersectsVoxel(ref Vector3D from, ref Vector3D to, MyVoxelBase voxel)
    {
      if (voxel == null || voxel.MarkedForClose)
        return false;

      if (PointInsideVoxel(from, voxel) || PointInsideVoxel(to, voxel))
        return true;

      var line = new LineD(from, to);
      var num = Math.Floor(line.Length);

      for (int i = 1; i < num; i++)
      {
        var point = from + line.Direction * i;
        if (PointInsideVoxel(point, voxel))
          return true;
      }

      return false;
    }

    /// <summary>
    /// Converted from MyVoxelBase.GetMaterialsInShape.
    /// </summary>
    /// <param name="worldBoundaries">WorldAABB to check</param>
    /// <param name="voxel">Root voxel to check</param>
    /// <param name="lod">Voxel LOD to check</param>
    /// <returns>true if any materials are found in the AABB, otherwise false</returns>
    public bool HasMaterialsInBox(BoundingBoxD worldBoundaries, MyVoxelBase voxel, int lod = 0)
    {
      if (voxel == null || voxel.MarkedForClose)
        return false;

      Vector3I max = voxel.Storage.Size - 1;
      Vector3D bottomLeftCorner = voxel.PositionLeftBottomCorner;
      Vector3I voxelCoordMin, voxelCoordMax;

      MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref worldBoundaries.Min, out voxelCoordMin);
      MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref worldBoundaries.Max, out voxelCoordMax);
      Vector3I voxelCoord3 = voxelCoordMin - 1;
      Vector3I voxelCoord4 = voxelCoordMax + 1;

      Vector3I.Clamp(ref voxelCoord3, ref Vector3I.Zero, ref max, out voxelCoord3);
      Vector3I.Clamp(ref voxelCoord4, ref Vector3I.Zero, ref max, out voxelCoord4);

      voxelCoord3 >>= lod;
      voxelCoord3 -= 1;
      voxelCoord4 >>= lod;
      voxelCoord4 += 1;

      MyStorageData tmpStorage;
      if (!AiSession.StorageStack.TryPop(out tmpStorage))
        tmpStorage = new MyStorageData();

      tmpStorage.Resize(voxelCoord3, voxelCoord4);

      if (voxel != null && !voxel.MarkedForClose)
      {
        using (voxel.Pin())
        {
          voxel.Storage.ReadRange(tmpStorage, MyStorageDataTypeFlags.Material, lod, voxelCoord3, voxelCoord4);
        }
      }
      else
        return false;

      Vector3I vector3I = default(Vector3I);
      vector3I.X = voxelCoord3.X;
      while (vector3I.X <= voxelCoord4.X)
      {
        vector3I.Y = voxelCoord3.Y;
        while (vector3I.Y <= voxelCoord4.Y)
        {
          vector3I.Z = voxelCoord3.Z;
          while (vector3I.Z <= voxelCoord4.Z)
          {
            Vector3I p = vector3I - voxelCoord3;
            int linearIdx = tmpStorage.ComputeLinear(ref p);
            byte b = tmpStorage.Material(linearIdx);

            if (b != byte.MaxValue)
            {
              return true;
            }

            vector3I.Z++;
          }
          vector3I.Y++;
        }
        vector3I.X++;
      }

      return false;
    }

    public virtual void Close()
    {
      try
      {
        IsValid = false;
        OnPositionsRemoved?.Invoke(true);
        OnGridBaseClosing?.Invoke();

        ObstacleNodes?.Clear();
        ObstacleNodesTemp?.Clear();
        TempBlockedNodes?.Clear();

        ObstacleNodes = null;
        ObstacleNodesTemp = null;
        TempBlockedNodes = null;
      }
      catch(Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception during GridBase.Close(): {ex}");
      }
    }

    public event Action<bool> OnPositionsRemoved;
    public event Action OnGridBaseClosing;

    internal void InvokePositionsRemoved(bool includeBlocks)
    {
      ObstacleNodes?.Clear();
      ObstacleNodesTemp?.Clear();
      TempBlockedNodes?.Clear();
      OnPositionsRemoved?.Invoke(includeBlocks);
    }

    public void HookEventsForPathCollection(PathCollection pc)
    {
      if (pc == null)
        return;

      var pcGraph = pc.Graph;
      if (pcGraph != null)
      {
        pcGraph.OnPositionsRemoved -= pc.ClearObstacles;
        pcGraph.OnGridBaseClosing -= pc.OnGridBaseClosing;
      }

      OnPositionsRemoved += pc.ClearObstacles;
      OnGridBaseClosing += pc.OnGridBaseClosing;
    }

    public bool IsPositionAirtight(Vector3D position)
    {
      var oxygenAtPosition = MyAPIGateway.Session.OxygenProviderSystem.GetOxygenInPoint(position);
      if (oxygenAtPosition > 0.5f)
        return true;

      if (IsGridGraph && IsValid)
      {
        var gridGraph = this as CubeGridMap;
        if (gridGraph?.MainGrid != null)
        {
          var localPoint = gridGraph.MainGrid.WorldToGridInteger(position);
          if (gridGraph.MainGrid.IsRoomAtPositionAirtight(localPoint))
            return true;

          List<IMyCubeGrid> gridList = AiSession.Instance.GridGroupListPool.Get();

          gridGraph.MainGrid.GetGridGroup(GridLinkTypeEnum.Mechanical)?.GetGrids(gridList);
          bool airtight = false;

          for (int i = 0; i < gridList.Count; i++)
          {
            var grid = gridList[i];
            localPoint = grid.WorldToGridInteger(position);
            if (grid.IsRoomAtPositionAirtight(localPoint))
            {
              airtight = true;
              break;
            }
          }

          AiSession.Instance.GridGroupListPool?.Return(ref gridList);
          return airtight;
        }
      }

      return false;
    }

    readonly HashSet<Vector3I> _tempDebug = new HashSet<Vector3I>(); // for debug only
    public void DrawDebug()
    {
      try
      {
        var player = MyAPIGateway.Session.Player?.Character;
        if (player == null)
          return;

        _tempDebug.Clear();
        Vector4 color = Color.Purple;
        var rotation = Quaternion.CreateFromRotationMatrix(WorldMatrix);
        var playerLocation = WorldToLocal(player.WorldAABB.Center);
        var camerLocationWorld = MyAPIGateway.Session?.Camera?.WorldMatrix.Translation ?? player.WorldAABB.Center;
        var cameraLocation = WorldToLocal(camerLocationWorld);

        MyAPIGateway.Utilities.ShowNotification($"{this}: ObstacleNodes = {ObstacleNodes.Count}, TempBlocked = {TempBlockedNodes.Count}, PlayerLoc = {playerLocation}, CameraLoc = {cameraLocation}", 16);

        if (AiSession.Instance.DrawObstacles)
        {
          MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe;
          MyOrientedBoundingBoxD obb;

          foreach (var localVec in ObstacleNodes.Keys)
          {
            if (_tempDebug.Add(localVec))
            {
              var vec = LocalToWorld(localVec);
              obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.25, rotation);
              AiUtils.DrawOBB(obb, Color.Firebrick, raster);
            }
          }

          raster = MySimpleObjectRasterizer.Solid;

          foreach (var point in TempBlockedNodes.Keys)
          {
            if (IsOpenTile(point) && _tempDebug.Add(point))
            {
              var vec = LocalToWorld(point);
              obb = new MyOrientedBoundingBoxD(vec, Vector3D.One * 0.1, rotation);

              AiUtils.DrawOBB(obb, Color.Red, MySimpleObjectRasterizer.Solid);
            }
          }
        }

        AiUtils.DrawOBB(OBB, Color.Orange * 0.5f, MySimpleObjectRasterizer.Solid);
      }
      catch { }
    }

    public override bool Equals(object obj)
    {
      var gb = obj as GridBase;
      if (gb == null)
        return base.Equals(obj);

      return Equals(this, gb);
    }

    public override int GetHashCode()
    {
      return Key.GetHashCode();
    }

    public bool Equals(GridBase x, GridBase y)
    {
      if (x == null || y == null)
        return false;

      return x.Key == y.Key;
    }

    public int GetHashCode(GridBase obj)
    {
      return obj.Key.GetHashCode();
    }

    public override string ToString()
    {
      var gridMap = this as CubeGridMap;
      if (gridMap != null)
        return $"{this.GetType().Name} ({gridMap.MainGrid?.DisplayName ?? "NULL GridMap"})";

      var voxelMap = this as VoxelGridMap;
      if (voxelMap != null)
        return $"{this.GetType().Name} ({voxelMap.Key})";

      return this.GetType().FullName;
    }
  }
}
