using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Utils;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

using VRageMath;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.Game.Components;
using AiEnabled.Ai.Support;
using Sandbox.Definitions;

namespace AiEnabled.Utilities
{
  public static class AiUtils
  {
    public const double PiOver3 = Math.PI / 3;
    public const double PiOver6 = Math.PI / 6;

    public static T CastHax<T>(T typeRef, object castObj) => (T)castObj;

    public static Vector3I GetCellForPosition(IMySlimBlock block, Vector3I localPosition)
    {
      if (block == null)
        return Vector3I.Zero;

      Matrix m;
      block.Orientation.GetMatrix(out m);
      m.TransposeRotationInPlace();

      Vector3 position = Vector3.Zero;
      if (block.FatBlock != null)
      {
        position = localPosition - block.FatBlock.Position;
      }

      var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
      Vector3I cell = Vector3I.Round(Vector3.Transform(position, m) + cubeDef.Center);

      return cell;
    }

    static bool? IsAirtightFromDefinition(MyCubeBlockDefinition blockDefinition, float buildLevelRatio)
    {
      if (blockDefinition.BuildProgressModels != null && blockDefinition.BuildProgressModels.Length != 0)
      {
        MyCubeBlockDefinition.BuildProgressModel buildProgressModel = blockDefinition.BuildProgressModels[blockDefinition.BuildProgressModels.Length - 1];
        if (buildLevelRatio < buildProgressModel.BuildRatioUpperBound)
        {
          return false;
        }
      }
      return blockDefinition.IsAirTight;
    }

    public static bool IsSidePressurizedForBlock(IMySlimBlock block, Vector3I pos, Vector3 normal)
    {
      if (block == null)
        return false;

      var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
      if (cubeDef == null)
        return false;

      bool? flag = IsAirtightFromDefinition(cubeDef, block.BuildLevelRatio);

      if (flag.HasValue)
      {
        return flag.Value;
      }

      Matrix m;
      block.Orientation.GetMatrix(out m);
      m.TransposeRotationInPlace();

      Vector3I transformedNormal = Vector3I.Round(Vector3.Transform(normal, m));

      Vector3 position = Vector3.Zero;
      if (block.FatBlock != null)
      {
        position = pos - block.FatBlock.Position;
      }

      Vector3I cell = Vector3I.Round(Vector3.Transform(position, m) + cubeDef.Center);
      var result = cubeDef.IsCubePressurized[cell][transformedNormal];

      return result == MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways;
    }

