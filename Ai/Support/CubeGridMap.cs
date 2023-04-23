using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;

using SpaceEngineers.Game.ModAPI;

using VRage;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

using VRageMath;
using AiEnabled.Support;
using AiEnabled.Utilities;
using VRage.Collections;
using AiEnabled.Bots;
using VRage.ModAPI;
using System.Threading;
using VRage.Voxels;
using AiEnabled.API;
using VRage.Game.Components;
using VRage.Game;
using AiEnabled.Parallel;
using ParallelTasks;
using Sandbox.Game.Entities.Blocks;
using AiEnabled.Bots.Roles.Helpers;

namespace AiEnabled.Ai.Support
{
  public class CubeGridMap : GridBase
  {
    /// <summary>
    /// The main grid this map refers to
    /// </summary>
    public MyCubeGrid MainGrid { get; protected set; }

    /// <summary>
    /// A collection of grids associated with the map
    /// </summary>
    public List<IMyCubeGrid> GridCollection;

    /// <summary>
    /// The cell size (in meters) for the map
    /// </summary>
    public override float CellSize => MainGrid?.GridSize ?? 2.5f;

    /// <summary>
    /// The smallest oriented bounding box that fits the grid
    /// </summary>
    public MyOrientedBoundingBoxD UnbufferedOBB { get; private set; }

    /// <summary>
    /// This will be true when the grid has moved more than 1m from its previous position
    /// </summary>
    public bool HasMoved { get; private set; }

    /// <summary>
    /// If true, there are grids to add to or remove from the GridCollection
    /// </summary>
    public bool NeedsGridUpdate { get; private set; }

    /// <summary>
    /// If true, blocks have been added or removed the main grid
    /// </summary>
    public bool NeedsBlockUpdate { get; private set; }

    /// <summary>
    /// If true, need to recheck for interior nodes (blocks added or removed)
    /// </summary>
    public bool NeedsInteriorNodesUpdate { get; private set; }

    /// <summary>
    /// If true, planet tiles have already been cleared from the dictionary
    /// </summary>
    public bool PlanetTilesRemoved { get; private set; }

    /// <summary>
    /// If true, the interior nodes list is ready to be used
    /// </summary>
    public bool InteriorNodesReady { get; private set; }

    /// <summary>
    /// Contains information about the components stored in the grid's inventories
    /// </summary>
    public InventoryCache InventoryCache;

    /// <summary>
    /// If repair bots are present on the grid, this will hold any tiles they are actively reparing or building.
    /// Key is grid entity id, value is a Dictionary where Key is block position on grid and Value is bot entity id.
    /// </summary>
    public ConcurrentDictionary<long, ConcurrentDictionary<Vector3I, long>> SelectedRepairTiles = new ConcurrentDictionary<long, ConcurrentDictionary<Vector3I, long>>(5, 10);

    /// <summary>
    /// Doors that are currently closed and non-functional will be in this collection
    /// </summary>
    public ConcurrentDictionary<Vector3I, IMyDoor> BlockedDoors = new ConcurrentDictionary<Vector3I, IMyDoor>(5, 10, Vector3I.Comparer);

    /// <summary>
    /// All doors for the current grid
    /// </summary>
    public ConcurrentDictionary<Vector3I, IMyDoor> AllDoors = new ConcurrentDictionary<Vector3I, IMyDoor>(5, 10, Vector3I.Comparer);

    /// <summary>
    /// These are nodes that correspond to blocks above slopes and ramps that are pre-checked for pathfinding validity.
    /// No need to check them again for blocked edges.
    /// </summary>
    public HashSet<Vector3I> ExemptNodesUpper = new HashSet<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// These are nodes that correspond to blocks to the side of slopes and ramps that are pre-checked for pathfinding validity.
    /// No need to check them again for blocked edges.
    /// </summary>
    public HashSet<Vector3I> ExemptNodesSide = new HashSet<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// Location of any weapon system in the grid collection. Used to determine movement cost of nodes (closer to a weapon = higher movement cost)
    /// </summary>
    public HashSet<Vector3I> WeaponPositions = new HashSet<Vector3I>(Vector3I.Comparer);

    /// <summary>
    /// Used for determining when a boss encounter spawns
    /// </summary>
    public byte BossSpawnChance = 100;

    /// <summary>
    /// The number of bots spawned on this grid
    /// </summary>
    public ushort TotalSpawnCount = 0;


    internal Dictionary<Vector3I, Node> OpenTileDict = new Dictionary<Vector3I, Node>(Vector3I.Comparer);
    internal Dictionary<Vector3I, Node> PlanetTileDictionary = new Dictionary<Vector3I, Node>(Vector3I.Comparer);

    Vector3D _worldPosition;
    MatrixD _lastMatrix;
    public List<Vector3I> InteriorNodeList;
    readonly byte _boxExpansion;
    List<CubeGridMap> _additionalMaps2;
    List<IMyCubeGrid> _gridsToAdd;
    List<IMyCubeGrid> _gridsToRemove;
    ObstacleWorkData _tempObstaclesWorkData;
    List<VoxelUpdateItem> _voxelUpdatesNeeded;
    HashSet<Vector3I> _blockUpdateHash;
    HashSet<Vector3I> _blockApplyHash;
    MyQueue<VoxelUpdateItem> _voxelUpdatesQueue;
    ParallelTasks.Task _obstacleTask, _updateTask, _asyncTileTask, _interiorNodesTask;
    FastResourceLock _pendingLockObject = new FastResourceLock();

    public CubeGridMap(MyCubeGrid grid, MatrixD spawnBlockMatrix)
    {
      try
      {
        if (grid?.Physics == null || grid.MarkedForClose)
        {
          AiSession.Instance.Logger.Log($"CubeGridMap.ctor: Grid '{grid?.DisplayName ?? "NULL"}' was null or marked for close in constructor!", MessageType.WARNING);
          return;
        }

        var gridGroup = grid.GetGridGroup(GridLinkTypeEnum.Mechanical);
        if (gridGroup == null)
        {
          AiSession.Instance.Logger.Log($"CubeGridMap.ctor: GridGroup was null for '{grid.DisplayName}'", MessageType.WARNING);
          return;
        }

        if (!AiSession.Instance.GridGroupListStack.TryPop(out GridCollection) || GridCollection == null)
          GridCollection = new List<IMyCubeGrid>();
        else
          GridCollection.Clear();

        gridGroup.GetGrids(GridCollection);

        if (GridCollection.Count == 0)
        {
          GridCollection.Clear();
          AiSession.Instance.GridGroupListStack.Push(GridCollection);

          AiSession.Instance.Logger.Log($"CubeGridMap.ctor: GridCollection was empty for '{grid.DisplayName}'", MessageType.WARNING);
          return;
        }

        IsGridGraph = true;
        Key = (ulong)grid.EntityId;
        gridGroup.OnGridAdded += GridGroup_OnGridAdded;
        gridGroup.OnGridRemoved += GridGroup_OnGridRemoved;
        gridGroup.OnReleased += GridGroup_OnReleased;

        var isStatic = grid.Physics.IsStatic;
        //if (isStatic)
        //  _boxExpansion = 10;
        //else
        _boxExpansion = 5;

        if (!AiSession.Instance.ObstacleWorkDataStack.TryPop(out _tempObstaclesWorkData) || _tempObstaclesWorkData == null)
          _tempObstaclesWorkData = new ObstacleWorkData();

        if (!AiSession.Instance.GridMapListStack.TryPop(out _additionalMaps2))
          _additionalMaps2 = new List<CubeGridMap>();
        else
          _additionalMaps2.Clear();

        if (!AiSession.Instance.InvCacheStack.TryPop(out InventoryCache))
          InventoryCache = new InventoryCache(grid);
        else
          InventoryCache.SetGrid(grid);

        if (!AiSession.Instance.GridGroupListStack.TryPop(out _gridsToAdd) || _gridsToAdd == null)
          _gridsToAdd = new List<IMyCubeGrid>();
        else
          _gridsToAdd.Clear();

        if (!AiSession.Instance.GridGroupListStack.TryPop(out _gridsToRemove) || _gridsToRemove == null)
          _gridsToRemove = new List<IMyCubeGrid>();
        else
          _gridsToRemove.Clear();

        if (!AiSession.Instance.LineListStack.TryPop(out InteriorNodeList) || InteriorNodeList == null)
          InteriorNodeList = new List<Vector3I>();
        else
          InteriorNodeList.Clear();

        if (!AiSession.Instance.VoxelUpdateListStack.TryPop(out _voxelUpdatesNeeded) || _voxelUpdatesNeeded == null)
          _voxelUpdatesNeeded = new List<VoxelUpdateItem>(10);
        else
          _voxelUpdatesNeeded.Clear();

        if (!AiSession.Instance.VoxelUpdateQueueStack.TryPop(out _voxelUpdatesQueue) || _voxelUpdatesQueue == null)
          _voxelUpdatesQueue = new MyQueue<VoxelUpdateItem>(10);
        else
          _voxelUpdatesQueue.Clear();

        if (!AiSession.Instance.LocalVectorHashStack.TryPop(out _blockUpdateHash) || _blockUpdateHash == null)
          _blockUpdateHash = new HashSet<Vector3I>();
        else
          _blockUpdateHash.Clear();

        if (!AiSession.Instance.LocalVectorHashStack.TryPop(out _blockApplyHash) || _blockApplyHash == null)
          _blockApplyHash = new HashSet<Vector3I>();
        else
          _blockApplyHash.Clear();

        InventoryCache.Update(false);

        MainGrid = grid;
        WorldMatrix = grid.WorldMatrix;
        _worldPosition = WorldMatrix.Translation;
        _lastMatrix = WorldMatrix;

        var hExtents = grid.PositionComp.LocalAABB.HalfExtents;
        var center = grid.PositionComp.WorldAABB.Center;
        var quat = Quaternion.CreateFromRotationMatrix(WorldMatrix);
        UnbufferedOBB = new MyOrientedBoundingBoxD(center, hExtents, quat);

        hExtents += (new Vector3I(11) * grid.GridSize);
        OBB = new MyOrientedBoundingBoxD(center, hExtents, quat);

        bool matrixSet = false;
        bool gravityChecked = false;
        if (isStatic)
        {
          var planet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);
          if (planet != null && planet.PositionComp.WorldVolume.Intersects(OBB.GetAABB()))
          {
            Vector3D[] corners;
            if (!AiSession.Instance.CornerArrayStack.TryPop(out corners) || corners == null)
              corners = new Vector3D[8];

            var gridOBB = new MyOrientedBoundingBoxD(OBB.Center, grid.PositionComp.LocalAABB.HalfExtents, quat);
            gridOBB.GetCorners(corners, 0);

            using (planet.Pin())
            {
              for (int i = 0; i < corners.Length; i++)
              {
                if (PointInsideVoxel(corners[i], planet))
                {
                  gravityChecked = true;
                  break;
                }
              }
            }

            AiSession.Instance.CornerArrayStack.Push(corners);

            if (gravityChecked)
            {
              float interference;
              var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out interference);
              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, interference);
              if (aGrav.LengthSquared() > 0)
              {
                var up = -Vector3D.Normalize(aGrav);
                var fwd = Vector3D.CalculatePerpendicularVector(up);

                var dir = grid.WorldMatrix.GetClosestDirection(up);
                up = grid.WorldMatrix.GetDirectionVector(dir);

                dir = grid.WorldMatrix.GetClosestDirection(fwd);
                fwd = grid.WorldMatrix.GetDirectionVector(dir);

                WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
                matrixSet = true;
              }
              else if (nGrav.LengthSquared() > 0)
              {
                var up = -Vector3D.Normalize(nGrav);
                var fwd = Vector3D.CalculatePerpendicularVector(up);

                var dir = grid.WorldMatrix.GetClosestDirection(up);
                up = grid.WorldMatrix.GetDirectionVector(dir);

                dir = grid.WorldMatrix.GetClosestDirection(fwd);
                fwd = grid.WorldMatrix.GetDirectionVector(dir);

                WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
                matrixSet = true;
              }
            }
          }
        }

