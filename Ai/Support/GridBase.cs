using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AiEnabled.Bots;
using AiEnabled.Utilities;

using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;

using VRageMath;

namespace AiEnabled.Ai.Support
{
  public abstract class GridBase : IEqualityComparer<GridBase>
  {
    internal readonly byte MovementCost = 1;
    internal byte LastActiveTicks;
    internal MyOrientedBoundingBoxD OBB;
    internal BoundingBoxI BoundingBox;
    internal ConcurrentDictionary<Vector3I, byte> ObstacleNodes = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, byte> ObstacleNodesTemp = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, byte> TempBlockedNodes = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, byte> Obstacles = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, Node> OpenTileDict = new ConcurrentDictionary<Vector3I, Node>(Vector3I.Comparer);
    internal List<IMyCubeGrid> GridGroups;
    internal bool _locked, _graphHasTunnel = false;
    byte _refreshTimer;

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
    public MyPlanet Planet { get; internal set; }
    
    /// <summary>
    /// Will be true once the grid has been processed
    /// </summary>
    public bool Ready { get; protected set; }

    /// <summary>
    /// This is true if tiles need to be updated
    /// </summary>
    public bool Dirty { get; protected set; }

    /// <summary>
    /// Whether or not any voxel changes have occurred
    /// </summary>
    public bool NeedsVoxelUpate;

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
    public virtual bool IsPositionAvailable(Vector3D position)
    {
      var node = WorldToLocal(position);
      return IsPositionValid(position) && OpenTileDict.ContainsKey(node) && !TempBlockedNodes.ContainsKey(node) && !ObstacleNodes.ContainsKey(node);        
    }

