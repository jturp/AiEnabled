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
using AiEnabled.API;

namespace AiEnabled.Graphics.Support
{
  public class CheckBox : Button
  {
    public bool IsChecked { get; private set; }
    HudAPIv2.BillBoardHUDMessage _leftSlash, _rightSlash;

    public CheckBox(HudAPIv2.BillBoardHUDMessage bg, HudAPIv2.HUDMessage txt, Color boxColor, Color borderColor, double aspectRatio,
      MyEntity3DSoundEmitter emitter, MySoundPair soundPair, bool isChecked = true)
      : base(bg, txt, aspectRatio, boxColor, borderColor, Color.LightCyan * 0.5f, Color.LightCyan * 0.5f, emitter, soundPair)
    {
      _leftSlash = new HudAPIv2.BillBoardHUDMessage(
        Material: bg.Material,
        Width: bg.Width * 0.1f,
        Height: bg.Height,
        BillBoardColor: borderColor,
        Origin: bg.Origin,
        Offset: bg.Offset,
        Blend: BlendTypeEnum.Standard,
        Rotation: MathHelper.PiOver4);
      _leftSlash.Visible = false;
      _leftSlash.Options = bg.Options;

      _rightSlash = new HudAPIv2.BillBoardHUDMessage(
        Material: bg.Material,
        Width: _leftSlash.Width,
        Height: bg.Height,
        BillBoardColor: borderColor,
        Origin: bg.Origin,
        Offset: bg.Offset,
        Blend: BlendTypeEnum.Standard,
        Rotation: -MathHelper.PiOver4);
      _rightSlash.Visible = false;
      _rightSlash.Options = bg.Options;

      IsChecked = isChecked;
    }

    public override void Close()
    {
      _leftSlash?.DeleteMessage();
      _rightSlash?.DeleteMessage();
      _leftSlash = _rightSlash = null;
      base.Close();
    }

    public void SetChecked(bool check)
    {
      IsChecked = check;
      _leftSlash.Visible = _rightSlash.Visible = check;
    }

    public override void SetVisibility(ref bool enable)
    {
      _leftSlash.Visible = _rightSlash.Visible = enable && IsChecked;
      base.SetVisibility(ref enable);
    }
  }
}
