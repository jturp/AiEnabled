using System.Linq;
using System.Collections.Generic;
using System.Text;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using Sandbox.Game;
using VRage;
using VRage.Input;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Definitions.GUI;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.World;
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Collections.Concurrent;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using System;
using VRage.Game.ObjectBuilders.AI.Bot;
using Sandbox.Game.Weapons;
using AiEnabled.Graphics.Support;
using AiEnabled.API;
using VRage.Voxels;
using AiEnabled.Bots;
using Sandbox.Game.Entities.Blocks;

namespace AiEnabled.Utilities
{
  public static class Extensions
  {
    public static Color? ToColor(this string colorString)
    {
      try
      {
        if (!string.IsNullOrEmpty(colorString))
        {
          colorString = colorString.ToUpperInvariant();

          Color clr;
          if (AiSession.Instance.ColorDictionary.TryGetValue(colorString, out clr))
            return clr;

          var split = colorString.Split(',');
          if (split.Length == 3)
          {
            int r, g, b;
            if (int.TryParse(split[0].Trim(), out r) && int.TryParse(split[1].Trim(), out g) && int.TryParse(split[2].Trim(), out b))
              return new Color(r, g, b);
          }

          if (!colorString.StartsWith("#"))
            colorString = "#" + colorString;

          var html = ColorExtensions.FromHtml(colorString);
          if (html.HasValue)
            return html.Value;
        }
      }
      catch
      {
        AiSession.Instance.Logger.Log($"{colorString} is not valid for a color", MessageType.WARNING);
      }

      return null;
    }

    public static bool ContainsItem(this Base6Directions.Direction[] array, Base6Directions.Direction item)
    {
      for (int i = 0; i < array.Length; i++)
      {
        if (array[i] == item)
          return true;
      }

      return false;
    }

    public static bool ContainsItem(this MyDefinitionId[] array, MyDefinitionId item)
    {
      var comparer = MyDefinitionId.Comparer;
      for (int i = 0; i < array.Length; i++)
      {
        if (comparer.Equals(array[i], item))
          return true;
      }

      return false;
    }

    public static bool ContainsItem(this MyObjectBuilderType[] array, MyObjectBuilderType item)
    {
      var comparer = MyObjectBuilderType.Comparer;
      for (int i = 0; i < array.Length; i++)
      {
        if (comparer.Equals(array[i], item))
          return true;
      }

      return false;
    }

    public static bool IsValidPlayer(this IMyPlayer player)
    {
      return player != null && !player.IsBot && !string.IsNullOrWhiteSpace(player.DisplayName) && MyAPIGateway.Players.TryGetSteamId(player.IdentityId) != 0;
    }

    public static float GetBlockHealth(this IMySlimBlock slim, List<MyEntity> entList = null)
    {
      if (slim == null || slim.MaxIntegrity == 0 || slim.IsDestroyed)
        return -1f;

      var cube = slim.FatBlock as MyCubeBlock;
      if (cube?.IsPreview == true)
        return 0;

      var realGrid = slim.CubeGrid as MyCubeGrid;
      if (realGrid?.Projector != null)
        return 0;

      float maxIntegrity = slim.MaxIntegrity;
      float buildIntegrity = slim.BuildIntegrity;
      float currentDamage = slim.CurrentDamage;
      float health = (buildIntegrity - currentDamage) / maxIntegrity;

      if (AiSession.Instance.ModSaveData.ObeyProjectionIntegrityForRepairs && entList != null && realGrid?.Projector == null)
      {
        entList.Clear();
        var worldPosition = slim.CubeGrid.GridIntegerToWorld(slim.Position);
        var sphere = new BoundingSphereD(worldPosition, slim.CubeGrid.GridSize * 0.5);
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, entList);

        for (int i = 0; i < entList.Count; i++)
        {
          var projGrid = entList[i] as MyCubeGrid;
          if (projGrid?.Projector != null)
          {
            var projectedPosition = projGrid.WorldToGridInteger(worldPosition);
            var projectedBlock = projGrid.GetCubeBlock(projectedPosition) as IMySlimBlock;

            if (projectedBlock?.BlockDefinition.Id == slim.BlockDefinition.Id && slim.BuildIntegrity >= projectedBlock.BuildIntegrity)
            {
              health = 1;
              break;
            }
          }
        }
      }