    /// <summary>
    /// Determines whether the given position is usable by the bot
    /// </summary>
    /// <param name="bot">The bot to test usability for</param>
    /// <param name="worldPosition">The world position to check</param>
    /// <returns></returns>
    public virtual bool IsPositionUsable(BotBase bot, Vector3D worldPosition)
    {
      if (bot == null || bot.IsDead)
        return false;

      Node node;
      var localPosition = WorldToLocal(worldPosition);
      if (!TempBlockedNodes.ContainsKey(localPosition) && !Obstacles.ContainsKey(localPosition)
        && !ObstacleNodes.ContainsKey(localPosition) && OpenTileDict.TryGetValue(localPosition, out node))
      {
        if (!PointInsideVoxel(worldPosition, Planet))
        {
          return (!node.IsAirNode || bot.CanUseAirNodes) && (!node.IsSpaceNode(this) || bot.CanUseSpaceNodes) && (!node.IsWaterNode || bot.CanUseWaterNodes);
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
    /// <returns></returns>
    public virtual bool IsPositionUsable(BotBase bot, Vector3D worldPosition, out Node node)
    {
      node = null;

      if (bot == null || bot.IsDead)
        return false;

      var localPosition = WorldToLocal(worldPosition);
      if (!TempBlockedNodes.ContainsKey(localPosition) && !Obstacles.ContainsKey(localPosition)
        && !ObstacleNodes.ContainsKey(localPosition) && OpenTileDict.TryGetValue(localPosition, out node))
      {
        var worldPoint = LocalToWorld(node.Position) + node.Offset;
        if (!PointInsideVoxel(worldPoint, Planet))
        {
          if (bot.WaterNodesOnly)
            return node.IsWaterNode;

          return (!node.IsAirNode || bot.CanUseAirNodes) && (!node.IsSpaceNode(this) || bot.CanUseSpaceNodes) && (!node.IsWaterNode || bot.CanUseWaterNodes);
        }
      }

      return false;
    }

    /// <summary>
    /// Determines whether the given position is usable by the bot
    /// </summary>
    /// <param name="bot">The bot to test usability for</param>
    /// <param name="localPosition">The node position to check</param>
    /// <returns></returns>
    public virtual bool IsPositionUsable(BotBase bot, Vector3I localPosition)
    {
      Node node;
      if (!TempBlockedNodes.ContainsKey(localPosition) && !Obstacles.ContainsKey(localPosition)
        && !ObstacleNodes.ContainsKey(localPosition) && OpenTileDict.TryGetValue(localPosition, out node))
      {
        var worldPoint = LocalToWorld(node.Position) + node.Offset;
        if (!PointInsideVoxel(worldPoint, Planet))
        {
          if (bot.WaterNodesOnly)
            return node.IsWaterNode;

          return (!node.IsAirNode || bot.CanUseAirNodes) && (!node.IsSpaceNode(this) || bot.CanUseSpaceNodes) && (!node.IsWaterNode || bot.CanUseWaterNodes);
        }
      }

      return false;
    }

    /// <summary>
    /// Determines whether the given node is an available tile position
    /// </summary>
    /// <param name="position">The local position to check</param>
    /// <returns></returns>
    public virtual bool IsPositionAvailable(Vector3I node)
    {
      return OpenTileDict.ContainsKey(node) && !TempBlockedNodes.ContainsKey(node) && !ObstacleNodes.ContainsKey(node);
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

      if (_graphHasTunnel && Planet != null && !Planet.MarkedForClose)
      {
        float _;
        gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(goal, out _);

        if (gravity.LengthSquared() > 0)
          gravity.Normalize();
        else
          gravity = WorldMatrix.Down;

        var surfacePoint = Planet.GetClosestSurfacePointGlobal(ref goal) - gravity;

        while (PointInsideVoxel(surfacePoint, Planet))
          surfacePoint -= gravity;

        var goalPoint = goal;

        while (PointInsideVoxel(goalPoint, Planet))
          goalPoint -= gravity;

        var line = new LineD(surfacePoint, goalPoint);
        var maxCount = Math.Floor(line.Length) - 3;

        for (int i = 0; i < maxCount; i++)
        {
          var point = surfacePoint + line.Direction * i;
          if (PointInsideVoxel(point, Planet))
          {
            goalUnderGround = true;
            break;
          }
        }

        if (goalUnderGround)
        {
          checkTunnelPoints = true;
        }
        else
        {
          var botPosition = bot.Position;
          surfacePoint = Planet.GetClosestSurfacePointGlobal(ref botPosition) - gravity;

          while (PointInsideVoxel(surfacePoint, Planet))
            surfacePoint -= gravity;

          goalPoint = botPosition;

          while (PointInsideVoxel(goalPoint, Planet))
            goalPoint -= gravity;

          line = new LineD(surfacePoint, goalPoint);
          maxCount = Math.Floor(line.Length) - 3;

          for (int i = 0; i < maxCount; i++)
          {
            var point = surfacePoint + line.Direction * i;
            if (PointInsideVoxel(point, Planet))
            {
              startUnderGound = true;
              break;
            }
          }

          checkTunnelPoints = startUnderGound;
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
      //int regularCount = 0;
      //int counter = 0;
      bool pointFound = false;

      for (int y = minY; y <= maxY; y++)
      {
        for (int z = minZ; z <= maxZ; z++)
        {
          for (int x = minX; x <= maxX; x++)
          {
            //counter++;
            var localPoint = new Vector3I(x, y, z);

            // worldPoint = LocalToWorld(localPoint);
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
      var extents = localBox.Max - localBox.Min + 1;
      var volume = extents.X * extents.Y * extents.Z;

      //AiSession.Instance.Logger.Log($"Checked {counter} of {volume} points in prunik (extents = {extents}. Result = {resultNode?.Position.ToString() ?? "NULL"}, Backup Result = {backupNode?.Position.ToString() ?? "NULL"}");
      //if (result != null)
      //  AiSession.Instance.Logger.Log($" -> Result: IsAir = {result.IsAirNode}, IsGround = {result.IsGroundNode}, IsWater = {result.IsWaterNode}, IsSpace = {result.IsSpaceNode(this)}, IsTunnel = {result.IsTunnelNode}, IsInVoxel = {PointInsideVoxel(LocalToWorld(result.Position) + result.Offset, Planet)}");
      //else
      //  AiSession.Instance.Logger.Log($" -> Result wasn't in OpenTileDict!");

      return result;
    }

    public void AddToObstacles(Vector3D prevNode, Vector3D curNode, Vector3D nextNode)
    {
      var prev = WorldToLocal(prevNode);
      var curr = WorldToLocal(curNode);
      var next = WorldToLocal(nextNode);
      bool addObstacle = true;

      Node node;
      if (OpenTileDict.TryGetValue(prev, out node))
      {
        var dirPN = next - prev;
        var min = -Vector3I.One;
        Vector3I.Clamp(ref dirPN, ref min, ref Vector3I.One, out dirPN);

        if (node.SetBlocked(dirPN))
          addObstacle = false;
      }
      
      if (addObstacle && OpenTileDict.TryGetValue(curr, out node))
      {
        var dirCN = next - curr;
        var min = -Vector3I.One;
        Vector3I.Clamp(ref dirCN, ref min, ref Vector3I.One, out dirCN);

        if (node.SetBlocked(dirCN))
          addObstacle = false;
      }

      if (addObstacle)
      {
        //AiSession.Instance.Logger.Log($"AddToObstacles: Adding {next} to obstacles");
        Obstacles[next] = new byte();
      }
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

    public abstract Vector3D LocalToWorld(Vector3I localVector);

    public abstract Vector3I WorldToLocal(Vector3D worldVector);

    public abstract IEnumerable<Vector3I> GetBlockedNodeEdges(Node nodeBase);

    public abstract IEnumerable<Vector3I> Neighbors(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, Vector3D? up = null);

    public abstract bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false);

    public abstract bool GetClosestValidNode(BotBase bot, Vector3I testNode, out Vector3I node, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false);

    public abstract bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I node);

    public abstract bool InBounds(Vector3I node);

    public abstract Vector3D? GetBufferZoneTargetPosition(Vector3D fromPosition, Vector3D toPosition, bool getEdgePoint = false);

    public abstract Vector3D? GetBufferZoneTargetPositionCentered(Vector3D fromPosition, Vector3D toPosition, Vector3D sideNormal, bool getEdgePoint = false);

    public abstract bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node);

    public abstract void UpdateTempObstacles();

    public abstract Node GetReturnHomePoint(BotBase bot);

    /// <summary>
    /// Used to cache the pathnodes for faster retrieval on subsequent pathfinding calls
    /// </summary>
    public ConcurrentDictionary<Vector3I, Node> OptimizedCache = new ConcurrentDictionary<Vector3I, Node>(Vector3I.Comparer);

    /// <summary>
    /// Item1 = stair coming from, Item2 = stair going to, Item3 = position to insert between them
    /// </summary>
    internal Queue<MyTuple<Vector3I, Vector3I, Vector3I>> StackedStairsFound { get; private set; } = new Queue<MyTuple<Vector3I, Vector3I, Vector3I>>();

    internal virtual void SetReady()
    {
      //MyAPIGateway.Utilities.ShowMessage("AiEnabled", "GridBase.SetReady()");
      _locked = false;
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

    internal Vector3D GetClosestSurfacePointFast(BotBase bot, Vector3D worldPosition, Vector3D upVec)
    {
      if (Planet == null || Planet.MarkedForClose)
      {
        return worldPosition;
      }

      var localPoint = WorldToLocal(worldPosition);

      if (IsPositionUsable(bot, localPoint))
        return worldPosition;

      float _;
      Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
      if (gravity.LengthSquared() > 0)
        upVec = Vector3D.Normalize(-gravity);

      var closestSurfacePoint = LocalToWorld(localPoint);
      var addVec = upVec * CellSize;
      //AiSession.Instance.Logger.Log($"Initial node: {localPoint}");

      if (PointInsideVoxel(closestSurfacePoint, Planet))
      {
        closestSurfacePoint += addVec;

        while (PointInsideVoxel(closestSurfacePoint, Planet))
          closestSurfacePoint += addVec;
      }
      else
      {
        closestSurfacePoint -= addVec;

        while (!PointInsideVoxel(closestSurfacePoint, Planet))
          closestSurfacePoint -= addVec;

        if (PointInsideVoxel(closestSurfacePoint, Planet))
          closestSurfacePoint += addVec;
      }

      Node node;
      localPoint = WorldToLocal(closestSurfacePoint);
      //AiSession.Instance.Logger.Log($"Final node: {localPoint}");

      if (OpenTileDict.TryGetValue(localPoint, out node))
      {
        closestSurfacePoint = LocalToWorld(node.Position) + node.Offset;
      }

      return closestSurfacePoint;
    }

    internal bool GetClosestSurfacePointFast(Vector3D worldPosition, Vector3D upVec, out Node node)
    {
      node = null;
      if (Planet == null || Planet.MarkedForClose)
      {
        return false;
      }

      var localPoint = WorldToLocal(worldPosition);

      float _;
      Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
      if (gravity.LengthSquared() > 0)
        upVec = Vector3D.Normalize(-gravity);

      var closestSurfacePoint = LocalToWorld(localPoint);
      var addVec = upVec * CellSize;

      if (PointInsideVoxel(closestSurfacePoint, Planet))
      {
        closestSurfacePoint += addVec;

        while (PointInsideVoxel(closestSurfacePoint, Planet))
          closestSurfacePoint += addVec;
      }
      else
      {
        closestSurfacePoint -= addVec;

        while (!PointInsideVoxel(closestSurfacePoint, Planet))
          closestSurfacePoint -= addVec;

        if (PointInsideVoxel(closestSurfacePoint, Planet))
          closestSurfacePoint += addVec;
      }

      localPoint = WorldToLocal(closestSurfacePoint);
      return OpenTileDict.TryGetValue(localPoint, out node) && node.IsGroundNode;
    }

    public static Vector3D GetClosestSurfacePointFast(Vector3D worldPosition, Vector3D upVec, MyPlanet planet, out bool pointOnGround)
    {
      pointOnGround = true;

      if (planet == null || planet.MarkedForClose)
      {
        return worldPosition;
      }

      float _;
      Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPosition, out _);
      if (gravity.LengthSquared() > 0)
        upVec = Vector3D.Normalize(-gravity);

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
        if (PointInsideVoxel(mid, planet))
        {
          pointBelow = mid;
        }
        else
        {
          pointAbove = mid;
        }
      }

      if (PointInsideVoxel(pointAbove, planet))
      {
        pointOnGround = false;
        return pointBelow;
      }

      return pointAbove;
    }

    public static bool PointInsideVoxel(Vector3D pos, MyVoxelBase voxel)
    {
      if (voxel == null || voxel.MarkedForClose)
        return false;

      float _;
      var natGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(pos, out _);
      if (natGrav.LengthSquared() == 0)
        return false;

      MyStorageData tmpStorage;
      if (!AiSession.StorageStack.TryPop(out tmpStorage))
        tmpStorage = new MyStorageData();

      var voxelMatrix = voxel.PositionComp.WorldMatrixInvScaled;
      var vecMax = new Vector3I(int.MaxValue);
      var vecMin = new Vector3I(int.MinValue);

      Vector3D result;
      Vector3D.Transform(ref pos, ref voxelMatrix, out result);
      var r = result + (Vector3D)(voxel.Size / 2);
      var v1 = Vector3D.Floor(r);
      Vector3D.Fract(ref r, out r);
      var v2 = v1 + voxel.StorageMin;
      var v3 = v2 + 1;

      if (v2 != vecMax && v3 != vecMin)
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

    public virtual void Close()
    {
      IsValid = false;
      Obstacles?.Clear();
      ObstacleNodes?.Clear();
      ObstacleNodesTemp?.Clear();
      TempBlockedNodes?.Clear();
      GridGroups?.Clear();
      StackedStairsFound?.Clear();

      if (OpenTileDict != null)
      {
        OpenTileDict?.Clear();
      }

      Obstacles = null;
      ObstacleNodes = null;
      ObstacleNodesTemp = null;
      TempBlockedNodes = null;
      GridGroups = null;
      OpenTileDict = null;
      StackedStairsFound = null;
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
      return WorldMatrix.Translation.GetHashCode();
    }

    public bool Equals(GridBase x, GridBase y)
    {
      if (x == null || y == null)
        return false;

      return Vector3D.IsZero(x.WorldMatrix.Translation - y.WorldMatrix.Translation);
    }

    public int GetHashCode(GridBase obj)
    {
      return obj.WorldMatrix.Translation.GetHashCode();
    }
  }
}
