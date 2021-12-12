using AiEnabled.Utilities;

using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace AiEnabled.Graphics.Support
{
  public class InventoryBox
  {
    public bool HasFocus;
    public InventoryMapItem? SelectedItem;

    internal HudAPIv2.BillBoardHUDMessage Background, Border;
    internal HudAPIv2.HUDMessage Header, Footer;
    internal TextBox ToolTip;
    internal Button ScrollBar;

    Stack<Button> _btnStack;
    Box _scrollBarBox;
    Dictionary<InventoryMapItem, Button> _buttonDict = new Dictionary<InventoryMapItem, Button>();
    Dictionary<InventoryMapItem, ButtonInfo> _infoDict = new Dictionary<InventoryMapItem, ButtonInfo>();
    MyEntity3DSoundEmitter _emitter;
    MySoundPair _soundPair;
    Vector4 _btnColor, _borderColor, _mouseOverColor, _selectedColor;
    Vector2 _size;
    bool _wasWithinBounds;
    string _lastPlayerName;
    int _maxVisibleRows, _totalRows, _lastVisibleIndex;
    float _buttonViewOffsetY;

    public InventoryBox(HudAPIv2.BillBoardHUDMessage bg, double aspectRatio, Color buttonColor,
      MyEntity3DSoundEmitter emitter, MySoundPair soundPair)
    {
      _selectedColor = (Color.LightCyan * 0.75f).ToVector4().ToLinearRGB();
      _mouseOverColor = (Color.LightCyan * 0.5f).ToVector4().ToLinearRGB();
      _btnColor = buttonColor.ToVector4().ToLinearRGB();
      _borderColor = new Color(200, 200, 200, 250);
      _emitter = emitter;
      _soundPair = soundPair;
      Background = bg;

      var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;

      Border = new HudAPIv2.BillBoardHUDMessage(
        Material: MyStringId.GetOrCompute("AiEnabled_SquareBorder2"),
        Origin: bg.Origin,
        Offset: bg.Offset,
        BillBoardColor: _borderColor,
        Width: bg.Width * 0.99f,
        Height: bg.Height * 0.89f,
        Blend: BlendTypeEnum.PostPP);

      Border.Options |= options;
      Border.Visible = false;

      Header = new HudAPIv2.HUDMessage(
        Message: new StringBuilder("~ Inventory ~"),
        Origin: bg.Origin + bg.Offset,
        Scale: 0.9,
        Blend: BlendTypeEnum.PostPP);

      var length = Header.GetTextLength();
      Header.Offset = new Vector2D(-Border.Width * aspectRatio * 0.25, Border.Height * 0.5) - new Vector2D(length.X * 0.5, length.Y * 1.25);
      Header.Options |= options;
      Header.Visible = false;

      Footer = new HudAPIv2.HUDMessage(
        Message: new StringBuilder(""),
        Origin: Header.Origin,
        Scale: 0.9,
        Blend: BlendTypeEnum.PostPP);

      Footer.Options |= options;
      Footer.Visible = false;

      _size = new Vector2(Border.Width / 7) * 0.9f;
      _maxVisibleRows = (int)Math.Floor(Border.Height / _size.Y);

      var numRows = (int)Math.Ceiling(Border.Height / _size.Y) + 1;
      var maxItems = numRows * 7;
      _btnStack = new Stack<Button>(maxItems);

      var size = _size * 0.9f;
      var iconSize = size.Y * 0.9f;

      for (int i = 0; i < maxItems; i++)
      {
        var hudBB = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Square"),
          Origin: Border.Origin,
          BillBoardColor: _btnColor,
          Width: size.X,
          Height: size.Y,
          Blend: BlendTypeEnum.PostPP);

        var icon = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Square"),
          Origin: hudBB.Origin,
          BillBoardColor: Color.White,
          Width: iconSize,
          Height: iconSize,
          Blend: BlendTypeEnum.PostPP);

        var hudMsg = new HudAPIv2.HUDMessage(
          Message: new StringBuilder(),
          Origin: hudBB.Origin,
          Scale: 0.7f,
          Blend: BlendTypeEnum.PostPP);

        hudBB.Visible = hudMsg.Visible = icon.Visible = false;
        hudBB.Options = hudMsg.Options = icon.Options = options;
        hudBB.uvEnabled = icon.uvEnabled = true;
        var btn = new Button(hudBB, hudMsg, aspectRatio, _btnColor, Color.Transparent, _mouseOverColor, _selectedColor, _emitter, _soundPair, false);
        btn.Icon = icon;

        _btnStack.Push(btn);
      }

      var scrollBoxBg = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Square"),
          Origin: Border.Origin + Border.Offset,
          BillBoardColor: Color.Transparent,
          Width: (Border.Width - _size.X * 7) * 0.9f,
          Height: Border.Height,
          Blend: BlendTypeEnum.PostPP);

      scrollBoxBg.Options |= options;
      scrollBoxBg.Offset = new Vector2D(Border.Width - scrollBoxBg.Width, 0) * 0.5 * aspectRatio;

      _scrollBarBox = new Box(aspectRatio, _borderColor, true, scrollBoxBg);
      _scrollBarBox.SetVisibility(false);

      var scrollBarBg = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Square"),
          Origin: scrollBoxBg.Origin + scrollBoxBg.Offset,
          BillBoardColor: _btnColor,
          Width: scrollBoxBg.Width - 0.01f,
          Height: scrollBoxBg.Height - 0.01f,
          Blend: BlendTypeEnum.PostPP);

      scrollBarBg.Options |= options;
      scrollBarBg.Visible = false;
      scrollBarBg.uvEnabled = true;

      ScrollBar = new Button(scrollBarBg, null, aspectRatio, _btnColor, Color.Transparent, _mouseOverColor, _selectedColor, null, null, false);

      var toolTipBB = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Square"),
          Origin: Border.Origin,
          BillBoardColor: Color.LightCyan,
          Width: (float)length.X,
          Height: (float)-length.Y,
          Blend: BlendTypeEnum.PostPP);

      toolTipBB.Options |= options;
      toolTipBB.Visible = false;

      var toolTipMsg = new HudAPIv2.HUDMessage(
          Message: new StringBuilder(),
          Origin: toolTipBB.Origin,
          Scale: 0.7f,
          Blend: BlendTypeEnum.PostPP);

      toolTipMsg.Options |= options;
      toolTipMsg.Visible = false;
      toolTipMsg.InitialColor = Color.Black;

      ToolTip = new TextBox(toolTipBB, toolTipMsg, aspectRatio, Color.Transparent, useBorder: false);
    }

    public void HideToolTip() => ToolTip?.SetVisibility(false);

    public void UpdateScrollBar(ref float yDelta, ref double aspectRatio, bool adjustViewOffset = true)
    {
      if (_lastVisibleIndex < 0)
        return;

      var offset = ScrollBar.Background.Offset;
      var offsetY = offset.Y + yDelta;
      var halfSize = ScrollBar.Background.Height * 0.5;

      var boxCenter = _scrollBarBox._background.Origin.Y + _scrollBarBox._background.Offset.Y;
      var boxHalfSize = (_scrollBarBox._background.Height - 0.01) * 0.5;
      var top = boxCenter + boxHalfSize - halfSize - ScrollBar.Background.Origin.Y;
      var bottom = boxCenter - boxHalfSize + halfSize - ScrollBar.Background.Origin.Y;

      if (offsetY < bottom)
      {
        _buttonViewOffsetY = (float)(top - bottom);
        offsetY = bottom;
      }
      else if (offsetY > top)
      {
        _buttonViewOffsetY = 0;
        offsetY = top;
      }

      yDelta = -(float)(offsetY - offset.Y);

      if (yDelta != 0)
      {
        if (adjustViewOffset)
        {
          _buttonViewOffsetY = Math.Max(0, (float)Math.Round(_buttonViewOffsetY + yDelta, 4));
        }

        var percentBar = yDelta / (float)(top - bottom);
        ScrollBar.Background.Offset = new Vector2D(offset.X, offsetY);
        UpdateItemPositions(ref yDelta, ref percentBar, ref aspectRatio);
      }
    }

    void UpdateItemPositions(ref float yDelta, ref float percentBar, ref double aspectRatio)
    {
      var screenPx = HudAPIv2.APIinfo.ScreenPositionOnePX;
      var totalRowheight = _totalRows * _size.Y;
      var visibleSpace = Border.Height - 0.02f;
      yDelta = (totalRowheight - visibleSpace) * percentBar;
      var offsetDelta = new Vector2D(0, yDelta);

      _lastVisibleIndex = -1;

      foreach (var kvp in _buttonDict)
      {
        var btn = kvp.Value;
        var info = _infoDict[kvp.Key];
        info.Offset += offsetDelta;
        _infoDict[kvp.Key] = info;

        if (btn.Text != null)
         btn.Text.Offset += offsetDelta;

        bool clipped;
        if ((ConfineButtonToSurface(btn, aspectRatio, screenPx, out clipped, info) && clipped) || !btn.Position.IsWithinBounds(Border, aspectRatio))
        {
          if (btn.Index > _lastVisibleIndex)
          {
            _lastVisibleIndex = btn.Index;
          }
        }
      }

      _lastVisibleIndex -= 7; // set it to the previous row so it's the last that ISN'T clipped
    }

    public void UpdateInventory(List<InventoryMapItem> items, double aspectRatio, string name, MyInventory inv, out bool newToolTipCreated, bool fullRefresh = true)
    {
      if (!string.IsNullOrWhiteSpace(name) && name != _lastPlayerName)
      {
        _lastPlayerName = name;
        Header.Message.Clear().Append($"~ {name} ~");

        var length = Header.GetTextLength();
        Header.Offset = new Vector2D(-Border.Width * aspectRatio * 0.25, Border.Height * 0.5) - new Vector2D(length.X * 0.5, length.Y * 1.25);
      }

      var size = _size * 0.9f;
      var iconSize = size.Y * 0.9f;

      foreach (var kvp in _buttonDict)
      {
        var btn = kvp.Value;
        btn.SetVisibility(false);
        btn.Reset();

        var info = _infoDict[kvp.Key];
        ResetButtonTextures(btn, info);
        _btnStack.Push(btn);
      }

      _buttonDict.Clear();
      _infoDict.Clear();

      var options = HudAPIv2.Options.Fixed | HudAPIv2.Options.FOVScale;
      var firstOffset = Border.Offset + new Vector2D(-Border.Width * aspectRatio, Border.Height) * 0.5 + new Vector2D(_size.X * aspectRatio, -_size.Y) * 0.55;
      _totalRows = 1;
      _lastVisibleIndex = -1;

      bool checkSelected = SelectedItem?.InventoryItem != null;
      bool selectedFound = false;
      newToolTipCreated = false;

      for (int i = 0; i < items.Count; i++)
      {
        var item = items[i];

        Button btn;
        if (_btnStack.Count > 0)
        {
          btn = _btnStack.Pop();
          btn.Icon.Material = MyStringId.GetOrCompute($"AiEnabled_{item.IconName}");
          btn.UpdateText($"x{(float)item.InventoryItem.Amount:0.##}", aspectRatio);
        }
        else
        {
          newToolTipCreated = true;

          var bg = new HudAPIv2.BillBoardHUDMessage(
            Material: MyStringId.GetOrCompute("Square"),
            Origin: Border.Origin,
            BillBoardColor: _btnColor,
            Width: size.X,
            Height: size.Y,
            Blend: BlendTypeEnum.PostPP);

          var icon = new HudAPIv2.BillBoardHUDMessage(
            Material: MyStringId.GetOrCompute($"AiEnabled_{item.IconName}"),
            Origin: bg.Origin,
            BillBoardColor: Color.White,
            Width: iconSize,
            Height: iconSize,
            Blend: BlendTypeEnum.PostPP);

          var hm = new HudAPIv2.HUDMessage(
            Message: new StringBuilder($"x{(float)item.InventoryItem.Amount:0.##}"),
            Origin: bg.Origin,
            Scale: 0.7f,
            Blend: BlendTypeEnum.PostPP);

          bg.Visible = hm.Visible = icon.Visible = false;
          bg.Options = hm.Options = icon.Options = options;
          bg.uvEnabled = icon.uvEnabled = true;
          btn = new Button(bg, hm, aspectRatio, _btnColor, Color.Transparent, _mouseOverColor, _selectedColor, _emitter, _soundPair, false);
          btn.Icon = icon;
        }

        var mod = i % 7;
        if (i > 0 && mod == 0)
        {
          _totalRows++;
          if (_lastVisibleIndex < 0 && _totalRows > _maxVisibleRows)
            _lastVisibleIndex = i - 1;

          firstOffset -= new Vector2D(0, _size.Y);
        }

        var position = firstOffset + new Vector2D(_size.X * aspectRatio, 0) * mod;
        btn.SetRelativePosition(position, aspectRatio);
        btn.SetTextBottomLeft(aspectRatio);
        btn.Index = i;

        _buttonDict[item] = btn;
        _infoDict[item] = new ButtonInfo(size, btn.Background.Origin, btn.Background.Offset);

        if (checkSelected && item.InventoryItem.ItemId == SelectedItem.Value.InventoryItem.ItemId)
        {
          selectedFound = true;
          checkSelected = false;
          SelectedItem = item;
          btn.SetSelected(true);
        }
      }

      if (checkSelected && !selectedFound)
        SelectedItem = null;

      var sBox = _scrollBarBox._background;
      var sBar = ScrollBar.Background;

      if (_totalRows > _maxVisibleRows)
      {
        var actualRowHeight = _size.Y * _totalRows + 0.02f;
        sBar.Height = sBox.Height / actualRowHeight;
        var sBarOffsetY = (sBox.Height - sBar.Height) * 0.5;
        sBar.Offset = new Vector2D(sBar.Offset.X, sBarOffsetY);
        sBar.Height -= 0.01f;

        if (!fullRefresh && _buttonViewOffsetY > 0)
        {
          var offset = -_buttonViewOffsetY;
          UpdateScrollBar(ref offset, ref aspectRatio, false);
        }
      }
      else
      {
        _buttonViewOffsetY = 0;
        sBar.Offset = Vector2D.Zero;
        sBar.Height = sBox.Height - 0.01f;
      }

      Footer.Message.Clear().Append($"Vol: {(float)inv.CurrentVolume * 1000:#,##0.#} / {(float)inv.MaxVolume * 1000:#,##0.#} L");
      var ftLength = Footer.GetTextLength();
      Footer.Offset = new Vector2D(Border.Width * aspectRatio * 0.25, -Border.Height * 0.5) + new Vector2D(ftLength.X * -0.5, ftLength.Y * 0.25);

      if (newToolTipCreated)
      {
        ToolTip.Close();

        var length = Header.GetTextLength();
        var toolTipBB = new HudAPIv2.BillBoardHUDMessage(
          Material: MyStringId.GetOrCompute("Square"),
          Origin: Border.Origin,
          BillBoardColor: Color.LightCyan,
          Width: (float)length.X,
          Height: (float)-length.Y,
          Blend: BlendTypeEnum.PostPP);

        toolTipBB.Options |= options;
        toolTipBB.Visible = false;

        var toolTipMsg = new HudAPIv2.HUDMessage(
            Message: new StringBuilder(),
            Origin: toolTipBB.Origin,
            Scale: 0.7f,
            Blend: BlendTypeEnum.PostPP);

        toolTipMsg.Options |= options;
        toolTipMsg.Visible = false;
        toolTipMsg.InitialColor = Color.Black;

        ToolTip = new TextBox(toolTipBB, toolTipMsg, aspectRatio, Color.Transparent, useBorder: false);
      }
    }

    public void ResetButtonPosition(Button btn, double aspectRatio)
    {
      var info = _infoDict[SelectedItem.Value];
      ResetButtonTextures(btn, info);

      btn.SetRelativePosition(info.Offset, aspectRatio);
      btn.SetTextBottomLeft(aspectRatio);

      bool clipped;
      ConfineButtonToSurface(btn, aspectRatio, HudAPIv2.APIinfo.ScreenPositionOnePX, out clipped);
    }

    public void SetVisibility(bool enable, ref double aspectRatio, ref Vector2D screenPx)
    {
      Background.Visible = enable;
      Border.Visible = enable;
      Header.Visible = enable;
      Footer.Visible = enable;

      ScrollBar.SetVisibility(enable);
      _scrollBarBox.SetVisibility(enable);

      bool checkIndex = enable && _lastVisibleIndex > 0;
      foreach (var kvp in _buttonDict)
      {
        if (checkIndex)
        {
          bool clipped;
          var info = _infoDict[kvp.Key];
          ConfineButtonToSurface(kvp.Value, aspectRatio, screenPx, out clipped, info);
        }
        else
          kvp.Value.SetVisibility(enable);
      }

      if (!enable)
      {
        _lastVisibleIndex = -1;
        _buttonViewOffsetY = 0;
        ToolTip.SetVisibility(false);
      }
    }

    public bool SetMouseOver(Vector2D cursorPosition, double aspectRatio, bool autoFalse = false)
    {
      bool isWithinBounds = cursorPosition.IsWithinBounds(Border, aspectRatio);
      bool any = false;

      if (!isWithinBounds && !_wasWithinBounds)
        return false;

      autoFalse |= !isWithinBounds;
      _wasWithinBounds = isWithinBounds;

      if (cursorPosition.IsWithinBounds(_scrollBarBox._background, aspectRatio))
      {
        var inScroll = !autoFalse && cursorPosition.IsWithinBounds(ScrollBar.Background, aspectRatio);
        ScrollBar.SetMouseOver(inScroll);
      }
      else
      {
        ScrollBar.SetMouseOver(false);

        foreach (var kvp in _buttonDict)
        {
          var btn = kvp.Value;
          var within = !autoFalse && btn.IsVisible && cursorPosition.IsWithinBounds(btn.Background, aspectRatio);
          if (within)
          {
            autoFalse = true;
            any = true;

            ToolTip.UpdateText(kvp.Key.ItemName, aspectRatio, updatePosition: false, updateBackground: true);
            ToolTip.SetAbsolutePosition(cursorPosition + new Vector2D(0.05 * aspectRatio, -ToolTip.Background.Height * 1.5), aspectRatio, false);
          }

          btn.SetMouseOver(within);
        }
      }

      ToolTip.SetVisibility(any);
      HasFocus = any;
      return HasFocus;
    }

    public Button SetSelected()
    {
      Button b = null;
      SelectedItem = null;

      foreach (var kvp in _buttonDict)
      {
        var btn = kvp.Value;
        if (btn.SetSelected())
        {
          SelectedItem = kvp.Key;
          b = btn;
        }
      }

      return b;
    }

    public void ClearSelected()
    {
      HasFocus = false;
      SelectedItem = null;
      ScrollBar.Reset();

      foreach (var btn in _buttonDict.Values)
        btn.Reset();
    }

    public void Close()
    {
      Background?.DeleteMessage();
      Background = null;

      Border?.DeleteMessage();
      Border = null;

      Header?.DeleteMessage();
      Header = null;

      Footer?.DeleteMessage();
      Footer = null;

      ScrollBar?.Close();
      _scrollBarBox?.Close();

      if (_btnStack != null)
      {
        while (_btnStack.Count > 0)
        {
          var btn = _btnStack.Pop();
          btn?.Close();
        }

        _btnStack = null;
      }

      if (_buttonDict != null)
      {
        foreach (var btn in _buttonDict.Values)
          btn?.Close();

        _buttonDict?.Clear();
        _buttonDict = null;
      }

      _infoDict?.Clear();
      _emitter?.Cleanup();
      _infoDict = null;
      _emitter = null;
      _soundPair = null;
    }

    bool ConfineButtonToSurface(Button btn, double aspectRatio, Vector2D screenPx, out bool clipped, ButtonInfo? info = null)
    {
      if (info.HasValue)
        ResetButtonTextures(btn, info.Value);
  
      clipped = false;
      var position = btn.Background.Origin + btn.Background.Offset;
      btn.SetVisibility(true);

      BoundingBox2D prunik, borderBox, btnBox;
      if (btn.Background.IntersectsBillboard(Border, ref aspectRatio, true, out prunik, out borderBox, out btnBox))
      {
        clipped = true;

        AdjustBillboard(btn.Background, ref prunik, ref borderBox, ref btnBox, ref aspectRatio, ref screenPx);

        if (btn.Icon != null)
        {
          if (btn.Icon.IntersectsBillboard(Border, ref aspectRatio, true, out prunik, out borderBox, out btnBox))
          {
            AdjustBillboard(btn.Icon, ref prunik, ref borderBox, ref btnBox, ref aspectRatio, ref screenPx);
          }
          else if (borderBox.Contains(btnBox) != ContainmentType.Contains)
          {
            btn.Icon.Visible = false;
          }
        }

        if (btn.Text != null)
        {
          if (btn.Text.IntersectsBillboard(Border, ref aspectRatio, false, out prunik, out borderBox, out btnBox)
            || borderBox.Contains(btnBox.Center) != ContainmentType.Contains)
          {
            btn.Text.Visible = false;
          }
        }
      }
      else if (!position.IsWithinBounds(Border, aspectRatio))
      {
        btn.SetVisibility(false);
        return false;
      }

      return true;
    }

    void ResetButtonTextures(Button btn, ButtonInfo info)
    {
      btn.Background.uvOffset = Vector2.Zero;
      btn.Background.uvSize = Vector2.One;
      btn.Background.TextureSize = 1;

      btn.Background.Origin = info.Origin;
      btn.Background.Offset = info.Offset;
      btn.Background.Width = info.Size.X;
      btn.Background.Height = info.Size.Y;

      if (btn.Icon != null)
      {
        var iconSize = _size.Y * 0.81f;
        btn.Icon.uvOffset = Vector2.Zero;
        btn.Icon.uvSize = Vector2.One;
        btn.Icon.TextureSize = 1;

        btn.Icon.Origin = info.Origin;
        btn.Icon.Offset = info.Offset;
        btn.Icon.Width = iconSize;
        btn.Icon.Height = iconSize;
      }
    }

    void AdjustBillboard(HudAPIv2.BillBoardHUDMessage bb, ref BoundingBox2D prunik, ref BoundingBox2D borderBox, ref BoundingBox2D btnBox, ref double aspectRatio, ref Vector2D screenPx)
    {
      var offset = bb.Offset;
      var size = new Vector2(bb.Width, bb.Height);

      var pSize = new Vector2D(prunik.Width, prunik.Height);
      var sizeDiff = new Vector2D(Math.Abs(aspectRatio * size.X - pSize.X), Math.Abs(size.Y - pSize.Y));
      var pixels = new Vector2((float)(pSize.X / screenPx.X), (float)(pSize.Y / screenPx.Y));
      var textureSize = (float)(size.Y / screenPx.Y);
      var ratioVec = pixels / textureSize;
      var uvOffX = Math.Max(0, borderBox.Min.X - btnBox.Min.X) / screenPx.X;
      var uvOffY = Math.Max(0, btnBox.Max.Y - borderBox.Max.Y) / screenPx.Y;

      double offAddX, offAddY;
      if (prunik.Width == borderBox.Width)
        offAddX = 0;
      else
        offAddX = (offset.X < 0 ? sizeDiff.X : -sizeDiff.X) * 0.5 + offset.X;

      if (prunik.Height == borderBox.Height)
        offAddY = 0;
      else
        offAddY = (offset.Y < 0 ? sizeDiff.Y : -sizeDiff.Y) * 0.5 + offset.Y;

      bb.uvEnabled = true;
      bb.TextureSize = textureSize;
      bb.uvSize = pixels;
      bb.uvOffset = new Vector2((float)uvOffX, (float)uvOffY);
      size *= ratioVec;
      offset = new Vector2D(offAddX, offAddY);

      bb.Width = size.X;
      bb.Height = size.Y;
      bb.Offset = offset;
    }

    internal struct ButtonInfo
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