    public static void FindAllPositionsForBlock(IMySlimBlock block, List<Vector3I> positions)
    {
      positions.Clear();

      var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;
      if (cubeDef?.IsCubePressurized.Count == 1)
      {
        positions.Add(block.Position);
        return;
      }

      var grid = block.CubeGrid;
      var queue = AiSession.Instance.LocalVectorQueuePool.Get();
      var hash = AiSession.Instance.LocalVectorHashStack.Get();

      queue.Enqueue(block.Position);
      hash.Add(block.Position);

      while (queue.Count > 0)
      {
        var pos = queue.Dequeue();

        foreach (var dir in AiSession.Instance.CardinalDirections)
        {
          var newPos = pos + dir;
          var newBlock = grid.GetCubeBlock(newPos);

          if (newBlock == block && hash.Add(newPos))
            queue.Enqueue(newPos);
        }
      }

      positions.AddRange(hash);
      AiSession.Instance.LocalVectorHashStack.Return(hash);
      AiSession.Instance.LocalVectorQueuePool.Return(queue);
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

    /// <summary>
    /// From the Math Wizard himself - Whiplash141 <3
    /// </summary>
    public static bool IsPositionInCone(Vector3D testPosition, Vector3D coneTipPosition, Vector3D coneDirection, double coneHeight = 250, double coneRadius = 250 * MathHelper.Sqrt3)
    {
      if (!Vector3D.IsUnit(ref coneDirection))
      {
        coneDirection = Vector3D.Normalize(coneDirection);
        // Don't plug in a zero vector, i dont feel like checking for it lol
      }

      /*
         /|
      h / |
       /  | y
      /   |
      -----
          x

      cos(ang) = x/h
      cos^2(ang) = x^2/h^2
      */

      Vector3D testDirection = testPosition - coneTipPosition;

      double x = Vector3D.Dot(testDirection, coneDirection);
      if (x < 0)
      {
        // Behind the cone
        return false;
      }

      double heightSq = coneHeight * coneHeight;
      double xSq = x * x;
      if (xSq > heightSq)
      {
        return false;
      }

      double minCosSq = heightSq / (heightSq + coneRadius * coneRadius); // You could technically cache this for better perf

      double hSq = testDirection.LengthSquared();
      double cosSq = x * x / hSq;
      return cosSq > minCosSq;
    }

    public static MatrixD GetRotationBetweenMatrices(ref MatrixD a, ref MatrixD b)
    {
      // Find rotation (q0) between new matrix (q1) and old matrix (q2)
      // q1 = q0 * q2
      // q0 = q1 * (q2)^-1
      // q0 = q1 * conj(q2)

      QuaternionD q0, q1, q2;
      QuaternionD.CreateFromRotationMatrix(ref b, out q1);
      QuaternionD.CreateFromRotationMatrix(ref a, out q2);
      q0 = q1 * QuaternionD.Conjugate(q2);

      return MatrixD.CreateFromQuaternion(q0);
    }

    public static bool CheckLineOfSight(ref Vector3D start, ref Vector3D end,
      List<Vector3I> cellList = null, List<MyLineSegmentOverlapResult<MyEntity>> resultList = null, MyVoxelBase voxel = null, params MyEntity[] ignoreEnts)
    {
      var line = new LineD(start, end);

      if (voxel != null && GridBase.LineIntersectsVoxel(ref start, ref end, voxel))
        return false;

      bool returnResultList = resultList == null;
      bool returnCellList = cellList == null;

      if (returnResultList)
      {
        resultList = AiSession.Instance.OverlapResultListStack.Get();
      }

      if (returnCellList)
      {
        cellList = AiSession.Instance.LineListStack.Get();
      }

      resultList.Clear();
      MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, resultList);

      bool result = true;

      for (int i = 0; i < resultList.Count; i++)
      {
        var hit = resultList[i];
        var ent = hit.Element;

        if (ent != null)
        {
          bool ignore = false;
          foreach (var ie in ignoreEnts)
          {
            if (ent.EntityId == ie.EntityId)
            {
              ignore = true;
              break;
            }
          }

          if (ignore)
            continue;

          var grid = ent as IMyCubeGrid;
          if (grid != null)
          {
            cellList.Clear();
            grid.RayCastCells(start, end, cellList);
            var localEnd = grid.WorldToGridInteger(end);
            var endBlock = grid.GetCubeBlock(localEnd);

            foreach (var cell in cellList)
            {
              var otherBlock = grid.GetCubeBlock(cell);
              if (otherBlock != null && cell != localEnd && otherBlock != endBlock)
              {
                var otherFat = otherBlock.FatBlock;
                if (otherFat != null)
                {
                  MyIntersectionResultLineTriangleEx? _;
                  if (otherFat.GetIntersectionWithLine(ref line, out _, IntersectionFlags.ALL_TRIANGLES))
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

            if (!result)
              break;
          }
          else if (ent is MyVoxelBase)
          {
            voxel = ent as MyVoxelBase;
            if (GridBase.LineIntersectsVoxel(ref start, ref end, voxel))
            {
              result = false;
              break;
            }
          }
          else
          {
            Vector3D? _;
            if (ent.GetIntersectionWithLine(ref line, out _))
            {
              result = false;
              break;
            }
          }
        }
      }

      if (returnResultList)
      {
        AiSession.Instance.OverlapResultListStack.Return(resultList);
      }

      if (returnCellList)
      {
        AiSession.Instance.LineListStack.Return(cellList);
      }

      return result;
    }

    public static double GetAngleBetween(Vector3D a, Vector3D b)
    {
      if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
        return 0;

      if (IsUnitVector(a) && IsUnitVector(b))
        return Math.Acos(MathHelperD.Clamp(a.Dot(b), -1, 1));

      return Math.Acos(MathHelperD.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
    }

    public static double GetCosineAngleBetween(Vector3D a, Vector3D b)
    {
      if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
        return 0;

      if (IsUnitVector(a) && IsUnitVector(b))
        return MathHelperD.Clamp(a.Dot(b), -1, 1);

      return MathHelperD.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1);
    }

    public static Vector3D Project(Vector3D a, Vector3D b)
    {
      if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
        return Vector3D.Zero;

      if (IsUnitVector(b))
        return a.Dot(b) * b;

      return (a.Dot(b) / b.LengthSquared()) * b;
    }

    public static bool IsUnitVector(Vector3D v)
    {
      double num = 1.0 - v.LengthSquared();
      return Math.Abs(num) < 1E-4;
    }
  }
}