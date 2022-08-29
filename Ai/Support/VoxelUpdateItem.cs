using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRageMath;

namespace AiEnabled.Ai.Support
{
  public class VoxelUpdateItem
  {
    public BoundingBoxI BoundingBox;
    public int Counter;

    public VoxelUpdateItem() { }

    /// <summary>
    /// Creates the BoundingBoxI and sets the Counter to zero
    /// </summary>
    /// <param name="min">Min Point</param>
    /// <param name="max">Max Point</param>
    public void Init(ref Vector3I min, ref Vector3I max)
    {
      BoundingBox = new BoundingBoxI(min, max);
      Counter = 0;
    }

    /// <summary>
    /// Checks if the BoundingBoxI created from the supplied min and max intersect this item's BoundingBoxI
    /// </summary>
    /// <param name="min">Min point</param>
    /// <param name="max">Max point</param>
    /// <returns>true if the boxes intersect, otherwise false</returns>
    public bool Check(ref Vector3I min, ref Vector3I max)
    {
      if (BoundingBox.IsValid)
      {
        var otherBox = new BoundingBoxI(min, max);

        if (BoundingBox.Intersects(otherBox))
        {
          BoundingBox = BoundingBox.Include(ref min);
          BoundingBox = BoundingBox.Include(ref max);
          Counter = 0;
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Increments the counter
    /// </summary>
    /// <returns>true if there have been no changes in at least 301 ticks, otherwise false</returns>
    public bool Update()
    {
      ++Counter;
      return Counter > 3;
    }
  }
}
