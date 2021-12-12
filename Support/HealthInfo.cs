using AiEnabled.Graphics.Support;

using Sandbox.Game.Components;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace AiEnabled.Support
{
  public class HealthInfoStat
  {
    public bool ShowHealthBars = true;
    public List<long> BotEntityIds = new List<long>(); // Entity Ids of health bars to draw
  }

  public class HealthInfo
  {
    public IMyCharacter Bot;
    MyStringId _square = MyStringId.GetOrCompute("Square");
    int _ticks;

    public void Set(IMyCharacter bot)
    {
      Bot = bot;
      _ticks = 0;
    }

    public void Renew()
    {
      _ticks = 0;
    }

    public bool Update(ref MatrixD cameraMatrix)
    {
      if (Bot == null || Bot.IsDead)
        return true;

      var statComp = Bot.Components?.Get<MyEntityStatComponent>() as MyCharacterStatComponent;
      if (statComp == null)
        return true;

      var position = Bot.WorldAABB.Center + Bot.WorldMatrix.Up * (Bot.LocalAABB.HalfExtents.Y + 0.1);
      float alpha = 255;

      _ticks++;
      if (_ticks > 60)
      {
        var num = _ticks - 60;
        alpha *= (30f - num) / 30f;

        if (alpha == 0)
          return true;

        alpha /= 255;
      }

      var red = Color.Red * alpha;
      var health = statComp.Health.Value;
      if (health >= statComp.Health.MaxValue)
      {
        MyTransparentGeometry.AddBillboardOriented(_square, red, position, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, 0.2f, 0.025f, Vector2.Zero, BlendTypeEnum.PostPP);
      }
      else
      {
        var black = Color.Black * alpha;
        var width = 0.2f * statComp.Health.CurrentRatio;
        MyTransparentGeometry.AddBillboardOriented(_square, black, position, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, 0.2f, 0.025f, Vector2.Zero, BlendTypeEnum.PostPP);
        MyTransparentGeometry.AddBillboardOriented(_square, red, position, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, width, 0.025f, Vector2.Zero, BlendTypeEnum.PostPP);
      }

      return false;
    }

    public void Clear()
    {
      Bot = null;
    }
  }
}