      return health;
    }

    public static bool IntersectsBillboard(this HudAPIv2.BillBoardHUDMessage bb, HudAPIv2.BillBoardHUDMessage other, ref double aspectRatio, bool getIntersection, out BoundingBox2D prunik, out BoundingBox2D otherBox, out BoundingBox2D bbBox)
    {
      prunik = BoundingBox2D.CreateInvalid();

      var bbCenter = bb.Origin + bb.Offset;
      var halfSize = new Vector2D(bb.Width * aspectRatio, bb.Height) * 0.5;
      var bbMin = bbCenter - halfSize;
      var bbMax = bbCenter + halfSize;

      var otherCenter = other.Origin + other.Offset;
      var halfSizeOther = new Vector2D(other.Width * aspectRatio, other.Height - 0.02) * 0.5;
      var otherMin = otherCenter - halfSizeOther;
      var otherMax = otherCenter + halfSizeOther;

      bbBox = new BoundingBox2D(bbMin, bbMax);
      otherBox = new BoundingBox2D(otherMin, otherMax);

      if (otherBox.Contains(bbBox) != ContainmentType.Intersects)
        return false;

      if (getIntersection)
        prunik = bbBox.Intersect(otherBox);

      return true;
    }

    public static bool IntersectsBillboard(this HudAPIv2.HUDMessage msg, HudAPIv2.BillBoardHUDMessage other, ref double aspectRatio, bool getIntersection, out BoundingBox2D prunik, out BoundingBox2D otherBox, out BoundingBox2D bbBox)
    {
      prunik = BoundingBox2D.CreateInvalid();

      var length = msg.GetTextLength();
      var bbCenter = msg.Origin + msg.Offset + length * 0.5;
      var halfSize = new Vector2D(length.X * aspectRatio, -length.Y) * 0.5;
      var bbMin = bbCenter - halfSize;
      var bbMax = bbCenter + halfSize;

      var otherCenter = other.Origin + other.Offset;
      var halfSizeOther = new Vector2D(other.Width * aspectRatio, other.Height - 0.02) * 0.5;
      var otherMin = otherCenter - halfSizeOther;
      var otherMax = otherCenter + halfSizeOther;

      bbBox = new BoundingBox2D(bbMin, bbMax);
      otherBox = new BoundingBox2D(otherMin, otherMax);

      if (otherBox.Contains(bbBox) != ContainmentType.Intersects)
        return false;

      if (getIntersection)
        prunik = bbBox.Intersect(otherBox);

      return true;
    }

    public static bool IsWithinButton(this HudAPIv2.BillBoardHUDMessage cursor, Button button, double aspectRatio)
    {
      var buttonPos = button.Background.Origin + button.Background.Offset;
      var halfSize = new Vector2D(button.Background.Width * aspectRatio, button.Background.Height) * 0.5;
      var min = buttonPos - halfSize;
      var max = buttonPos + halfSize;

      var cursorPos = cursor.Origin + cursor.Offset;
      return cursorPos.X > min.X && cursorPos.Y > min.Y && cursorPos.X < max.X && cursorPos.Y < max.Y;
    }

    public static bool IsWithinBounds(this Vector2D vector, HudAPIv2.BillBoardHUDMessage bb, double aspectRatio, float sizeModifierX = 1, float sizeModifierY = 1)
    {
      var position = bb.Origin + bb.Offset;
      var halfSize = new Vector2D(bb.Width * aspectRatio, bb.Height) * new Vector2D(sizeModifierX, sizeModifierY) * 0.5;
      var min = position - halfSize;
      var max = position + halfSize;

      return vector.X > min.X && vector.Y > min.Y && vector.X < max.X && vector.Y < max.Y;
    }

