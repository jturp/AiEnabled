using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;


namespace AiEnabled.Support
{
  public class IconInfo
  {
    public IMyCharacter Bot;
    int _ticks;
    int _maxTicks = 60;

    public void Set(IMyCharacter bot, int maxTicks)
    {
      Bot = bot;
      _ticks = 0;
      _maxTicks = maxTicks;
    }

    public bool Update(ref MatrixD cameraMatrix, ref MyStringId material, ref Vector4 color)
    {
      if (Bot == null || Bot.MarkedForClose)
      {
        return true;
      }

      var position = Bot.WorldAABB.Center + Bot.WorldMatrix.Up * (Bot.LocalAABB.HalfExtents.Y + 0.1);
      MyTransparentGeometry.AddBillboardOriented(material, color, position, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, radius: 0.15f, blendType: BlendTypeEnum.PostPP);

      ++_ticks;
      return _ticks > _maxTicks;
    }

    public void Clear()
    {
      Bot = null;
    }
  }
}
