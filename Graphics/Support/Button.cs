using System;
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
using Sandbox.Game;
using AiEnabled.API;

namespace AiEnabled.Graphics.Support
{
  public class Button : TextBox
  {
    public bool IsMouseOver, IsSelected;
    public int Index;

    bool _soundPlayed;
    Vector4 _defaultColor, _mouseOverColor, _selectedColor;
    MyEntity3DSoundEmitter _emitter;
    MySoundPair _soundPair;

    public Button(HudAPIv2.BillBoardHUDMessage bg, HudAPIv2.HUDMessage txt, double aspectRatio,
      Vector4 buttonColor, Vector4 borderColor, Vector4 highlightColor, Vector4 selectColor,
      MyEntity3DSoundEmitter emitter, MySoundPair soundPair, bool useBorder = true)
      : base(bg, txt, aspectRatio, borderColor, useBorder: useBorder)
    {
      _defaultColor = buttonColor;
      _mouseOverColor = highlightColor;
      _selectedColor = selectColor;
      _emitter = emitter;
      _soundPair = soundPair;
    }

    public override void Close()
    {
      _emitter?.Cleanup();
      _emitter = null;
      _soundPair = null;

      base.Close();
    }
    
    public void Reset()
    {
      _soundPlayed = false;
      IsMouseOver = false;
      IsSelected = false;
      Background.BillBoardColor = _defaultColor;
    }

    public void ResetButtonTextures(ref ButtonInfo info, ref Vector2 iconSize)
    {
      Background.uvOffset = Vector2.Zero;
      Background.uvSize = Vector2.One;
      Background.TextureSize = 1;

      Background.Origin = info.Origin;
      Background.Offset = info.Offset;
      Background.Width = info.Size.X;
      Background.Height = info.Size.Y;

      if (Icon != null)
      {
        Icon.uvOffset = Vector2.Zero;
        Icon.uvSize = Vector2.One;
        Icon.TextureSize = 1;

        Icon.Origin = info.Origin;
        Icon.Offset = info.Offset;
        Icon.Width = iconSize.X;
        Icon.Height = iconSize.Y;
      }
    }

    public void SetMouseOver(bool enable)
    {
      IsMouseOver = enable;
      if (IsSelected)
        Background.BillBoardColor = _selectedColor;
      else 
        Background.BillBoardColor = enable ? _mouseOverColor : _defaultColor;

      if (!enable)
      {
        _soundPlayed = false;
      }
      else if (!_soundPlayed)
      {
        try
        {
          PlaySound();
        }
        catch (Exception e)
        {
          MyAPIGateway.Utilities.ShowNotification($"Failed to play sound.. reason:\n{e.Message}");
        }
      }
    }

    public bool SetSelected(bool force = false)
    {
      IsSelected = IsMouseOver || force;
      Background.BillBoardColor = IsSelected ? _selectedColor : _defaultColor;

      return IsSelected;
    }

    void PlaySound()
    {
      var obj = MyAPIGateway.Session?.ControlledObject?.Entity as MyEntity;
      if (obj == null || _soundPair == null || _emitter == null)
        return;

      _soundPlayed = true;
      _emitter.Entity = obj;
      _emitter.PlaySound(_soundPair);
    }

    public struct ButtonInfo
    {
      internal Vector2 Size;
      internal Vector2D Origin, Offset;

      internal ButtonInfo(Vector2 size, Vector2D origin, Vector2D offset)
      {
        Size = size;
        Origin = origin;
        Offset = offset;
      }
    }
  }
}
