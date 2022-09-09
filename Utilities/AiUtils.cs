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
