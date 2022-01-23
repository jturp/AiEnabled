using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRageMath;

namespace AiEnabled.Utilities
{
  public static class VectorUtils
  {
    public const double PiOver3 = Math.PI / 3;
    public const double PiOver6 = Math.PI / 6;

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