    public static string ToString(this Vector2D vector, int decimals)
    {
      var x = Math.Round(vector.X, decimals);
      var y = Math.Round(vector.Y, decimals);
      return $"{{X:{y.ToString()} Y:{x.ToString()}}}";
    }

    public static string ToString(this Vector3D vector, int decimals)
    {
      var v = Vector3D.Round(vector, decimals);
      return $" X: {v.X.ToString()}\n Y: {v.Y.ToString()}\n Z: {v.Z.ToString()}";
    }

    public static float Volume(this BoundingSphere sphere)
    {
      var r = sphere.Radius;
      return 4f / 3f * MathHelper.Pi * r * r * r;
    }

    public static void ShellSort(this List<MyEntity> list, Vector3D checkPosition, bool reverse = false)
    {
      int length = list.Count;
      var half = length / 2;

      for (int h = half; h > 0; h /= 2)
      {
        for (int i = h; i < length; i += 1)
        {
          var tempValue = list[i];
          double temp;
          var pos = tempValue.PositionComp.WorldAABB.Center;
          Vector3D.DistanceSquared(ref pos, ref checkPosition, out temp);

          int j;
          for (j = i; j >= h && Vector3D.DistanceSquared(list[j - h].PositionComp.WorldAABB.Center, checkPosition) > temp; j -= h)
          {
            list[j] = list[j - h];
          }

          list[j] = tempValue;
        }
      }

      if (reverse)
      {
        for (int i = 0; i < half; i++)
        {
          var tmp = list[i];
          list[i] = list[length - i - 1];
          list[length - i - 1] = tmp;
        }
      }
    }

    public static void ShellSort(this List<IMySlimBlock> list, Vector3D checkPosition, bool reverse = false)
    {
      int length = list.Count;
      var half = length / 2;

      for (int h = half; h > 0; h /= 2)
      {
        for (int i = h; i < length; i += 1)
        {
          var tempValue = list[i];
          double temp;
          var pos = tempValue.CubeGrid.GridIntegerToWorld(tempValue.Position);
          Vector3D.DistanceSquared(ref pos, ref checkPosition, out temp);

          int j;
          for (j = i; j >= h && Vector3D.DistanceSquared(list[j - h].CubeGrid.GridIntegerToWorld(list[j - h].Position), checkPosition) > temp; j -= h)
          {
            list[j] = list[j - h];
          }

          list[j] = tempValue;
        }
      }

      if (reverse)
      {
        for (int i = 0; i < half; i++)
        {
          var tmp = list[i];
          list[i] = list[length - i - 1];
          list[length - i - 1] = tmp;
        }
      }
    }

    public static void ShellSort(this List<object> list, Vector3D checkPosition, bool reverse = false)
    {
      int length = list.Count;
      var half = length / 2;

      for (int h = half; h > 0; h /= 2)
      {
        for (int i = h; i < length; i += 1)
        {
          var tempValue = list[i];
          double temp;
          var ent = tempValue as IMyEntity;
          var slim = tempValue as IMySlimBlock;
          Vector3D pos = (ent != null) ? ent.GetPosition() : slim.CubeGrid.GridIntegerToWorld(slim.Position);
          Vector3D.DistanceSquared(ref pos, ref checkPosition, out temp);

          int j;
          for (j = i; j >= h && Vector3D.DistanceSquared(GetObjectPosition(list[j - h]), checkPosition) > temp; j -= h)
          {
            list[j] = list[j - h];
          }

          list[j] = tempValue;
        }
      }

      if (reverse)
      {
        for (int i = 0; i < half; i++)
        {
          var tmp = list[i];
          list[i] = list[length - i - 1];
          list[length - i - 1] = tmp;
        }
      }
    }

