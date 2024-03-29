﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using Task = ParallelTasks.Task;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

using Sandbox.ModAPI;
using VRage.Utils;
using VRage.ModAPI;
using VRage.Game;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using Sandbox.Game.Contracts;
using VRage.Game.ModAPI;
using AiEnabled.API;

namespace AiEnabled.Graphics.Support
{
  public enum TextAlignment { Center, Left, Right }

  public class TextBox : Border
  {
    public HudAPIv2.BillBoardHUDMessage Icon;
    //public HudAPIv2.BillBoardHUDMessage Background;
    public HudAPIv2.HUDMessage Text;

    public bool IsVisible => Background?.Visible ?? false;
    public Vector2D Position => Background == null ? Vector2D.Zero : Background.Origin + Background.Offset;

    public TextBox(HudAPIv2.BillBoardHUDMessage bg, HudAPIv2.HUDMessage txt, double aspectRatio, Color borderColor,
      HudAPIv2.BillBoardHUDMessage icon = null, bool useBorder = true)
      : base(bg, aspectRatio, borderColor, useBorder)
    {
      Icon = icon;
      Background = bg;
      Text = txt;
    }

    public override void Close()
    {
      Icon?.DeleteMessage();
      Background?.DeleteMessage();
      Text?.DeleteMessage();
      Icon = null;
      Background = null;
      Text = null;

      base.Close();
    }

    public void UpdateText(string txt, double aspectRatio, bool updatePosition = true, bool updateBackground = false)
    {
      if (Text?.Message == null)
        return;

      Text.Message.Clear()
        .Append(txt);

      if (updateBackground)
      {
        bool useBorder = _useBorder && _left.BillBoardColor != Color.Transparent;
        if (useBorder || Background.BillBoardColor != Color.Transparent)
        {
          var length = Text.GetTextLength();
          Background.Width = (float)((length.X + 0.01) / aspectRatio);
          Background.Height = (float)length.Y * -1.5f;

          if (useBorder)
            UpdateBorder(ref aspectRatio);
        }
      }

      if (updatePosition)
      {
        Text.Origin = Background.Origin;
        Text.Offset = Background.Offset - Text.GetTextLength() * 0.5;
      }
    }

    public void SetPositionAligned(ref Vector2D offset, ref double aspectRatio, TextAlignment align = TextAlignment.Center)
    {
      // offset is center of button background
      Text.Origin = Background.Origin;
      Background.Offset = offset;
      var length = Text.GetTextLength();

      if (align == TextAlignment.Center)
      {
        Text.Offset = offset - length * 0.5;
      }
      else if (align == TextAlignment.Left)
      {
        Text.Offset = offset - new Vector2D(Background.Width * aspectRatio * 0.98, length.Y) * 0.5;
      }
      else // right align
      {
        Text.Offset = offset + new Vector2D(Background.Width * aspectRatio * 0.98, 0) * 0.5 - length * 0.5;
      }

      if (Icon != null)
      {
        Icon.Origin = Background.Origin;
        Icon.Offset = Background.Offset;
      }

      if (_useBorder)
        UpdateBorder(ref aspectRatio);
    }

    public void SetRelativePosition(ref Vector2D offset, ref double aspectRatio, bool center = true)
    {
      Text.Origin = Background.Origin;

      if (center)
      {
        Background.Offset = offset;
        Text.Offset = offset - Text.GetTextLength() * 0.5;
      }
      else
      {
        Text.Offset = offset;
        Background.Offset = offset + Text.GetTextLength() * 0.5;
      }

      if (Icon != null)
      {
        Icon.Origin = Background.Origin;
        Icon.Offset = Background.Offset;
      }

      UpdateBorder(ref aspectRatio);
    }

    public void SetAbsolutePosition(ref Vector2D position, ref double aspectRatio, bool center = true)
    {
      Background.Origin = Text.Origin = Vector2D.Zero;

      if (center)
      {
        Background.Offset = position;
        Text.Offset = position - Text.GetTextLength() * 0.5;
      }
      else
      {
        Text.Offset = position;
        Background.Offset = position + Text.GetTextLength() * 0.5;
      }

      if (Icon != null)
      {
        Icon.Origin = Background.Origin;
        Icon.Offset = Background.Offset;
      }

      UpdateBorder(ref aspectRatio);
    }

    public void SetTextBottomLeft(double aspectRatio)
    {
      var bottomLeft = Background.Offset - new Vector2D(Background.Width * aspectRatio, Background.Height) * 0.5;
      var length = Text.GetTextLength();

      Text.Origin = Background.Origin;
      Text.Offset = bottomLeft - new Vector2D(length.X * -0.1, length.Y);
    }

    public void Move(Vector2D delta, ref double aspectRatio)
    {
      if (delta.X != 0)
        delta.X *= aspectRatio;

      Background.Offset += delta;

      if (Text != null)
        Text.Offset += delta;

      if (Icon != null)
        Icon.Offset += delta;
    }

    public void SetIconVisibility(bool enable)
    {
      if (Icon != null)
        Icon.Visible = enable;

      if (Text != null)
        Text.Visible = enable;
    }

    public override void SetVisibility(ref bool enable)
    {
      Background.Visible = enable;
      SetIconVisibility(enable);
      base.SetVisibility(ref enable);
    }
  }
}
