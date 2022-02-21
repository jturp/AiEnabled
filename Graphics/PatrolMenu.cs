using AiEnabled.API;
using AiEnabled.Graphics.Support;
using AiEnabled.Utilities;

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace AiEnabled.Graphics
{
  public class Route
  {
    public string WorldName;
    public List<Vector3D> Waypoints;

    public void Set(List<Vector3D> points)
    {
      WorldName = MyAPIGateway.Session.Name;

      if (Waypoints == null)
        Waypoints = new List<Vector3D>();
      else
        Waypoints.Clear();

      Waypoints.AddList(points);
    }

    public void Set(List<SerializableVector3D> points, string world)
    {
      WorldName = string.IsNullOrWhiteSpace(world) ? MyAPIGateway.Session.Name : world;

      if (Waypoints == null)
        Waypoints = new List<Vector3D>();
      else
        Waypoints.Clear();

      for (int i = 0; i < points.Count; i++)
        Waypoints.Add(points[i]);
    }
  }

  public class PatrolMenu
  {
    public bool HasFocus;
    internal HudAPIv2.BillBoardHUDMessage MenuBgBorder, MenuBackground;
    internal TextBox Title;
    internal Button BtnSelect, BtnCreate, BtnRename, BtnDelete, BtnClose;
    internal ListBox RouteBox;

    MyEntity3DSoundEmitter _emitter;
    MySoundPair _soundPair;
    MySoundPair _hudClickSoundPair;
    MySoundPair _errorSoundPair;

    int _routeIndexAwaitingName;
    Vector4 _borderColor, _billboardColor, _mouseOverColor;
    bool _wasWithinBounds;

    public PatrolMenu(double aspectRatio, Color buttonColor, MyEntity3DSoundEmitter emitter, MySoundPair mouseOver, MySoundPair hudClick, MySoundPair hudError)
    {
      _mouseOverColor = Color.LightCyan * 0.5f;
      _borderColor = new Color(200, 200, 200, 250);
      _billboardColor = new Color(41, 54, 62, 150);
      _emitter = emitter;
      _soundPair = mouseOver;
      _hudClickSoundPair = hudClick;
      _errorSoundPair = hudError;

      var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
      var squareOuter = MyStringId.GetOrCompute("AiEnabled_SquareOuter");
      var square = MyStringId.GetOrCompute("AiEnabled_Square");
      var square2 = MyStringId.GetOrCompute("AiEnabled_Square2");

      MenuBackground = new HudAPIv2.BillBoardHUDMessage(
        Material: squareOuter,
        Origin: new Vector2D(0, 0.2),
        BillBoardColor: _billboardColor,
        Width: 1.5f,
        Height: 1.25f,
        Blend: BlendTypeEnum.Standard);

      MenuBackground.Options |= options;
      MenuBackground.Visible = false;

      MenuBgBorder = new HudAPIv2.BillBoardHUDMessage(
        Material: MyStringId.GetOrCompute("AiEnabled_SquareOuterBorder"),
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        BillBoardColor: _borderColor,
        Width: MenuBackground.Width,
        Height: MenuBackground.Height,
        Blend: BlendTypeEnum.PostPP);

      MenuBgBorder.Options |= options;
      MenuBgBorder.Visible = false;

      var renameBB = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        BillBoardColor: _billboardColor,
        Blend: BlendTypeEnum.PostPP);

      renameBB.Options |= options;
      renameBB.Visible = false;

      var renameMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Rename ~"), renameBB.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
      renameMsg.Options |= options;
      renameMsg.Visible = false;

      var length = renameMsg.GetTextLength();
      var width = length.X * 1.25f / aspectRatio;
      var height = length.Y * -2;

      renameBB.Width = (float)width;
      renameBB.Height = (float)height;
      renameBB.Offset = new Vector2D(MenuBackground.Width * 0.3125 * aspectRatio, height * -1.75 * 2);
      renameMsg.Offset = renameBB.Offset - length * 0.5;

      BtnRename = new Button(renameBB, renameMsg, aspectRatio, _billboardColor, _borderColor, _mouseOverColor, _mouseOverColor, _emitter, _soundPair);

      var deleteBB = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        BillBoardColor: _billboardColor,
        Blend: BlendTypeEnum.PostPP);

      deleteBB.Options |= options;
      deleteBB.Visible = false;

      var deleteMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Delete ~"), deleteBB.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
      deleteMsg.Options |= options;
      deleteMsg.Visible = false;

      length = deleteMsg.GetTextLength();
      deleteBB.Width = (float)width;
      deleteBB.Height = (float)height;
      deleteBB.Offset = new Vector2D(MenuBackground.Width * 0.3125 * aspectRatio, height * -1.75 * 3);
      deleteMsg.Offset = deleteBB.Offset - length * 0.5;

      BtnDelete = new Button(deleteBB, deleteMsg, aspectRatio, _billboardColor, _borderColor, _mouseOverColor, _mouseOverColor, _emitter, _soundPair);

      var closeBB = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        BillBoardColor: _billboardColor,
        Blend: BlendTypeEnum.PostPP);

      closeBB.Options |= options;
      closeBB.Visible = false;

      var closeMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Close ~"), closeBB.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
      closeMsg.Options |= options;
      closeMsg.Visible = false;

      length = closeMsg.GetTextLength();
      closeBB.Width = (float)width;
      closeBB.Height = (float)height;
      closeBB.Offset = new Vector2D(renameBB.Offset.X, height * -1.75 * 4);
      closeMsg.Offset = closeBB.Offset - length * 0.5;

      BtnClose = new Button(closeBB, closeMsg, aspectRatio, _billboardColor, _borderColor, _mouseOverColor, _mouseOverColor, _emitter, _soundPair);

      var createBB = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        BillBoardColor: _billboardColor,
        Blend: BlendTypeEnum.PostPP);

      createBB.Options |= options;
      createBB.Visible = false;

      var createMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ New ~"), createBB.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
      createMsg.Options |= options;
      createMsg.Visible = false;

      length = createMsg.GetTextLength();
      createBB.Width = (float)width;
      createBB.Height = (float)height;
      createBB.Offset = new Vector2D(renameBB.Offset.X, 0);
      createMsg.Offset = createBB.Offset - length * 0.5;

      BtnCreate = new Button(createBB, createMsg, aspectRatio, _billboardColor, _borderColor, _mouseOverColor, _mouseOverColor, _emitter, _soundPair);

      var selectBB = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: closeBB.Origin,
        BillBoardColor: _billboardColor,
        Blend: BlendTypeEnum.PostPP);

      selectBB.Options |= options;
      selectBB.Visible = false;

      var selectMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Start ~"), selectBB.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
      selectMsg.Options |= options;
      selectMsg.Visible = false;

      length = selectMsg.GetTextLength();
      selectBB.Width = (float)width;
      selectBB.Height = (float)height;
      selectBB.Offset = new Vector2D(renameBB.Offset.X, height * -1.75);
      selectMsg.Offset = selectBB.Offset - length * 0.5;

      BtnSelect = new Button(selectBB, selectMsg, aspectRatio, _billboardColor, _borderColor, _mouseOverColor, _mouseOverColor, _emitter, _soundPair);

      var logoSize = MenuBackground.Height * 0.15f;
      var logoBg = new HudAPIv2.BillBoardHUDMessage(
        Material: square,
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        Offset: new Vector2D(renameBB.Offset.X, MenuBackground.Height * 0.25),
        BillBoardColor: Color.Transparent,
        Width: logoSize,
        Height: logoSize,
        Blend: BlendTypeEnum.PostPP);

      logoBg.Options |= options;
      logoBg.Visible = false;

      var logoIcon = new HudAPIv2.BillBoardHUDMessage(
        Material: MyStringId.GetOrCompute("AiEnabled_Logo"),
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        Offset: logoBg.Offset,
        BillBoardColor: new Color(255, 255, 255, 225),
        Width: logoSize,
        Height: logoSize,
        Blend: BlendTypeEnum.PostPP);

      logoIcon.Options |= options;
      logoIcon.Visible = false;

      var logoMsg = new HudAPIv2.HUDMessage(new StringBuilder("~ Bot Patrol Manager ~"), logoBg.Origin, Scale: 0.9, Blend: BlendTypeEnum.PostPP);
      length = logoMsg.GetTextLength();
      logoMsg.Options |= options;
      logoMsg.Visible = false;
      logoMsg.Offset = logoBg.Offset - new Vector2D(length.X * 0.5, logoBg.Height * 0.5 + length.Y * -0.5);

      Title = new TextBox(logoBg, logoMsg, aspectRatio, Color.Transparent, logoIcon, useBorder: false);

      var bg = new HudAPIv2.BillBoardHUDMessage(
        Material: square2,
        Origin: MenuBackground.Origin + MenuBackground.Offset,
        Offset: new Vector2D(MenuBackground.Width * -0.155 * aspectRatio, 0),
        BillBoardColor: _billboardColor,
        Width: MenuBackground.Width * 0.585f,
        Height: MenuBackground.Height * 0.85f,
        Blend: BlendTypeEnum.Standard);

      bg.Options |= options;
      bg.Visible = false;

      var bdr = new HudAPIv2.BillBoardHUDMessage(
        Material: MyStringId.GetOrCompute("AiEnabled_SquareBorder2"),
        Origin: bg.Origin,
        Offset: bg.Offset,
        BillBoardColor: _borderColor,
        Width: bg.Width * 0.99f,
        Height: bg.Height * 0.89f,
        Blend: BlendTypeEnum.PostPP);

      bdr.Options |= options;
      bdr.Visible = false;

      RouteBox = new ListBox(bg, bdr, buttonColor, emitter, mouseOver, aspectRatio);
    }

    public void TryActivate(ref double aspectRatio, List<Vector3D> patrolList, out bool newPatrol, out bool closeMenu, out bool renamePatrol)
    {
      newPatrol = BtnCreate.IsMouseOver;
      closeMenu = BtnClose.IsMouseOver;
      renamePatrol = false;

      if (newPatrol || closeMenu)
      {
        PlaySound(_hudClickSoundPair);
        RouteBox.ClearSelected();
      }
      else if (RouteBox.HasFocus)
      {
        if (RouteBox.SetSelected())
          PlaySound(_hudClickSoundPair);
      }
      else if (BtnSelect.IsMouseOver)
      {
        if (RouteBox.SelectedItem != null)
        {
          PlaySound(_hudClickSoundPair);
          RouteBox.GetPatrolList(patrolList);
        }
        else
        {
          PlaySound(_errorSoundPair);
        }
      }
      else if (BtnDelete.IsMouseOver)
      {
        if (RouteBox.SelectedItem != null)
        {
          PlaySound(_hudClickSoundPair);
          RouteBox.DeleteSelected(ref aspectRatio);
          AiSession.Instance.UpdateConfig(true);
        }
        else
        {
          PlaySound(_errorSoundPair);
        }
      }
      else if (BtnRename.IsMouseOver)
      {
        if (RouteBox.SelectedItem != null)
        {
          var idx = RouteBox.GetRouteIndex();
          if (idx >= 0)
          {
            renamePatrol = true;
            PlaySound(_hudClickSoundPair);
            _routeIndexAwaitingName = idx;
          }
          else
          {
            PlaySound(_errorSoundPair);
          }
        }
        else
        {
          PlaySound(_errorSoundPair);
        }
      }
    }

    public void RenameRoute(string name, ref double aspectRatio)
    {
      if (_routeIndexAwaitingName >= 0)
      {
        RouteBox.RenameRoute(ref _routeIndexAwaitingName, name, ref aspectRatio);
        _routeIndexAwaitingName = -1;
      }
      else
      {
        AiSession.Instance.Logger.Log($"PatrolMenu.RenameRoute: Route to rename had an invalid index", MessageType.WARNING);
      }
    }

    void PlaySound(MySoundPair sp)
    {
      var obj = MyAPIGateway.Session?.ControlledObject?.Entity as MyEntity;
      if (obj == null || _emitter == null)
        return;

      _emitter.Entity = obj;
      _emitter.PlaySound(sp);
    }

    public bool SetMouseOver(ref Vector2D cursorPosition, ref double aspectRatio, bool autoFalse = false)
    {
      bool isWithinBounds = cursorPosition.IsWithinBounds(MenuBackground, aspectRatio);

      if (!isWithinBounds && !_wasWithinBounds)
        return false;

      autoFalse |= !isWithinBounds;
      _wasWithinBounds = isWithinBounds;
      bool any;

      if (cursorPosition.IsWithinBounds(BtnClose.Background, aspectRatio))
      {
        any = true;
        BtnClose.SetMouseOver(true);
        BtnSelect.SetMouseOver(false);
        BtnCreate.SetMouseOver(false);
        BtnRename.SetMouseOver(false);
        BtnDelete.SetMouseOver(false);
      }
      else if (cursorPosition.IsWithinBounds(BtnCreate.Background, aspectRatio))
      {
        any = true;
        BtnClose.SetMouseOver(false);
        BtnSelect.SetMouseOver(false);
        BtnCreate.SetMouseOver(true);
        BtnRename.SetMouseOver(false);
        BtnDelete.SetMouseOver(false);
      }
      else if (cursorPosition.IsWithinBounds(BtnSelect.Background, aspectRatio))
      {
        any = true;
        BtnClose.SetMouseOver(false);
        BtnSelect.SetMouseOver(true);
        BtnCreate.SetMouseOver(false);
        BtnRename.SetMouseOver(false);
        BtnDelete.SetMouseOver(false);
      }
      else if (cursorPosition.IsWithinBounds(BtnRename.Background, aspectRatio))
      {
        any = true;
        BtnClose.SetMouseOver(false);
        BtnSelect.SetMouseOver(false);
        BtnCreate.SetMouseOver(false);
        BtnRename.SetMouseOver(true);
        BtnDelete.SetMouseOver(false);
      }
      else if (cursorPosition.IsWithinBounds(BtnDelete.Background, aspectRatio))
      {
        any = true;
        BtnClose.SetMouseOver(false);
        BtnSelect.SetMouseOver(false);
        BtnCreate.SetMouseOver(false);
        BtnRename.SetMouseOver(false);
        BtnDelete.SetMouseOver(true);
      }
      else
      {
        BtnClose.SetMouseOver(false);
        BtnSelect.SetMouseOver(false);
        BtnCreate.SetMouseOver(false);
        BtnRename.SetMouseOver(false);
        BtnDelete.SetMouseOver(false);

        any = RouteBox.SetMouseOver(ref cursorPosition, ref aspectRatio, ref autoFalse);
      }

      HasFocus = any;
      return any;
    }

    public void SetVisibility(bool enable)
    {
      MenuBgBorder.Visible = enable;
      MenuBackground.Visible = enable;

      Title.SetVisibility(ref enable);
      BtnClose.SetVisibility(ref enable);
      BtnCreate.SetVisibility(ref enable);
      BtnSelect.SetVisibility(ref enable);
      BtnRename.SetVisibility(ref enable);
      BtnDelete.SetVisibility(ref enable);

      RouteBox.SetVisibility(ref enable);
    }

    public void Close()
    {
      MenuBackground?.DeleteMessage();
      MenuBackground = null;

      MenuBgBorder?.DeleteMessage();
      MenuBgBorder = null;

      _emitter?.Cleanup();
      _emitter = null;
      _soundPair = null;

      RouteBox?.Close();
    }
  }
}