        if (!matrixSet)
        {
          int numSeats, numFactories;
          if (grid.HasMainCockpit())
          {
            WorldMatrix = grid.MainCockpit.WorldMatrix;
            matrixSet = true;
          }
          else if (grid.HasMainRemoteControl())
          {
            WorldMatrix = grid.MainRemoteControl.WorldMatrix;
            matrixSet = true;
          }
          else if ((grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
            || (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_RemoteControl), out numSeats) && numSeats > 0)
            || (grid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_ConveyorSorter), out numFactories) && numFactories > 0))
          {
            foreach (var b in grid.GetFatBlocks())
            {
              if (b is IMyShipController || (b is MyConveyorSorter && b.BlockDefinition.Id.SubtypeName == "RoboFactory"))
              {
                WorldMatrix = b.WorldMatrix;
                matrixSet = true;
                break;
              }
            }
          }

          if (!matrixSet)
          {
            if (!gravityChecked)
            {
              float _;
              var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
              if (aGrav.LengthSquared() > 0)
              {
                var up = -Vector3D.Normalize(aGrav);
                var fwd = Vector3D.CalculatePerpendicularVector(up);

                var dir = grid.WorldMatrix.GetClosestDirection(up);
                up = grid.WorldMatrix.GetDirectionVector(dir);

                dir = grid.WorldMatrix.GetClosestDirection(fwd);
                fwd = grid.WorldMatrix.GetDirectionVector(dir);

                WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
              }
              else if (nGrav.LengthSquared() > 0)
              {
                var up = -Vector3D.Normalize(nGrav);
                var fwd = Vector3D.CalculatePerpendicularVector(up);

                var dir = grid.WorldMatrix.GetClosestDirection(up);
                up = grid.WorldMatrix.GetDirectionVector(dir);

                dir = grid.WorldMatrix.GetClosestDirection(fwd);
                fwd = grid.WorldMatrix.GetDirectionVector(dir);

                WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
              }
              else
                WorldMatrix = spawnBlockMatrix;
            }
            else
              WorldMatrix = spawnBlockMatrix;

            bool rotate = true;
            foreach (var block in grid.GetFatBlocks())
            {
              rotate = false;
              break;
            }

            if (rotate)
            {
              var rotatedMatrix = MatrixD.CreateRotationY(MathHelper.PiOver2) * WorldMatrix;
              WorldMatrix = rotatedMatrix;
            }
          }
        }

        UnhookEventsForGrid(MainGrid);
        HookEventsForGrid(MainGrid);
        AiSession.Instance.MapInitQueue.Enqueue(this);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.ctor: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void GridGroup_OnGridRemoved(IMyGridGroupData groupRemovedFrom, IMyCubeGrid grid, IMyGridGroupData groupAddedTo)
    {
      try
      {
        if (grid != null && !grid.IsSameConstructAs(MainGrid))
          return;

        Dirty = true;
        Ready = false;

        var myGrid = grid as MyCubeGrid;

        if (myGrid != null)
        {
          _gridsToRemove.Add(grid);
          NeedsGridUpdate = true;
        }
        else if (MainGrid == null || MainGrid.MarkedForClose || grid?.EntityId == MainGrid.EntityId)
          UpdateMainGrid();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.GridGroup_OnGridRemoved: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void GridGroup_OnGridAdded(IMyGridGroupData groupAddedTo, IMyCubeGrid grid, IMyGridGroupData groupRemovedFrom)
    {
      try
      {
        if (grid == null || !grid.IsSameConstructAs(MainGrid))
          return;

        Dirty = true;
        Ready = false;

        var myGrid = grid as MyCubeGrid;

        if (myGrid?.Physics != null && !myGrid.IsPreview && !grid.MarkedForClose)
        {
          _gridsToAdd.Add(grid);
          NeedsGridUpdate = true;
        }
        else if (MainGrid == null || MainGrid.MarkedForClose)
          UpdateMainGrid();
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.GridGroup_OnGridAdded: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void GridGroup_OnReleased(IMyGridGroupData gridGroup)
    {
      try
      {
        Dirty = true;
        Ready = false;

        gridGroup.OnGridAdded -= GridGroup_OnGridAdded;
        gridGroup.OnGridRemoved -= GridGroup_OnGridRemoved;
        gridGroup.OnReleased -= GridGroup_OnReleased;

        if (MainGrid == null || MainGrid.MarkedForClose)
        {
          for (int i = GridCollection.Count - 1; i >= 0; i--)
          {
            var grid = GridCollection[i] as MyCubeGrid;
            if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.GridSizeEnum == MyCubeSize.Small)
              continue;

            MainGrid = grid;
            break;
          }
        }

        if (MainGrid != null && !MainGrid.MarkedForClose)
        {
          gridGroup = MainGrid.GetGridGroup(GridLinkTypeEnum.Mechanical);

          if (gridGroup != null)
          {
            gridGroup.OnGridAdded += GridGroup_OnGridAdded;
            gridGroup.OnGridRemoved += GridGroup_OnGridRemoved;
            gridGroup.OnReleased += GridGroup_OnReleased;
          }
          else
          {
            Close();
          }
        }
        else
        {
          Close();
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.GridGroup_OnReleased: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public void UpdateGridCollection()
    {
      try
      {
        Dirty = true;
        Ready = false;
        NeedsGridUpdate = false;

        for (int i = 0; i < _gridsToRemove.Count; i++)
        {
          var grid = _gridsToRemove[i];
          if (grid != null)
          {
            CloseGrid(grid as MyEntity);

            for (int j = GridCollection.Count; j < GridCollection.Count; j++)
            {
              if (grid.EntityId == GridCollection[j]?.EntityId)
              {
                GridCollection.RemoveAtFast(j);
                break;
              }
            }
          }
        }

        for (int i = 0; i < _gridsToAdd.Count; i++)
        {
          var myGrid = _gridsToAdd[i] as MyCubeGrid;
          if (myGrid != null)
          {
            GridCollection.Add(myGrid);
          }
        }

        _gridsToAdd.Clear();
        _gridsToRemove.Clear();
        UpdateMainGrid();
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.UpdateGridCollection: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public override void Close()
    {
      try
      {
        IsGridGraph = false;

        if (AiSession.Instance != null && AiSession.Instance.Registered)
        {
          if (InventoryCache != null)
          {
            InventoryCache.SetGrid(null);
            InventoryCache._needsUpdate = false;

            AiSession.Instance.InvCacheStack?.Push(InventoryCache);
          }

          if (_additionalMaps2 != null)
          {
            _additionalMaps2.Clear();
            AiSession.Instance.GridMapListStack?.Push(_additionalMaps2);
          }

          if (GridCollection != null)
          {
            for (int i = 0; i < GridCollection.Count; i++)
            {
              var grid = GridCollection[i] as MyEntity;
              if (grid != null)
                CloseGrid(grid);
            }

            GridCollection.Clear();
            AiSession.Instance.GridGroupListStack?.Push(GridCollection);
          }

          if (_gridsToAdd != null)
          {
            _gridsToAdd.Clear();
            AiSession.Instance.GridGroupListStack?.Push(_gridsToAdd);
          }

          if (_gridsToRemove != null)
          {
            _gridsToRemove.Clear();
            AiSession.Instance.GridGroupListStack?.Push(_gridsToRemove);
          }

          if (InteriorNodeList != null)
          {
            InteriorNodeList.Clear();
            AiSession.Instance.LineListStack?.Push(InteriorNodeList);
          }

          if (_tempObstaclesWorkData != null)
          {
            AiSession.Instance.ObstacleWorkDataStack?.Push(_tempObstaclesWorkData);
          }

          if (_voxelUpdatesNeeded != null)
          {
            AiSession.Instance.VoxelUpdateListStack?.Push(_voxelUpdatesNeeded);
          }

          if (_voxelUpdatesQueue != null)
          {
            AiSession.Instance.VoxelUpdateQueueStack?.Push(_voxelUpdatesQueue);
          }

          if (_blockUpdateHash != null)
          {
            _blockUpdateHash.Clear();
            AiSession.Instance.LocalVectorHashStack?.Push(_blockUpdateHash);
          }

          if (_blockApplyHash != null)
          {
            _blockApplyHash.Clear();
            AiSession.Instance.LocalVectorHashStack?.Push(_blockApplyHash);
          }
        }
        else
        {
          _gridsToAdd?.Clear();
          _gridsToRemove?.Clear();
          _additionalMaps2?.Clear();
          _voxelUpdatesNeeded?.Clear();
          _voxelUpdatesQueue?.Clear();
          _blockUpdateHash?.Clear();
          _blockApplyHash?.Clear();

          GridCollection?.Clear();
          InteriorNodeList?.Clear();

          _tempObstaclesWorkData = null;
          _gridsToAdd = null;
          _gridsToRemove = null;
          _additionalMaps2 = null;
          _voxelUpdatesNeeded = null;
          _voxelUpdatesQueue = null;
          _blockUpdateHash = null;
          _blockApplyHash = null;

          InventoryCache = null;
          GridCollection = null;
        }

        AllDoors?.Clear();
        AllDoors = null;

        BlockedDoors?.Clear();
        BlockedDoors = null;

        OpenTileDict?.Clear();
        OpenTileDict = null;

        SelectedRepairTiles?.Clear();
        SelectedRepairTiles = null;

        PlanetTileDictionary?.Clear();
        PlanetTileDictionary = null;

        WeaponPositions?.Clear();
        WeaponPositions = null;

        WeaponPositions?.Clear();
        WeaponPositions = null;

        ExemptNodesUpper?.Clear();
        ExemptNodesUpper = null;

        _blockBoxList?.Clear();
        _blockBoxList = null;

        _positionsToRemove?.Clear();
        _positionsToRemove = null;

        _tempKVPList?.Clear();
        _tempKVPList = null;

        _pendingLockObject = null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.Close: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
      finally
      {
        base.Close();
      }
    }

    void HookEventsForGrid(MyCubeGrid grid)
    {
      try
      { 
        if (grid != null)
        {
          grid.OnBlockAdded += OnBlockAddRemove;
          grid.OnBlockRemoved += OnBlockAddRemove;
          grid.OnGridSplit += OnGridSplit;
          grid.OnMarkForClose += CloseGrid;
          grid.OnClosing += CloseGrid;
          grid.OnClose += CloseGrid;
          grid.PositionComp.OnPositionChanged += OnGridPositionChanged;
          grid.OnStaticChanged += Grid_OnStaticChanged;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.HookEventsForGrid: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void Grid_OnStaticChanged(MyCubeGrid grid, bool isStatic)
    {
      if (isStatic && grid != null && !grid.MarkedForClose)
      {
        bool checkGravity = false;
        var planet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);
        if (planet != null && planet.PositionComp.WorldVolume.Intersects(UnbufferedOBB.GetAABB()))
        {
          Vector3D[] corners;
          if (!AiSession.Instance.CornerArrayStack.TryPop(out corners) || corners == null)
            corners = new Vector3D[8];

          var gridOBB = new MyOrientedBoundingBoxD(OBB.Center, grid.PositionComp.LocalAABB.HalfExtents, Quaternion.CreateFromRotationMatrix(grid.WorldMatrix));
          gridOBB.GetCorners(corners, 0);

          using (planet.Pin())
          {
            for (int i = 0; i < corners.Length; i++)
            {
              if (PointInsideVoxel(corners[i], planet))
              {
                checkGravity = true;
                break;
              }
            }
          }

          AiSession.Instance.CornerArrayStack.Push(corners);

          if (checkGravity)
          {
            MatrixD newMatrix = WorldMatrix;
            Base6Directions.Direction oldUpDir = Base6Directions.Direction.Up;
            Base6Directions.Direction newUpDir = Base6Directions.Direction.Up;

            float interference;
            var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out interference);
            var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, interference);
            if (aGrav.LengthSquared() > 0)
            {
              var up = -Vector3D.Normalize(aGrav);
              var fwd = Vector3D.CalculatePerpendicularVector(up);

              oldUpDir = WorldMatrix.GetClosestDirection(up);
              newUpDir = grid.WorldMatrix.GetClosestDirection(up);
              up = grid.WorldMatrix.GetDirectionVector(newUpDir);

              var dir = grid.WorldMatrix.GetClosestDirection(fwd);
              fwd = grid.WorldMatrix.GetDirectionVector(dir);

              newMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
            }
            else if (nGrav.LengthSquared() > 0)
            {
              var up = -Vector3D.Normalize(nGrav);
              var fwd = Vector3D.CalculatePerpendicularVector(up);

              oldUpDir = WorldMatrix.GetClosestDirection(up);
              newUpDir = grid.WorldMatrix.GetClosestDirection(up);
              up = grid.WorldMatrix.GetDirectionVector(newUpDir);

              var dir = grid.WorldMatrix.GetClosestDirection(fwd);
              fwd = grid.WorldMatrix.GetDirectionVector(dir);

              newMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
            }

            if (oldUpDir != newUpDir)
            {
              WorldMatrix = newMatrix;
              Dirty = true;
            }
          }
        }
      }
    }

    void UnhookEventsForGrid(MyCubeGrid grid)
    {
      try
      {
        if (grid != null)
        {
          grid.OnBlockAdded -= OnBlockAddRemove;
          grid.OnBlockRemoved -= OnBlockAddRemove;
          grid.OnGridSplit -= OnGridSplit;
          grid.OnMarkForClose -= CloseGrid;
          grid.OnClosing -= CloseGrid;
          grid.OnClose -= CloseGrid;
          grid.PositionComp.OnPositionChanged -= OnGridPositionChanged;
          grid.OnStaticChanged -= Grid_OnStaticChanged;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.UnhookEventsForGrid: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    void UpdateMainGrid()
    {
      try
      {
        Ready = false;
        Dirty = true;

        var lastMainGrid = MainGrid;

        for (int i = GridCollection.Count - 1; i >= 0; i--)
        {
          var grid = GridCollection[i] as MyCubeGrid;
          if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.GridSizeEnum == MyCubeSize.Small)
            continue;

          if (MainGrid == null || MainGrid.MarkedForClose || grid.PositionComp.WorldAABB.Volume > MainGrid.PositionComp.WorldAABB.Volume)
            MainGrid = grid;
        }

        if (MainGrid == null || MainGrid.MarkedForClose)
        {
          Close();
        }
        else
        {
          if (lastMainGrid != null && lastMainGrid.EntityId != MainGrid.EntityId)
          {
            UnhookEventsForGrid(lastMainGrid);
          }

          UnhookEventsForGrid(MainGrid);
          HookEventsForGrid(MainGrid);
        }

      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.UpdateMainGrid: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void OnGridPositionChanged(MyPositionComponentBase positionComp)
    {
      try
      {
        if (!HasMoved && MainGrid != null && !MainGrid.MarkedForClose && !MainGrid.IsStatic)
        {
          var pos = MainGrid.WorldMatrix.Translation;
          if (Vector3D.DistanceSquared(_worldPosition, pos) < 1 && MainGrid.Physics.AngularVelocity.LengthSquared() < 0.001)
            return;

          HasMoved = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.OnGridPositionChanged: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    /// <summary>
    /// Checks if a given position is being repaired by a different repair bot
    /// </summary>
    /// <param name="gridEntityId">The EntityId of the grid</param>
    /// <param name="position">Block position on the grid</param>
    /// <param name="botId">Entity Id of the asking repair bot</param>
    /// <returns>true if a DIFFERENT bot is currently planning to repair the given block, otherwise false</returns>
    public bool IsTileBeingRepaired(long gridEntityId, Vector3I position, long botId)
    {
      ConcurrentDictionary<Vector3I, long> repairDict;
      if (SelectedRepairTiles.TryGetValue(gridEntityId, out repairDict))
      {
        long id;
        return repairDict.TryGetValue(position, out id) && id == botId;
      }

      return false;
    }

    /// <summary>
    /// Adds the bots repair target to the dictionary to keep other bots from trying to repair the same block
    /// </summary>
    /// <param name="gridEntityId">The EntityId of the grid</param>
    /// <param name="position">Block position on the grid</param>
    /// <param name="botId">Entity Id of the asking repair bot</param>
    /// <param name="botRepairTiles">The repair bot's repair dictionary</param>
    public void AddRepairTile(long gridEntityId, Vector3I position, long botId, Dictionary<long, HashSet<Vector3I>> botRepairTiles)
    {
      ConcurrentDictionary<Vector3I, long> repairDict;
      if (!SelectedRepairTiles.TryGetValue(gridEntityId, out repairDict))
      {
        repairDict = new ConcurrentDictionary<Vector3I, long>(Vector3I.Comparer);
        SelectedRepairTiles[gridEntityId] = repairDict;
      }

      repairDict[position] = botId;

      HashSet<Vector3I> tiles;
      if (!botRepairTiles.TryGetValue(gridEntityId, out tiles))
      {
        tiles = new HashSet<Vector3I>(Vector3I.Comparer);
        botRepairTiles[gridEntityId] = tiles;
      }

      tiles.Add(position);
    }

    /// <summary>
    /// Clears all repair tiles for the a bot
    /// </summary>
    /// <param name="tiles">The repair bot's current repair dictionary</param>
    public void RemoveRepairTiles(Dictionary<long, HashSet<Vector3I>> tiles)
    {
      ConcurrentDictionary<Vector3I, long> repairDict;

      foreach (var kvp in tiles)
      {
        if (SelectedRepairTiles.TryGetValue(kvp.Key, out repairDict))
        {
          foreach (var tile in kvp.Value)
          {
            long id;
            repairDict.TryRemove(tile, out id);
          }

          kvp.Value.Clear();
        }
      }
    }

    public void ResetMovement()
    {
      _worldPosition = MainGrid.WorldMatrix.Translation;
      WorldMatrix.Translation = _worldPosition;
      HasMoved = false;

      if (RootVoxel != null && !RootVoxel.MarkedForClose)
      {
        using (RootVoxel.Pin())
        {
          var tuple = RootVoxel?.GetVoxelContentInBoundingBox_Fast(BoundingBox, WorldMatrix, true) ?? MyTuple.Create(0f, 0f);

          if (tuple.Item1 > 0 || tuple.Item2 > 0)
          {
            Dirty = true;
          }
        }
      }
    }

    private void CloseGrid(MyEntity obj)
    {
      try
      {
        Ready = false;
        Dirty = true;

        var myGrid = obj as MyCubeGrid;
        if (myGrid != null)
        {
          myGrid.OnBlockAdded -= OnBlockAddRemove;
          myGrid.OnBlockRemoved -= OnBlockAddRemove;
          myGrid.OnGridSplit -= OnGridSplit;
          myGrid.OnMarkForClose -= CloseGrid;
          myGrid.OnClosing -= CloseGrid;
          myGrid.OnClose -= CloseGrid;
          myGrid.PositionComp.OnPositionChanged -= OnGridPositionChanged;

          InvokePositionsRemoved(null, true);
        }
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.CloseGrid: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void RemovePlanetTiles()
    {
      if (!_asyncTileTask.IsComplete)
        return;

      if (_asyncTileTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during RemovePlanetTiles task!\n");
        foreach (var ex in _asyncTileTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

        AiSession.Instance.Logger.LogAll();
        MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
      }

      if (!GraphLocked)
      {
        Ready = false;
        GraphLocked = true;
        PlanetTilesRemoved = true;

        _asyncTileTask = MyAPIGateway.Parallel.Start(RemovePlanetTilesAsync, SetReady);
      }
    }

    void RemovePlanetTilesAsync()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.RemovePlanetTilesAsync: Start");
        var iter = new Vector3I_RangeIterator(ref BoundingBox.Min, ref BoundingBox.Max);

        while (iter.IsValid())
        {
          if (Dirty)
            return;

          var localPoint = iter.Current;
          iter.MoveNext();

          if (OpenTileDict.ContainsKey(localPoint))
            continue;

          // TODO: This doesn't look right... updating tiles, not removing them??
          // think this is so bots can still fly around while grid is moving!

          Node pn;
          if (!PlanetTileDictionary.TryGetValue(localPoint, out pn) || pn == null)
          {
            if (!AiSession.Instance.NodeStack.TryPop(out pn) || pn == null)
              pn = new Node();
          }

          pn.Update(localPoint, Vector3.Zero, this, NodeType.GridPlanet, 0, MainGrid);
        }

        foreach (var kvp in PlanetTileDictionary)
        {
          GetBlockedNodeEdges(kvp.Value);
        }

        //AiSession.Instance.Logger.Log($"{this}.RemovePlanetTilesAsync: Finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.RemovePlanetTilesAsync: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    private void OnBlockAddRemove(IMySlimBlock obj)
    {
      var grid = obj?.CubeGrid as MyCubeGrid;

      if (MainGrid == null || MainGrid.MarkedForClose)
      {
        Dirty = true;
      }
      else if (grid != null && !grid.MarkedForClose && !grid.IsPreview && grid.EntityId == MainGrid.EntityId)
      {
        // Only care about main grid because all subgrids are now obstacles

        var blockPos = obj.Position;
        _blockUpdateHash.Add(blockPos);

        NeedsBlockUpdate = true;
      }
    }

    private void OnGridSplit(MyCubeGrid originalGrid, MyCubeGrid newGrid)
    {
      if (MainGrid == null || MainGrid.MarkedForClose
        || originalGrid?.EntityId == MainGrid.EntityId
        || newGrid?.EntityId == MainGrid.EntityId)
      {
        Dirty = true;
      }
    }

    public bool IsInBufferZone(Vector3D botPosition)
    {
      var allExt = OBB.HalfExtent;
      var ext = UnbufferedOBB.HalfExtent;
      var extra = (allExt - ext) * 0.75;

      var excludeOBB = new MyOrientedBoundingBoxD(UnbufferedOBB.Center, ext + extra, UnbufferedOBB.Orientation);
      return OBB.Contains(ref botPosition) && !excludeOBB.Contains(ref botPosition);
    }

    public override void Refresh() => Init();

    internal override void Init()
    {
      if (GraphLocked || !IsValid)
        return;

      GraphLocked = true;
      InteriorNodesReady = false;
      Ready = false;
      Dirty = false;
      ObstacleNodes.Clear();
      AllDoors.Clear();
      BlockedDoors.Clear();
      ExemptNodesUpper.Clear();
      ExemptNodesSide.Clear();
      WeaponPositions.Clear();
      InvokePositionsRemoved(null, false);

      if (AiSession.Instance?.NodeStack != null)
      {
        foreach (var kvp in OpenTileDict)
        {
          AiSession.Instance.NodeStack.Push(kvp.Value);
        }

        foreach (var kvp in PlanetTileDictionary)
        {
          AiSession.Instance.NodeStack.Push(kvp.Value);
        }
      }

      OpenTileDict.Clear();
      PlanetTileDictionary.Clear();

      IsGridGraph = MainGrid != null && !MainGrid.MarkedForClose;
      if (IsGridGraph)
        MyAPIGateway.Parallel.StartBackground(InitGridArea, SetReady);

      // Testing only!
      //if (IsGridGraph)
      //{
      //  InitGridArea();
      //  SetReady();
      //}
    }

    public override Vector3D? GetBufferZoneTargetPosition(Vector3D fromPosition, Vector3D toPosition, bool getEdgePoint = false)
    {
      if (IsInBufferZone(fromPosition))
        return fromPosition;

      var line = new LineD(fromPosition, toPosition);
      var num = OBB.Intersects(ref line);
      if (!num.HasValue)
      {
        return null;
      }

      var point = fromPosition + line.Direction * num.Value;

      if (!getEdgePoint)
        point += line.Direction * CellSize * 5;

      return point;
    }

    public override Vector3D? GetBufferZoneTargetPositionCentered(Vector3D fromPosition, Vector3D toPosition, Vector3D sideNormal, bool getEdgePoint = false)
    {
      if (IsInBufferZone(fromPosition))
        return fromPosition;

      var dSqd = Vector3D.DistanceSquared(fromPosition, toPosition);
      fromPosition = toPosition + sideNormal * dSqd;

      var line = new LineD(fromPosition, toPosition);
      var num = OBB.Intersects(ref line);
      if (!num.HasValue)
        return null;

      var point = fromPosition + line.Direction * num.Value;

      if (!getEdgePoint)
        point += line.Direction * 10;

      return point;
    }


    public override Vector3D LocalToWorld(Vector3I localVector) => MainGrid?.GridIntegerToWorld(localVector) ?? Vector3D.PositiveInfinity;

    public override Vector3I WorldToLocal(Vector3D worldVector) => MainGrid?.WorldToGridInteger(worldVector) ?? Vector3I.MaxValue;

    public override bool InBounds(Vector3I node) => BoundingBox.Contains(node) != ContainmentType.Disjoint;

    public override bool GetClosestValidNode(BotBase bot, Vector3I testPosition, out Vector3I localPosition, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true)
    {
      localPosition = testPosition;
      if (!IsValid || !Ready)
        return false;

      Node node;
      TryGetNodeForPosition(testPosition, out node);

      if (bot != null && node != null && !currentIsDenied && !IsObstacle(testPosition, bot, true) && !TempBlockedNodes.ContainsKey(localPosition))
      {
        var isAir = node.IsAirNode;
        var isWater = node.IsWaterNode;
        if ((bot.RequiresJetpack || !preferGroundNode || !isAir) && (allowAirNodes || !isAir)
          && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes)
          && (!bot.WaterNodesOnly || isWater) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
        { 

          if (isSlimBlock)
          {
            var cube = node.Block ?? GetBlockAtPosition(testPosition);
            if (cube == null || (cube.FatBlock is IMyTextPanel)
              || (cube.BlockDefinition as MyCubeBlockDefinition)?.HasPhysics != true
              || AiSession.Instance.PassageBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.CatwalkBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.RailingBlockDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || AiSession.Instance.FlatWindowDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || cube.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large"))
            {
              return true;
            }
          }
          else
            return true;
        }
      }

      if (isSlimBlock)
      {
        var block = node?.Block ?? GetBlockAtPosition(localPosition);

        if (node?.IsGridNodeUnderGround == true)
        {
          var grid = block?.CubeGrid ?? node.Grid;
          if (grid != null)
          {
            var upDir = grid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
            var intVec = Base6Directions.GetIntVector(upDir);

            localPosition = testPosition + intVec;

            if (PointInsideVoxel(localPosition, RootVoxel))
            {
              localPosition = testPosition - intVec;
            }
          }
        }

        if (block != null && !block.IsDestroyed)
        {
          var min = block.Min;
          var max = block.Max;

          if ((max - min) != Vector3I.Zero)
          {
            if (block.CubeGrid.EntityId != MainGrid.EntityId)
            {
              var minWorld = block.CubeGrid.GridIntegerToWorld(block.Min);
              var maxWorld = block.CubeGrid.GridIntegerToWorld(block.Max);
              min = MainGrid.WorldToGridInteger(minWorld);
              max = MainGrid.WorldToGridInteger(maxWorld);
            }

            Vector3I.MinMax(ref min, ref max);
            Vector3I_RangeIterator iter = new Vector3I_RangeIterator(ref min, ref max);

            while (iter.IsValid())
            {
              var current = iter.Current;
              iter.MoveNext();

              //if (current != block.Position && TryGetNodeForPosition(current, out node) && node != null)
              //{
              //  if (allowAirNodes || !node.IsAirNode) 
              //    return true;
              //}

              if (GetClosestNodeInternal(bot, current, out localPosition, up, isSlimBlock, currentIsDenied, allowAirNodes, preferGroundNode))
                return true;
            }

            return false;
          }
        }
      }

      return GetClosestNodeInternal(bot, testPosition, out localPosition, up, isSlimBlock, currentIsDenied, allowAirNodes, preferGroundNode);
    }

    public bool CanBotUseTile(BotBase bot, Node node, bool allowAirNodes = true)
    {
      if (node == null || bot?.Character == null || bot.IsDead)
        return false;

      var isAir = node.IsAirNode;
      var isWater = node.IsWaterNode;
      if ((allowAirNodes || !isAir) && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
      {
        return !bot.WaterNodesOnly || !isWater;
      }

      return false;
    }

    bool GetClosestNodeInternal(BotBase bot, Vector3I testPosition, out Vector3I localPosition, Vector3D? up = null, bool isSlimBlock = false, bool currentIsDenied = false, bool allowAirNodes = true, bool preferGroundNode = true)
    {
      localPosition = testPosition;
      Node node;

      if (!currentIsDenied
        && !IsObstacle(localPosition, bot, true)
        && TryGetNodeForPosition(localPosition, out node))
      {
        var isAir = node.IsAirNode;
        var isWater = node.IsWaterNode;
        if ((bot.RequiresJetpack || !preferGroundNode || !isAir) && (allowAirNodes || !isAir)
          && (!isWater || bot.CanUseWaterNodes) && (!isAir || bot.CanUseAirNodes)
          && (!bot.WaterNodesOnly || isWater) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this)))
        {

          if (isSlimBlock)
          {
            var cube = node.Block ?? GetBlockAtPosition(testPosition);
            if (cube == null || (cube.FatBlock is IMyTextPanel)
              || (cube.BlockDefinition as MyCubeBlockDefinition)?.HasPhysics != true
              || AiSession.Instance.PassageBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.CatwalkBlockDefinitions.Contains(cube.BlockDefinition.Id)
              || AiSession.Instance.RailingBlockDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || AiSession.Instance.FlatWindowDefinitions.ContainsItem(cube.BlockDefinition.Id)
              || cube.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large"))
            {
              return true;
            }
          }
          else
            return true;
        }
      }

      var rBot = bot as RepairBot;
      var slimTgt = rBot?.Target.Entity as IMySlimBlock;
      bool checkRepairInfo = slimTgt?.Position == testPosition;

      List<MyEntity> entList;
      if (!AiSession.Instance.EntListStack.TryPop(out entList) || entList == null)
        entList = new List<MyEntity>();

      var center = localPosition;
      double localDistance = double.MaxValue;
      double groundDistance = double.MaxValue;
      var worldPosition = MainGrid.GridIntegerToWorld(localPosition);
      Vector3I? closestGround = null;

      foreach (var point in Neighbors(bot, center, center, worldPosition, true, true, isSlimBlock, up, checkRepairInfo: checkRepairInfo))
      {
        TryGetNodeForPosition(point, out node);
        var isAirNode = node.IsAirNode;
        var isWaterNode = node.IsWaterNode;

        if (!allowAirNodes && isAirNode && !bot.RequiresJetpack)
          continue;

        KeyValuePair<IMyCubeGrid, bool> kvp;
        if (ObstacleNodes.TryGetValue(point, out kvp))
        {
          if (!checkRepairInfo || (kvp.Key?.EntityId == slimTgt.CubeGrid.EntityId && !kvp.Value))
            continue;

          //entList.Clear();
          //var worldPoint = LocalToWorld(point);
          //var sphere = new BoundingSphereD(worldPoint, 0.2);
          //MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

          //bool allowed = true;
          //for (int i = 0; i < entList.Count; i++)
          //{
          //  var grid = entList[i] as MyCubeGrid;
          //  if (grid != null)
          //  {
          //    var gridLocal = grid.WorldToGridInteger(worldPoint);
          //    var cube = grid.GetCubeBlock(gridLocal) as IMySlimBlock;
          //    if (cube != null && ((MyCubeBlockDefinition)cube.BlockDefinition).HasPhysics)
          //    {
          //      allowed = false;
          //      break;
          //    }
          //  }
          //}

          //if (!allowed)
          //  continue;
        }
        else if (TempBlockedNodes.ContainsKey(point))
        {
          continue;
        }

        IMyDoor door;
        if (BlockedDoors.TryGetValue(point, out door) && door != null)
        {
          if (bot == null || bot.Target.Entity == null)
            continue;

          var ch = bot.Target.Entity as IMyCharacter;
          if (ch != null && ch == bot.Owner?.Character)
            continue;

          var grid = door.CubeGrid;
          var gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
          var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

          var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
          if (rel != MyRelationsBetweenPlayers.Enemies)
            continue;
        }

        if (bot != null && !isWaterNode && bot.WaterNodesOnly)
          continue;

        if (isSlimBlock)
        {
          var block = node.Block;
          if (block != null && (AiSession.Instance.RampBlockDefinitions.Contains(block.BlockDefinition.Id)
            || AiSession.Instance.SlopeBlockDefinitions.Contains(block.BlockDefinition.Id)))
          {
            continue;
          }
        }

        if (bot == null || ((!isAirNode || bot.CanUseAirNodes) && (!isWaterNode || bot.CanUseWaterNodes) && (bot.CanUseSpaceNodes || !node.IsSpaceNode(this))))
        {
          var testPositionWorld = MainGrid.GridIntegerToWorld(point);
          var dist = Vector3D.DistanceSquared(testPositionWorld, worldPosition);

          if (dist < localDistance)
          {
            localDistance = dist;
            localPosition = point;
          }

          if (preferGroundNode && !isAirNode && dist < groundDistance)
          {
            groundDistance = dist;
            closestGround = point;
          }
        }
      }

      entList.Clear();
      AiSession.Instance.EntListStack.Push(entList);

      if (preferGroundNode && closestGround.HasValue)
        localPosition = closestGround.Value;

      return localDistance < double.MaxValue;
    }

    public override IEnumerable<Vector3I> Neighbors(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, Vector3D? up = null, bool checkRepairInfo = false)
    {
      Vector3I upVec = Vector3I.Zero;
      var rBot = bot as RepairBot;
      var checkAdditionalTile = checkRepairInfo && (rBot.Target.Entity as IMySlimBlock)?.CubeGrid.GridSizeEnum == MyCubeSize.Small;

      if (up.HasValue)
      {
        var upDir = WorldMatrix.GetClosestDirection(up.Value);
        upVec = Base6Directions.GetIntVector(upDir);
      }

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref upVec) != 0)
        {
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, currentIsObstacle, isSlimBlock, checkRepairInfo))
        {
          yield return next;
        }

        if (checkAdditionalTile)
        {
          next = currentNode + dir * 2;
          if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, currentIsObstacle, isSlimBlock, checkRepairInfo))
          {
            yield return next;
          }
        }
      }

      bool isVoxelNode = false;
      Node node;
      if (TryGetNodeForPosition(currentNode, out node))
      {
        isVoxelNode = !node.IsGridNode || node.IsGridNodePlanetTile;
      }

      if (isSlimBlock)
      {
        if (node?.IsGridNodeUnderGround == true)
        {
          foreach (var dir in AiSession.Instance.VoxelMovementDirections)
          {
            if (dir.Dot(ref upVec) != 0)
            {
              continue;
            }

            var next = currentNode + dir;
            if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, currentIsObstacle, isSlimBlock, checkRepairInfo))
            {
              yield return next;
            }
          }
        }

        yield break;
      }

      if (isVoxelNode || (currentIsObstacle && bot?.Target.IsFloater != true))
      {
        foreach (var dir in AiSession.Instance.DiagonalDirections)
        {
          if (dir.Dot(ref upVec) != 0)
          {
            continue;
          }

          var next = currentNode + dir;
          if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, currentIsObstacle, checkRepairInfo: checkRepairInfo))
          {
            yield return next;
          }
        }
      }

      if (!isVoxelNode || currentIsObstacle)
      {
        yield break;
      }

      foreach (var dir in AiSession.Instance.VoxelMovementDirections)
      {
        if (dir.Dot(ref upVec) != 0)
        {
          continue;
        }

        var next = currentNode + dir;
        if (InBounds(next) && Passable(bot, previousNode, currentNode, next, worldPosition, checkDoors, checkRepairInfo: checkRepairInfo))
        {
          yield return next;
        }
      }
    }

    public override bool Passable(BotBase bot, Vector3I previousNode, Vector3I currentNode, Vector3I nextNode, Vector3D worldPosition, bool checkDoors, bool currentIsObstacle = false, bool isSlimBlock = false, bool checkRepairInfo = false)
    {
      IMyDoor door;
      if (checkDoors && BlockedDoors.TryGetValue(nextNode, out door) && door != null)
      {
        if (bot == null || bot.Target.Entity == null)
          return false;

        var ch = bot.Target.Entity as IMyCharacter;
        if (ch != null && ch == bot.Owner?.Character)
          return false;

        var grid = door.CubeGrid;
        var gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
        var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

        var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
        if (rel != MyRelationsBetweenPlayers.Enemies)
          return false;
      }

      if (TempBlockedNodes.ContainsKey(nextNode) || bot?._pathCollection?.Obstacles.ContainsKey(nextNode) == true)
      {
        return false;
      }

      KeyValuePair<IMyCubeGrid, bool> kvp;
      bool obstacle = ObstacleNodes.TryGetValue(nextNode, out kvp);
      if (!checkRepairInfo)
      {
        if (obstacle)
          return false;
      }
      else if (obstacle && !kvp.Value)
      {
        return false;
      }

      Node nNext;
      if (!TryGetNodeForPosition(nextNode, out nNext))
      {
        return false;
      }

      var nextIsWater = nNext.IsWaterNode;
      if ((nextIsWater && !bot.CanUseWaterNodes) || (!nextIsWater && bot.WaterNodesOnly)
        || (nNext.IsAirNode && !bot.CanUseAirNodes) || (!bot.CanUseSpaceNodes && nNext.IsSpaceNode(this)))
      {
        return false;
      }

      if (isSlimBlock)
      {
        if (TempBlockedNodes.ContainsKey(currentNode) || (bot?.Target.IsFloater == true && DoesBlockExist(nextNode)))
        {
          return false;
        }

        if (RootVoxel != null && !nNext.IsGridNodeUnderGround)
        {
          Vector3D worldNext = MainGrid.GridIntegerToWorld(nNext.Position) + nNext.Offset;

          if (PointInsideVoxel(worldNext, RootVoxel))
            return false;
        }

        return true;
      }

      if (currentIsObstacle)
      {
        var castTo = LocalToWorld(nextNode);
        var castFrom = LocalToWorld(currentNode);
        var dirToNext = Vector3D.Normalize(castTo - castFrom);
        castFrom -= dirToNext * CellSize * 0.5;

        List<IHitInfo> hitInfoList;
        if (!AiSession.Instance.HitListStack.TryPop(out hitInfoList))
          hitInfoList = new List<IHitInfo>();
        else
          hitInfoList.Clear();

        MyAPIGateway.Physics.CastRay(castFrom, castTo, hitInfoList, CollisionLayers.CharacterCollisionLayer);

        var result = true;
        for (int i = 0; i < hitInfoList.Count; i++)
        {
          var hit = hitInfoList[i];
          var grid = hit?.HitEntity?.GetTopMostParent() as MyCubeGrid;
          if (grid != null)
          {
            if (grid.GridSizeEnum == MyCubeSize.Small)
            {
              var gridLocal = grid.WorldToGridInteger(castTo);
              if (grid.CubeExists(gridLocal))
              {
                result = false;
                break;
              }
            }
            else
            {
              result = false;
              break;
            }
          }
        }

        hitInfoList.Clear();
        AiSession.Instance.HitListStack.Push(hitInfoList);
        return result;
      }

      if (nNext.IsBlocked(currentNode - nextNode))
      {
        return false;
      }

      Node nCur;
      if (!TryGetNodeForPosition(currentNode, out nCur) || nCur.IsBlocked(nextNode - currentNode))
      {
        return false;
      }

      if (RootVoxel != null && !nCur.IsGridNodeUnderGround && !nNext.IsGridNodeUnderGround)
      {
        Vector3D worldCurrent = MainGrid.GridIntegerToWorld(nCur.Position) + nCur.Offset;
        Vector3D worldNext = MainGrid.GridIntegerToWorld(nNext.Position) + nNext.Offset;

        using (RootVoxel.Pin())
        {
          if (LineIntersectsVoxel(ref worldCurrent, ref worldNext, RootVoxel))
            return false;
        }
      }

      var movement = nextNode - currentNode;
      if (movement.RectangularLength() > 1)
      {
        Node testNode;
        if (movement.Y != 0)
        {
          var dirVec = new Vector3I(0, movement.Y, 0);
          var testPosition = currentNode + dirVec;

          if (DoesBlockExist(testPosition) == true || (TryGetNodeForPosition(testPosition, out testNode) && (testNode.Block != null || nCur.IsBlocked(dirVec) || testNode.IsBlocked(-dirVec))))
          {
            return false;
          }
        }

        if (movement.X != 0)
        {
          var dirVec = new Vector3I(movement.X, 0, 0);
          var testPosition = currentNode + dirVec;

          if (DoesBlockExist(testPosition) == true || (TryGetNodeForPosition(testPosition, out testNode) && (testNode.Block != null || nCur.IsBlocked(dirVec) || testNode.IsBlocked(-dirVec))))
          {
            return false;
          }
        }

        if (movement.Z != 0)
        {
          var dirVec = new Vector3I(0, 0, movement.Z);
          var testPosition = currentNode + dirVec;

          if (DoesBlockExist(testPosition) == true || (TryGetNodeForPosition(testPosition, out testNode) && (testNode.Block != null || nCur.IsBlocked(dirVec) || testNode.IsBlocked(-dirVec))))
          {
            return false;
          }
        }
      }

      Vector3 totalMovement = nextNode - previousNode;
      Vector3 botUpVec = Base6Directions.GetIntVector(MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up));

      var checkVec = Vector3I.Round(Vector3.ProjectOnVector(ref totalMovement, ref botUpVec));
      var checkTotal = checkVec.RectangularLength();

      if (checkTotal > 1)
      {
        if (!nCur.IsGridNode && nNext.IsGridNode)
        {
          return false;
        }

        IMySlimBlock prevBlock = GetBlockAtPosition(previousNode);
        IMySlimBlock curBlock = GetBlockAtPosition(currentNode);
        bool usePrevCur = prevBlock != null && curBlock != null && prevBlock != curBlock;

        IMySlimBlock nextBlock = null;
        bool useCurNext = false;
        if (!usePrevCur || checkTotal < 0)
        {
          nextBlock = GetBlockAtPosition(nextNode);
          useCurNext = curBlock != null && nextBlock != null && curBlock != nextBlock;
          usePrevCur = usePrevCur && !useCurNext;
        }

        if (usePrevCur || useCurNext)
        {
          IMySlimBlock from = usePrevCur ? prevBlock : curBlock;
          IMySlimBlock to = usePrevCur ? curBlock : nextBlock;

          if (AiSession.Instance.HalfStairBlockDefinitions.Contains(from.BlockDefinition.Id)
            && AiSession.Instance.HalfStairBlockDefinitions.Contains(to.BlockDefinition.Id))
          {
            Vector3I insertPosition;
            if (!GetValidPositionForStackedStairs(currentNode, out insertPosition))
            {
              return false;
            }

            bot._pathCollection.StackedStairsFound.Enqueue(MyTuple.Create(previousNode, currentNode, insertPosition));
          }
        }
      }
      else if (checkTotal > 0 && !nCur.IsGridNode && nNext.IsGridNode)
      {
        return false;
      }

      return true;
    }

    public bool GetValidPositionForStackedStairs(Vector3I stairPosition, out Vector3I adjusted)
    {
      var botUp = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(botUp);
      Vector3I center = stairPosition;

      foreach (var dir in AiSession.Instance.CardinalDirections)
      {
        if (dir.Dot(ref upVec) != 0)
          continue;

        adjusted = center + dir;
        if (!BlockedDoors.ContainsKey(adjusted) && !ObstacleNodes.ContainsKey(adjusted) && !TempBlockedNodes.ContainsKey(adjusted) && IsOpenTile(adjusted))
        {
          Node node;
          if (TryGetNodeForPosition(center, out node) && !node.IsBlocked(dir))
            return true;
        }
      }

      adjusted = Vector3I.MaxValue;
      return false;
    }

    void InitGridArea()
    {
      try
      {
        if (AiSession.Instance == null || !AiSession.Instance.Registered)
          return;

        //AiSession.Instance.Logger.Log($"{this}.InitGridArea: Start");

        ApiWorkData data;
        if (!AiSession.Instance.ApiWorkDataStack.TryPop(out data) || data == null)
          data = new ApiWorkData();

        data.Grid = MainGrid;
        data.NodeList = InteriorNodeList;
        data.EnclosureRating = 5;
        data.AirtightNodesOnly = false;
        data.AllowAirNodes = false;
        data.CallBack = null;

        BotFactory.GetInteriorNodes(data);
        AiSession.Instance.ApiWorkDataStack.Push(data);
        InteriorNodesReady = true;

        List<IMySlimBlock> blocks;
        if (!AiSession.Instance.SlimListStack.TryPop(out blocks) || blocks == null)
          blocks = new List<IMySlimBlock>();
        else
          blocks.Clear();

        Base6Directions.Direction upDir;
        Vector3I upVec;

        var cellSize = CellSize;
        var halfCellSize = CellSize * 0.5f;

        BoundingBox = GetMapBoundingBoxLocal();
        upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        upVec = Base6Directions.GetIntVector(upDir);

        foreach (var fatblock in MainGrid.GetFatBlocks())
        {
          if (fatblock is IMyUserControllableGun)
          {
            WeaponPositions.Add(fatblock.Position);
          }
        }

        blocks.Clear();
        ((IMyCubeGrid)MainGrid).GetBlocks(blocks);

        foreach (var block in blocks)
        {
          if (Dirty)
            break;

          if (block == null || block.IsDestroyed)
            continue;

          var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
          if (cubeDef == null || !cubeDef.HasPhysics || block.FatBlock is IMyButtonPanel || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_MotorRotor))
            continue;

          if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeDef.Id))
            continue;

          InitBlock(block, ref upVec, ref upDir, ref cellSize, ref halfCellSize, cubeDef);
        }

        blocks.Clear();
        AiSession.Instance.SlimListStack.Push(blocks);

        if (Dirty)
          return;

        var worldCenter = LocalToWorld(BoundingBox.Center);
        var quat = Quaternion.CreateFromRotationMatrix(MainGrid.WorldMatrix);
        var halfVector = Vector3D.Half * cellSize;
        UnbufferedOBB = new MyOrientedBoundingBoxD(worldCenter, BoundingBox.HalfExtents * cellSize + halfVector, quat);

        BoundingBox.Inflate(_boxExpansion);
        OBB = new MyOrientedBoundingBoxD(worldCenter, BoundingBox.HalfExtents * cellSize + halfVector, quat);

        List<MyVoxelBase> voxelMaps;
        if (!AiSession.Instance.VoxelMapListStack.TryPop(out voxelMaps) || voxelMaps == null)
          voxelMaps = new List<MyVoxelBase>();
        else
          voxelMaps.Clear();

        var obbCenter = OBB.Center;
        var distanceToOBBCenter = double.MaxValue;

        float _;
        Vector3 gravityNorm;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(obbCenter, out _);
        if (nGrav.LengthSquared() > 0)
        {
          RootVoxel = MyGamePruningStructure.GetClosestPlanet(WorldMatrix.Translation);
          gravityNorm = Vector3.Normalize(nGrav);
          distanceToOBBCenter = Vector3D.DistanceSquared(RootVoxel.PositionComp.WorldAABB.Center, obbCenter);

          var aabb = OBB.GetAABB();
          MyGamePruningStructure.GetAllVoxelMapsInBox(ref aabb, voxelMaps);
          if (voxelMaps.Count > 0)
          {
            MyVoxelBase rootVoxel = null;
            for (int i = 0; i < voxelMaps.Count; i++)
            {
              var voxel = voxelMaps[i]?.RootVoxel;
              if (voxel != null && voxel.EntityId != rootVoxel?.EntityId)
              {
                var dSquared = Vector3D.DistanceSquared(voxel.PositionComp.WorldAABB.Center, obbCenter);
                if (dSquared < distanceToOBBCenter)
                {
                  distanceToOBBCenter = dSquared;
                  rootVoxel = voxel;
                }
              }
            }

            if (rootVoxel != null)
            {
              RootVoxel = rootVoxel;

              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
              if (aGrav.LengthSquared() > 0)
                gravityNorm = Vector3D.Normalize(aGrav);
            }
          }
        }
        else
        {
          var aabb = OBB.GetAABB();
          MyGamePruningStructure.GetAllVoxelMapsInBox(ref aabb, voxelMaps);
          if (voxelMaps.Count > 0)
          {
            MyVoxelBase rootVoxel = null;
            for (int i = 0; i < voxelMaps.Count; i++)
            {
              var voxel = voxelMaps[i]?.RootVoxel;
              if (voxel != null && voxel.EntityId != rootVoxel?.EntityId)
              {
                var dSquared = Vector3D.DistanceSquared(voxel.PositionComp.WorldAABB.Center, obbCenter);
                if (dSquared < distanceToOBBCenter)
                {
                  distanceToOBBCenter = dSquared;
                  rootVoxel = voxel;
                }
              }
            }

            if (rootVoxel != null)
            {
              RootVoxel = rootVoxel;

              var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
              if (aGrav.LengthSquared() > 0)
                gravityNorm = Vector3D.Normalize(aGrav);
              else
                gravityNorm = (Vector3)WorldMatrix.Down;
            }
            else
            {
              RootVoxel = null;
              gravityNorm = (Vector3)WorldMatrix.Down;
            }
          }
          else
          {
            RootVoxel = null;
            gravityNorm = (Vector3)WorldMatrix.Down;
          }
        }

        voxelMaps.Clear();
        AiSession.Instance.VoxelMapListStack.Push(voxelMaps);

        upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        upVec = Base6Directions.GetIntVector(upDir);
        CheckForPlanetTiles(ref BoundingBox, ref gravityNorm, ref upVec);
        PlanetTilesRemoved = false;

        foreach (var kvp in OpenTileDict)
        {
          if (Dirty)
            return;

          var tile = kvp.Value;
          if (tile.IsGridNodeAdditional)
            continue;

          GetBlockedNodeEdges(tile);

          var worldNode = MainGrid.GridIntegerToWorld(tile.Position);
          if (PointInsideVoxel(worldNode, RootVoxel))
          {
            tile.SetNodeType(NodeType.GridUnderground);

            if (tile.Offset == Vector3.Zero)
              tile.Offset = WorldMatrix.Up * halfCellSize;
          }
        }

        foreach (var kvp in PlanetTileDictionary)
        {
          if (Dirty)
            return;

          var tile = kvp.Value;
          GetBlockedNodeEdges(tile);
        }

        //if (!MainGrid.Physics.IsStatic)
        //  AddAdditionalGridTiles();

        //AiSession.Instance.Logger.Log($"{this}.InitGridArea: Finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception during InitGridArea: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    void InitBlock(IMySlimBlock block, ref Vector3I upVec, ref Base6Directions.Direction upDir, ref float cellSize, ref float halfCellSize, MyCubeBlockDefinition cubeDef)
    {

      CheckFaces(block, upVec, cubeDef);

      var position = block.Position;
      var mainGridPosition = position;
      //bool isDigiLadder = block.BlockDefinition.Id.SubtypeName.StartsWith("LargeShipUsableLadder");

      var door = block.FatBlock as IMyDoor;
      if (door != null)
      {
        if (door.MarkedForClose)
          return;

        AllDoors[mainGridPosition] = door;

        if (!door.Enabled && door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Open)
          Door_EnabledChanged(door);

        door.EnabledChanged -= Door_EnabledChanged;
        door.EnabledChanged += Door_EnabledChanged;
        door.IsWorkingChanged -= Door_EnabledChanged;
        door.IsWorkingChanged += Door_EnabledChanged;
        door.OnDoorStateChanged -= Door_OnDoorStateChanged;
        door.OnDoorStateChanged += Door_OnDoorStateChanged;
        door.OnMarkForClose -= Door_OnMarkForClose;
        door.OnMarkForClose += Door_OnMarkForClose;

        //if (isDigiLadder)
        //{
        //  // Retractable ladder is a door block
        //  if (block.Orientation.Up == upDir && !OpenTileDict.ContainsKey(mainGridPosition))
        //  {
        //    Node node;
        //    if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
        //      node = new Node();

        //    node.Update(mainGridPosition, Vector3.Zero, NodeType.Ground, 0, MainGrid, block);
        //    OpenTileDict[mainGridPosition] = node;

        //    var blockUp = Base6Directions.GetIntVector(block.Orientation.Up);
        //    if (blockUp.Dot(ref upVec) != 0)
        //    {
        //      var positionAbove = position + upVec;
        //      var cubeAbove = GetBlockAtPosition(positionAbove);
        //      if (cubeAbove == null)
        //      {
        //        var newPos = mainGridPosition + upVec;

        //        Node newNode;
        //        if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
        //          newNode = new Node();

        //        newNode.Update(newPos, Vector3.Zero, NodeType.Ground, 0, MainGrid);
        //        OpenTileDict[newPos] = newNode;
        //      }
        //    }
        //  }
        //}
        if (door is IMyAirtightHangarDoor) // TODO: Possibly - check block groups for this door and open others with it? Maybe check at open time?
        {
          if (block.Orientation.Up == upDir)
          {
            for (int j = 1; j < cubeDef.Size.Y; j++)
            {
              var newPos = mainGridPosition - upVec * j;

              if (!OpenTileDict.ContainsKey(newPos))
              {
                Node newNode;
                if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
                  newNode = new Node();

                newNode.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
                OpenTileDict[newPos] = newNode;
              }
            }
          }
          else if (block.Orientation.Up == Base6Directions.GetOppositeDirection(upDir))
          {
            for (int j = 1; j < cubeDef.Size.Y; j++)
            {
              var newPos = mainGridPosition + upVec * j;

              if (j == 1 || !OpenTileDict.ContainsKey(newPos))
              {
                Node newNode;
                if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
                  newNode = new Node();

                newNode.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 

                if (j == 1)
                  newNode.SetNodeType(NodeType.Ground);

                OpenTileDict[newPos] = newNode;
              }
            }
          }
        }
        else if (door.BlockDefinition.SubtypeName.StartsWith("SlidingHatchDoor"))
        {
          // Warfare 2 update

          var newPos = door.Position;

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block);
            OpenTileDict[newPos] = node;
          }
        }
        else if (door.BlockDefinition.SubtypeName == "LargeBlockGate")
        {
          // Large DLC gate
          var center = door.WorldAABB.Center;
          var doorMatrix = door.WorldMatrix;
          var pos = center + doorMatrix.Down * halfCellSize;
          var newPos = MainGrid.WorldToGridInteger(pos);

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block);
            OpenTileDict[newPos] = node;
          }

          pos += door.WorldMatrix.Left * cellSize;
          newPos = MainGrid.WorldToGridInteger(pos);

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
            OpenTileDict[newPos] = node;
          }

          pos += door.WorldMatrix.Up * cellSize;
          newPos = MainGrid.WorldToGridInteger(pos);

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
            OpenTileDict[newPos] = node;
          }

          pos += door.WorldMatrix.Right * cellSize;
          newPos = MainGrid.WorldToGridInteger(pos);

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
            OpenTileDict[newPos] = node;
          }

          pos += door.WorldMatrix.Right * cellSize;
          newPos = MainGrid.WorldToGridInteger(pos);

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
            OpenTileDict[newPos] = node;
          }

          pos += door.WorldMatrix.Down * cellSize;
          newPos = MainGrid.WorldToGridInteger(pos);

          if (!OpenTileDict.ContainsKey(newPos))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(newPos, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
            OpenTileDict[newPos] = node;
          }
        }
        else // if (block.Orientation.Up == upDir)
        {
          if (!OpenTileDict.ContainsKey(mainGridPosition))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
      }
      else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(cubeDef.Id))
      {
        if (AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(cubeDef.Id))
        {
          if (upDir == block.Orientation.Left)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
        else if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeDef.Id))
        {
          if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref upVec) == 0)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
        else if (AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(cubeDef.Id))
        {
          if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref upVec) == 0)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;

            var newPos = mainGridPosition + upVec;
            var cubeAbove = GetBlockAtPosition(newPos);
            var includeAbove = cubeAbove == null || cubeAbove.BlockDefinition.Id == block.BlockDefinition.Id;

            if (!includeAbove)
            {
              var cubeAboveDef = cubeAbove.BlockDefinition.Id;
              var entrance = block.Orientation.Forward;
              var entrance2 = block.Orientation.Up;
              var entranceIsUp = entrance == upDir || entrance2 == upDir;

              if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef))
              {
                includeAbove = !CheckCatwalkForRails(cubeAbove, -upVec);
              }
              else if (AiSession.Instance.SlopeBlockDefinitions.Contains(cubeAboveDef))
              {
                var aboveOr = cubeAbove.Orientation;
                var downDir = Base6Directions.GetOppositeDirection(block.Orientation.Up);
                if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeAboveDef))
                {
                  if (!cubeAboveDef.SubtypeName.EndsWith("Slope2Base"))
                  {
                    if (entranceIsUp)
                    {
                      includeAbove = ((aboveOr.Up == entrance || aboveOr.Up == entrance2) && aboveOr.Forward == downDir)
                        || ((aboveOr.Forward == Base6Directions.GetOppositeDirection(entrance) || aboveOr.Forward == Base6Directions.GetOppositeDirection(entrance2)) && aboveOr.Up == upDir);

                      if (includeAbove)
                      {
                        ExemptNodesUpper.Add(newPos);
                      }
                    }
                    else // Up is up
                    {
                      includeAbove = (aboveOr.Up == downDir && (aboveOr.Forward == entrance || aboveOr.Forward == entrance2))
                        || (aboveOr.Forward == upDir && (aboveOr.Up == Base6Directions.GetOppositeDirection(entrance) || aboveOr.Up == Base6Directions.GetOppositeDirection(entrance2)));

                      if (includeAbove)
                      {
                        ExemptNodesUpper.Add(newPos);
                      }
                    }
                  }
                }
                else
                {
                  if (entranceIsUp)
                  {
                    includeAbove = ((aboveOr.Up == entrance || aboveOr.Up == entrance2) && aboveOr.Forward == downDir)
                      || ((aboveOr.Forward == entrance || aboveOr.Forward == entrance2) && aboveOr.Up == downDir);

                    if (includeAbove)
                    {
                      ExemptNodesUpper.Add(newPos);
                    }
                  }
                  else // Up is up
                  {
                    includeAbove = (aboveOr.Up == downDir && (aboveOr.Forward == entrance || aboveOr.Forward == entrance2))
                      || (aboveOr.Forward == downDir && (aboveOr.Up == entrance || aboveOr.Up == entrance2));

                    if (includeAbove)
                    {
                      ExemptNodesUpper.Add(newPos);
                    }
                  }
                }
              }
            }

            if (includeAbove)
            {
              Node newNode;
              if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
                newNode = new Node();

              var newOffset = cubeAbove != null ? Vector3.Zero : -(Vector3)MainGrid.WorldMatrix.GetDirectionVector(upDir);

              var worldPositionAbove = MainGrid.GridIntegerToWorld(newPos) + newOffset;
              if (GridBase.PointInsideVoxel(worldPositionAbove, RootVoxel))
                newOffset = Vector3.Zero;

              newNode.Update(newPos, newOffset, this, NodeType.Ground, 0, MainGrid, block);
              OpenTileDict[newPos] = newNode;
            }
          }
        }
      }
      else if (AiSession.Instance.PassageBlockDefinitions.Contains(cubeDef.Id))
      {
        var downDir = Base6Directions.GetOppositeDirection(upDir);
        if (cubeDef.Id.TypeId == typeof(MyObjectBuilder_Passage))
        {
          if (block.Orientation.Forward == upDir || block.Orientation.Forward == downDir)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block);
            OpenTileDict[mainGridPosition] = node;
          }
        }
        else if (!cubeDef.Id.SubtypeName.StartsWith("HalfPassageCorner"))
        {
          if (block.Orientation.Up == upDir || block.Orientation.Up == downDir)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
          else if (AiSession.Instance.PassageIntersectionDefinitions.Contains(cubeDef.Id))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
      }
      else if (AiSession.Instance.BeamBlockDefinitions.ContainsItem(cubeDef.Id))
      {
        var beamPosition = block.Position;

        var cubeAbove = GetBlockAtPosition(beamPosition + upVec);
        if (cubeAbove == null)
        {
          var blockSubtype = cubeDef.Id.SubtypeName;
          if (blockSubtype.EndsWith("Slope"))
          {
            if (block.Orientation.Forward == upDir || block.Orientation.Left == Base6Directions.GetOppositeDirection(upDir))
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (blockSubtype.EndsWith("TJunction") || blockSubtype.EndsWith("Tip") || blockSubtype.EndsWith("Base"))
          {
            if (block.Orientation.Left == upDir)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (block.Orientation.Left == upDir || block.Orientation.Left == Base6Directions.GetOppositeDirection(upDir))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
      }
      else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeDef.Id))
      {
        if (block.Orientation.Up == upDir)
        {
          Node node;
          if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
            node = new Node();

          node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
          OpenTileDict[mainGridPosition] = node;
        }
      }
      else if (AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id))
      {
        var positionAbove = position + upVec;
        var cubeAbove = GetBlockAtPosition(positionAbove);
        var entrance = Base6Directions.GetOppositeDirection(block.Orientation.Forward);
        bool cubeAboveEmpty = cubeAbove == null || !((MyCubeBlockDefinition)cubeAbove.BlockDefinition).HasPhysics;
        bool validHalfStair = false;
        bool includeAbove = false;
        bool entranceIsUp = entrance == upDir;
        bool upIsUp = !entranceIsUp && block.Orientation.Up == upDir;

        bool addThis = (upIsUp && cubeAboveEmpty)
          || (entranceIsUp && (cubeAboveEmpty || cubeAbove == block));

        if (!addThis)
        {
          if (cubeAboveEmpty && block.BlockDefinition.Id.SubtypeName.EndsWith("HalfSlopeArmorBlock"))
          {
            addThis = upIsUp || Base6Directions.GetOppositeDirection(upDir) == block.Orientation.Forward;
          }
          else if (block.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip"))
          {
            addThis = upDir == block.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == block.Orientation.Left;
          }

          if (!addThis)
          {
            if (!cubeAboveEmpty && (entranceIsUp || upIsUp))
            {
              var cubeAboveDef = cubeAbove.BlockDefinition.Id;

              if (AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id))
              {
                if (cubeAboveDef == cubeDef.Id && block.Orientation.Up == cubeAbove.Orientation.Up)
                  addThis = validHalfStair = (entrance == cubeAbove.Orientation.Forward);

                if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef))
                {
                  if (!addThis)
                    addThis = !CheckCatwalkForRails(cubeAbove, -upVec);

                  includeAbove = addThis;
                }
                else if (!addThis && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeAboveDef))
                {
                  var aboveOr = cubeAbove.Orientation;
                  var downDir = Base6Directions.GetOppositeDirection(block.Orientation.Up);
                  if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeAboveDef))
                  {
                    if (!cubeAboveDef.SubtypeName.EndsWith("Slope2Base"))
                    {
                      if (entranceIsUp)
                      {
                        addThis = (aboveOr.Up == entrance && aboveOr.Forward == downDir)
                          || (aboveOr.Forward == Base6Directions.GetOppositeDirection(entrance) && aboveOr.Up == upDir);
                        if (addThis)
                        {
                          includeAbove = true;
                          ExemptNodesUpper.Add(positionAbove);
                        }
                      }
                      else // Up is up
                      {
                        addThis = (aboveOr.Up == downDir && aboveOr.Forward == entrance)
                          || (aboveOr.Forward == upDir && aboveOr.Up == Base6Directions.GetOppositeDirection(entrance));
                        if (addThis)
                        {
                          includeAbove = true;
                          ExemptNodesUpper.Add(positionAbove);
                        }
                      }
                    }
                  }
                  else
                  {
                    if (entranceIsUp)
                    {
                      addThis = (aboveOr.Up == entrance && aboveOr.Forward == downDir) || (aboveOr.Forward == entrance && aboveOr.Up == downDir);
                      if (addThis)
                      {
                        includeAbove = true;
                        ExemptNodesUpper.Add(positionAbove);
                      }
                    }
                    else // Up is up
                    {
                      addThis = (aboveOr.Up == downDir && aboveOr.Forward == entrance) || (aboveOr.Forward == downDir && aboveOr.Up == entrance);
                      if (addThis)
                      {
                        includeAbove = true;
                        ExemptNodesUpper.Add(positionAbove);
                      }
                    }
                  }
                }
              }
              else if (AiSession.Instance.SlopeBlockDefinitions.Contains(cubeAboveDef))
              {
                var aboveOr = cubeAbove.Orientation;
                var downDir = Base6Directions.GetOppositeDirection(block.Orientation.Up);
                if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeAboveDef))
                {
                  if (!cubeAboveDef.SubtypeName.EndsWith("Slope2Base"))
                  {
                    if (entranceIsUp)
                    {
                      addThis = (aboveOr.Up == entrance && aboveOr.Forward == downDir)
                        || (aboveOr.Forward == Base6Directions.GetOppositeDirection(entrance) && aboveOr.Up == upDir);
                      if (addThis)
                      {
                        includeAbove = true;
                        ExemptNodesUpper.Add(positionAbove);
                      }
                    }
                    else // Up is up
                    {
                      addThis = (aboveOr.Up == downDir && aboveOr.Forward == entrance)
                        || (aboveOr.Forward == upDir && aboveOr.Up == Base6Directions.GetOppositeDirection(entrance));
                      if (addThis)
                      {
                        includeAbove = true;
                        ExemptNodesUpper.Add(positionAbove);
                      }
                    }
                  }
                }
                else
                {
                  if (entranceIsUp)
                  {
                    addThis = (aboveOr.Up == entrance && aboveOr.Forward == downDir) || (aboveOr.Forward == entrance && aboveOr.Up == downDir);
                    if (addThis)
                    {
                      includeAbove = true;
                      ExemptNodesUpper.Add(positionAbove);
                    }
                  }
                  else // Up is up
                  {
                    addThis = (aboveOr.Up == downDir && aboveOr.Forward == entrance) || (aboveOr.Forward == downDir && aboveOr.Up == entrance);
                    if (addThis)
                    {
                      includeAbove = true;
                      ExemptNodesUpper.Add(positionAbove);
                    }
                  }
                }
              }
              else if (AiSession.Instance.RampBlockDefinitions.Contains(cubeAboveDef))
              {
                // TODO: Is this needed?
              }
              else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef))
              {
                addThis = !CheckCatwalkForRails(cubeAbove, -upVec);
                includeAbove = addThis;
              }
            }

            if (!addThis)
            {
              if (cubeDef.Id.SubtypeName.IndexOf("BlockArmorSlope2") >= 0)
              {
                var blockOr = block.Orientation;
                var blockDown = Base6Directions.GetOppositeDirection(blockOr.Up);
                var blockBack = Base6Directions.GetOppositeDirection(blockOr.Forward);
                bool isBlockTip = cubeDef.Id.SubtypeName.EndsWith("Tip");

                if (entranceIsUp)
                {
                  addThis = true;
                  includeAbove = cubeAboveEmpty;

                  if (!includeAbove)
                  {
                    var aboveOr = cubeAbove.Orientation;
                    var aboveDef = cubeAbove.BlockDefinition.Id;
                    var aboveSubtype = aboveDef.SubtypeName;

                    if (AiSession.Instance.RampBlockDefinitions.Contains(cubeAbove.BlockDefinition.Id))
                    {
                      includeAbove = !isBlockTip && aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward;
                    }
                    else if (isBlockTip)
                    {
                      if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                      {
                        if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                        {
                          if (aboveSubtype.EndsWith("Tip"))
                          {
                            includeAbove = aboveOr.Forward == blockBack && aboveOr.Up == blockDown;
                          }
                        }
                        else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                        {
                          includeAbove = aboveOr.Up == blockOr.Forward || aboveOr.Forward == blockBack;
                        }
                        else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                        {
                          includeAbove = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                            || (aboveOr.Forward == blockOr.Up && aboveOr.Up == blockOr.Forward);
                        }
                        else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                        {
                          includeAbove = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                            || (aboveOr.Up == blockOr.Forward && aboveOr.Forward == blockOr.Up);
                        }
                      }
                    }
                    else // block base -> add adjacent
                    {
                      var adjacentPos = mainGridPosition + Base6Directions.GetIntVector(blockOr.Up);
                      var adjacentBlock = GetBlockAtPosition(adjacentPos);

                      if (adjacentBlock != null)
                      {
                        var adjacentDef = adjacentBlock.BlockDefinition.Id;
                        var adjacentOr = adjacentBlock.Orientation;
                        bool addAdjacent = false;

                        if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(adjacentDef))
                        {
                          addAdjacent = adjacentOr.Up == blockDown && (adjacentOr.Forward == blockBack || adjacentOr.Forward == blockOr.Forward);

                          if (!addAdjacent && !adjacentDef.SubtypeName.EndsWith("Tip"))
                          {
                            addAdjacent = adjacentOr.Forward == blockOr.Up && (adjacentOr.Up == blockBack || adjacentOr.Up == blockOr.Forward);
                          }
                        }
                        else if (AiSession.Instance.RampBlockDefinitions.Contains(adjacentDef))
                        {
                          addAdjacent = adjacentOr.Forward == blockOr.Forward && adjacentOr.Up == blockDown;
                        }
                        else if (AiSession.Instance.SlopeBlockDefinitions.Contains(adjacentDef))
                        {
                          if (adjacentDef.SubtypeName.EndsWith("ArmorHalfSlopedCorner"))
                          {
                            addAdjacent = adjacentOr.Left == blockDown || adjacentOr.Up == blockDown || adjacentOr.Forward == blockDown;
                          }
                        }

                        if (addAdjacent)
                        {
                          var node = new Node(adjacentPos, Vector3.Zero, this, MainGrid, adjacentBlock);
                          node.SetNodeType(NodeType.Ground);
                          OpenTileDict[adjacentPos] = node;
                          ExemptNodesSide.Add(adjacentPos);
                        }
                      }
                    }
                  }
                }
                else if (upIsUp && !cubeAboveEmpty)
                {
                  var aboveOr = cubeAbove.Orientation;
                  var aboveDef = cubeAbove.BlockDefinition.Id;
                  var aboveSubtype = aboveDef.SubtypeName;

                  if (!isBlockTip)
                  {
                    // block base is the tall side - less head room
                    if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(aboveDef))
                    {
                      if (aboveSubtype.EndsWith("ArmorSlope2Tip"))
                      {
                        addThis = aboveOr.Forward == blockBack && aboveOr.Up == blockDown;
                      }
                      else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                      {
                        addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                      }
                      else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                      {
                        addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                          || (aboveOr.Forward == blockOr.Up && aboveOr.Left == blockOr.Left);
                      }
                    }
                    else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                    {
                      if (aboveSubtype.EndsWith("HalfArmorBlock") || aboveSubtype.StartsWith("WindowWall"))
                      {
                        addThis = blockDown == Base6Directions.GetOppositeDirection(aboveOr.Forward);
                      }
                      else if (aboveSubtype.EndsWith("ArmorHalfSlopedCorner"))
                      {
                        addThis = aboveOr.Forward == blockDown || aboveOr.Left == blockDown || aboveOr.Up == blockDown;
                      }
                    }
                  }
                  else // slope tip
                  {
                    if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                    {
                      if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                      {
                        addThis = aboveOr.Up == blockDown;

                        if (addThis && aboveSubtype.EndsWith("Base"))
                        {
                          addThis &= aboveOr.Forward == blockBack;
                        }
                      }
                      else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                      {
                        addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                      }
                      else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                      {
                        addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockBack)
                          || (aboveOr.Forward == blockOr.Up && blockBack == Base6Directions.GetOppositeDirection(aboveOr.Up));
                      }
                      else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                      {
                        addThis = aboveOr.Up == blockDown || aboveOr.Forward == blockOr.Up;
                      }
                    }
                    else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                    {
                      bool isHalfSlopedCorner = aboveSubtype.EndsWith("ArmorHalfSlopedCorner") || aboveSubtype.EndsWith("Concrete_Half_Block_Slope");
                      if (isHalfSlopedCorner || aboveSubtype.EndsWith("ArmorHalfCorner") || aboveSubtype.EndsWith("HalfArmorBlock") || aboveSubtype.StartsWith("WindowWall"))
                      {
                        addThis = aboveOr.Up == blockDown;

                        if (addThis && isHalfSlopedCorner)
                        {
                          addThis &= (aboveOr.Forward == blockBack || aboveOr.Left == blockBack);
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }

        if (addThis)
        {
          Node node;
          if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
            node = new Node();

          node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
          OpenTileDict[mainGridPosition] = node;

          if (includeAbove || validHalfStair || (cubeAboveEmpty && (upIsUp || entranceIsUp) && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)))
          {
            var newPos = mainGridPosition + upVec;

            Node newNode;
            if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
              newNode = new Node();

            var newOffset = cubeAboveEmpty ? -(Vector3)MainGrid.WorldMatrix.GetDirectionVector(upDir) : Vector3.Zero;

            var worldPositionAbove = MainGrid.GridIntegerToWorld(newPos) + newOffset;
            if (GridBase.PointInsideVoxel(worldPositionAbove, RootVoxel))
              newOffset = Vector3.Zero;

            newNode.Update(newPos, newOffset, this, NodeType.Ground, 0, MainGrid, cubeAbove);
            OpenTileDict[newPos] = newNode;

            if (!cubeAboveEmpty && !validHalfStair)
            {
              ExemptNodesUpper.Add(newPos);
            }
          }
        }
      }
      else if (AiSession.Instance.RampBlockDefinitions.Contains(cubeDef.Id))
      {
        Matrix matrix = new Matrix
        {
          Forward = Base6Directions.GetVector(block.Orientation.Forward),
          Left = Base6Directions.GetVector(block.Orientation.Left),
          Up = Base6Directions.GetVector(block.Orientation.Up)
        };

        var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];
        if (faceDict.Count < 2)
          matrix.TransposeRotationInPlace();

        var dotUp = Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref upVec);
        var dotFwd = Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref upVec);

        Vector3I center = cubeDef.Center;
        Vector3I.TransformNormal(ref center, ref matrix, out center);
        var adjustedPosition = block.Position - center;
        var blockOr = block.Orientation;

        foreach (var kvp in faceDict)
        {
          Vector3I offset = kvp.Key;
          Vector3I.TransformNormal(ref offset, ref matrix, out offset);
          var cell = adjustedPosition + offset;
          var positionAbove = cell + upVec;
          mainGridPosition = cell;

          var cubeAbove = GetBlockAtPosition(positionAbove);
          bool cubeAboveEmpty = cubeAbove == null || !((MyCubeBlockDefinition)cubeAbove.BlockDefinition).HasPhysics;
          bool isBlockTip = block.Position == cell;
          bool addAbove = false, addThis = false;

          addThis = (blockOr.Up == upDir && cubeAboveEmpty) || (blockOr.Forward == upDir && (cubeAboveEmpty || cubeAbove == block));

          if (!addThis && !cubeAboveEmpty)
          {
            var aboveOr = cubeAbove.Orientation;
            var aboveDef = cubeAbove.BlockDefinition.Id;
            var aboveSubtype = aboveDef.SubtypeName;
            var blockDown = Base6Directions.GetOppositeDirection(blockOr.Up);

            if (dotUp > 0) // normal placement
            {
              addAbove = !isBlockTip;

              if (AiSession.Instance.RampBlockDefinitions.Contains(aboveDef))
              {
                addThis = aboveOr.Up == blockDown && aboveOr.Forward == Base6Directions.GetOppositeDirection(blockOr.Forward);
              }
              else if (isBlockTip)
              {
                if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                {
                  if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                  {
                    addThis = aboveOr.Up == blockDown;

                    if (addThis && aboveSubtype.EndsWith("Base"))
                    {
                      addThis &= aboveOr.Forward == blockOr.Forward;
                    }
                  }
                  else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                  {
                    addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                  }
                  else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                  {
                    addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                      || (aboveOr.Forward == blockOr.Up && blockOr.Forward == Base6Directions.GetOppositeDirection(aboveOr.Up));
                  }
                  else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                  {
                    addThis = aboveOr.Up == blockDown || aboveOr.Forward == blockOr.Up;
                  }
                }
                else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                {
                  bool isHalfSlopedCorner = aboveSubtype.EndsWith("ArmorHalfSlopedCorner") || aboveSubtype.EndsWith("Concrete_Half_Block_Slope");
                  if (isHalfSlopedCorner || aboveSubtype.EndsWith("ArmorHalfCorner") || aboveSubtype.EndsWith("HalfArmorBlock") || aboveSubtype.StartsWith("WindowWall"))
                  {
                    addThis = aboveOr.Up == blockDown;

                    if (addThis && isHalfSlopedCorner)
                    {
                      addThis &= (aboveOr.Forward == blockOr.Forward || aboveOr.Left == blockOr.Forward);
                    }
                  }
                }
              }
              else // block base is the tall side - less head room
              {
                if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(aboveDef))
                {
                  if (aboveSubtype.EndsWith("ArmorSlope2Tip"))
                  {
                    addThis = aboveOr.Forward == blockOr.Forward && aboveOr.Up == blockDown;
                  }
                  else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                  {
                    addThis = aboveOr.Up == blockDown || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockDown;
                  }
                  else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                  {
                    addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                      || (aboveOr.Forward == blockOr.Up && aboveOr.Left == blockOr.Left);
                  }
                }
                else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(aboveDef))
                {
                  if (aboveSubtype.EndsWith("HalfArmorBlock") || aboveSubtype.StartsWith("WindowWall"))
                  {
                    addThis = blockDown == Base6Directions.GetOppositeDirection(aboveOr.Forward);
                  }
                  else if (aboveSubtype.EndsWith("ArmorHalfSlopedCorner"))
                  {
                    addThis = aboveOr.Forward == blockDown || aboveOr.Left == blockDown || aboveOr.Up == blockDown;
                  }
                }
              }
            }
            else if (dotFwd > 0) // steep placement
            {
              addThis = cubeAbove == block;
              addAbove = isBlockTip;

              if (!addThis)
              {
                if (AiSession.Instance.RampBlockDefinitions.Contains(cubeAbove.BlockDefinition.Id))
                {
                  addThis = aboveOr.Up == Base6Directions.GetOppositeDirection(blockOr.Up)
                    && aboveOr.Forward == Base6Directions.GetOppositeDirection(blockOr.Forward);
                }
                else if (isBlockTip)
                {
                  var blockBack = Base6Directions.GetOppositeDirection(blockOr.Forward);

                  if (AiSession.Instance.SlopeBlockDefinitions.Contains(aboveDef))
                  {
                    if (aboveSubtype.IndexOf("ArmorSlope2") >= 0)
                    {
                      if (aboveSubtype.EndsWith("Tip"))
                      {
                        addThis = aboveOr.Forward == blockOr.Forward && aboveOr.Up == blockDown;
                      }
                    }
                    else if (aboveSubtype.EndsWith("ArmorHalfSlopeCorner")) // SlopeCorner vs SlopedCorner, gotta love it :/
                    {
                      addThis = aboveOr.Up == blockBack || Base6Directions.GetOppositeDirection(aboveOr.Forward) == blockBack;
                    }
                    else if (aboveSubtype.EndsWith("BlockArmorSlope"))
                    {
                      addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                        || (aboveOr.Forward == blockOr.Up && aboveOr.Up == blockBack);
                    }
                    else if (aboveSubtype.EndsWith("HalfSlopeArmorBlock"))
                    {
                      addThis = (aboveOr.Up == blockDown && aboveOr.Forward == blockOr.Forward)
                        || (aboveOr.Up == blockBack && aboveOr.Forward == blockOr.Up);
                    }
                  }
                }
              }
            }
          }
          else if (cubeAboveEmpty)
          {
            addAbove = (dotUp > 0 && !isBlockTip) || (dotFwd > 0 && isBlockTip);
          }
          else if (cubeAbove == block && !isBlockTip) // add adjacent
          {
            var blockDown = Base6Directions.GetOppositeDirection(blockOr.Up);
            var adjacentPos = mainGridPosition + Base6Directions.GetIntVector(blockOr.Up);
            var adjacentBlock = GetBlockAtPosition(adjustedPosition); // connectedGrid.GetCubeBlock(adjacentPos) as IMySlimBlock;

            if (adjacentBlock != null)
            {
              var blockBack = Base6Directions.GetOppositeDirection(blockOr.Forward);
              var adjacentDef = adjacentBlock.BlockDefinition.Id;
              var adjacentOr = adjacentBlock.Orientation;
              bool addAdjacent = false;

              if (AiSession.Instance.SlopedHalfBlockDefinitions.Contains(adjacentDef))
              {
                addAdjacent = adjacentOr.Up == blockDown && (adjacentOr.Forward == blockBack || adjacentOr.Forward == blockOr.Forward);

                if (!addAdjacent && !adjacentDef.SubtypeName.EndsWith("Tip"))
                {
                  addAdjacent = adjacentOr.Forward == blockOr.Up && (adjacentOr.Up == blockBack || adjacentOr.Up == blockOr.Forward);
                }
              }
              else if (AiSession.Instance.RampBlockDefinitions.Contains(adjacentDef))
              {
                addAdjacent = adjacentOr.Forward == blockBack && adjacentOr.Up == blockDown;
              }
              else if (AiSession.Instance.SlopeBlockDefinitions.Contains(adjacentDef))
              {
                if (adjacentDef.SubtypeName.EndsWith("ArmorHalfSlopedCorner"))
                {
                  addAdjacent = adjacentOr.Left == blockDown || adjacentOr.Up == blockDown || adjacentOr.Forward == blockDown;
                }
              }

              if (addAdjacent)
              {
                var node = new Node(adjacentPos, Vector3.Zero, this, MainGrid, adjacentBlock);
                node.SetNodeType(NodeType.Ground);
                OpenTileDict[adjacentPos] = node;
                ExemptNodesSide.Add(adjacentPos);
              }
            }
          }

          if (addThis)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block);
            OpenTileDict[mainGridPosition] = node;

            if (addAbove)
            {
              var newPos = mainGridPosition + upVec;

              Node newNode;
              if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
                newNode = new Node();

              var newOffset = cubeAboveEmpty ? -(Vector3)MainGrid.WorldMatrix.GetDirectionVector(upDir) : Vector3.Zero;

              var worldPositionAbove = MainGrid.GridIntegerToWorld(newPos) + newOffset;
              if (GridBase.PointInsideVoxel(worldPositionAbove, RootVoxel))
                newOffset = Vector3.Zero;

              newNode.Update(newPos, newOffset, this, NodeType.Ground, 0, MainGrid, cubeAbove);
              OpenTileDict[newPos] = newNode;

              if (!cubeAboveEmpty)
              {
                ExemptNodesUpper.Add(newPos);
              }
            }
          }
        }
      }
      else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id))
      {
        Base6Directions.Direction sideWithPane;
        if (cubeDef.Id.SubtypeName == "LargeWindowSquare")
          sideWithPane = block.Orientation.Forward;
        else
          sideWithPane = Base6Directions.GetOppositeDirection(block.Orientation.Left);

        Matrix matrix = new Matrix
        {
          Forward = Base6Directions.GetVector(block.Orientation.Forward),
          Left = Base6Directions.GetVector(block.Orientation.Left),
          Up = Base6Directions.GetVector(block.Orientation.Up)
        };

        var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];
        if (faceDict.Count < 2)
          matrix.TransposeRotationInPlace();

        Vector3I center = cubeDef.Center;
        Vector3I.TransformNormal(ref center, ref matrix, out center);
        var adjustedPosition = block.Position - center;
        var downDir = Base6Directions.GetOppositeDirection(upDir);

        foreach (var kvp in faceDict)
        {
          Vector3I offset = kvp.Key;
          Vector3I.TransformNormal(ref offset, ref matrix, out offset);
          var cell = adjustedPosition + offset;
          mainGridPosition = cell; // connectedIsMain ? cell : MainGrid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(cell));

          if (sideWithPane == downDir || (cubeDef.Id.SubtypeName.StartsWith("HalfWindowCorner") && block.Orientation.Forward == downDir))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
      }
      else if (AiSession.Instance.AngledWindowDefinitions.ContainsItem(cubeDef.Id))
      {
        Matrix matrix = new Matrix
        {
          Forward = Base6Directions.GetVector(block.Orientation.Forward),
          Left = Base6Directions.GetVector(block.Orientation.Left),
          Up = Base6Directions.GetVector(block.Orientation.Up)
        };

        var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];
        if (faceDict.Count < 2)
          matrix.TransposeRotationInPlace();

        Vector3I center = cubeDef.Center;
        Vector3I.TransformNormal(ref center, ref matrix, out center);
        var adjustedPosition = block.Position - center;

        bool isEdgeWindow = cubeDef.Id.SubtypeName == "LargeWindowEdge";

        foreach (var kvp in faceDict)
        {
          Vector3I offset = kvp.Key;
          Vector3I.TransformNormal(ref offset, ref matrix, out offset);
          var cell = adjustedPosition + offset;
          var positionAbove = cell + upVec;
          mainGridPosition = cell;

          var cubeAbove = GetBlockAtPosition(positionAbove);
          bool cubeAboveEmpty = cubeAbove == null || !((MyCubeBlockDefinition)cubeAbove.BlockDefinition).HasPhysics;

          if (isEdgeWindow)
          {
            if (cubeAboveEmpty && (block.Orientation.Up == upDir || block.Orientation.Forward == upDir))
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block);
              OpenTileDict[mainGridPosition] = node;

              var newPos = mainGridPosition + upVec;

              Node newNode;
              if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
                newNode = new Node();

              var newOffset = -(Vector3)MainGrid.WorldMatrix.GetDirectionVector(upDir);

              var worldPositionAbove = MainGrid.GridIntegerToWorld(newPos) + newOffset;
              if (GridBase.PointInsideVoxel(worldPositionAbove, RootVoxel))
                newOffset = Vector3.Zero;

              newNode.Update(newPos, newOffset, this, NodeType.Ground, 0, MainGrid);
              OpenTileDict[newPos] = newNode;
            }
          }
          else
          {
            if (cubeAboveEmpty || cubeAbove == block)
            {
              var vecLeft = Base6Directions.GetIntVector(block.Orientation.Left);
              if (vecLeft.Dot(ref upVec) == 0)
              {
                Node node;
                if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                  node = new Node();

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block);
                OpenTileDict[mainGridPosition] = node;

                var newPos = mainGridPosition + upVec;

                Node newNode;
                if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
                  newNode = new Node();

                var newOffset = cubeAboveEmpty ? -(Vector3)MainGrid.WorldMatrix.GetDirectionVector(upDir) : Vector3.Zero;

                var worldPositionAbove = MainGrid.GridIntegerToWorld(newPos) + newOffset;
                if (GridBase.PointInsideVoxel(worldPositionAbove, RootVoxel))
                  newOffset = Vector3.Zero;

                newNode.Update(newPos, newOffset, this, NodeType.Ground, 0, MainGrid, cubeAbove);
                OpenTileDict[newPos] = newNode;
              }
            }
          }
        }
      }
      else if (cubeDef.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
      {
        if (block.Orientation.Forward == Base6Directions.GetOppositeDirection(upDir))
        {
          Dictionary<Vector3I, HashSet<Vector3I>> faceDict;
          if (!AiSession.Instance.BlockFaceDictionary.TryGetValue(cubeDef.Id, out faceDict))
          {
            AiSession.Instance.Logger.Log($"There was no cube face dictionary found for {cubeDef.Id} (Size = {cubeDef.CubeSize}, Grid = {block.CubeGrid.DisplayName}, Position = {block.Position})", MessageType.WARNING);
          }
          else
          {

            Matrix matrix = new Matrix
            {
              Forward = Base6Directions.GetVector(block.Orientation.Forward),
              Left = Base6Directions.GetVector(block.Orientation.Left),
              Up = Base6Directions.GetVector(block.Orientation.Up)
            };

            if (faceDict.Count < 2)
              matrix.TransposeRotationInPlace();

            Vector3I side, center = cubeDef.Center;
            Vector3I.TransformNormal(ref upVec, ref matrix, out side);
            Vector3I.TransformNormal(ref center, ref matrix, out center);
            var adjustedPosition = block.Position - center;

            foreach (var kvp in faceDict)
            {
              var cell = kvp.Key;
              Vector3I.TransformNormal(ref cell, ref matrix, out cell);
              var cellPosition = adjustedPosition + cell;

              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(cellPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block);
              OpenTileDict[cellPosition] = node;
            }
          }
        }
      }
      else if (/*isDigiLadder ||*/ cubeDef.Id.TypeId == typeof(MyObjectBuilder_Ladder2))
      {
        Node node;
        if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
          node = new Node();

        node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
        OpenTileDict[mainGridPosition] = node;

        var blockUp = Base6Directions.GetIntVector(block.Orientation.Up);
        if (blockUp.Dot(ref upVec) != 0)
        {
          var positionAbove = position + upVec;
          var cubeAbove = GetBlockAtPosition(positionAbove);
          if (cubeAbove == null)
          {
            var newPos = mainGridPosition + upVec;

            Node newNode;
            if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
              newNode = new Node();

            var newOffset = -(Vector3)MainGrid.WorldMatrix.GetDirectionVector(upDir);

            var worldPositionAbove = MainGrid.GridIntegerToWorld(newPos) + newOffset;
            if (GridBase.PointInsideVoxel(worldPositionAbove, RootVoxel))
              newOffset = Vector3.Zero;

            newNode.Update(newPos, newOffset, this, NodeType.Ground, 0, MainGrid);
            OpenTileDict[newPos] = newNode;
          }
        }
      }
      else if (cubeDef.Id.SubtypeName.EndsWith("HalfArmorBlock") || cubeDef.Id.SubtypeName.StartsWith("WindowWall"))
      {
        if (OpenTileDict.ContainsKey(position))
          return;

        var blockFwd = Base6Directions.GetIntVector(block.Orientation.Forward);
        var dotFwd = blockFwd.Dot(ref upVec);

        if (dotFwd < 0)
        {
          var positionAbove = position + upVec;
          var cubeAbove = GetBlockAtPosition(positionAbove);
          if (cubeAbove == null)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, MainGrid, block); 
            OpenTileDict[mainGridPosition] = node;
          }
        }
        else if (dotFwd == 0)
        {
          Node node;
          if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
            node = new Node();

          node.Update(mainGridPosition, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
          OpenTileDict[mainGridPosition] = node;
        }
      }
      else if (block.FatBlock is IMyInteriorLight && block.BlockDefinition.Id.SubtypeName == "LargeLightPanel")
      {
        if (!OpenTileDict.ContainsKey(mainGridPosition))
        {
          Node node;
          if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
            node = new Node();

          node.Update(mainGridPosition, Vector3.Zero, this, NodeType.None, 0, MainGrid, block); 
          OpenTileDict[mainGridPosition] = node;
        }
      }
    }

    List<BoundingBoxI> _blockBoxList = new List<BoundingBoxI>();

    public void ProcessBlockChanges()
    {
      try
      {
        using (_pendingLockObject.AcquireExclusiveUsing())
        {
          if (GraphLocked || !Ready || !_updateTask.IsComplete)
            return;

          if (_updateTask.Exceptions != null)
          {
            AiSession.Instance.Logger.ClearCached();
            AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
            foreach (var ex in _updateTask.Exceptions)
              AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

            AiSession.Instance.Logger.LogAll();
            MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
          }

          InteriorNodesReady = false;
          NeedsBlockUpdate = false;
          GraphLocked = true;
          Ready = false;

          _blockApplyHash.Clear();
          _blockApplyHash.UnionWith(_blockUpdateHash);
          _updateTask = MyAPIGateway.Parallel.Start(ApplyBlockChanges, SetReady);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.ProcessBlockChanges: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    HashSet<Vector3I> _positionsToRemove = new HashSet<Vector3I>();
    void ApplyBlockChanges()
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.ApplyBlockChanges: Start");

        _blockBoxList.Clear();
        _positionsToRemove.Clear();
        var dirArray = AiSession.Instance.CardinalDirections;

        foreach (var pos in _blockApplyHash)
        {
          var box = BoundingBoxI.CreateInvalid();
          box.Include(pos);

          foreach (var dir in dirArray)
          {
            box.Include(pos + dir);
          }

          box.Inflate(1);

          var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);
          while (iter.IsValid())
          {
            var current = iter.Current;
            iter.MoveNext();

            _positionsToRemove.Add(current);
          }

          bool found = false;
          for (int i = 0; i < _blockBoxList.Count; i++)
          {
            var otherBox = _blockBoxList[i];

            if (otherBox.Intersects(box))
            {
              found = true;
              otherBox.Include(ref box);
              _blockBoxList[i] = otherBox;
              break;
            }
          }

          if (!found)
          {
            _blockBoxList.Add(box);
          }
        }

        InvokePositionsRemoved(_positionsToRemove, false);

        var newBox = GetMapBoundingBoxLocal();
        newBox.Inflate(_boxExpansion);

        var worldCenter = MainGrid.GridIntegerToWorld(newBox.Center);
        var quat = Quaternion.CreateFromRotationMatrix(MainGrid.WorldMatrix);
        var halfVector = Vector3D.Half * CellSize;
        var newOBB = new MyOrientedBoundingBoxD(worldCenter, newBox.HalfExtents * CellSize + halfVector, quat);

        if (newOBB.HalfExtent.LengthSquared() > OBB.HalfExtent.LengthSquared())
        {
          Dirty = true;
          return;
        }

        WeaponPositions.Clear();
        foreach (var fatblock in MainGrid.GetFatBlocks())
        {
          if (fatblock is IMyUserControllableGun)
          {
            WeaponPositions.Add(fatblock.Position);
          }
        }

        var upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
        var upVec = Base6Directions.GetIntVector(upDir);
        Vector3I upVecPlanet;

        var cellSize = CellSize;
        var halfCellSize = CellSize * 0.5f;

        float _;
        var gravityNorm = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);

        if (gravityNorm.LengthSquared() > 0)
        {
          gravityNorm.Normalize();

          var upDirPlanet = MainGrid.WorldMatrix.GetClosestDirection(-gravityNorm);
          upVecPlanet = Base6Directions.GetIntVector(upDirPlanet);
        }
        else
        {
          gravityNorm = WorldMatrix.Down;
          upVecPlanet = upVec;
        }

        for (int i = 0; i < _blockBoxList.Count; i++)
        {
          var box = _blockBoxList[i];
          box.Min = Vector3I.Max(box.Min, BoundingBox.Min);
          box.Max = Vector3I.Min(box.Max, BoundingBox.Max);

          var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            Node n;
            if (PlanetTileDictionary.TryGetValue(pos, out n))
            {
              PlanetTileDictionary.Remove(pos);
              AiSession.Instance.NodeStack.Push(n);
            }
            else if (OpenTileDict.TryGetValue(pos, out n))
            {
              OpenTileDict.Remove(pos);
              AiSession.Instance.NodeStack.Push(n);
            }

            _positionsToRemove.Add(pos);
          }

          iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            var slim = GetBlockAtPosition(pos);
            if (slim != null)
            {
              var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;
              InitBlock(slim, ref upVec, ref upDir, ref cellSize, ref halfCellSize, cubeDef);
            }
          }

          CheckForPlanetTiles(ref box, ref gravityNorm, ref upVecPlanet);

          iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            Node n;
            if (OpenTileDict.TryGetValue(pos, out n))
            {
              GetBlockedNodeEdges(n);
            }
          }

          iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

          while (iter.IsValid())
          {
            var pos = iter.Current;
            iter.MoveNext();

            Node n;
            if (PlanetTileDictionary.TryGetValue(pos, out n))
            {
              GetBlockedNodeEdges(n);
            }
          }
        }

        _blockApplyHash.Clear();

        NeedsInteriorNodesUpdate = true;
        //AiSession.Instance.Logger.Log($"{this}.ApplyBlockChanges: Finish");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception during block update task: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public void InteriorNodesCheck()
    {
      if (_interiorNodesTask.IsComplete)
      {
        if (_interiorNodesTask.Exceptions != null)
        {
          AiSession.Instance.Logger.ClearCached();
          AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
          foreach (var ex in _interiorNodesTask.Exceptions)
            AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

          AiSession.Instance.Logger.LogAll();
          MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
        }

        NeedsInteriorNodesUpdate = false;

        ApiWorkData data;
        if (!AiSession.Instance.ApiWorkDataStack.TryPop(out data) || data == null)
          data = new ApiWorkData();

        data.Grid = MainGrid;
        data.NodeList = InteriorNodeList;
        data.EnclosureRating = 5;
        data.AirtightNodesOnly = false;
        data.AllowAirNodes = false;
        data.CallBack = InteriorNodeCallback;

        _interiorNodesTask = MyAPIGateway.Parallel.StartBackground(BotFactory.GetInteriorNodes, BotFactory.GetInteriorNodesCallback, data);
      }
    }

    void InteriorNodeCallback(IMyCubeGrid grid, List<Vector3I> nodeList)
    {
      //AiSession.Instance.Logger.Log($"InteriorNodeCallback");
      InteriorNodesReady = true;
    }

    BoundingBoxI GetMapBoundingBoxLocal()
    {
      var box = new BoundingBoxI(MainGrid.Min, MainGrid.Max);
      for (int i = GridCollection.Count - 1; i >= 0; i--)
      {
        var connectedGrid = GridCollection[i] as MyCubeGrid;
        if (connectedGrid?.Physics == null || connectedGrid.MarkedForClose)
        {
          GridCollection.RemoveAtFast(i);
          continue;
        }

        bool connectedIsMain = connectedGrid.EntityId == MainGrid.EntityId;

        Vector3I min, max;
        if (connectedIsMain)
        {
          min = connectedGrid.Min;
          max = connectedGrid.Max;
        }
        else
        {
          min = MainGrid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(connectedGrid.Min));
          max = MainGrid.WorldToGridInteger(connectedGrid.GridIntegerToWorld(connectedGrid.Max));
        }

        box.Include(ref min);
        box.Include(ref max);
      }

      return box;
    }

    private void Door_OnDoorStateChanged(IMyDoor door, bool open)
    {
      Door_EnabledChanged(door);
    }

    private void Door_OnMarkForClose(VRage.ModAPI.IMyEntity ent)
    {
      try
      {
        if (ent != null)
        {
          ent.OnMarkForClose -= Door_OnMarkForClose;

          var door = ent as IMyDoor;
          if (door != null)
          {
            door.EnabledChanged -= Door_EnabledChanged;
            door.IsWorkingChanged -= Door_EnabledChanged;
            door.OnDoorStateChanged -= Door_OnDoorStateChanged;

            Door_EnabledChanged(door);
          }
        }
      }
      catch (Exception ex)
      {
        var msg = $"Exception in CubeGridMap.Door_OnMarkForClose: {ex.Message}\n{ex.StackTrace}";
        AiSession.Instance.Logger.Log(msg, MessageType.ERROR);

        if (MyAPIGateway.Session?.Player != null)
          AiSession.Instance.ShowMessage(msg);
      }
    }

    internal void Door_EnabledChanged(IMyCubeBlock block)
    {
      try
      {
        if (!IsValid || AiSession.Instance == null || !AiSession.Instance.Registered)
          return;

        var door = block as IMyDoor;
        if (door == null)
          return;

        Vector3I pos = block.Position;
        var grid = block.CubeGrid as MyCubeGrid;
        var needsPositionAdjusted = MainGrid.EntityId != grid.EntityId;

        if (needsPositionAdjusted)
          pos = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(pos));

        bool isOpen = false;
        if (door.SlimBlock.IsBlockUnbuilt())
          isOpen = true;

        if (isOpen || door.MarkedForClose || door.Enabled || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
        {
          IMyDoor _;
          if (BlockedDoors.TryRemove(pos, out _))
          {
            var cubeDef = block.SlimBlock.BlockDefinition as MyCubeBlockDefinition;
            var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

            Matrix matrix = new Matrix
            {
              Forward = Base6Directions.GetVector(block.Orientation.Forward),
              Left = Base6Directions.GetVector(block.Orientation.Left),
              Up = Base6Directions.GetVector(block.Orientation.Up)
            };

            matrix.TransposeRotationInPlace();

            Vector3I center = cubeDef.Center;
            Vector3I.TransformNormal(ref center, ref matrix, out center);
            var adjustedPosition = block.Position - center;

            foreach (var kvp in faceDict)
            {
              var cell = kvp.Key;
              Vector3I.TransformNormal(ref cell, ref matrix, out cell);
              var position = adjustedPosition + cell;

              if (needsPositionAdjusted)
                position = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(position));

              BlockedDoors.TryRemove(position, out _);
            }
          }
        }
        else if (!door.Enabled && !BlockedDoors.ContainsKey(pos))
        {
          BlockedDoors[pos] = door;

          var cubeDef = block.SlimBlock.BlockDefinition as MyCubeBlockDefinition;
          var faceDict = AiSession.Instance.BlockFaceDictionary[cubeDef.Id];

          Matrix matrix = new Matrix
          {
            Forward = Base6Directions.GetVector(block.Orientation.Forward),
            Left = Base6Directions.GetVector(block.Orientation.Left),
            Up = Base6Directions.GetVector(block.Orientation.Up)
          };

          matrix.TransposeRotationInPlace();

          Vector3I center = cubeDef.Center;
          Vector3I.TransformNormal(ref center, ref matrix, out center);
          var adjustedPosition = block.Position - center;

          foreach (var kvp in faceDict)
          {
            var cell = kvp.Key;
            Vector3I.TransformNormal(ref cell, ref matrix, out cell);
            var position = adjustedPosition + cell;

            if (needsPositionAdjusted)
              position = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(position));

            BlockedDoors[position] = door;
          }
        }
      }
      catch (Exception ex)
      {
        var msg = $"Exception in CubeGridMap.Door_EnabledChanged: {ex.Message}\n{ex.StackTrace}";
        AiSession.Instance.Logger.Log(msg, MessageType.ERROR);

        if (MyAPIGateway.Session?.Player != null)
          AiSession.Instance.ShowMessage(msg);
      }
    }

    void AddAdditionalGridTiles()
    {
      List<CubeGridMap> addMaps;
      if (!AiSession.Instance.GridMapListStack.TryPop(out addMaps))
        addMaps = new List<CubeGridMap>();
      else
       addMaps.Clear();

      List<MyEntity> tempEntities;
      if (!AiSession.Instance.EntListStack.TryPop(out tempEntities))
        tempEntities = new List<MyEntity>();
      else
        tempEntities.Clear();

      var sphere = new BoundingSphereD(MainGrid.PositionComp.WorldAABB.Center, BoundingBox.HalfExtents.AbsMax() * CellSize);
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, tempEntities);

      foreach (var ent in tempEntities)
      {
        var grid = ent as MyCubeGrid;
        if (grid?.Physics == null || grid.IsSameConstructAs(MainGrid) || grid.MarkedForClose || grid.MarkedAsTrash || grid.GridSize < 1)
          continue;

        var gridMap = AiSession.Instance.GetGridGraph(grid, WorldMatrix);
        if (gridMap != null)
          addMaps.Add(gridMap);
      }

      tempEntities.Clear();
      AiSession.Instance.EntListStack.Push(tempEntities);

      int countMS = 0;
      int maxMS = MyUtils.GetRandomInt(1000, 2000);
      while (addMaps.Count > 0)
      {
        if (Dirty)
          return;

        for (int i = addMaps.Count - 1; i >= 0; i--)
        {
          if (Dirty)
            return;

          var map = addMaps[i];
          if (map == null)
          {
            addMaps.RemoveAtFast(i);
            continue;
          }

          if (!map.Ready)
            continue;

          foreach (var kvp in map.OpenTileDict)
          {
            if (map == null || map.Dirty || map.MainGrid?.MarkedForClose != false)
              break;

            var node = kvp.Value;
            if (!node.IsGroundNode || node.IsGridNodePlanetTile || map.ObstacleNodes.ContainsKey(node.Position))
              continue;

            var localTile = MainGrid.WorldToGridInteger(node.Grid.GridIntegerToWorld(kvp.Key));
            if (!OpenTileDict.ContainsKey(localTile) && !ObstacleNodes.ContainsKey(localTile) && BoundingBox.Contains(localTile) != ContainmentType.Disjoint)
            {
              var nodeType = NodeType.Ground | NodeType.GridAdditional;
              if (node.IsGridNode)
                nodeType |= NodeType.Grid;
              if (node.IsGridNodeUnderGround)
                nodeType |= NodeType.GridUnderground;

              OpenTileDict[localTile] = new Node(localTile, node.Offset, this, nodeType, node.BlockedMask, node.Grid, node.Block);
            }
          }

          _additionalMaps2.Add(map);
          addMaps.RemoveAtFast(i);
        }

        MyAPIGateway.Parallel.Sleep(25);
        countMS += 25;

        if (countMS > maxMS) // TODO: possibly check if this grid has less blocks than the other and break on smaller one ??
          break;
      }

      addMaps.Clear();
      AiSession.Instance.GridMapListStack.Push(addMaps);
    }

    void CheckForPlanetTiles(ref BoundingBoxI box, ref Vector3 gravityNorm, ref Vector3I upVec)
    {
      var blockedVoxelEdges = AiSession.Instance.BlockedVoxelEdges;
      var cellSize = CellSize;
      var cellSizeCutoff = cellSize * 0.65f;
      cellSizeCutoff *= cellSizeCutoff;
      var worldMatrix = WorldMatrix;
      var planet = RootVoxel as MyPlanet;

      bool checkForWater = false; 
      bool checkForVoxel = false;
      if (RootVoxel != null && !RootVoxel.MarkedForClose)
      {
        checkForWater = planet != null && WaterAPI.Registered && WaterAPI.HasWater(planet);
        checkForVoxel = true;

        RootVoxel.RangeChanged -= Planet_RangeChanged;
        RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;

        RootVoxel.RangeChanged += Planet_RangeChanged;
        RootVoxel.OnMarkForClose += Planet_OnMarkForClose;
      }

      double edgeDistance;
      if (!GetEdgeDistanceInDirection(WorldMatrix.Up, out edgeDistance))
        edgeDistance = VoxelGridMap.DefaultHalfSize * cellSize;

      var topOfGraph = OBB.Center + WorldMatrix.Up * edgeDistance;
      var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);
      int tunnelCount = 0;

      while (iter.IsValid())
      {
        if (Dirty)
        {
          return;
        }

        var localPoint = iter.Current;
        iter.MoveNext();

        Node node;
        if (OpenTileDict.TryGetValue(localPoint, out node))
          continue;

        bool isGroundNode = false;
        bool isTunnelNode = false;
        bool addNodeBelow = false;

        Vector3D worldPoint = MainGrid.GridIntegerToWorld(localPoint);
        var groundPoint = worldPoint;
        var groundPointBelow = worldPoint;

        var worldBelow = worldPoint + gravityNorm;
        var localBelow = localPoint - upVec;

        if (checkForVoxel)
        {
          if (RootVoxel == null || RootVoxel.MarkedForClose)
            checkForVoxel = false;

          if (PointInsideVoxel(worldPoint, RootVoxel))
            continue;

          var pointBelow = worldPoint + gravityNorm * cellSize;
          if (PointInsideVoxel(pointBelow, RootVoxel))
          {
            isGroundNode = true;

            var pointInAir = worldPoint;
            var pointInVoxel = pointBelow;
            int count = 0;

            while (Vector3D.DistanceSquared(pointInAir, pointInVoxel) > 0.1)
            {
              count++;
              if (count > 500)
              {
                AiSession.Instance.Logger.Log($"Point {localPoint} took too many attempts to finish", MessageType.WARNING);
                break;
              }

              var mid = (pointInAir + pointInVoxel) * 0.5;
              if (PointInsideVoxel(mid, RootVoxel))
              {
                pointInVoxel = mid;
              }
              else
              {
                pointInAir = mid;
              }
            }

            var offsetPosition = pointInAir - gravityNorm;

            if (Vector3D.DistanceSquared(pointInAir, worldPoint) > cellSizeCutoff)
            {
              isGroundNode = false;
              addNodeBelow = true;
              groundPointBelow = offsetPosition;
            }
            else
            {
              groundPoint = offsetPosition;
            }
          }

          Vector3D? surfacePoint = null;
          if (planet != null)
          {
            surfacePoint = planet.GetClosestSurfacePointGlobal(worldPoint) - gravityNorm;
          }
          else
          {
            var closestPoint = GetClosestSurfacePointFast(null, worldPoint - gravityNorm * 20, -gravityNorm);
            if (closestPoint.HasValue)
              surfacePoint = closestPoint.Value - gravityNorm;
          }

          if (surfacePoint.HasValue)
          {
            var surfaceValue = surfacePoint.Value;

            while (PointInsideVoxel(surfaceValue, RootVoxel) && OBB.Contains(ref surfaceValue))
              surfaceValue -= gravityNorm;

            var vector = worldPoint - surfaceValue;
            if (vector.LengthSquared() > 9 && vector.Dot(worldMatrix.Down) > 0)
            {
              var line = new LineD(surfaceValue, groundPoint);
              var lerpAmount = MathHelper.Clamp(1 / line.Length, 0, 1);
              var point = Vector3D.Lerp(line.From, line.To, lerpAmount);
              var testAmount = MathHelper.Clamp(lerpAmount * 2, 0, 1);

              int testCount = 0;
              int maxCount = (int)Math.Ceiling(line.Length) + 1;

              while (Vector3D.DistanceSquared(point, line.To) > 9)
              {
                if (PointInsideVoxel(point, RootVoxel))
                {
                  isTunnelNode = true;
                  tunnelCount++;
                  break;
                }
                else if (++testCount > maxCount)
                {
                  break;
                }

                point = Vector3D.Lerp(line.From, line.To, testAmount);
                testAmount = MathHelper.Clamp(testAmount + lerpAmount, 0, 1);
              }
            }
          }
        }

        if (addNodeBelow && PlanetTileDictionary.TryGetValue(localBelow, out node))
        {
          NodeType nType = NodeType.None;

          if (!node.IsGroundNode)
            nType |= NodeType.Ground;

          if (!node.IsTunnelNode && isTunnelNode)
            nType |= NodeType.Tunnel;

          node.SetNodeType(nType);
          addNodeBelow = false;
        }

        if (PlanetTileDictionary.TryGetValue(localPoint, out node))
        {
          NodeType nType = NodeType.None;

          if (isGroundNode && !node.IsGroundNode)
            nType |= NodeType.Ground;

          if (isTunnelNode && !node.IsTunnelNode)
            nType |= NodeType.Tunnel;

          node.SetNodeType(nType);
          continue;
        }

        bool add = true;
        for (int i = _additionalMaps2.Count - 1; i >= 0; i--)
        {
          if (Dirty)
          {
            return;
          }

          var otherGrid = _additionalMaps2[i]?.MainGrid;
          if (otherGrid == null || otherGrid.MarkedForClose)
            continue;

          if (addNodeBelow)
          {
            var localBelowOther = otherGrid.WorldToGridInteger(worldBelow);
            var slimBelow = otherGrid.GetCubeBlock(localBelowOther) as IMySlimBlock;
            if (slimBelow != null && ((MyCubeBlockDefinition)slimBelow.BlockDefinition).HasPhysics)
            {
              if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(slimBelow.BlockDefinition.Id))
              {
                var turretBasePosition = slimBelow.Position - Base6Directions.GetIntVector(slimBelow.Orientation.Up);
                if (turretBasePosition == localBelowOther || slimBelow.Position == localBelowOther)
                  addNodeBelow = false;
              }
              else if (slimBelow.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
              {
                var turbineBasePosition = slimBelow.Position;
                var turbineUpVec = Base6Directions.GetIntVector(slimBelow.Orientation.Up);
                var midPoint = turbineBasePosition + turbineUpVec;
                var topPoint = midPoint + turbineUpVec;
                if (localBelowOther == turbineBasePosition || localBelowOther == midPoint || localBelowOther == topPoint)
                {
                  addNodeBelow = false;
                }
              }
              else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(slimBelow.BlockDefinition.Id))
                addNodeBelow = false;
            }
          }

          var localPointOther = otherGrid.WorldToGridInteger(worldPoint);
          var slim = otherGrid.GetCubeBlock(localPointOther) as IMySlimBlock;
          if (slim != null && ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics && !(slim.FatBlock is IMyLightingBlock))
          {
            if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(slim.BlockDefinition.Id))
            {
              var turretBasePosition = slim.Position - Base6Directions.GetIntVector(slim.Orientation.Up);
              if (turretBasePosition == localPointOther || slim.Position == localPointOther)
              {
                add = false;
                break;
              }
            }
            else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
            {
              var turbineBasePosition = slim.Position;
              var turbineUpVec = Base6Directions.GetIntVector(slim.Orientation.Up);
              var midPoint = turbineBasePosition + turbineUpVec;
              var topPoint = midPoint + turbineUpVec;
              if (localPointOther == turbineBasePosition || localPointOther == midPoint || localPointOther == topPoint)
              {
                add = false;
                break;
              }
            }
            else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
            {
              if (slim.BlockDefinition.Id.SubtypeName.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                add = false;
                break;
              }
            }

            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(slim.BlockDefinition.Id))
            {
              if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(slim.BlockDefinition.Id)
                || slim.BlockDefinition.Id.SubtypeName.Contains("Centered"))
              {
                add = false;
                break;
              }
            }
            else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(slim.BlockDefinition.Id))
            {
              add = false;
              break;
            }
          }
        }

        if (add)
        {
          if (addNodeBelow)
          {
            var blockBelow = GetBlockAtPosition(localBelow);
            if (blockBelow != null && blockBelow.CubeGrid.EntityId == MainGrid.EntityId && !(blockBelow.FatBlock is IMyLightingBlock))
            {
              if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(blockBelow.BlockDefinition.Id))
              {
                var turretBasePosition = blockBelow.Position - Base6Directions.GetIntVector(blockBelow.Orientation.Up);
                if (turretBasePosition == localBelow || blockBelow.Position == localBelow)
                  addNodeBelow = false;
              }
              else if (blockBelow.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
              {
                var turbineBasePosition = blockBelow.Position;
                var turbineUpVec = Base6Directions.GetIntVector(blockBelow.Orientation.Up);
                var midPoint = turbineBasePosition + turbineUpVec;
                var topPoint = midPoint + turbineUpVec;
                if (localBelow == turbineBasePosition || localBelow == midPoint || localBelow == topPoint)
                {
                  addNodeBelow = false;
                }
              }
              else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(blockBelow.BlockDefinition.Id))
                addNodeBelow = false;
            }
          }

          if (Dirty)
          {
            return;
          }

          var block = GetBlockAtPosition(localPoint);
          if (block != null && block.CubeGrid.EntityId == MainGrid.EntityId && !(block.FatBlock is IMyLightingBlock))
          {
            if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(block.BlockDefinition.Id))
            {
              var turretBasePosition = block.Position - Base6Directions.GetIntVector(block.Orientation.Up);
              if (turretBasePosition == localPoint || localPoint == block.Position)
                add = false;
            }
            else if (block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
            {
              var turbineBasePosition = block.Position;
              var turbineUpVec = Base6Directions.GetIntVector(block.Orientation.Up);
              var midPoint = turbineBasePosition + turbineUpVec;
              var topPoint = midPoint + turbineUpVec;
              if (localPoint == turbineBasePosition || localPoint == midPoint || localPoint == topPoint)
              {
                add = false;
              }
            }
            else if (block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
            {
              if (block.BlockDefinition.Id.SubtypeName.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                add = false;
              }
            }
            else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(block.BlockDefinition.Id))
            {
              if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(block.BlockDefinition.Id)
                || block.BlockDefinition.Id.SubtypeName.Contains("Centered"))
              {
                add = false;
              }
            }
            else if (!AiSession.Instance.FlatWindowDefinitions.ContainsItem(block.BlockDefinition.Id))
              add = false;
          }
          
          if (add && isGroundNode)
          {
            var localAbove = localPoint + upVec;
            var slim = GetBlockAtPosition(localAbove);
            if (slim != null && slim.CubeGrid.EntityId == MainGrid.EntityId && !(slim.FatBlock is IMyLightingBlock))
            {
              if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(slim.BlockDefinition.Id))
              {
                var turretBasePosition = slim.Position - Base6Directions.GetIntVector(slim.Orientation.Up);
                if (turretBasePosition == localAbove || localAbove == slim.Position)
                  add = false;
              }
              else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
              {
                var turbineBasePosition = slim.Position;
                var turbineUpVec = Base6Directions.GetIntVector(slim.Orientation.Up);
                var midPoint = turbineBasePosition + turbineUpVec;
                var topPoint = midPoint + turbineUpVec;
                if (localAbove == turbineBasePosition || localAbove == midPoint || localAbove == topPoint)
                {
                  add = false;
                }
              }
              else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(slim.BlockDefinition.Id))
              {
                if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(slim.BlockDefinition.Id)
                  || slim.BlockDefinition.Id.SubtypeName.Contains("Centered"))
                {
                  add = false;
                }
              }
              else if (slim.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel))
              {
                if (slim.BlockDefinition.Id.SubtypeName.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  add = false;
                }
              }
              else if ((!AiSession.Instance.FlatWindowDefinitions.ContainsItem(slim.BlockDefinition.Id)
                && !AiSession.Instance.CatwalkBlockDefinitions.Contains(slim.BlockDefinition.Id)) || Base6Directions.GetIntVector(slim.Orientation.Up) != -upVec)
              {
                var actualSurface = groundPoint + gravityNorm;
                var direction = Base6Directions.GetDirection(-upVec);
                var downVec = slim.CubeGrid.WorldMatrix.GetDirectionVector(direction);
                var bottomEdge = slim.CubeGrid.GridIntegerToWorld(localAbove) + downVec * slim.CubeGrid.GridSize * 0.5;
                if (Vector3D.DistanceSquared(actualSurface, bottomEdge) < 7)
                {
                  add = false;
                }
              }
            }
          }

          if (Dirty)
          {
            return;
          }

          if (add)
          {
            var isInWater = checkForWater && WaterAPI.IsUnderwater(groundPoint);

            NodeType nType = NodeType.GridPlanet;
            if (isGroundNode)
              nType |= NodeType.Ground;
            if (isInWater)
              nType |= NodeType.Water;
            if (isTunnelNode)
              nType |= NodeType.Tunnel;

            var offset = (Vector3)(groundPoint - LocalToWorld(localPoint));
            block = GetBlockAtPosition(localPoint);

            Node newNode;
            if (!AiSession.Instance.NodeStack.TryPop(out newNode) || newNode == null)
              newNode = new Node();

            newNode.Update(localPoint, offset, this, nType, 0, MainGrid, block);

            if (checkForVoxel)
            {
              foreach (var dir in blockedVoxelEdges)
              {
                newNode.SetBlocked(dir);
              }
            }

            PlanetTileDictionary[localPoint] = newNode;

            if (addNodeBelow)
            {
              nType |= NodeType.Ground;
              offset = (Vector3)(groundPointBelow - LocalToWorld(localBelow));

              Node nodeBelow;
              if (!AiSession.Instance.NodeStack.TryPop(out nodeBelow) || nodeBelow == null)
                nodeBelow = new Node();

              nodeBelow.Update(localBelow, offset, this, nType, newNode.BlockedMask, MainGrid);
              PlanetTileDictionary[localBelow] = nodeBelow;
            }
          }
        }
      }

      GraphHasTunnel = tunnelCount > 25;
    }

    private void Planet_OnMarkForClose(MyEntity obj)
    {
      if (RootVoxel != null)
      {
        RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;
        RootVoxel.RangeChanged -= Planet_RangeChanged;
      }
    }

    private void Planet_RangeChanged(MyVoxelBase storage, Vector3I minVoxelChanged, Vector3I maxVoxelChanged, MyStorageDataTypeFlags changedData)
    {
      try
      {
        if (AiSession.Instance == null || !AiSession.Instance.Registered || !IsValid)
          return;

        using (_pendingLockObject.AcquireExclusiveUsing())
        {
          var min = minVoxelChanged;
          var max = maxVoxelChanged;

          bool found = false;
          for (int i = _voxelUpdatesNeeded.Count - 1; i >= 0; i--)
          {
            var updateItem = _voxelUpdatesNeeded[i];
            if (updateItem.Check(ref min, ref max))
            {
              found = true;
              break;
            }  
          }

          if (!found)
          {
            VoxelUpdateItem updateItem;
            if (!AiSession.Instance.VoxelUpdateItemStack.TryPop(out updateItem) || updateItem == null)
              updateItem = new VoxelUpdateItem();

            updateItem.Init(ref min, ref max);
            _voxelUpdatesNeeded.Add(updateItem);
          }

          NeedsVoxelUpdate = true;
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.Planet_RangeChanged: {ex.Message}\n{ex.StackTrace}");
      }
    }

    public void UpdateVoxels()
    {
      try
      {
        if (RootVoxel == null || RootVoxel.MarkedForClose)
        {
          NeedsVoxelUpdate = false;
          return;
        }

        if (!Ready || GraphLocked)
          return;

        using (_pendingLockObject.AcquireExclusiveUsing())
        {
          for (int i = _voxelUpdatesNeeded.Count - 1; i >= 0; i--)
          {
            var updateItem = _voxelUpdatesNeeded[i];
            if (updateItem.Update())
            {
              _voxelUpdatesNeeded.RemoveAtFast(i);
              _voxelUpdatesQueue.Enqueue(updateItem);
            }
          }

          if (_updateTask.IsComplete)
          {
            if (_updateTask.Exceptions != null)
            {
              AiSession.Instance.Logger.ClearCached();
              AiSession.Instance.Logger.AddLine($"Exceptions found during update task!\n");
              foreach (var ex in _updateTask.Exceptions)
                AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

              AiSession.Instance.Logger.LogAll();
              MyAPIGateway.Utilities.ShowNotification($"Exception during task!");
            }

            if (_voxelUpdatesQueue.Count > 0)
            {
              Ready = false;
              _updateTask = MyAPIGateway.Parallel.Start(ApplyVoxelChanges, SetReady);
            }

            NeedsVoxelUpdate = _voxelUpdatesNeeded.Count > 0;
          }
        }
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in CubeGridMap.UpdateVoxels: {ex.Message}\n{ex.StackTrace}");
      }
    }

    void ApplyVoxelChanges() 
    {
      try
      {
        //AiSession.Instance.Logger.Log($"{this}.ApplyVoxelChanges: Start");

        _positionsToRemove.Clear();
        while (_voxelUpdatesQueue.Count > 0)
        {
          var updateItem = _voxelUpdatesQueue.Dequeue();
          var updateBox = updateItem.BoundingBox;

          var minWorld = Vector3D.Transform((Vector3)updateBox.Min - RootVoxel.SizeInMetresHalf, RootVoxel.WorldMatrix);
          var maxWorld = Vector3D.Transform((Vector3)updateBox.Max - RootVoxel.SizeInMetresHalf, RootVoxel.WorldMatrix);

          if (!OBB.Contains(ref minWorld) && !OBB.Contains(ref maxWorld))
          {
            AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
            return;
          }

          var min = WorldToLocal(minWorld);
          var max = WorldToLocal(maxWorld);
          Vector3I.MinMax(ref min, ref max);

          var mapMin = Vector3I.Max(BoundingBox.Min, min - 3);
          var mapMax = Vector3I.Min(BoundingBox.Max, max + 3);

          var iter = new Vector3I_RangeIterator(ref mapMin, ref mapMax);

          while (iter.IsValid())
          {
            if (Dirty || RootVoxel == null || RootVoxel.MarkedForClose)
            {
              AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
              return;
            }

            var current = iter.Current;
            iter.MoveNext();

            Node node;
            if (PlanetTileDictionary.TryGetValue(current, out node))
            {
              if (node != null)
              {
                AiSession.Instance.NodeStack.Push(node);
              }

              PlanetTileDictionary.Remove(current);
            }

            _positionsToRemove.Add(current);
          }

          InvokePositionsRemoved(_positionsToRemove, false);

          float _;
          var gravityNorm = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
          gravityNorm.Normalize();

          var upDir = MainGrid.WorldMatrix.GetClosestDirection(-gravityNorm);
          var upVec = Base6Directions.GetIntVector(upDir);
          var box = new BoundingBoxI(mapMin, mapMax);

          CheckForPlanetTiles(ref box, ref gravityNorm, ref upVec);

          iter = new Vector3I_RangeIterator(ref mapMin, ref mapMax);

          while (iter.IsValid())
          {
            if (Dirty || RootVoxel == null || RootVoxel.MarkedForClose)
            {
              AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
              return;
            }

            var current = iter.Current;
            iter.MoveNext();

            Node node;
            if (PlanetTileDictionary.TryGetValue(current, out node))
            {
              GetBlockedNodeEdges(node);
            }
          }

          AiSession.Instance.VoxelUpdateItemStack.Push(updateItem);
        }

        //AiSession.Instance.Logger.Log($"{this}.ApplyVoxelChanges: Finished");
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in ApplyVoxelChanges: {ex.Message}\n{ex.StackTrace}");
      }
    }

    void CheckFaces(IMySlimBlock block, Vector3I normal, MyCubeBlockDefinition cubeDef = null)
    {
      if (cubeDef == null)
        cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

      Dictionary<Vector3I, HashSet<Vector3I>> faceDict;
      if (!AiSession.Instance.BlockFaceDictionary.TryGetValue(cubeDef.Id, out faceDict))
      {
        AiSession.Instance.Logger.Log($"There was no cube face dictionary found for {cubeDef.Id} (Size = {cubeDef.CubeSize}, Grid = {block.CubeGrid.DisplayName}, Position = {block.Position})", MessageType.WARNING);
        return;
      }

      bool isPassageStair = cubeDef.Context.ModName == "PassageIntersections" && cubeDef.Id.SubtypeName.EndsWith("PassageStairs_Large");
      if (isPassageStair && normal != Base6Directions.GetIntVector(block.Orientation.Up))
        return;

      if (AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(cubeDef.Id))
      {
        if (cubeDef.Id.SubtypeName.EndsWith("Raised"))
        {
          if (normal != Base6Directions.GetIntVector(block.Orientation.Up))
            return;
        }
        else if (normal != -Base6Directions.GetIntVector(block.Orientation.Up))
        {
          return;
        }
      }

      bool airTight = cubeDef.IsAirTight ?? false;
      bool allowSolar = !airTight && block.FatBlock is IMySolarPanel && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0;
      //bool allowConn = !allowSolar && block.FatBlock is IMyShipConnector && cubeDef.Id.SubtypeName == "Connector";
      bool isFlatWindow = !allowSolar && AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeDef.Id);
      bool isCylinder = !isFlatWindow && AiSession.Instance.PipeBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isAllowedConveyor = !isCylinder && AiSession.Instance.ConveyorFullBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isAutomatonsFull = !isAllowedConveyor && AiSession.Instance.AutomatonsFullBlockDefinitions.ContainsItem(cubeDef.Id);
      bool isSlopeBlock = !isAutomatonsFull && AiSession.Instance.SlopeBlockDefinitions.Contains(cubeDef.Id)
        && !AiSession.Instance.SlopedHalfBlockDefinitions.Contains(cubeDef.Id)
        && !AiSession.Instance.HalfStairBlockDefinitions.Contains(cubeDef.Id);

      bool isScaffold = !isSlopeBlock && AiSession.Instance.ScaffoldBlockDefinitions.Contains(cubeDef.Id);
      bool exclude = isScaffold && (cubeDef.Id.SubtypeName.EndsWith("Open") || cubeDef.Id.SubtypeName.EndsWith("Structure"));
      bool isPlatform = isScaffold && !exclude && cubeDef.Id.SubtypeName.EndsWith("Unsupported");

      var grid = block.CubeGrid as MyCubeGrid;
      bool needsPositionAdjusted = grid.EntityId != MainGrid.EntityId;

      Matrix matrix = new Matrix
      {
        Forward = Base6Directions.GetVector(block.Orientation.Forward),
        Left = Base6Directions.GetVector(block.Orientation.Left),
        Up = Base6Directions.GetVector(block.Orientation.Up)
      };

      if (faceDict.Count < 2)
        matrix.TransposeRotationInPlace();

      Vector3I side, center = cubeDef.Center;
      Vector3I.TransformNormal(ref normal, ref matrix, out side);
      Vector3I.TransformNormal(ref center, ref matrix, out center);
      var adjustedPosition = block.Position - center;

      foreach (var kvp in faceDict)
      {
        var cell = kvp.Key;
        Vector3I.TransformNormal(ref cell, ref matrix, out cell);
        var position = adjustedPosition + cell;

        if (isPassageStair)
        {
          var cellKey = kvp.Key;
          if (cellKey != new Vector3I(1, 0, 0))
          {
            if (needsPositionAdjusted)
              position = MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(position));

            Vector3 offset;
            if (cellKey == new Vector3I(0, 0, 0))
            {
              offset = Vector3.Zero;
            }
            else if (cellKey == new Vector3I(0, 1, 0))
            {
              offset = -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Up) * CellSize * 0.75f + -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Left) * CellSize * 0.5f;
            }
            else
            {
              offset = -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Up) * CellSize * 0.25f + -(Vector3)WorldMatrix.GetDirectionVector(block.Orientation.Left) * CellSize * 0.25f;
            }

            var node = new Node(position, offset, this, grid, block);
            node.SetNodeType(NodeType.Ground);
            OpenTileDict[position] = node;
          }

          continue;
        }

        var positionAbove = position + normal;
        var mainGridPosition = needsPositionAdjusted ? MainGrid.WorldToGridInteger(grid.GridIntegerToWorld(positionAbove)) : positionAbove;
        var cubeAbove = GetBlockAtPosition(positionAbove);
        var cubeAboveDef = cubeAbove?.BlockDefinition as MyCubeBlockDefinition;
        bool cubeAboveEmpty = cubeAbove == null || !cubeAboveDef.HasPhysics;
        bool aboveisScaffold = cubeAboveDef != null && AiSession.Instance.ScaffoldBlockDefinitions.Contains(cubeAboveDef.Id);
        bool aboveIsPassageStair = cubeAbove != null && cubeAbove.BlockDefinition.Id.SubtypeName.EndsWith("PassageStairs_Large");
        bool aboveIsConveyorCap = cubeAbove != null && AiSession.Instance.ConveyorEndCapDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id);
        bool aboveisAutomatonsFlat = cubeAbove != null && AiSession.Instance.AutomatonsFlatBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id);
        bool checkAbove = !exclude && (airTight /*|| allowConn*/ || allowSolar || isCylinder || aboveisScaffold || isAllowedConveyor || (kvp.Value?.Contains(side) ?? false));

        if (cubeAboveEmpty)
        {
          if (checkAbove)
          {
            var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
            node.SetNodeType(NodeType.Ground);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (isAutomatonsFull)
          {
            var subtype = cubeDef.Id.SubtypeName;
            if (subtype.EndsWith("WallB"))
            {
              if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) != 0)
              {
                var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
                node.SetNodeType(NodeType.Ground);
                OpenTileDict[mainGridPosition] = node;
              }
            }
            else if (subtype == "AirDuct2")
            {
              if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref normal) > 0)
              {
                var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
                node.SetNodeType(NodeType.Ground);
                OpenTileDict[mainGridPosition] = node;
              }
            }
            else
            {
              var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
              node.SetNodeType(NodeType.Ground);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (isPlatform)
          {
            if (Base6Directions.GetIntVector(block.Orientation.Up).Dot(ref normal) > 0)
            {
              var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
              node.SetNodeType(NodeType.Ground);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (isScaffold)
          {
            // TODO ??
            //AiSession.Instance.Logger.Log($"Scaffold block found: {cubeDef.Id.SubtypeName}, HasPhysics = {cubeDef.HasPhysics}");
          }
          else if (isFlatWindow)
          {
            if (block.BlockDefinition.Id.SubtypeName == "LargeWindowSquare")
            {
              if (Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
              {
                var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
                node.SetNodeType(NodeType.Ground);
                OpenTileDict[mainGridPosition] = node;
              }
            }
            else if (Base6Directions.GetIntVector(block.Orientation.Left).Dot(ref normal) < 0)
            {
              var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
              node.SetNodeType(NodeType.Ground);
              OpenTileDict[mainGridPosition] = node;
            }
            else if (block.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner")
              && Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0)
            {
              var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
              node.SetNodeType(NodeType.Ground);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (isSlopeBlock)
          {
            var leftVec = Base6Directions.GetIntVector(block.Orientation.Left);
            if (leftVec.Dot(ref normal) == 0)
              return;

            var node = new Node(mainGridPosition, Vector3.Zero, this, grid);
            var up = Base6Directions.GetIntVector(block.Orientation.Up);
            var bwd = -Base6Directions.GetIntVector(block.Orientation.Forward);
            node.SetBlocked(up);
            node.SetBlocked(bwd);
            node.SetNodeType(NodeType.Ground);

            OpenTileDict[mainGridPosition] = node;
          }
        }
        else if (!aboveIsPassageStair && (checkAbove || aboveisScaffold || aboveIsConveyorCap || aboveisAutomatonsFlat))
        {
          if (aboveisScaffold)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (aboveisAutomatonsFlat)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (aboveIsConveyorCap)
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) >= 0)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (cubeAbove.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_WindTurbine))
          {
            var blockPos = cubeAbove.Position;
            if (needsPositionAdjusted)
              blockPos = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(blockPos));

            if (blockPos != mainGridPosition)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (cubeAbove.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_MedicalRoom) && cubeAbove.BlockDefinition.Id.SubtypeName == "LargeMedicalRoom")
          {
            var relPosition = positionAbove - cubeAbove.Position;
            if (relPosition.RectangularLength() > 1)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (cubeAbove.BlockDefinition.Id.SubtypeName.IndexOf("NeonTubes", StringComparison.OrdinalIgnoreCase) >= 0)
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (cubeAbove.FatBlock is IMyInteriorLight && cubeAbove.BlockDefinition.Id.SubtypeName == "LargeLightPanel")
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("DeadBody"))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (cubeAbove.BlockDefinition.Id.SubtypeName == "RoboFactory")
          {
            var cubeAbovePosition = cubeAbove.Position;
            if (needsPositionAdjusted)
              cubeAbovePosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(cubeAbovePosition));

            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0 && positionAbove != cubeAbovePosition)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(cubeAbove.BlockDefinition.Id))
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (AiSession.Instance.RailingBlockDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (cubeAboveDef.Id.SubtypeName.StartsWith("LargeCoverWall") || cubeAboveDef.Id.SubtypeName.StartsWith("FireCover"))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (AiSession.Instance.LockerDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) > 0)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (AiSession.Instance.ArmorPanelAllDefinitions.Contains(cubeAboveDef.Id))
          {
            if (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(cubeAboveDef.Id)
              || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(cubeAboveDef.Id))
            {
              if (Base6Directions.GetIntVector(cubeAbove.Orientation.Left).Dot(ref normal) == 0)
              {
                Node node;
                if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                  node = new Node();

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                OpenTileDict[mainGridPosition] = node;
              }
            }
            else
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (AiSession.Instance.FreightBlockDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (AiSession.Instance.VanillaTurretDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            var turretBasePosition = cubeAbove.Position - Base6Directions.GetIntVector(cubeAbove.Orientation.Up);
            if (needsPositionAdjusted)
              turretBasePosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(turretBasePosition));

            if (turretBasePosition != positionAbove || cubeAboveDef.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret))
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (AiSession.Instance.CatwalkBlockDefinitions.Contains(cubeAboveDef.Id))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (AiSession.Instance.FlatWindowDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            Node node;
            if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
              node = new Node();

            node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
            OpenTileDict[mainGridPosition] = node;
          }
          else if (AiSession.Instance.HalfBlockDefinitions.ContainsItem(cubeAboveDef.Id))
          {
            if (Base6Directions.GetIntVector(cubeAbove.Orientation.Up).Dot(ref normal) != 0)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
          else if (cubeAbove.FatBlock != null)
          {
            var door = cubeAbove.FatBlock as IMyDoor;
            if (door != null)
            {
              var doorPosition = door.Position;
              if (needsPositionAdjusted)
                doorPosition = MainGrid.WorldToGridInteger(cubeAbove.CubeGrid.GridIntegerToWorld(doorPosition));

              if (door is IMyAirtightHangarDoor)
              {
                if (positionAbove != doorPosition)
                {
                  Node node;
                  if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                    node = new Node();

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  OpenTileDict[mainGridPosition] = node;
                }
              }
              else if (cubeAbove.BlockDefinition.Id.SubtypeName.StartsWith("SlidingHatchDoor"))
              {
                if (positionAbove == doorPosition)
                {
                  Node node;
                  if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                    node = new Node();

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  OpenTileDict[mainGridPosition] = node;
                }
              }
              else if (cubeAbove.BlockDefinition.Id.SubtypeName == "LargeBlockGate")
              {
                var doorCenter = door.WorldAABB.Center;
                var nextPos = MainGrid.GridIntegerToWorld(positionAbove);
                var vector = nextPos - doorCenter;

                if (vector.LengthSquared() < 8)
                {
                  Node node;
                  if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                    node = new Node();

                  node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                  OpenTileDict[mainGridPosition] = node;
                }
              }
              else
              {
                Node node;
                if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                  node = new Node();

                node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
                OpenTileDict[mainGridPosition] = node;
              }
            }
            else if (cubeAbove.FatBlock is IMyTextPanel || cubeAbove.FatBlock is IMySolarPanel || cubeAbove.FatBlock is IMyButtonPanel)
            {
              Node node;
              if (!AiSession.Instance.NodeStack.TryPop(out node) || node == null)
                node = new Node();

              node.Update(mainGridPosition, Vector3.Zero, this, NodeType.Ground, 0, grid, cubeAbove);
              OpenTileDict[mainGridPosition] = node;
            }
          }
        }
      }
    }

    public override void GetBlockedNodeEdges(Node node)
    {
      if (node.Grid == null || node.Grid.MarkedForClose)
      {
        Dirty = true;
        return;
      }

      var upDir = MainGrid.WorldMatrix.GetClosestDirection(WorldMatrix.Up);
      var upVec = Base6Directions.GetIntVector(upDir);

      var nodePos = node.Position;
      var thisBlock = node.Block;
      var blockBelowThis = GetBlockAtPosition(nodePos - upVec);

      bool thisBlockEmpty = thisBlock == null || !((MyCubeBlockDefinition)thisBlock.BlockDefinition).HasPhysics;
      bool thisIsDoor = false, thisIsSlope = false, thisIsHalfSlope = false, thisIsHalfStair = false, thisIsRamp = false, thisIsSolar = false;
      bool thisIsCatwalk = false, thisIsBtnPanel = false, thisIsLadder = false, thisIsWindowFlat = false, thisIsWindowSlope = false;
      bool thisIsHalfBlock = false, thisIsCoverWall = false, thisIsFreight = false, thisIsRailing = false, thisIsPassage = false;
      bool thisIsPanel = false, thisIsFullPanel = false, thisIsHalfPanel = false, thisIsSlopePanel = false, thisIsHalfSlopePanel = false;
      bool thisIsLocker = false, thisIsBeam = false, thisIsDeadBody = false, thisIsDeco = false, thisIsNeonTube = false, thisIsLightPanel = false;
      bool thisIsScaffold = false, thisIsPassageStair = false, thisIsConveyorCap = false, thisIsAutomatonsFlat = false;
      bool belowThisIsPanelSlope = false, belowThisIsHalfSlope = false, belowThisIsSlope = false, belowThisIsRamp = false, belowThisIsLadderOrStair = false;

      if (!thisBlockEmpty)
      {
        var def = thisBlock.BlockDefinition.Id;
        thisIsLadder = AiSession.Instance.LadderBlockDefinitions.Contains(def);
        thisIsDoor = !thisIsLadder && thisBlock.FatBlock is IMyDoor; // to make sure digi's retractable ladder is marked as ladder
        thisIsSolar = thisBlock.FatBlock is IMySolarPanel;
        thisIsDeadBody = def.SubtypeName.StartsWith("DeadBody");
        thisIsFreight = AiSession.Instance.FreightBlockDefinitions.ContainsItem(def);
        thisIsSlope = AiSession.Instance.SlopeBlockDefinitions.Contains(def);
        thisIsHalfStair = thisIsSlope && AiSession.Instance.HalfStairBlockDefinitions.Contains(def);
        thisIsHalfSlope = thisIsSlope && !thisIsHalfStair && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
        thisIsPassageStair = thisIsSlope && thisBlock.BlockDefinition.Context.ModName == "PassageIntersections" && def.SubtypeName.EndsWith("PassageStairs_Large");
        thisIsPassage = !thisIsPassageStair && AiSession.Instance.PassageBlockDefinitions.Contains(def);
        thisIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
        thisIsCatwalk = AiSession.Instance.CatwalkBlockDefinitions.Contains(def);
        thisIsBtnPanel = AiSession.Instance.BtnPanelDefinitions.ContainsItem(def);
        thisIsWindowFlat = AiSession.Instance.FlatWindowDefinitions.ContainsItem(def);
        thisIsWindowSlope = AiSession.Instance.AngledWindowDefinitions.ContainsItem(def);
        thisIsCoverWall = def.SubtypeName.StartsWith("LargeCoverWall") || def.SubtypeName.StartsWith("FireCover");
        thisIsHalfBlock = def.SubtypeName.EndsWith("HalfArmorBlock") || def.SubtypeName.EndsWith("Concrete_Half_Block")
          || def.SubtypeName.StartsWith("WindowWall") || def.SubtypeName.StartsWith("StorageShelf");
        thisIsRailing = AiSession.Instance.RailingBlockDefinitions.ContainsItem(def);
        thisIsLocker = AiSession.Instance.LockerDefinitions.ContainsItem(def);
        thisIsBeam = AiSession.Instance.BeamBlockDefinitions.ContainsItem(def);
        thisIsPanel = AiSession.Instance.ArmorPanelAllDefinitions.Contains(def);
        thisIsDeco = AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(def);
        thisIsNeonTube = def.SubtypeName.IndexOf("NeonTubes", StringComparison.OrdinalIgnoreCase) >= 0;
        thisIsLightPanel = thisBlock.FatBlock is IMyInteriorLight && def.SubtypeName == "LargeLightPanel";
        thisIsScaffold = AiSession.Instance.ScaffoldBlockDefinitions.Contains(def);
        thisIsConveyorCap = AiSession.Instance.ConveyorEndCapDefinitions.ContainsItem(def);
        thisIsAutomatonsFlat = AiSession.Instance.AutomatonsFlatBlockDefinitions.ContainsItem(def);

        if (thisIsPanel)
        {
          thisIsFullPanel = AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(def);
          thisIsHalfPanel = AiSession.Instance.ArmorPanelHalfDefinitions.ContainsItem(def);
          thisIsSlopePanel = AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def);
          thisIsHalfSlopePanel = AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def);
        }
      }

      if (blockBelowThis != null)
      {
        var def = blockBelowThis.BlockDefinition.Id;
        belowThisIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
        belowThisIsHalfSlope = AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
        belowThisIsPanelSlope = !belowThisIsRamp && !belowThisIsHalfSlope
          && (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def) || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def));
        belowThisIsSlope = !belowThisIsPanelSlope
          && (belowThisIsRamp || AiSession.Instance.AngledWindowDefinitions.ContainsItem(def) || (AiSession.Instance.SlopeBlockDefinitions.Contains(def)
          && !belowThisIsHalfSlope && !AiSession.Instance.HalfStairBlockDefinitions.Contains(def)));
        belowThisIsLadderOrStair = AiSession.Instance.LadderBlockDefinitions.Contains(def) || def.SubtypeName.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0;
      }

      foreach (var dirVec in AiSession.Instance.CardinalDirections)
      {
        var next = nodePos + dirVec;

        Node nextNode;
        if (!TryGetNodeForPosition(next, out nextNode))
        {
          node.SetBlocked(dirVec); 
          continue;
        }

        var dir = Base6Directions.GetDirection(dirVec);
        var oppositeDir = Base6Directions.GetOppositeDirection(dir);

        if (thisBlockEmpty && belowThisIsLadderOrStair 
          && (dir == blockBelowThis.Orientation.Left || oppositeDir == blockBelowThis.Orientation.Left))
        {
          node.SetBlocked(dirVec);
          continue;
        }

        var nextBlock = nextNode.Block;
        var blockBelowNext = GetBlockAtPosition(next - upVec);

        bool nextBlockEmpty = nextBlock == null || !((MyCubeBlockDefinition)nextBlock.BlockDefinition).HasPhysics;
        bool nextIsDoor = false, nextIsSlope = false, nextIsHalfSlope = false, nextIsHalfStair = false, nextIsRamp = false, nextIsSolar = false;
        bool nextIsCatwalk = false, nextIsBtnPanel = false, nextIsLadder = false, nextIsWindowFlat = false, nextIsWindowSlope = false;
        bool nextIsHalfBlock = false, nextIsCoverWall = false, nextIsFreight = false, nextIsRailing = false, nextIsPassage = false;
        bool nextIsPanel = false, nextIsFullPanel = false, nextIsHalfPanel = false, nextIsSlopePanel = false, nextIsHalfSlopePanel = false;
        bool nextIsLocker = false, nextIsBeam = false, nextIsDeadBody = false, nextIsDeco = false, nextIsNeonTube = false, nextIsLightPanel = false;
        bool nextIsScaffold = false, nextIsPassageStair = false, nextIsConveyorCap = false, nextIsAutomatonsFlat = false;
        bool belowNextIsPanelSlope = false, belowNextIsHalfSlope = false, belowNextIsSlope = false, belowNextIsRamp = false;

        if (!nextBlockEmpty)
        {
          var def = nextBlock.BlockDefinition.Id;
          nextIsLadder = AiSession.Instance.LadderBlockDefinitions.Contains(def);
          nextIsDoor = !nextIsLadder && nextBlock.FatBlock is IMyDoor; // to make sure digi's retractable ladder is marked as ladder
          nextIsSolar = nextBlock.FatBlock is IMySolarPanel;
          nextIsDeadBody = def.SubtypeName.StartsWith("DeadBody");
          nextIsFreight = AiSession.Instance.FreightBlockDefinitions.ContainsItem(def);
          nextIsSlope = AiSession.Instance.SlopeBlockDefinitions.Contains(def);
          nextIsHalfStair = nextIsSlope && AiSession.Instance.HalfStairBlockDefinitions.Contains(def);
          nextIsHalfSlope = nextIsSlope && !nextIsHalfStair && AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
          nextIsPassageStair = nextIsSlope && nextBlock.BlockDefinition.Context.ModName == "PassageIntersections" && def.SubtypeName.EndsWith("PassageStairs_Large");
          nextIsPassage = !nextIsPassageStair && AiSession.Instance.PassageBlockDefinitions.Contains(def);
          nextIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
          nextIsCatwalk = AiSession.Instance.CatwalkBlockDefinitions.Contains(def);
          nextIsBtnPanel = AiSession.Instance.BtnPanelDefinitions.ContainsItem(def);
          nextIsWindowFlat = AiSession.Instance.FlatWindowDefinitions.ContainsItem(def);
          nextIsWindowSlope = AiSession.Instance.AngledWindowDefinitions.ContainsItem(def);
          nextIsCoverWall = def.SubtypeName.StartsWith("LargeCoverWall") || def.SubtypeName.StartsWith("FireCover");
          nextIsHalfBlock = def.SubtypeName.EndsWith("HalfArmorBlock") || def.SubtypeName.EndsWith("Concrete_Half_Block")
            || def.SubtypeName.StartsWith("WindowWall") || def.SubtypeName.StartsWith("StorageShelf");
          nextIsRailing = AiSession.Instance.RailingBlockDefinitions.ContainsItem(def);
          nextIsLocker = AiSession.Instance.LockerDefinitions.ContainsItem(def);
          nextIsBeam = AiSession.Instance.BeamBlockDefinitions.ContainsItem(def);
          nextIsPanel = AiSession.Instance.ArmorPanelAllDefinitions.Contains(def);
          nextIsDeco = AiSession.Instance.DecorativeBlockDefinitions.ContainsItem(def);
          nextIsNeonTube = def.SubtypeName.IndexOf("NeonTubes", StringComparison.OrdinalIgnoreCase) >= 0;
          nextIsLightPanel = nextBlock.FatBlock is IMyInteriorLight && def.SubtypeName == "LargeLightPanel";
          nextIsScaffold = AiSession.Instance.ScaffoldBlockDefinitions.Contains(def);
          nextIsConveyorCap = AiSession.Instance.ConveyorEndCapDefinitions.ContainsItem(def);
          nextIsAutomatonsFlat = AiSession.Instance.AutomatonsFlatBlockDefinitions.ContainsItem(def);

          if (nextIsPanel)
          {
            nextIsFullPanel = AiSession.Instance.ArmorPanelFullDefinitions.ContainsItem(def);
            nextIsHalfPanel = AiSession.Instance.ArmorPanelHalfDefinitions.ContainsItem(def);
            nextIsSlopePanel = AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def);
            nextIsHalfSlopePanel = AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def);
          }
        }

        if (blockBelowNext != null)
        {
          var def = blockBelowNext.BlockDefinition.Id;
          belowNextIsRamp = AiSession.Instance.RampBlockDefinitions.Contains(def);
          belowNextIsHalfSlope = AiSession.Instance.SlopedHalfBlockDefinitions.Contains(def);
          belowNextIsPanelSlope = !belowNextIsRamp && !belowNextIsHalfSlope
            && (AiSession.Instance.ArmorPanelSlopeDefinitions.ContainsItem(def) || AiSession.Instance.ArmorPanelHalfSlopeDefinitions.ContainsItem(def));
          belowNextIsSlope = !belowNextIsPanelSlope
            && (belowNextIsRamp || AiSession.Instance.AngledWindowDefinitions.ContainsItem(def) || (AiSession.Instance.SlopeBlockDefinitions.Contains(def)
            && !belowNextIsHalfSlope && !AiSession.Instance.HalfStairBlockDefinitions.Contains(def)));
        }

        if (node.IsGridNodePlanetTile && !nextNode.IsGridNodePlanetTile)
        {
          if (!nextBlockEmpty)
          {
            var nextOr = nextBlock.Orientation;
            var downDir = Base6Directions.GetOppositeDirection(upDir);

            if (nextIsSlope || nextIsHalfSlope || nextIsHalfStair)
            {
              if (nextOr.Up == upDir)
              {
                if (dir != nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Forward == downDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsSlopePanel || nextIsHalfSlopePanel)
            {
              if (nextOr.Forward == upDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Forward == downDir)
              {
                if (dir != nextOr.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Up == downDir)
              {
                if (dir != nextOr.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Up == upDir)
              {
                if (oppositeDir != nextOr.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsRamp)
            {
              if (nextOr.Up == upDir)
              {
                if (oppositeDir != nextOr.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Forward == upDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsLightPanel)
            {
              if (dir == nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsNeonTube)
            {
              var subtype = nextBlock.BlockDefinition.Id.SubtypeName;
              if (subtype.EndsWith("U"))
              {
                if (dir == nextOr.Up || dir == nextOr.Left || oppositeDir == nextOr.Left)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (subtype.EndsWith("BendUp"))
              {
                if (dir == nextOr.Up || oppositeDir == nextOr.Left)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (subtype != "LargeNeonTubesBendDown" && dir == nextOr.Up)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsWindowSlope)
            {
              if (nextOr.Up == upDir)
              {
                if (oppositeDir != nextOr.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Up == downDir)
              {
                if (dir != nextOr.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Forward == upDir)
              {
                if (oppositeDir != nextOr.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextOr.Forward == downDir)
              {
                if (dir != nextOr.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsLadder)
            {
              if (nextOr.Up != upDir && nextOr.Up != downDir)
              {
                node.SetBlocked(dirVec); 
                continue;
              }

              if (nextBlock.BlockDefinition.Id.SubtypeName == "LadderShaft")
              {
                if (dir == nextOr.Left || oppositeDir == nextOr.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
            }
            else if (nextIsBeam)
            {
              var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
              if (blockSubtype.IndexOf("Slope", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                if (blockSubtype.EndsWith("HalfSlope"))
                {
                  if (nextOr.Left == downDir)
                  {
                    if (oppositeDir != nextOr.Forward)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (nextOr.Forward == upDir)
                  {
                    if (dir != nextOr.Left)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (blockSubtype.EndsWith("BeamBlockSlope") || blockSubtype.IndexOf("2x1") >= 0)
                {
                  if (nextOr.Left == upDir)
                  {
                    if (oppositeDir != nextOr.Forward)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (nextOr.Forward == upDir)
                  {
                    if (oppositeDir != nextOr.Left)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
                else
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsScaffold)
            {
              // TODO
            }
            else if (nextIsDoor)
            {
              var door = nextBlock.FatBlock as IMyDoor;
              if (door is IMyAirtightHangarDoor)
              {
                var doorPosition = door.Position;
                if (door.CubeGrid.EntityId != MainGrid.EntityId)
                  doorPosition = MainGrid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(doorPosition));

                if (next == doorPosition)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (door.BlockDefinition.SubtypeName.StartsWith("SlidingHatchDoor"))
              {
                if (dir != door.Orientation.Up && oppositeDir != door.Orientation.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (door.BlockDefinition.SubtypeName == "RotaryAirlockCorner")
              {
                if (oppositeDir != door.Orientation.Left && dir != door.Orientation.Forward)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (door.BlockDefinition.SubtypeName == "LargeBlockGate")
              {
                // Large DLC gate
                var center = door.WorldAABB.Center;
                var nextPos = MainGrid.GridIntegerToWorld(nextNode.Position);
                var vector = nextPos - center;

                if (vector.LengthSquared() > 8)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }

                var doorMatrix = MatrixD.Transpose(door.WorldMatrix);
                Vector3D.Rotate(ref vector, ref doorMatrix, out vector);
                if (vector.Y > 2 || vector.X > 3)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (!nextIsPassage && !nextIsSolar && !nextIsHalfBlock)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (dirVec.RectangularLength() > 1)
          {
            node.SetBlocked(dirVec); 
            continue;
          }
        }

        if (!thisBlockEmpty)
        {
          if (thisIsPassage && !thisIsPassageStair)
          {
            var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            var blockFwd = thisBlock.Orientation.Forward;
            var blockUp = thisBlock.Orientation.Up;

            if (AiSession.Instance.PassageIntersectionDefinitions.Contains(thisBlock.BlockDefinition.Id))
            {
              if (blockSubtype.StartsWith("Lighted"))
                blockSubtype = blockSubtype.Substring(7);

              if (thisBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Ladder2))
              {
                if (dir == thisBlock.Orientation.Left || oppositeDir == thisBlock.Orientation.Left || dir == blockFwd)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }

                if (oppositeDir == blockFwd && blockSubtype.IndexOf("enc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype == "Passage")
              {
                if (dir != blockUp && oppositeDir != blockUp)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageTop"))
              {
                if (dir == blockUp)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
                else if (oppositeDir == blockUp && blockSubtype.IndexOf("enc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("HalfPassage"))
              {
                if (blockSubtype.StartsWith("HalfPassageCorner"))
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
                else if (blockSubtype.StartsWith("HalfPassagePillar"))
                {
                  if (dir != blockFwd && dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (blockSubtype.StartsWith("HalfPassageGlass"))
                {
                  if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left && oppositeDir != blockFwd)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (blockSubtype.StartsWith("HalfPassageL"))
                {
                  if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left && dir != blockFwd)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
              }
              else if (blockSubtype.StartsWith("PassagePillar"))
              {
                if (dir == blockUp || oppositeDir == blockUp)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageL"))
              {
                if (dir != blockUp && dir != thisBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageT"))
              {
                if (blockSubtype.StartsWith("PassageTVert"))
                {
                  if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (blockSubtype.StartsWith("PassageTGlass"))
                {
                  if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left && dir != thisBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageGlass"))
              {
                if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageCargo") || blockSubtype.StartsWith("PassageConveyor"))
              {
                if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (thisBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_TextPanel))
              {
                if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageDoor") || blockSubtype.StartsWith("PassageAirlock"))
              {
                if (blockSubtype.StartsWith("PassageDoorSide"))
                {
                  if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left && oppositeDir != blockFwd)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (dir != blockFwd && oppositeDir != blockFwd) // airlock and regular door are fwd / bwd only
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("Passage4Way"))
              {
                if (dir == blockFwd || oppositeDir == blockFwd)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
            }
            else if (thisBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Passage))
            {
              if (dir != blockUp && oppositeDir != blockUp)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (dir == blockUp || oppositeDir == blockUp)
            {
              node.SetBlocked(dirVec);
              continue;
            }
            else if (blockSubtype.StartsWith("Passage2"))
            {
              var blockLeft = thisBlock.Orientation.Left;
              if (blockSubtype.EndsWith("Wall"))
              {
                if (dir != blockFwd && dir != blockLeft && oppositeDir != blockLeft)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (dir != blockLeft && oppositeDir != blockLeft)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (blockSubtype.EndsWith("Wall") || blockSubtype.EndsWith("Tjunction"))
            {
              if (dir != blockFwd && oppositeDir != blockFwd && oppositeDir != thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (blockSubtype.EndsWith("Corner"))
            {
              if (oppositeDir != blockFwd && oppositeDir != thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (!blockSubtype.EndsWith("Intersection") && dir != blockFwd && oppositeDir != blockFwd)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (thisIsConveyorCap)
          {
            if (oppositeDir == thisBlock.Orientation.Up)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (thisIsAutomatonsFlat)
          {
            if (dir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (thisIsDeadBody)
          {
            if (CheckCatwalkForRails(thisBlock, dirVec))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsScaffold)
          {
            var blockOr = thisBlock.Orientation;
            var subtype = thisBlock.BlockDefinition.Id.SubtypeName;
            if (subtype.EndsWith("Platform"))
            {
              if (dir == blockOr.Left || dir == blockOr.Up || oppositeDir == blockOr.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (subtype.EndsWith("Structure"))
            {
              if (dir == blockOr.Left || oppositeDir == blockOr.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (subtype.EndsWith("Unsupported"))
            {
              if (dir == blockOr.Up)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
          }
          else if (thisIsLightPanel)
          {
            if (oppositeDir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsNeonTube)
          {
            var subtype = thisBlock.BlockDefinition.Id.SubtypeName;
            var thisOr = thisBlock.Orientation;
            if (subtype.EndsWith("U"))
            {
              if (oppositeDir == thisOr.Up || oppositeDir == thisOr.Left || dir == thisOr.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype.EndsWith("BendUp"))
            {
              if (oppositeDir == thisOr.Up || dir == thisOr.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype != "LargeNeonTubesBendDown" && oppositeDir == thisOr.Up)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsDoor)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("SlidingHatchDoor"))
            {
              if (dir != thisBlock.Orientation.Up && oppositeDir != thisBlock.Orientation.Up)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (thisBlock.BlockDefinition.Id.SubtypeName == "RotaryAirlockCorner")
            {
              if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (dir != thisBlock.Orientation.Forward && oppositeDir != thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsDeco)
          {
            var def = thisBlock.BlockDefinition.Id;
            var subtype = def.SubtypeName;
            if (subtype == "LargeBlockPlanters" || subtype == "LargeBlockKitchen" || subtype == "LargeBlockBarCounter"
              || subtype == "LargeBlockDesk" || subtype == "LargeBlockDeskChairless")
            {
              if (dir == thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "LargeBlockCouch")
            {
              if (oppositeDir == thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "LargeBlockCouchCorner")
            {
              if (dir == thisBlock.Orientation.Left || oppositeDir == thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "LargeBlockBarCounterCorner")
            {
              if (oppositeDir == thisBlock.Orientation.Left || dir == thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "Shower")
            {
              if (oppositeDir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype.EndsWith("Toilet"))
            {
              if (oppositeDir == thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "Jukebox" || subtype == "AtmBlock" || subtype == "FoodDispenser" || subtype == "VendingMachine")
            {
              if (dir == thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
          else if (thisIsSlope)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName == "GratedStairs")
            {
              if (dir == thisBlock.Orientation.Left || oppositeDir == thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }

            if (thisIsHalfSlope)
            {
              bool dirIsForwardOfThis = dir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Forward;
              if (nextIsDoor && dirIsForwardOfThis)
              {
                var door = nextBlock.FatBlock as IMyDoor;
                if (door is IMyAirtightHangarDoor)
                {
                  var doorPosition = door.Position;
                  if (door.CubeGrid.EntityId != MainGrid.EntityId)
                    doorPosition = MainGrid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(doorPosition));

                  if (next == doorPosition)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (door.BlockDefinition.SubtypeName.StartsWith("SlidingHatchDoor"))
                {
                  if (dir != door.Orientation.Up && oppositeDir != door.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (door.BlockDefinition.SubtypeName == "RotaryAirlockCorner")
                {
                  if (oppositeDir != door.Orientation.Left && dir != door.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (door.BlockDefinition.SubtypeName == "LargeBlockGate")
                {
                  // Large DLC gate
                  var center = door.WorldAABB.Center;
                  var nextPos = MainGrid.GridIntegerToWorld(nextNode.Position);
                  var vector = nextPos - center;

                  if (vector.LengthSquared() > 8)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  var doorMatrix = MatrixD.Transpose(door.WorldMatrix);
                  Vector3D.Rotate(ref vector, ref doorMatrix, out vector);
                  if (vector.Y > 2 || vector.X > 3)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextIsLightPanel)
              {
                if (dir == nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (!nextIsHalfBlock && !dirIsForwardOfThis)
              {
                if ((belowThisIsRamp || belowThisIsSlope || belowThisIsHalfSlope) && ExemptNodesUpper.Contains(nodePos))
                {
                  var belowThisPos = blockBelowThis.Position;
                  if (blockBelowThis.CubeGrid.EntityId != MainGrid.EntityId)
                    belowThisPos = MainGrid.WorldToGridInteger(blockBelowThis.CubeGrid.GridIntegerToWorld(belowThisPos));

                  var nextIsBlockBelow = next == belowThisPos;

                  if (!nextIsBlockBelow)
                  {
                    var dotUp = Base6Directions.GetIntVector(blockBelowThis.Orientation.Up).Dot(ref upVec);
                    var dotFwd = Base6Directions.GetIntVector(blockBelowThis.Orientation.Forward).Dot(ref upVec);
                    bool valid = true;

                    if (dotUp > 0)
                    {
                      if (dir != blockBelowThis.Orientation.Forward
                        && oppositeDir != blockBelowThis.Orientation.Forward
                        && oppositeDir != blockBelowThis.Orientation.Up)
                      {
                        valid = false;
                      }
                    }
                    else if (dotFwd > 0)
                    {
                      if (dir != blockBelowThis.Orientation.Up
                        && oppositeDir != blockBelowThis.Orientation.Up
                        && oppositeDir != blockBelowThis.Orientation.Forward)
                      {
                        valid = false;
                      }
                    }
                    else if (dotFwd < 0 && belowThisIsHalfSlope)
                    {
                      if (dir != blockBelowThis.Orientation.Up
                        && oppositeDir != blockBelowThis.Orientation.Up
                        && dir != blockBelowThis.Orientation.Forward)
                      {
                        valid = false;
                      }
                    }
                    else
                    {
                      valid = false;
                    }

                    if (!valid)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
                else if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip")
                  && (upDir == thisBlock.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == thisBlock.Orientation.Left))
                {
                  if (oppositeDir == thisBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (ExemptNodesSide.Contains(nodePos))
                {
                  if (dir != thisBlock.Orientation.Up && dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else if (thisIsPassageStair)
            {
              if (dir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec);
                continue;
              }

              var relativeVector = nodePos - thisBlock.Position;
              var blockUpVec = Base6Directions.GetIntVector(thisBlock.Orientation.Up);
              var blockFwdVec = -Base6Directions.GetIntVector(thisBlock.Orientation.Left);
             
              if (relativeVector == Vector3I.Zero) // base of stair
              {
                if (dir != thisBlock.Orientation.Left && dir != thisBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (relativeVector == blockUpVec) // above base of stair, needed in order to complete pathing link
              {
                if (oppositeDir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (relativeVector == blockUpVec + blockFwdVec) // top of stair
              {
                if (dir != thisBlock.Orientation.Left && oppositeDir != thisBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else
            {
              bool thisIsSlopeBase = thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("BlockArmorSlope2Base");
              if (nextIsHalfBlock && thisIsSlopeBase)
              {
                if (upDir != thisBlock.Orientation.Up || oppositeDir != thisBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (nextIsHalfSlope)
              {
                if (!ExemptNodesUpper.Contains(next) && dir != thisBlock.Orientation.Up && oppositeDir != thisBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (dir != thisBlock.Orientation.Up)
              {
                if (oppositeDir != thisBlock.Orientation.Forward)
                {
                  if (!thisIsHalfStair || oppositeDir != thisBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (!nextBlockEmpty)
                {
                  if (nextBlock?.FatBlock is IMyTextPanel)
                  {
                    if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (nextIsLightPanel)
                  {
                    if (dir == nextBlock.Orientation.Forward)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (!nextBlockEmpty && !nextIsCatwalk && !nextIsRailing && !nextIsWindowFlat && !nextIsWindowSlope
                  && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                  && !ExemptNodesUpper.Contains(next) && !ExemptNodesUpper.Contains(nodePos))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }

              if (dir == thisBlock.Orientation.Up && !nextBlockEmpty && nextBlock != thisBlock)
              {
                if (nextIsHalfStair)
                {
                  if (!ExemptNodesUpper.Contains(nodePos) && thisBlock.Orientation.Up != nextBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                {
                  if (!ExemptNodesUpper.Contains(next) && (oppositeDir != upDir || !ExemptNodesUpper.Contains(nodePos)))
                  {
                    if (dir != thisBlock.Orientation.Up || !ExemptNodesSide.Contains(next))
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
              }
            }
          }
          else if (thisIsPanel)
          {
            var blockLeft = thisBlock.Orientation.Left;
            var blockFwd = thisBlock.Orientation.Forward;
            var blockUp = thisBlock.Orientation.Up;

            if (thisIsFullPanel)
            {
              if (oppositeDir == blockLeft)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (thisIsHalfPanel)
            {
              if (oppositeDir == blockLeft && thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("LargeArmorHalf"))
              {
                if (Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else if (thisIsSlopePanel)
            {
              var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
              var downDir = Base6Directions.GetOppositeDirection(upDir);
              bool isSlope = blockSubtype.StartsWith("LargeArmor2x1SlopedPanel");
              bool isSlopeTip = isSlope && blockSubtype.StartsWith("LargeArmor2x1SlopedPanelTip");
              bool isBase = isSlope && !isSlopeTip;
              bool isDown = dir == downDir;

              if (isDown || (dir != blockUp && dir != blockFwd))
              {
                if (isSlopeTip)
                {
                  var positionBelow = nodePos - upVec;
                  var blockBelow = GetBlockAtPosition(positionBelow); 

                  if (blockBelow == null || blockBelow.Orientation != thisBlock.Orientation)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  var blockBelowSubtype = blockBelow.BlockDefinition.Id.SubtypeName;
                  if (blockBelowSubtype == blockSubtype || !blockBelowSubtype.StartsWith("LargeArmor2x1SlopedPanel"))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (!isDown && blockSubtype.StartsWith("LargeArmorSlopedPanel"))
                {
                  if (blockFwd == downDir || blockUp == downDir)
                  {
                    if (oppositeDir != blockFwd && oppositeDir != blockUp)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
                else
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }

              if (isBase)
              {
                if (dir == blockUp && Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
                {
                  var positionAbove = nodePos + upVec;
                  var blockAbove = GetBlockAtPosition(positionAbove); 
                  if (blockAbove == null || blockAbove.Orientation != thisBlock.Orientation)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  var blockAboveSubtype = blockAbove.BlockDefinition.Id.SubtypeName;
                  if (!blockAboveSubtype.StartsWith("LargeArmor2x1SlopedPanelTip"))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
            }
            else if (thisIsHalfSlopePanel)
            {
              if (dir != blockUp && dir != blockFwd)
              {
                node.SetBlocked(dirVec); 
                continue;
              }

              var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
              if (blockSubtype.StartsWith("LargeArmor2x1HalfSlopedPanel") && dir == blockUp && Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
              {
                var positionAbove = nodePos + upVec;
                var blockAbove = GetBlockAtPosition(positionAbove); 
                if (blockAbove == null || blockAbove.Orientation != thisBlock.Orientation)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }

                var blockAboveSubtype = blockAbove.BlockDefinition.Id.SubtypeName;
                if (!blockAboveSubtype.StartsWith("LargeArmor2x1HalfSlopedTip"))
                {
                  node.SetBlocked(dirVec);
                  continue;
                }

                var tail = blockSubtype.Substring(blockSubtype.Length - 4);
                if (!blockAboveSubtype.EndsWith(tail))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else // miscellaneous panels
            {
              if (thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Inv"))
              {
                if (dir == blockLeft)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (oppositeDir == blockLeft)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
          else if (thisIsLocker)
          {
            var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            if (blockSubtype == "LargeBlockLockerRoom")
            {
              if (oppositeDir != thisBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (blockSubtype == "LargeBlockLockerRoomCorner")
            {
              if (dir == thisBlock.Orientation.Forward || oppositeDir == thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (dir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsBeam)
          {
            var blockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            var blockLeft = thisBlock.Orientation.Left;
            var blockFwd = thisBlock.Orientation.Forward;

            if (blockSubtype.EndsWith("Slope"))
            {
              if (!blockSubtype.EndsWith("HalfSlope") && dir != blockLeft && oppositeDir != blockLeft)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (blockSubtype.EndsWith("End"))
            {
              if (oppositeDir != blockFwd)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (blockSubtype.EndsWith("TJunction"))
            {
              if (dir != blockFwd)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (dir != blockFwd && oppositeDir != blockFwd)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsFreight)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName != "Freight1" && oppositeDir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
            else if (nextIsFreight)
            {
              if (thisBlock.Orientation.Up != nextBlock.Orientation.Up)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
              else if (thisBlock.Orientation.Forward != nextBlock.Orientation.Forward)
              {
                var opp = Base6Directions.GetOppositeDirection(thisBlock.Orientation.Forward);
                var rightVec = -Base6Directions.GetIntVector(thisBlock.Orientation.Left);

                if (opp == nextBlock.Orientation.Forward && rightVec == (next - nodePos))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
          }
          else if (thisIsHalfBlock)
          {
            if (dir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsCoverWall)
          {
            var thisBlockSubtype = thisBlock.BlockDefinition.Id.SubtypeName;
            if (oppositeDir == thisBlock.Orientation.Forward && !thisBlockSubtype.EndsWith("Half"))
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (oppositeDir == thisBlock.Orientation.Left && thisBlockSubtype.EndsWith("Corner"))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsRamp)
          {
            if (dir != thisBlock.Orientation.Up)
            {
              if (dir != thisBlock.Orientation.Forward)
              {
                if (oppositeDir != thisBlock.Orientation.Forward && !ExemptNodesSide.Contains(nodePos))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (!nextBlockEmpty && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward))
              {
                if (nextIsDoor)
                {
                  var door = nextBlock.FatBlock as IMyDoor;
                  if (door is IMyAirtightHangarDoor)
                  {
                    var doorPosition = door.Position;
                    if (door.CubeGrid.EntityId != MainGrid.EntityId)
                      doorPosition = MainGrid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(doorPosition));

                    if (next == doorPosition)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (door.BlockDefinition.SubtypeName.StartsWith("SlidingHatchDoor"))
                  {
                    if (dir != door.Orientation.Up && oppositeDir != door.Orientation.Up)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (door.BlockDefinition.SubtypeName == "RotaryAirlockCorner")
                  {
                    if (oppositeDir != door.Orientation.Left && dir != door.Orientation.Forward)
                    {
                      node.SetBlocked(dirVec);
                      continue;
                    }
                  }
                  else if (door.BlockDefinition.SubtypeName == "LargeBlockGate")
                  {
                    // Large DLC gate
                    var center = door.WorldAABB.Center;
                    var nextPos = MainGrid.GridIntegerToWorld(nextNode.Position);
                    var vector = nextPos - center;

                    if (vector.LengthSquared() > 8)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }

                    var doorMatrix = MatrixD.Transpose(door.WorldMatrix);
                    Vector3D.Rotate(ref vector, ref doorMatrix, out vector);
                    if (vector.Y > 2 || vector.X > 3)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (!nextIsRamp || nextBlock != thisBlock || dir != thisBlock.Orientation.Forward)
                {
                  if (!ExemptNodesUpper.Contains(next) && !ExemptNodesUpper.Contains(nodePos))
                  {
                    if (!nextIsDeadBody || CheckCatwalkForRails(nextBlock, -dirVec))
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
              }
            }
            else if (!nextBlockEmpty && nextBlock != thisBlock)
            {
              if (nextIsDoor)
              {
                var door = nextBlock.FatBlock as IMyDoor;
                if (door is IMyAirtightHangarDoor)
                {
                  var doorPosition = door.Position;
                  if (door.CubeGrid.EntityId != MainGrid.EntityId)
                    doorPosition = MainGrid.WorldToGridInteger(door.CubeGrid.GridIntegerToWorld(doorPosition));

                  if (next == doorPosition)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (door.BlockDefinition.SubtypeName.StartsWith("SlidingHatchDoor"))
                {
                  if (dir != door.Orientation.Up && oppositeDir != door.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (door.BlockDefinition.SubtypeName == "RotaryAirlockCorner")
                {
                  if (oppositeDir != door.Orientation.Left && dir != door.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (door.BlockDefinition.SubtypeName == "LargeBlockGate")
                {
                  // Large DLC gate
                  var center = door.WorldAABB.Center;
                  var nextPos = MainGrid.GridIntegerToWorld(nextNode.Position);
                  var vector = nextPos - center;

                  if (vector.LengthSquared() > 8)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  var doorMatrix = MatrixD.Transpose(door.WorldMatrix);
                  Vector3D.Rotate(ref vector, ref doorMatrix, out vector);
                  if (vector.Y > 2 || vector.X > 3)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextIsLightPanel)
              {
                if (dir == nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (!nextIsCatwalk && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward))
              {
                if (!ExemptNodesUpper.Contains(next) && (!ExemptNodesUpper.Contains(nodePos) || blockBelowThis != nextBlock))
                {
                  if ((!ExemptNodesSide.Contains(next) && !ExemptNodesSide.Contains(nodePos)) || dir != thisBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
            }
          }
          else if (thisIsCatwalk)
          {
            if (AiSession.Instance.GratedCatwalkExpansionBlocks.Contains(thisBlock.BlockDefinition.Id) && thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Raised"))
            {
              if (dir == thisBlock.Orientation.Up)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (oppositeDir == thisBlock.Orientation.Up)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
            else if (CheckCatwalkForRails(thisBlock, dirVec))
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("CatwalkHalf") && oppositeDir == thisBlock.Orientation.Left)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (thisIsRailing)
          {
            // added the rail sides to the catwalk rail array bc lazy..
            if (CheckCatwalkForRails(thisBlock, dirVec))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsWindowFlat)
          {
            Base6Directions.Direction sideWithPane;
            if (thisBlock.BlockDefinition.Id.SubtypeName == "LargeWindowSquare" || thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner"))
              sideWithPane = thisBlock.Orientation.Forward;
            else
              sideWithPane = Base6Directions.GetOppositeDirection(thisBlock.Orientation.Left);

            if (dir == sideWithPane)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsSolar)
          {
            if (dir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsWindowSlope)
          {
            if (thisBlock.BlockDefinition.Id.SubtypeName == "LargeWindowEdge") // can only walk up one side
            {
              bool dirIsUp = dir == thisBlock.Orientation.Up;

              if (!dirIsUp)
              {
                if (dir != thisBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
                else if (!nextBlockEmpty)
                {
                  if (nextBlock?.FatBlock is IMyTextPanel)
                  {
                    if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (nextIsCatwalk)
                  {
                    if (dir == nextBlock.Orientation.Up || CheckCatwalkForRails(nextBlock, -dirVec))
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (nextIsRailing)
                  {
                    if (CheckCatwalkForRails(nextBlock, -dirVec))
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else if (!nextBlockEmpty)
              {
                if (nextBlock?.FatBlock is IMyTextPanel)
                {
                  if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (nextIsCatwalk)
                {
                  if (dir == nextBlock.Orientation.Up || CheckCatwalkForRails(nextBlock, -dirVec))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (nextIsRailing)
                {
                  if (CheckCatwalkForRails(nextBlock, -dirVec))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else // can walk up any side
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);

              if (upDir == thisBlock.Orientation.Up)
              {
                if (dir != upDir)
                {
                  if (dir != thisBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (upDir == thisBlock.Orientation.Forward)
              {
                if (dir != upDir)
                {
                  if (dir != thisBlock.Orientation.Up)
                  {
                    if (nextBlockEmpty || nextBlock != thisBlock || dir != downDir)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (!nextBlockEmpty)
                {
                  if (nextBlock != thisBlock && (!nextIsBtnPanel || oppositeDir == nextBlock.Orientation.Up))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else if (downDir == thisBlock.Orientation.Up)
              {
                if (oppositeDir != thisBlock.Orientation.Up)
                {
                  if (oppositeDir != thisBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward))
                  {
                    if (!nextIsWindowSlope || nextBlock != thisBlock || oppositeDir != thisBlock.Orientation.Forward)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }

                if (oppositeDir == thisBlock.Orientation.Up && !nextBlockEmpty && nextBlock != thisBlock)
                {
                  if (!nextIsBtnPanel || dir == nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else if (downDir == thisBlock.Orientation.Forward)
              {
                if (oppositeDir != thisBlock.Orientation.Forward)
                {
                  if (oppositeDir != thisBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                  else if (!nextBlockEmpty && (!nextIsBtnPanel || dir == nextBlock.Orientation.Up))
                  {
                    if (!nextIsWindowSlope || nextBlock != thisBlock || oppositeDir != thisBlock.Orientation.Up)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }

                if (!nextBlockEmpty && nextBlock != thisBlock && oppositeDir == thisBlock.Orientation.Forward)
                {
                  if (!nextIsBtnPanel || dir == nextBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
          else if (thisBlock?.FatBlock is IMyTextPanel)
          {
            if (dir == thisBlock.Orientation.Forward && thisBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner", StringComparison.OrdinalIgnoreCase) < 0)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (thisIsLadder)
          {
            if (oppositeDir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (thisBlock.BlockDefinition.Id.SubtypeName == "LadderShaft")
            {
              if (dir == thisBlock.Orientation.Left || oppositeDir == thisBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
          }
          else if (thisIsBtnPanel)
          {
            if (dir == thisBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
        }
        else if (belowThisIsSlope)
        {
          bool isSlopeBlock = AiSession.Instance.SlopeBlockDefinitions.Contains(blockBelowThis.BlockDefinition.Id);

          if (isSlopeBlock && blockBelowThis.BlockDefinition.Id.SubtypeName == "GratedStairs")
          {
            if (dir == blockBelowThis.Orientation.Left || oppositeDir == blockBelowThis.Orientation.Left)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }

          bool isSlopeUpsideDown = isSlopeBlock
            && (blockBelowThis.Orientation.Forward == upDir || blockBelowThis.Orientation.Up == Base6Directions.GetOppositeDirection(upDir));

          if (!isSlopeUpsideDown)
          {
            if (isSlopeBlock
              && (upDir == blockBelowThis.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == blockBelowThis.Orientation.Left))
            {
              if (dir == blockBelowThis.Orientation.Up || oppositeDir == blockBelowThis.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }

            if (blockBelowNext != null && !belowNextIsSlope && dirVec != Vector3I.Down)
            {
              var belowThisPos = blockBelowThis.Position;
              if (blockBelowThis.CubeGrid.EntityId != MainGrid.EntityId)
                belowThisPos = MainGrid.WorldToGridInteger(blockBelowThis.CubeGrid.GridIntegerToWorld(belowThisPos));

              var belowNextPos = blockBelowNext.Position;
              if (blockBelowNext.CubeGrid.EntityId != MainGrid.EntityId)
                belowNextPos = MainGrid.WorldToGridInteger(blockBelowNext.CubeGrid.GridIntegerToWorld(belowNextPos));

              var dirToNext = belowNextPos - belowThisPos; // blockBelowNext.Position - blockBelowThis.Position;
              var dirLeft = Base6Directions.GetIntVector(blockBelowThis.Orientation.Left);

              if (dirLeft.Dot(ref dirToNext) != 0)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
        }

        if (!nextBlockEmpty)
        {
          if (nextIsPassage && !nextIsPassageStair)
          {
            var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            var blockFwd = nextBlock.Orientation.Forward;
            var blockUp = nextBlock.Orientation.Up;

            if (AiSession.Instance.PassageIntersectionDefinitions.Contains(nextBlock.BlockDefinition.Id))
            {
              if (blockSubtype.StartsWith("Lighted"))
                blockSubtype = blockSubtype.Substring(7);

              if (nextBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Ladder2))
              {
                if (oppositeDir == nextBlock.Orientation.Left || dir == nextBlock.Orientation.Left || oppositeDir == blockFwd)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }

                if (dir == blockFwd && blockSubtype.IndexOf("enc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype == "Passage")
              {
                if (dir != blockUp && oppositeDir != blockUp)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageTop"))
              {
                if (oppositeDir == blockUp)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
                else if (dir == blockUp && blockSubtype.IndexOf("enc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("HalfPassage"))
              {
                if (blockSubtype.StartsWith("HalfPassageCorner"))
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
                else if (blockSubtype.StartsWith("HalfPassagePillar"))
                {
                  if (oppositeDir != blockFwd && oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (blockSubtype.StartsWith("HalfPassageGlass"))
                {
                  if (oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Left && dir != blockFwd)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (blockSubtype.StartsWith("HalfPassageL"))
                {
                  if (oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Left && oppositeDir != blockFwd)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
              }
              else if (blockSubtype.StartsWith("PassagePillar"))
              {
                if (dir == blockUp || oppositeDir == blockUp)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageL"))
              {
                if (oppositeDir != blockUp && oppositeDir != nextBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageT"))
              {
                if (blockSubtype.StartsWith("PassageTVert"))
                {
                  if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (blockSubtype.StartsWith("PassageTGlass"))
                {
                  if (oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageGlass"))
              {
                if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageCargo") || blockSubtype.StartsWith("PassageConveyor"))
              {
                if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (nextBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_TextPanel))
              {
                if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("PassageDoor") || blockSubtype.StartsWith("PassageAirlock"))
              {
                if (blockSubtype.StartsWith("PassageDoorSide"))
                {
                  if (oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Left && dir != blockFwd)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (dir != blockFwd && oppositeDir != blockFwd) // airlock and regular door are fwd / bwd only
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (blockSubtype.StartsWith("Passage4Way"))
              {
                if (dir == blockFwd || oppositeDir == blockFwd)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
            }
            else if (nextBlock.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Passage))
            {
              if (dir != blockUp && oppositeDir != blockUp)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (dir == blockUp || oppositeDir == blockUp)
            {
              node.SetBlocked(dirVec);
              continue;
            }
            else if (blockSubtype.StartsWith("Passage2"))
            {
              if (blockSubtype.EndsWith("Wall"))
              {
                if (dir == blockFwd)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (dir == blockFwd || oppositeDir == blockFwd)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (blockSubtype.EndsWith("Wall") || blockSubtype.EndsWith("Tjunction"))
            {
              if (dir != blockFwd && oppositeDir != blockFwd && dir != nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (blockSubtype.EndsWith("Corner"))
            {
              if (dir != blockFwd && dir != nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (!blockSubtype.EndsWith("Intersection") && dir != blockFwd && oppositeDir != blockFwd)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (nextIsConveyorCap)
          {
            if (dir == nextBlock.Orientation.Up)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (nextIsAutomatonsFlat)
          {
            if (oppositeDir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (nextIsDeadBody)
          {
            if (CheckCatwalkForRails(nextBlock, -dirVec))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsScaffold)
          {
            var blockOr = nextBlock.Orientation;
            var subtype = nextBlock.BlockDefinition.Id.SubtypeName;
            if (subtype.EndsWith("Platform"))
            {
              if (dir == blockOr.Left || oppositeDir == blockOr.Up || oppositeDir == blockOr.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (subtype.EndsWith("Structure"))
            {
              if (dir == blockOr.Left || oppositeDir == blockOr.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (subtype.EndsWith("Unsupported"))
            {
              if (oppositeDir == blockOr.Up)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
          }
          else if (nextIsLightPanel)
          {
            if (dir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsNeonTube)
          {
            var subtype = nextBlock.BlockDefinition.Id.SubtypeName;
            var nextOr = nextBlock.Orientation;
            if (subtype.EndsWith("U"))
            {
              if (dir == nextOr.Up || dir == nextOr.Left || oppositeDir == nextOr.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype.EndsWith("BendUp"))
            {
              if (dir == nextOr.Up || oppositeDir == nextOr.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype != "LargeNeonTubesBendDown" && dir == nextOr.Up)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsDoor)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("SlidingHatchDoor"))
            {
              if (dir != nextBlock.Orientation.Up && oppositeDir != nextBlock.Orientation.Up)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextBlock.BlockDefinition.Id.SubtypeName == "RotaryAirlockCorner")
            {
              if (oppositeDir != nextBlock.Orientation.Left && dir != nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsDeco)
          {
            var def = nextBlock.BlockDefinition.Id;
            var subtype = def.SubtypeName;
            if (subtype == "LargeBlockPlanters" || subtype == "LargeBlockKitchen" || subtype == "LargeBlockBarCounter"
              || subtype == "LargeBlockDesk" || subtype == "LargeBlockDeskChairless")
            {
              if (oppositeDir == nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "LargeBlockCouch")
            {
              if (dir == nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "LargeBlockCouchCorner")
            {
              if (oppositeDir == nextBlock.Orientation.Left || dir == nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "LargeBlockBarCounterCorner")
            {
              if (dir == nextBlock.Orientation.Left || oppositeDir == nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "Shower")
            {
              if (dir == nextBlock.Orientation.Forward || dir == nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype.EndsWith("Toilet"))
            {
              if (dir == nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (subtype == "Jukebox" || subtype == "AtmBlock" || subtype == "FoodDispenser" || subtype == "VendingMachine")
            {
              if (oppositeDir == nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
          else if (nextIsSlope)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName == "GratedStairs")
            {
              if (dir == nextBlock.Orientation.Left || oppositeDir == nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }

            if (nextIsHalfSlope)
            {
              if (dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Forward)
              {
                if ((belowNextIsRamp || belowNextIsSlope || belowNextIsHalfSlope) && ExemptNodesUpper.Contains(next))
                {
                  if (blockBelowNext != thisBlock)
                  {
                    var dotUp = Base6Directions.GetIntVector(blockBelowNext.Orientation.Up).Dot(ref upVec);
                    var dotFwd = Base6Directions.GetIntVector(blockBelowNext.Orientation.Forward).Dot(ref upVec);
                    bool valid = true;

                    if (dotUp > 0)
                    {
                      if (!ExemptNodesUpper.Contains(next) && dir != blockBelowNext.Orientation.Forward)
                      {
                        valid = false;
                      }
                    }
                    else if (dotFwd > 0 || (dotFwd < 0 && belowNextIsHalfSlope))
                    {
                      if (dir != blockBelowNext.Orientation.Up)
                      {
                        valid = false;
                      }
                    }
                    else
                    {
                      valid = false;
                    }

                    if (!valid)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
                else if (ExemptNodesSide.Contains(next))
                {
                  if (!thisBlockEmpty)
                  {
                    var blockUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up);
                    var adjacentBlock = GetBlockAtPosition(nodePos + blockUp);
                    if (adjacentBlock == nextBlock)
                    {
                      if (dir != thisBlock.Orientation.Up)
                      {
                        node.SetBlocked(dirVec); 
                        continue;
                      }
                    }
                    else if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                  else if (dir != nextBlock.Orientation.Left && oppositeDir != nextBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("Slope2Tip")
                && (upDir == nextBlock.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == nextBlock.Orientation.Left))
              {
                if (dir == nextBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else if (thisBlockEmpty && dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
              {
                var subtype = nextBlock.BlockDefinition.Id.SubtypeName;
                if (subtype.EndsWith("Slope2Tip") || subtype.EndsWith("HalfSlopeArmorBlock") || subtype.EndsWith("Concrete_Half_Block_Slope"))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else if (nextIsPassageStair)
            {
              if (thisBlockEmpty || thisBlock.Position != nextBlock.Position)
              {
                if (dir == nextBlock.Orientation.Forward || oppositeDir == nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }

                var relativeVector = next - nextBlock.Position;
                var blockUpVec = Base6Directions.GetIntVector(nextBlock.Orientation.Up);
                var blockFwdVec = -Base6Directions.GetIntVector(nextBlock.Orientation.Left);

                if (relativeVector == Vector3I.Zero)
                {
                  if (oppositeDir != nextBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else if (relativeVector == blockUpVec + blockFwdVec)
                {
                  if (dir != nextBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec);
                    continue;
                  }
                }
                else
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
            }
            else if (thisIsHalfStair && nextIsHalfStair)
            {
              if (dir != thisBlock.Orientation.Up && dir != Base6Directions.GetOppositeDirection(thisBlock.Orientation.Up))
              {
                node.SetBlocked(dirVec); 
                continue;
              }

              var thisUp = Base6Directions.GetIntVector(thisBlock.Orientation.Up);
              var nextUp = Base6Directions.GetIntVector(nextBlock.Orientation.Up);
              var upDot = thisUp.Dot(ref nextUp);

              if (thisBlock.BlockDefinition.Id == nextBlock.BlockDefinition.Id)
              {
                if (upDot != 0)
                {
                  if (thisBlock.Orientation.Forward == nextBlock.Orientation.Left)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (thisBlock.Orientation.Up == nextBlock.Orientation.Forward && thisBlock.Orientation.Forward != nextBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (upDot != 0)
              {
                if (upDot > 0 && thisBlock.Orientation.Forward != nextBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
                else if (upDot < 0 && thisBlock.Orientation.Forward != Base6Directions.GetOppositeDirection(nextBlock.Orientation.Forward))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                var thisForward = Base6Directions.GetIntVector(thisBlock.Orientation.Forward);
                var fwdDotUp = thisForward.Dot(ref nextUp);

                if (fwdDotUp == 0)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
                else if (fwdDotUp > 0)
                {
                  if (nextBlock.Orientation.Forward == thisBlock.Orientation.Left || nextBlock.Orientation.Forward == Base6Directions.GetOppositeDirection(thisBlock.Orientation.Left))
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
                else if (thisBlock.Orientation.Up != nextBlock.Orientation.Forward && thisBlock.Orientation.Up != Base6Directions.GetOppositeDirection(nextBlock.Orientation.Forward))
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else if (!ExemptNodesUpper.Contains(next) && dir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (thisBlockEmpty && dir == nextBlock.Orientation.Forward && oppositeDir != upDir
              && nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("BlockArmorSlope2Base"))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsPanel)
          {
            var blockLeft = nextBlock.Orientation.Left;
            var blockFwd = nextBlock.Orientation.Forward;
            var blockUp = nextBlock.Orientation.Up;

            if (nextIsFullPanel)
            {
              if (dir == blockLeft)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (nextIsHalfPanel)
            {
              if (dir == blockLeft && nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("LargeArmorHalf"))
              {
                if (Base6Directions.GetIntVector(blockUp).Dot(ref upVec) != 0)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else if (nextIsSlopePanel)
            {
              bool isUp = dir == upDir;
              bool oppIsBlockUp = oppositeDir == blockUp;
              bool oppIsBlockFwd = oppositeDir == blockFwd;
              bool oppDirNoGood = !oppIsBlockFwd && !oppIsBlockUp;

              if (!thisIsSlopePanel && (isUp || oppDirNoGood))
              {
                if (!isUp && oppDirNoGood && nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("LargeArmorSlopedPanel"))
                {
                  var downDir = Base6Directions.GetOppositeDirection(upDir);

                  if (blockFwd == downDir || blockUp == downDir)
                  {
                    if (dir != blockFwd && dir != blockUp)
                    {
                      node.SetBlocked(dirVec); 
                      continue;
                    }
                  }
                }
                else
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
            else if (nextIsHalfSlopePanel)
            {
              if (oppositeDir != blockUp && oppositeDir != blockFwd)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else // miscellaneous panels
            {
              if (nextBlock.BlockDefinition.Id.SubtypeName.EndsWith("Inv"))
              {
                if (oppositeDir == blockLeft)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (dir == blockLeft)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
          else if (nextIsBeam)
          {
            var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            var blockLeft = nextBlock.Orientation.Left;
            var blockFwd = nextBlock.Orientation.Forward;

            if (blockSubtype.EndsWith("Slope"))
            {
              if (!blockSubtype.EndsWith("HalfSlope") && oppositeDir != blockLeft && dir != blockLeft)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (blockSubtype.EndsWith("End"))
            {
              if (dir != blockFwd)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (blockSubtype.EndsWith("TJunction"))
            {
              if (oppositeDir != blockFwd)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (oppositeDir != blockFwd && dir != blockFwd)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsLocker)
          {
            var blockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            if (blockSubtype == "LargeBlockLockerRoom")
            {
              if (dir != nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (blockSubtype == "LargeBlockLockerRoomCorner")
            {
              if (oppositeDir == nextBlock.Orientation.Forward || dir == nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (oppositeDir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsFreight)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName != "Freight1" && dir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsHalfBlock)
          {
            if (upDir == dir || Base6Directions.GetOppositeDirection(upDir) == dir || oppositeDir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (thisIsHalfBlock)
            {
              if (thisBlock.Orientation.Forward != nextBlock.Orientation.Forward)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else if (!thisIsSlope)
            {
              if (thisBlockEmpty)
              {
                if (Base6Directions.GetIntVector(nextBlock.Orientation.Forward).Dot(ref upVec) != 0)
                {
                  node.SetBlocked(dirVec);
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
            else
            {
              bool thisIsSlopeBase = thisIsSlope
                && thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("Large")
                && thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("BlockArmorSlope2Base");

              if (thisIsSlopeBase)
              {
                if (oppositeDir != thisBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if (thisIsHalfSlope)
              {
                if (dir != thisBlock.Orientation.Forward)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else if ((thisBlock.BlockDefinition.Id.SubtypeName.StartsWith("Large") && thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("HalfSlopeArmorBlock"))
                || thisBlock.BlockDefinition.Id.SubtypeName.EndsWith("Concrete_Half_Block_Slope"))
              {
                if (dir != thisBlock.Orientation.Forward && oppositeDir != thisBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
              else
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
          }
          else if (nextIsCoverWall)
          {
            var nextBlockSubtype = nextBlock.BlockDefinition.Id.SubtypeName;
            if (dir == nextBlock.Orientation.Forward && !nextBlockSubtype.EndsWith("Half"))
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (dir == nextBlock.Orientation.Left && nextBlockSubtype.EndsWith("Corner"))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsRamp)
          {
            if (oppositeDir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
            {
              if (!thisIsRamp || thisBlock != nextBlock || dir != thisBlock.Orientation.Forward)
              {
                if (!ExemptNodesSide.Contains(next) || dir == nextBlock.Orientation.Up)
                {
                  node.SetBlocked(dirVec); 
                  continue;
                }
              }
            }
          }
          else if (nextIsCatwalk)
          {
            if (dir == nextBlock.Orientation.Up || CheckCatwalkForRails(nextBlock, -dirVec))
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("CatwalkHalf") && dir == nextBlock.Orientation.Left)
            {
              node.SetBlocked(dirVec);
              continue;
            }
          }
          else if (nextIsRailing)
          {
            if (CheckCatwalkForRails(nextBlock, -dirVec))
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsWindowFlat)
          {
            Base6Directions.Direction sideWithPane;
            if (nextBlock.BlockDefinition.Id.SubtypeName == "LargeWindowSquare" || nextBlock.BlockDefinition.Id.SubtypeName.StartsWith("HalfWindowCorner"))
              sideWithPane = nextBlock.Orientation.Forward;
            else
              sideWithPane = Base6Directions.GetOppositeDirection(nextBlock.Orientation.Left);

            if (oppositeDir == sideWithPane)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsSolar)
          {
            if (oppositeDir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsWindowSlope)
          {
            if (nextBlock.BlockDefinition.Id.SubtypeName == "LargeWindowEdge") // can only walk up one side
            {
              if (oppositeDir != nextBlock.Orientation.Forward && oppositeDir != nextBlock.Orientation.Up)
              {
                node.SetBlocked(dirVec); 
                continue;
              }
            }
            else // can walk up any side
            {
              var downDir = Base6Directions.GetOppositeDirection(upDir);

              if (upDir == nextBlock.Orientation.Up)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (oppositeDir != upDir && oppositeDir != nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  if (dir == nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else if (upDir == nextBlock.Orientation.Forward)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (oppositeDir != upDir && oppositeDir != nextBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  if (dir == nextBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else if (downDir == nextBlock.Orientation.Up)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (dir != downDir && dir != nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  if (oppositeDir == nextBlock.Orientation.Forward)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
              else if (downDir == nextBlock.Orientation.Forward)
              {
                if (thisBlockEmpty || thisBlock != nextBlock)
                {
                  if (dir != downDir && dir != nextBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }

                  if (oppositeDir == nextBlock.Orientation.Up)
                  {
                    node.SetBlocked(dirVec); 
                    continue;
                  }
                }
              }
            }
          }
          else if (nextBlock?.FatBlock is IMyTextPanel)
          {
            if (oppositeDir == nextBlock.Orientation.Forward && nextBlock.BlockDefinition.Id.SubtypeName.IndexOf("corner") < 0)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
          else if (nextIsLadder)
          {
            if (dir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }

            if (nextBlock.BlockDefinition.Id.SubtypeName == "LadderShaft")
            {
              if (dir == nextBlock.Orientation.Left || oppositeDir == nextBlock.Orientation.Left)
              {
                node.SetBlocked(dirVec);
                continue;
              }
            }
          }
          else if (nextIsBtnPanel)
          {
            if (oppositeDir == nextBlock.Orientation.Forward)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
        }
        else if (belowNextIsSlope)
        {
          if (blockBelowNext.BlockDefinition.Id.SubtypeName == "GratedStairs")
          {
            if (dir == blockBelowNext.Orientation.Left || oppositeDir == blockBelowNext.Orientation.Left)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }

          if (AiSession.Instance.SlopeBlockDefinitions.Contains(blockBelowNext.BlockDefinition.Id)
            && (upDir == blockBelowNext.Orientation.Left || Base6Directions.GetOppositeDirection(upDir) == blockBelowNext.Orientation.Left))
          {
            if (dir == blockBelowNext.Orientation.Forward || oppositeDir == blockBelowNext.Orientation.Up)
            {
              node.SetBlocked(dirVec); 
              continue;
            }
          }
        }
      }

      foreach (var dirVec in AiSession.Instance.VoxelMovementDirections)
      {
        Node nextNode;
        var next = nodePos + dirVec;
        if (node.IsGridNode && !node.IsGridNodePlanetTile)
        {
          node.SetBlocked(dirVec); 
        }
        else if (DoesBlockExist(next))
        {
          node.SetBlocked(dirVec); 
        }
        else if (!TryGetNodeForPosition(next, out nextNode))
        {
          node.SetBlocked(dirVec); 
        }
        else if (node.IsGridNodePlanetTile && !nextNode.IsGridNodePlanetTile)
        {
          node.SetBlocked(dirVec); 
        }
      }

      if (!node.IsGridNode || node.IsGridNodePlanetTile)
      {
        foreach (var dirVec in AiSession.Instance.DiagonalDirections)
        {
          Node nextNode;
          var next = nodePos + dirVec;
          if (DoesBlockExist(next))
          {
            node.SetBlocked(dirVec); 
          }
          else if (!TryGetNodeForPosition(next, out nextNode))
          {
            node.SetBlocked(dirVec); 
          }
          else if (node.IsGridNodePlanetTile && !nextNode.IsGridNodePlanetTile)
          {
            node.SetBlocked(dirVec); 
          }
        }
      }
    }

    bool CheckCatwalkForRails(IMySlimBlock block, Vector3I side)
    {
      var def = block.BlockDefinition.Id;
      Base6Directions.Direction[] railArray;
      if (!AiSession.Instance.CatwalkRailDirections.TryGetValue(def, out railArray))
      {
        return false;
      }

      Matrix m = new Matrix
      {
        Up = Base6Directions.GetVector(block.Orientation.Up),
        Forward = Base6Directions.GetVector(block.Orientation.Forward),
        Left = Base6Directions.GetVector(block.Orientation.Left)
      };

      m.TransposeRotationInPlace();
      Vector3I.TransformNormal(ref side, ref m, out side);

      var checkDir = Base6Directions.GetDirection(side);
      return railArray.ContainsItem(checkDir);
    }

    public override bool GetRandomNodeNearby(BotBase bot, Vector3D targetPosition, out Vector3I node)
    {
      List<Vector3I> localNodes;
      if (!AiSession.Instance.LineListStack.TryPop(out localNodes))
        localNodes = new List<Vector3I>();
      else
        localNodes.Clear();

      var botPosition = bot.BotInfo.CurrentBotPositionActual;
      var collection = bot._pathCollection;
      var botWorldMatrix = bot.WorldMatrix;

      node = MainGrid.WorldToGridInteger(botPosition);

      foreach (var point in Neighbors(bot, node, node, botPosition, true, up: botWorldMatrix.Up))
      {
        if (collection.DeniedDoors.ContainsKey(point))
          continue;

        IHitInfo hitInfo;
        var worldNode = MainGrid.GridIntegerToWorld(point);

        if (MyAPIGateway.Physics.CastRay(botPosition, worldNode, out hitInfo, CollisionLayers.CharacterCollisionLayer))
        {
          var grid = hitInfo?.HitEntity?.GetTopMostParent() as MyCubeGrid;
          if (grid != null)
            continue;
        }

        if (MyAPIGateway.Physics.CastRay(worldNode, targetPosition, out hitInfo, CollisionLayers.CharacterCollisionLayer))
        {
          var grid = hitInfo?.HitEntity?.GetTopMostParent() as MyCubeGrid;
          if (grid != null)
            continue;
        }

        localNodes.Add(point);
      }

      bool result = false;
      if (localNodes.Count > 0)
      {
        var rnd = MyUtils.GetRandomInt(0, localNodes.Count);
        node = localNodes[rnd];
        result = true;
      }

      localNodes.Clear();
      AiSession.Instance.LineListStack.Push(localNodes);
      return result;
    }

    public bool GetRandomOpenTile(BotBase bot, out Node node, bool allowAirNodes = true, bool allowPlanetTiles = true, bool interiorFirst = true)
    {
      node = null;
      if (OpenTileDict.Count == 0 && (!allowPlanetTiles || PlanetTileDictionary.Count == 0))
        return false;

      foreach (var tile in OpenTileDict)
      {
        if (tile.Value != null && tile.Value.Block == null && CanBotUseTile(bot, tile.Value, allowAirNodes))
        {
          node = tile.Value;

          if (!interiorFirst || (node.IsGridNode && node.Grid?.IsRoomAtPositionAirtight(node.Position) == true))
          {
            break;
          }
        }
      }

      if (node == null && allowPlanetTiles)
      {
        foreach (var tile in PlanetTileDictionary)
        {
          if (tile.Value != null && tile.Value.Block == null && CanBotUseTile(bot, tile.Value, allowAirNodes))
          {
            node = tile.Value;
            break;
          }
        }
      }

      return node != null;
    }

    public override bool GetRandomOpenNode(BotBase bot, Vector3D requestedPosition, out Node node)
    {
      node = null;
      if (OpenTileDict.Count == 0 && PlanetTileDictionary.Count == 0)
        return false;

      List<Vector3I> nodeList;
      if (!AiSession.Instance.LineListStack.TryPop(out nodeList))
        nodeList = new List<Vector3I>();
      else
        nodeList.Clear();

      List<Vector3I> testList;
      if (!AiSession.Instance.LineListStack.TryPop(out testList))
        testList = new List<Vector3I>();
      else
        testList.Clear();

      MainGrid.RayCastCells(bot.BotInfo.CurrentBotPositionActual, requestedPosition, nodeList, new Vector3I(11));

      bool getGroundFirst = !bot.CanUseAirNodes;

      for (int i = 0; i < nodeList.Count; i++)
      {
        var localPosition = nodeList[i];

        if (getGroundFirst)
          GetClosestGroundNode(localPosition, testList, out localPosition);

        Node tempNode;
        if (IsPositionUsable(bot, LocalToWorld(localPosition), out tempNode))
        {
          node = tempNode;
          continue;
        }

        break;
      }

      //var localBot = WorldToLocal(bot.Position);
      //var botPosition = LocalToWorld(localBot);

      //var vector = requestedPosition - botPosition;
      //var length = vector.Normalize();
      //var cellSize = (double)CellSize;

      //var count = (int)Math.Floor(length / cellSize);

      //for (int i = count; i >= 0; i--)
      //{
      //  var worldPos = botPosition + (vector * count * cellSize);
      //  if (!OBB.Contains(ref worldPos))
      //    continue;

      //  Vector3I tempNode;
      //  if (!bot.CanUseAirNodes && GetClosestGroundNode(WorldToLocal(worldPos), testList, out tempNode))
      //    worldPos = LocalToWorld(tempNode);

      //  if (IsPositionUsable(bot, worldPos, out node))
      //    break;
      //}

      nodeList.Clear();
      testList.Clear();
      AiSession.Instance.LineListStack.Push(nodeList);
      AiSession.Instance.LineListStack.Push(testList);

      return node != null;
    }

    bool GetClosestGroundNode(Vector3I pos, List<Vector3I> list, out Vector3I groundPos)
    {
      groundPos = pos;

      Node node;
      if (TryGetNodeForPosition(pos, out node) && node.IsGroundNode)
      {
        groundPos = node.Position;
        return true;
      }

      var top = pos + Vector3I.Up * 10;
      var btm = pos - Vector3I.Up * 10;

      var posWorld = MainGrid.GridIntegerToWorld(pos);
      var topWorld = MainGrid.GridIntegerToWorld(top);
      var btmWorld = MainGrid.GridIntegerToWorld(btm);

      list.Clear();
      MainGrid.RayCastCells(topWorld, btmWorld, list, new Vector3I(11));

      bool result = false;
      int distance = int.MaxValue;

      for (int i = 0; i < list.Count; i++)
      {
        var localPos = list[i];

        if (TryGetNodeForPosition(localPos, out node) && node.IsGroundNode)
        {
          var dManhattan = Vector3I.DistanceManhattan(localPos, pos);
          if (dManhattan < distance)
          {
            distance = dManhattan;
            groundPos = localPos;
            result = true;
          }
        }
      }

      return result;
    }

    public override void UpdateTempObstacles()
    {
      if (!_obstacleTask.IsComplete)
        return;

      if (_obstacleTask.Exceptions != null)
      {
        AiSession.Instance.Logger.ClearCached();
        AiSession.Instance.Logger.AddLine($"Exceptions found during pathfinder task!\n");
        foreach (var ex in _obstacleTask.Exceptions)
          AiSession.Instance.Logger.AddLine($" -> {ex.Message}\n{ex.StackTrace}\n");

        AiSession.Instance.Logger.LogAll();
        MyAPIGateway.Utilities.ShowNotification($"Exception during ObstacleTask!");
      }

      List<MyEntity> tempEntities;
      if (!AiSession.Instance.EntListStack.TryPop(out tempEntities))
        tempEntities = new List<MyEntity>();
      else
        tempEntities.Clear();

      List<IMySlimBlock> blocks;
      if (!AiSession.Instance.SlimListStack.TryPop(out blocks) || blocks == null)
        blocks = new List<IMySlimBlock>();
      else
        blocks.Clear();

      var sphere = new BoundingSphereD(OBB.Center, OBB.HalfExtent.AbsMax());
      MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, tempEntities);

      for (int i = tempEntities.Count - 1; i >= 0; i--)
      {
        var grid = tempEntities[i] as MyCubeGrid;
        if (grid?.Physics == null || grid.IsPreview || grid.MarkedForClose || grid.EntityId == MainGrid?.EntityId)
          continue;

        ((IMyCubeGrid)grid).GetBlocks(blocks);
      }

      tempEntities.Clear();
      _tempObstaclesWorkData.Blocks = blocks;
      _tempObstaclesWorkData.Entities = tempEntities;
      _obstacleTask = MyAPIGateway.Parallel.Start(UpdateTempObstaclesAsync, UpdateTempObstaclesCallback, _tempObstaclesWorkData);
    }

    List<KeyValuePair<IMySlimBlock, Vector3I>> _tempKVPList = new List<KeyValuePair<IMySlimBlock, Vector3I>>();

    void UpdateTempObstaclesAsync(WorkData data)
    {
      //AiSession.Instance.Logger.Log($"{this}.UpdateTempObstaclesAsync: Start");

      var obstacleData = data as ObstacleWorkData;
      if (obstacleData != null && AiSession.Instance?.BlockFaceDictionary != null && AiSession.Instance.Registered)
      {
        ObstacleNodesTemp.Clear();
        var blocks = obstacleData.Blocks;
        var tempEntities = obstacleData.Entities;
        _tempKVPList.Clear();

        for (int i = 0; i < blocks.Count; i++)
        {
          var b = blocks[i];
          if (b?.CubeGrid == null || b.IsDestroyed || b.CubeGrid.MarkedForClose)
            continue;

          var cubeDef = b.BlockDefinition as MyCubeBlockDefinition;
          Dictionary<Vector3I, HashSet<Vector3I>> faceDict;
          if (!AiSession.Instance.BlockFaceDictionary.TryGetValue(b.BlockDefinition.Id, out faceDict))
          {
            AiSession.Instance.Logger.Log($"There was no cube face dictionary found for {cubeDef.Id} (Size = {cubeDef.CubeSize}, Grid = {b.CubeGrid.DisplayName}, Position = {b.Position})", MessageType.WARNING);
            continue;
          }

          Matrix matrix = new Matrix
          {
            Forward = Base6Directions.GetVector(b.Orientation.Forward),
            Left = Base6Directions.GetVector(b.Orientation.Left),
            Up = Base6Directions.GetVector(b.Orientation.Up)
          };

          if (faceDict.Count < 2)
            matrix.TransposeRotationInPlace();

          Vector3I center = cubeDef.Center;
          Vector3I.TransformNormal(ref center, ref matrix, out center);
          var adjustedPosition = b.Position - center;
          var grid = b.CubeGrid;

          foreach (var kvp in faceDict)
          {
            var cell = kvp.Key;
            Vector3I.TransformNormal(ref cell, ref matrix, out cell);
            var position = adjustedPosition + cell;

            var worldPoint = b.CubeGrid.GridIntegerToWorld(position);
            if (!OBB.Contains(ref worldPoint))
              continue;

            var graphLocal = WorldToLocal(worldPoint);
            if (IsOpenTile(graphLocal) && !ObstacleNodesTemp.ContainsKey(graphLocal))
            {
              ObstacleNodesTemp[graphLocal] = new KeyValuePair<IMyCubeGrid, bool>(grid, false);
            }

            var sphere = new BoundingSphereD(worldPoint, b.CubeGrid.GridSize * 0.6f);

            foreach (var dir in AiSession.Instance.CardinalDirections)
            {
              var otherLocal = graphLocal + dir;

              if (IsOpenTile(otherLocal) && !ObstacleNodesTemp.ContainsKey(otherLocal))
              {
                var otherWorld = LocalToWorld(otherLocal);
                var otherSphere = new BoundingSphereD(otherWorld, 1);

                if (sphere.Contains(otherSphere) != ContainmentType.Disjoint)
                {
                  //ObstacleNodesTemp[otherLocal] = new KeyValuePair<IMyCubeGrid, bool>(grid, true);
                  _tempKVPList.Add(new KeyValuePair<IMySlimBlock, Vector3I>(b, otherLocal));
                }
              }
            }
          }
        }

        foreach (var kvp in _tempKVPList)
        {
          var node = kvp.Value;
          if (!ObstacleNodesTemp.ContainsKey(node))
            ObstacleNodesTemp[node] = new KeyValuePair<IMyCubeGrid, bool>(kvp.Key.CubeGrid, true);
        }
      }

      //AiSession.Instance.Logger.Log($"{this}.UpdateTempObstaclesAsync: Finished");
    }

    void UpdateTempObstaclesCallback(WorkData data)
    {
      Interlocked.CompareExchange(ref ObstacleNodes, ObstacleNodesTemp, ObstacleNodes);

      if (AiSession.Instance != null && AiSession.Instance.Registered)
      {
        var obstacleData = data as ObstacleWorkData;

        if (obstacleData?.Blocks != null && AiSession.Instance.ObstacleWorkDataStack != null)
        {
          obstacleData.Blocks.Clear();
          AiSession.Instance.SlimListStack.Push(obstacleData.Blocks);
        }

        if (obstacleData?.Entities != null && AiSession.Instance.EntListStack != null)
        {
          obstacleData.Entities.Clear();
          AiSession.Instance.EntListStack.Push(obstacleData.Entities);
        }
      }
    }

    public override Node GetReturnHomePoint(BotBase bot)
    {
      if (bot == null || bot.IsDead)
        return null;

      Node node = null;
      Node backup = null;
      foreach (var kvp in OpenTileDict)
      {
        if (IsPositionUsable(bot, kvp.Key))
        {
          node = kvp.Value;

          if (node.IsGroundNode)
            return node;
          else if (backup == null)
            backup = node;
        }
      }

      foreach (var kvp in PlanetTileDictionary)
      {
        if (IsPositionUsable(bot, kvp.Key))
        {
          node = kvp.Value;

          if (node.IsGroundNode)
            return node;
          else if (backup == null)
            backup = node;
        }
      }

      return node ?? backup;
    }

    public Vector3D GetLastValidNodeOnLine(Vector3D start, Vector3D directionNormalized, double desiredDistance, bool ensureOpenTiles = true)
    {
      List<Vector3I> nodeList;
      if (!AiSession.Instance.LineListStack.TryPop(out nodeList))
        nodeList = new List<Vector3I>();
      else
        nodeList.Clear();

      Vector3D result = start;
      var end = start + directionNormalized * desiredDistance;

      MainGrid.RayCastCells(start, end, nodeList);
      Vector3I prevNode = WorldToLocal(start);

      for (int i = 0; i < nodeList.Count; i++)
      {
        var localPos = nodeList[i];
        var cube = GetBlockAtPosition(localPos);
        var def = cube?.BlockDefinition as MyCubeBlockDefinition;

        if (!ensureOpenTiles && def?.HasPhysics != true)
        {
          var world = LocalToWorld(localPos);
          if (!PointInsideVoxel(world, RootVoxel))
          {
            result = world;
            continue;
          }
        }

        Node node;
        if (BlockedDoors.ContainsKey(localPos) || TempBlockedNodes.ContainsKey(localPos) || ObstacleNodes.ContainsKey(localPos) || !TryGetNodeForPosition(localPos, out node))
          break;

        if (def?.HasPhysics == true)
        {
          if (node.IsBlocked(prevNode - localPos))
            break;

          if (TryGetNodeForPosition(prevNode, out node) && node.IsBlocked(localPos - prevNode))
            break;
        }

        result = LocalToWorld(localPos);
        prevNode = localPos;
      }

      nodeList.Clear();
      AiSession.Instance.LineListStack.Push(nodeList);
      return result;
    }

    public override bool IsPositionAvailable(Vector3D position, BotBase bot)
    {
      var node = WorldToLocal(position);
      return IsPositionAvailable(node, bot);
    }

    public override bool IsPositionAvailable(Vector3I node, BotBase bot)
    {
      if (base.IsPositionAvailable(node, bot))
      {
        IMyDoor door;
        if (BlockedDoors.TryGetValue(node, out door) && door != null)
        {
          if (bot == null || bot.Target.Entity == null)
            return false;

          var ch = bot.Target.Entity as IMyCharacter;
          if (ch != null && ch == bot.Owner?.Character)
            return false;

          var grid = door.CubeGrid;
          var gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
          var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

          var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
          if (rel != MyRelationsBetweenPlayers.Enemies)
            return false;
        }
      }

      return false;
    }

    public void RecalculateOBB()
    {
      var box = GetMapBoundingBoxLocal();
      box.Inflate(_boxExpansion);

      var worldCenter = MainGrid.GridIntegerToWorld(box.Center);
      var quat = Quaternion.CreateFromRotationMatrix(MainGrid.WorldMatrix);
      var halfVector = Vector3D.Half * CellSize;

      var newOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + halfVector, quat);

      if (newOBB.HalfExtent.LengthSquared() > OBB.HalfExtent.LengthSquared())
      {
        Dirty = true;
      }
      else
      {
        box = BoundingBox;
        worldCenter = MainGrid.GridIntegerToWorld(box.Center);
        newOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + halfVector, quat);
      }

      OBB = newOBB;

      box.Inflate(-_boxExpansion);
      UnbufferedOBB = new MyOrientedBoundingBoxD(worldCenter, box.HalfExtents * CellSize + halfVector, quat);
    }

    public void CheckPlanet()
    {
      if (MainGrid?.Physics == null || MainGrid.MarkedForClose || MainGrid.Physics.IsStatic)
        return;

      float _;
      var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
      if (gravity.LengthSquared() > 0)
      {
        var newPlanet = MyGamePruningStructure.GetClosestPlanet(OBB.Center);

        if (RootVoxel != null && newPlanet != RootVoxel)
          Planet_OnMarkForClose(RootVoxel);

        RootVoxel = newPlanet;

        if (RootVoxel != null)
        {
          RootVoxel.RangeChanged -= Planet_RangeChanged;
          RootVoxel.OnMarkForClose -= Planet_OnMarkForClose;

          RootVoxel.RangeChanged += Planet_RangeChanged;
          RootVoxel.OnMarkForClose += Planet_OnMarkForClose;
        }
      }
      else if (RootVoxel != null)
      {
        Planet_OnMarkForClose(RootVoxel);
        RootVoxel = null;
      }
    }

    public void AdjustWorldMatrix()
    {
      if (MainGrid?.Physics == null || MainGrid.Physics.IsStatic || MainGrid.WorldMatrix.EqualsFast(ref _lastMatrix))
        return;

      int numSeats, numFactories;
      bool matrixSet = false;
      if (MainGrid.HasMainCockpit())
      {
        WorldMatrix = MainGrid.MainCockpit.WorldMatrix;
        matrixSet = true;
      }
      else if (MainGrid.HasMainRemoteControl())
      {
        WorldMatrix = MainGrid.MainRemoteControl.WorldMatrix;
        matrixSet = true;
      }
      else if ((MainGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
        || (MainGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_RemoteControl), out numSeats) && numSeats > 0)
        || (MainGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_ConveyorSorter), out numFactories) && numFactories > 0))
      {
        foreach (var b in MainGrid.GetFatBlocks())
        {
          if (b is IMyShipController || (b is MyConveyorSorter && b.BlockDefinition.Id.SubtypeName == "RoboFactory"))
          {
            WorldMatrix = b.WorldMatrix;
            matrixSet = true;
            break;
          }
        }
      }

      if (!matrixSet)
      {
        float _;
        var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(OBB.Center, out _);
        var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(OBB.Center, 0);
        if (aGrav.LengthSquared() > 0)
        {
          var up = -Vector3D.Normalize(aGrav);
          var fwd = Vector3D.CalculatePerpendicularVector(up);

          var dir = MainGrid.WorldMatrix.GetClosestDirection(up);
          up = MainGrid.WorldMatrix.GetDirectionVector(dir);

          dir = MainGrid.WorldMatrix.GetClosestDirection(fwd);
          fwd = MainGrid.WorldMatrix.GetDirectionVector(dir);

          WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
        }
        else if (nGrav.LengthSquared() > 0)
        {
          var up = -Vector3D.Normalize(nGrav);
          var fwd = Vector3D.CalculatePerpendicularVector(up);

          var dir = MainGrid.WorldMatrix.GetClosestDirection(up);
          up = MainGrid.WorldMatrix.GetDirectionVector(dir);

          dir = MainGrid.WorldMatrix.GetClosestDirection(fwd);
          fwd = MainGrid.WorldMatrix.GetDirectionVector(dir);

          WorldMatrix = MatrixD.CreateWorld(OBB.Center, fwd, up);
        }
        else
          return;

        bool rotate = true;
        foreach (var block in MainGrid.GetFatBlocks())
        {
          rotate = false;
          break;
        }

        if (rotate)
        {
          var rotatedMatrix = MatrixD.CreateRotationY(MathHelper.PiOver2) * WorldMatrix;
          WorldMatrix = rotatedMatrix;
        }
      }

      _lastMatrix = MainGrid.WorldMatrix;
    }

    public override bool IsPositionValid(Vector3D position)
    {
      return OBB.Contains(ref position);
    }

    public override bool TryGetNodeForPosition(Vector3I position, out Node node)
    {
      if (OpenTileDict.TryGetValue(position, out node))
        return node != null;

      if (PlanetTileDictionary.TryGetValue(position, out node))
        return node != null;

      return false;
    }

    public override bool IsOpenTile(Vector3I position)
    {
      return PlanetTileDictionary.ContainsKey(position) || OpenTileDict.ContainsKey(position);
    }

    public override bool IsObstacle(Vector3I position, BotBase bot, bool includeTemp)
    {
      bool result = bot?._pathCollection != null && bot._pathCollection.Obstacles.ContainsKey(position);

      if (!result && bot != null && !(bot is RepairBot) && (bot.Target.IsSlimBlock || bot.Target.IsCubeBlock))
      {
        var cube = bot.Target.Entity as IMyCubeBlock;
        var slim = cube?.SlimBlock;

        if (slim == null)
          slim = bot.Target.Entity as IMySlimBlock;

        if (slim?.CubeGrid != null)
        {
          List<KeyValuePair<IMySlimBlock, Vector3D>> blocks;
          if (bot._pathCollection.BlockObstacles.TryGetValue(slim.CubeGrid.EntityId, out blocks))
          {
            Vector3D slimWorld;
            if (cube != null)
              slimWorld = cube.GetPosition();
            else if (slim.FatBlock != null)
              slimWorld = slim.FatBlock.GetPosition();
            else
              slim.ComputeWorldCenter(out slimWorld);

            for (int i = 0; i < blocks.Count; i++)
            {
              var kvp = blocks[i];
              if (kvp.Key == slim)
              {
                if (Vector3D.IsZero(slimWorld - kvp.Value, 1))
                {
                  result = true;
                } 
                else
                {
                  blocks.RemoveAtFast(i);
                }
                
                break;
              }
            }
          }
        }
      }

      if (!includeTemp)
        return result;

      IMyDoor door;
      if (!result && BlockedDoors.TryGetValue(position, out door) && door != null)
      {
        if (bot == null || bot.Target.Entity == null)
          return false;

        var ch = bot.Target.Entity as IMyCharacter;
        if (ch != null && ch == bot.Owner?.Character)
          return false;

        var grid = door.CubeGrid;
        var gridOwner = grid.BigOwners?.Count > 0 ? grid.BigOwners[0] : grid.SmallOwners?.Count > 0 ? grid.SmallOwners[0] : door.OwnerId;
        var botOwner = bot.Owner?.IdentityId ?? bot.BotIdentityId;

        var rel = MyIDModule.GetRelationPlayerPlayer(gridOwner, botOwner);
        if (rel != MyRelationsBetweenPlayers.Enemies)
          return false;
      }

      return result || ObstacleNodes.ContainsKey(position) || TempBlockedNodes.ContainsKey(position);
    }

    public override Node GetValueOrDefault(Vector3I position, Node defaultValue)
    {
      Node node;
      if (PlanetTileDictionary.TryGetValue(position, out node) && node != null)
        return node;

      if (OpenTileDict.TryGetValue(position, out node) && node != null)
        return node;

      return defaultValue;
    }

    public override IMySlimBlock GetBlockAtPosition(Vector3I mainGridPosition)
    {
      var block = MainGrid?.GetCubeBlock(mainGridPosition) as IMySlimBlock;

      //if (block == null)
      //{
      //  var worldPosition = MainGrid.GridIntegerToWorld(mainGridPosition);

      //  for (int i = 0; i < GridCollection.Count; i++)
      //  {
      //    var grid = GridCollection[i] as MyCubeGrid;
      //    if (grid?.Physics == null || grid.MarkedForClose || grid.IsPreview || grid.EntityId == MainGrid?.EntityId)
      //    {
      //      continue;
      //    }

      //    var localPosition = grid.WorldToGridInteger(worldPosition);
      //    block = grid.GetCubeBlock(localPosition);

      //    if (block != null)
      //      break;
      //  }
      //}

      return block;
    }

    public bool DoesBlockExist(Vector3I mainGridPosition, bool checkPhysics = true)
    {
      if (MainGrid == null || MainGrid.MarkedAsTrash)
        return false;

      IMySlimBlock slim = MainGrid.GetCubeBlock(mainGridPosition);
      if (slim != null)
      {
        return !checkPhysics || ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics;
      }

      //var worldPosition = MainGrid.GridIntegerToWorld(mainGridPosition);

      //for (int i = 0; i < GridCollection.Count; i++)
      //{
      //  var grid = GridCollection[i] as MyCubeGrid;
      //  if (grid?.Physics == null || grid.MarkedForClose || grid.IsPreview || grid.EntityId == MainGrid?.EntityId)
      //    continue;

      //  var localPosition = grid.WorldToGridInteger(worldPosition);
      //  slim = grid.GetCubeBlock(localPosition);
      //  if (slim != null)
      //  {
      //    return !checkPhysics || ((MyCubeBlockDefinition)slim.BlockDefinition).HasPhysics;
      //  }
      //}

      return false;
    }

    public MyRelationsBetweenPlayers GetRelationshipTo(BotBase bot)
    {
      if (bot == null || bot.IsDead || MainGrid == null || MainGrid.MarkedForClose)
        return MyRelationsBetweenPlayers.Neutral;

      var botOwnerId = bot.Owner?.IdentityId ?? bot.BotIdentityId;
      var gridOwnerId = MainGrid.BigOwners.Count > 0 ? MainGrid.BigOwners[0] : MainGrid.SmallOwners.Count > 0 ? MainGrid.SmallOwners[0] : 0L;
      return MyIDModule.GetRelationPlayerPlayer(botOwnerId, gridOwnerId, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
    }
  }
}