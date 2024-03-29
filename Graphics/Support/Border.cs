﻿using AiEnabled.API;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Utils;

using VRageMath;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace AiEnabled.Graphics.Support
{
  public class Border
  {
    public HudAPIv2.BillBoardHUDMessage Background;
    internal HudAPIv2.BillBoardHUDMessage _left, _right, _top, _bottom;
    internal bool _useBorder;

    public Border(HudAPIv2.BillBoardHUDMessage background, double aspectRatio, Vector4 borderColor, bool use = true)
    {
      _useBorder = use;
      if (!use)
        return;

      Background = background;
      var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
      var material = MyStringId.GetOrCompute("Square");

      _left = new HudAPIv2.BillBoardHUDMessage(
        Material: material,
        BillBoardColor: borderColor,
        Blend: BlendTypeEnum.PostPP,
        Origin: Vector2D.Zero,
        Width: 0.005f);
      _left.Visible = false;
      _left.Options = options;

      _right = new HudAPIv2.BillBoardHUDMessage(
        Material: material,
        BillBoardColor: borderColor,
        Blend: BlendTypeEnum.PostPP,
        Origin: Vector2D.Zero,
        Width: 0.005f);
      _right.Visible = false;
      _right.Options = options;

      _top = new HudAPIv2.BillBoardHUDMessage(
        Material: material,
        BillBoardColor: borderColor,
        Blend: BlendTypeEnum.PostPP,
        Origin: Vector2D.Zero,
        Height: 0.005f);
      _top.Visible = false;
      _top.Options = options;

      _bottom = new HudAPIv2.BillBoardHUDMessage(
        Material: material,
        BillBoardColor: borderColor,
        Blend: BlendTypeEnum.PostPP,
        Origin: Vector2D.Zero,
        Height: 0.005f);
      _bottom.Visible = false;
      _bottom.Options = options;

      if (background != null)
        UpdateBorder(ref aspectRatio);
    }

    public virtual void Close()
    {
      _left?.DeleteMessage();
      _right?.DeleteMessage();
      _top?.DeleteMessage();
      _bottom?.DeleteMessage();
      _left = _right = _top = _bottom = null;
    }

    public void UpdateBorder(ref double aspectRatio)
    {
      if (!_useBorder)
        return;

      var halfSize = new Vector2D((Background.Width - _left.Width) * aspectRatio * 0.5, (Background.Height - _top.Height) * 0.5);
      var halfSizeX = new Vector2D(halfSize.X, 0);
      var halfSizeY = new Vector2D(0, halfSize.Y);
      _left.Offset = Background.Offset - halfSizeX;
      _right.Offset = Background.Offset + halfSizeX;
      _left.Height = _right.Height = Background.Height;

      _top.Offset = Background.Offset + halfSizeY;
      _bottom.Offset = Background.Offset - halfSizeY;
      _top.Width = _bottom.Width = Background.Width - _left.Width * 2f;
      _left.Origin = _right.Origin = _top.Origin = _bottom.Origin = Background.Origin;
    }

    public virtual void SetVisibility(ref bool enable)
    {
      if (!_useBorder)
        return;
      
      if (_left != null)
        _left.Visible = enable;

      if (_right != null)
        _right.Visible = enable;

      if (_top != null)
        _top.Visible = enable;

      if (_bottom != null)
        _bottom.Visible = enable;
    }
  }
}
