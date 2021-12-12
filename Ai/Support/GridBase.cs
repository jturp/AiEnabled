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
    internal readonly int MovementCost = 1;
    internal uint LastActiveTicks;
    internal MyOrientedBoundingBoxD OBB;
    internal BoundingBoxI BoundingBox;
    internal ConcurrentDictionary<Vector3I, byte> ObstacleNodes = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, byte> ObstacleNodesTemp = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, byte> TempBlockedNodes = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, byte> Obstacles = new ConcurrentDictionary<Vector3I, byte>(Vector3I.Comparer);
    internal ConcurrentDictionary<Vector3I, Node> OpenTileDict = new ConcurrentDictionary<Vector3I, Node>(Vector3I.Comparer);
    internal List<IMyCubeGrid> GridGroups = new List<IMyCubeGrid>();
    internal List<Vector3I> NodeList = new List<Vector3I>(100);
    internal Vector3D[] _corners1 = new Vector3D[8];
    internal Vector3D[] _corners2 = new Vector3D[8];
    internal bool _locked, _graphHasTunnel = false;
    int _refreshTimer;

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
      Node node;
      var localPosition = WorldToLocal(worldPosition);
      if (!TempBlockedNodes.ContainsKey(localPosition) && !Obstacles.ContainsKey(localPosition)
        && !ObstacleNodes.ContainsKey(localPosition) && OpenTileDict.TryGetValue(localPosition, out node) && node != null)
      {
        if (!PointInsideVoxel(worldPosition, Planet))
          return (!node.IsAirNode || bot._canUseAirNodes) && (!node.IsSpaceNode || bot._canUseSpaceNodes) && (!node.IsWaterNode || bot._canUseWaterNodes);
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
      var localPosition = WorldToLocal(worldPosition);
      if (!TempBlockedNodes.ContainsKey(localPosition) && !Obstacles.ContainsKey(localPosition)
        && !ObstacleNodes.ContainsKey(localPosition) && OpenTileDict.TryGetValue(localPosition, out node) && node != null)
      {
        var groundPoint = node.SurfacePosition ?? worldPosition;
        if (!PointInsideVoxel(groundPoint, Planet))
          return (!node.IsAirNode || bot._canUseAirNodes) && (!node.IsSpaceNode || bot._canUseSpaceNodes) && (!node.IsWaterNode || bot._canUseWaterNodes);
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
        && !ObstacleNodes.ContainsKey(localPosition) && OpenTileDict.TryGetValue(localPosition, out node) && node != null)
      {
        var worldNode = node.SurfacePosition ?? LocalToWorld(node.Position);
        if (!PointInsideVoxel(worldNode, Planet))
          return (!node.IsAirNode || bot._canUseAirNodes) && (!node.IsSpaceNode || bot._canUseSpaceNodes) && (!node.IsWaterNode || bot._canUseWaterNodes);
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

      //bot._pathCollection.PathTimer.Restart();
      BoundingBoxI box = BoundingBoxI.CreateInvalid();
      OBB.GetCorners(_corners2, 0);
      for (int j = 0; j < _corners2.Length; j++)
      {
        var localCorner = WorldToLocal(_corners2[j]);
        box.Include(ref localCorner);
      }

      nextObb.GetCorners(_corners2, 0);
      BoundingBoxI otherBox = BoundingBoxI.CreateInvalid();
      for (int j = 0; j < _corners2.Length; j++)
      {
        var localCorner = WorldToLocal(_corners2[j]);
        otherBox.Include(ref localCorner);
      }

      box.IntersectWith(ref otherBox);

      var vectorToNext = nextObb.Center - OBB.Center;
      forward = WorldMatrix.GetClosestDirection(vectorToNext);
      var fwdVector = WorldMatrix.GetDirectionVector(forward);

      MatrixD matrix;
      BoundingBoxI localBox;

      if (forward == Base6Directions.Direction.Up)
      {
        matrix = WorldMatrix;
        localBox = box;
      }
      else
      {
        var localExt = box.HalfExtents;
        var center = LocalToWorld(box.Center);
        matrix = MatrixD.CreateWorld(center, fwdVector, WorldMatrix.Up);

        var upDir = Base6Directions.Direction.Up;
        var m = new MatrixI(forward, upDir);

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
      int regularCount = 0;
      int counter = 0;
      bool pointFound = false;

      for (int y = minY; y <= maxY; y++)
      {
        for (int z = minZ; z <= maxZ; z++)
        {
          for (int x = minX; x <= maxX; x++)
          {
            counter++;
            var localPoint = new Vector3I(x, y, z);

            // worldPoint = LocalToWorld(localPoint);
            Vector3D worldPoint = localPoint * cellSize;
            Vector3D.Transform(ref worldPoint, ref matrix, out worldPoint);

            if (OBB.Contains(ref worldPoint) && nextObb.Contains(ref worldPoint))
            {
              Node node;
              if (IsPositionUsable(bot, worldPoint, out node))
              {
                if ((!node.IsSpaceNode || bot._canUseSpaceNodes) && (!node.IsAirNode || bot._canUseAirNodes) && (!node.IsWaterNode || bot._canUseWaterNodes))
                {
                  if (checkTunnelPoints && node.IsTunnelNode)
                  {
                    var distance = Vector3D.DistanceSquared(worldPoint, goal);

                    if (distance < distanceTunnel)
                    {
                      distanceTunnel = distance;
                      resultNode = node;
                    }
                  }
                  else if (regularCount < 25)
                  {
                    var distance = Vector3D.DistanceSquared(worldPoint, goal);

                    if (distance < distanceRegular)
                    {
                      regularCount++;
                      distanceRegular = distance;
                      backupNode = node;
                    }
                  }
                  else if (!checkTunnelPoints)
                  {
                    pointFound = true;
                    break;
                  }
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

      //AiSession.Instance.Logger.Log($"Checked {counter} of {volume} points in prunik (extents = {extents} and took {bot._pathCollection.PathTimer.Elapsed.TotalMilliseconds} ms. Result = {resultNode?.Position.ToString() ?? "NULL"}, Backup Result = {backupNode?.Position.ToString() ?? "NULL"}");
      //if (result != null)
      //  AiSession.Instance.Logger.Log($" -> Result: IsAir = {result.IsAirNode}, IsGround = {result.IsGroundNode}, IsWater = {result.IsWaterNode}, IsSpace = {result.IsSpaceNode}, IsTunnel = {result.IsTunnelNode}, IsInVoxel = {PointInsideVoxel(result.SurfacePosition ?? LocalToWorld(result.Position), Planet)}");
      //else
      //  AiSession.Instance.Logger.Log($" -> Result wasn't in OpenTileDict!");

      //bot._pathCollection.PathTimer.Stop();

      return result;
    }

    public void AddToObstacles(Vector3D prevNode, Vector3D curNode, Vector3D nextNode)
    {
      var prev = WorldToLocal(prevNode);
      var curr = WorldToLocal(curNode);
      var next = WorldToLocal(nextNode);
      bool addObstacle = true;

      Node n;
      if (OpenTileDict.TryGetValue(prev, out n))
      {
        var dirPN = next - prev;
        var min = -Vector3I.One;
        Vector3I.Clamp(ref dirPN, ref min, ref Vector3I.One, out dirPN);

        if (n.AddBlockage(dirPN))
          addObstacle = false;
      }
      
      if (addObstacle && OpenTileDict.TryGetValue(curr, out n))
      {
        var dirCN = next - curr;
        var min = -Vector3I.One;
        Vector3I.Clamp(ref dirCN, ref min, ref Vector3I.One, out dirCN);

        if (n.AddBlockage(dirCN))
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

    public abstract IEnumerable<Vector3I> GetBlockedNodeEdges(NodeBase nodeBase);

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
      var ray = new RayD(OBB.Center, normal);
      return OBB.GetDistanceToEdgeInDirection(ray, _corners2, out distance);
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

      Node n;
      localPoint = WorldToLocal(closestSurfacePoint);
      //AiSession.Instance.Logger.Log($"Final node: {localPoint}");

      if (OpenTileDict.TryGetValue(localPoint, out n) && n?.SurfacePosition != null)
        closestSurfacePoint = n.SurfacePosition.Value;

      return closestSurfacePoint;
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

    internal bool GetClosestSurfacePointLocal(ref Vector3 gravityNorm, ref Vector3I point, out Vector3I secondaryPoint, out Vector3D surfacePoint)
    {
      secondaryPoint = Vector3I.Zero;
      surfacePoint = Vector3D.Zero;

      if (Planet == null || Planet.MarkedForClose)
        return false;

      var localToWorld = LocalToWorld(point);
      localToWorld = Planet.GetClosestSurfacePointGlobal(localToWorld);

      if (PointInsideVoxel(localToWorld, Planet))
      {
        localToWorld -= gravityNorm * 0.1f;

        int count = 0;
        while (PointInsideVoxel(localToWorld, Planet))
        {
          count++;
          if (count > 500)
          {
            AiSession.Instance.Logger.Log($"Point {point} took too many attempts to finish (going up)", MessageType.WARNING);
            break;
          }

          localToWorld -= gravityNorm * 0.1f;
        }

        localToWorld -= gravityNorm * 0.1f;
      }
      else
      {
        localToWorld += gravityNorm * 0.1f;

        int count = 0;
        while (!PointInsideVoxel(localToWorld, Planet))
        {
          count++;
          if (count > 500)
          {
            AiSession.Instance.Logger.Log($"Point {point} took too many attempts to finish (going down)", MessageType.WARNING);
            break;
          }

          localToWorld += gravityNorm * 0.1f;
        }

        localToWorld -= gravityNorm * 0.1f;
      }

      surfacePoint = localToWorld - gravityNorm;

      point = secondaryPoint = WorldToLocal(localToWorld);
      localToWorld = LocalToWorld(point);

      if (PointInsideVoxel(localToWorld, Planet))
      {
        localToWorld -= gravityNorm * CellSize;
        point = WorldToLocal(localToWorld);
      }
      else
      {
        localToWorld += gravityNorm * CellSize;
        secondaryPoint = WorldToLocal(localToWorld);
      } 
      
      return true;
    }

    internal static ConcurrentStack<MyStorageData> _storageStack = new ConcurrentStack<MyStorageData>();
    public static bool PointInsideVoxel(Vector3D pos, MyVoxelBase voxel)
    {
      if (voxel == null || voxel.MarkedForClose)
        return false;

      float _;
      var natGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(pos, out _);
      if (natGrav.LengthSquared() == 0)
        return false;

      MyStorageData tmpStorage;
      if (!_storageStack.TryPop(out tmpStorage))
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

      _storageStack.Push(tmpStorage);
      return num13 + (num14 - num13) * r.Z >= sbyte.MaxValue;
    }

    public virtual void Close()
    {
      Obstacles?.Clear();
      ObstacleNodes?.Clear();
      ObstacleNodesTemp?.Clear();
      TempBlockedNodes?.Clear();
      GridGroups?.Clear();
      NodeList?.Clear();
      StackedStairsFound?.Clear();
      _storageStack?.Clear();

      if (OpenTileDict != null)
      {
        foreach (var t in OpenTileDict.Values)
          t?.BlockedEdges?.Clear();

        OpenTileDict?.Clear();
      }

      Obstacles = null;
      ObstacleNodes = null;
      ObstacleNodesTemp = null;
      TempBlockedNodes = null;
      GridGroups = null;
      NodeList = null;
      OpenTileDict = null;
      StackedStairsFound = null;
      _storageStack = null;
      _corners1 = null;
      _corners2 = null;
    }

    public void Nullify()
    {
      _storageStack?.Clear();
      _storageStack = null;
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
