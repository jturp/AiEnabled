using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

using Sandbox.ModAPI;
using VRage.Utils;
using VRage.ModAPI;
using VRage.Game;
using AiEnabled.API;

namespace AiEnabled.Graphics.Support
{
  public class Box : Border
  {
    Vector2D _lastSize = Vector2D.Zero;
    Vector2D _lastPosition = Vector2D.Zero;
    Vector4 _color;
    HudAPIv2.BillBoardHUDMessage _boxBackground;

    public Box(double aspectRatio, Vector4 borderColor, bool useBorder = true, HudAPIv2.BillBoardHUDMessage background = null)
      : base(background, aspectRatio, borderColor, useBorder) 
    {
      _boxBackground = background;
      _color = _boxBackground.BillBoardColor;
    }

    public override void Close()
    {
      base.Close();
    }

    public void Reset()
    {
      _left.BillBoardColor = _right.BillBoardColor = _top.BillBoardColor = _bottom.BillBoardColor = _color;
    }

    public void Draw(Vector2D size, Vector2D position, double aspectRatio)
    {
      if (size == _lastSize && position == _lastPosition)
        return;

      _lastSize = size;
      _lastPosition = position;

      var leftRight = new Vector2D(size.X * 0.5, 0);
      var upDown = new Vector2D(0, size.Y * 0.5);
      var left = position - leftRight;
      var right = position + leftRight;
      var up = position + upDown;
      var down = position - upDown;

      var height = (float)Math.Abs(size.Y) + 0.005f;
      var width = (float)(Math.Abs(size.X) / aspectRatio) - 0.005f;
      
      _left.Origin = left;
      _right.Origin = right;
      _left.Height = _right.Height = height;

      _top.Origin = up;
      _bottom.Origin = down;
      _top.Width = _bottom.Width = width;

      var val = true;
      base.SetVisibility(ref val);
    }

    public void FadeToBlack(float amount)
    {
      var color = Vector4.Lerp(_left.BillBoardColor, Color.Transparent, amount);
      _left.BillBoardColor = _right.BillBoardColor = _top.BillBoardColor = _bottom.BillBoardColor = color;
    }

    public override void SetVisibility(ref bool enable)
    {
      if (_boxBackground != null)
        _boxBackground.Visible = enable;

      base.SetVisibility(ref enable);
    }
  }
}