    static Vector3D GetObjectPosition(object a)
    {
      var ent = a as IMyEntity;
      if (ent != null)
        return ent.GetPosition();

      var slim = a as IMySlimBlock;
      return slim.CubeGrid.GridIntegerToWorld(slim.Position);
    }

    public static void PrioritySort(this List<object> list, SortedDictionary<int, List<object>> taskPriorities, RemoteBotAPI.Priorities priorities, Vector3D botPosition)
    {
      foreach (var kvp in taskPriorities)
        kvp.Value.Clear();

      if (priorities != null)
      {
        for (int i = 0; i < list.Count; i++)
        {
          var obj = list[i];
          if (obj == null)
            continue;

          var cube = obj as IMyCubeBlock;
          var block = cube?.SlimBlock;
          if (block == null)
            block = obj as IMySlimBlock;

          var character = obj as IMyCharacter;
          if (character == null)
          {
            if (block?.CubeGrid == null || (block.IsDestroyed && block.StockpileEmpty))
              continue;

            if (((MyCubeGrid)block.CubeGrid).BlocksCount == 1)
            {
              var wheel = block.FatBlock as IMyWheel;
              if (wheel == null || !wheel.IsAttached)
                continue;
            }

            var tgtPriorities = priorities as RemoteBotAPI.TargetPriorities;
            if (tgtPriorities?.DamageToDisable == true && block.FatBlock != null)
            {
              var funcBlock = block.FatBlock as IMyFunctionalBlock;
              if (funcBlock != null && !funcBlock.IsFunctional)
                continue;
              else if (block.FatBlock is IMyDoor && block.IsBlockUnbuilt())
                continue;
            }
          }

          var priNum = priorities.GetBlockPriority(obj);
          if (priNum < 0)
            continue;

          List<object> taskList;
          if (!taskPriorities.TryGetValue(priNum, out taskList))
          {
            taskList = new List<object>();
            taskPriorities[priNum] = taskList;
          }

          taskList.Add(obj);
        }
      }

      foreach (var kvp in taskPriorities)
        kvp.Value.ShellSort(botPosition);
    }

    public static bool GetMissingComponentsProjected(this IMySlimBlock slim, Dictionary<string, int> comps, IMyInventory botInventory)
    {
      if (slim?.BlockDefinition == null || comps == null)
        return false;

      var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;
      if (cubeDef == null)
        return false;

      bool botHasFirstItem = false;
      comps.Clear();
      for (int i = 0; i < cubeDef.Components.Length; i++)
      {
        var component = cubeDef.Components[i];

        if (i == 0)
        {
          botHasFirstItem = botInventory?.GetItemAmount(component.Definition.Id) > 0;
        }

        int amount;
        var componentId = component.Definition.Id.SubtypeName;
        comps.TryGetValue(componentId, out amount);
        amount += component.Count;
        comps[componentId] = amount;
      }

      return botHasFirstItem;
    }

    public static bool GetDistanceToEdgeInDirection(this MyOrientedBoundingBoxD obb, RayD ray, Vector3D[] array, out double distance)
    {
      if (array == null || array.Length != 8)
        array = new Vector3D[8];

      var position = ray.Position;
      var direction = ray.Direction;

      obb.GetCorners(array, 0);
      distance = double.MaxValue;

      for (int i = 0; i < array.Length; i++)
      {
        Vector3D vector = array[i] - position;
        var projection = AiUtils.Project(vector, direction);
        if (projection.Dot(direction) > 0)
        {
          var lengthSqd = projection.LengthSquared();
          if (lengthSqd < distance)
          {
            distance = lengthSqd;
          }
        }
      }

      if (distance == double.MaxValue)
        return false;

      distance = Math.Floor(Math.Sqrt(distance));
      return true;
    }

    public static bool IsBlockUnbuilt(this IMySlimBlock block)
    {
      if (block == null)
        return false;

      var blockDef = (MyCubeBlockDefinition)block.BlockDefinition;
      return block.BuildLevelRatio < blockDef.CriticalIntegrityRatio;
    }
  }
}