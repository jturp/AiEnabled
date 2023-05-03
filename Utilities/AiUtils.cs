using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Utils;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

using VRageMath;

namespace AiEnabled.Utilities
{
  public static class AiUtils
  {
    public const double PiOver3 = Math.PI / 3;
    public const double PiOver6 = Math.PI / 6;

    public static T CastHax<T>(T typeRef, object castObj) => (T)castObj;

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
