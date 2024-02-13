using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AiEnabled.Ai.Support;
using AiEnabled.API;
using AiEnabled.Bots.Roles;
using AiEnabled.Bots.Roles.Helpers;
using AiEnabled.Networking;
using AiEnabled.Networking.Packets;
using AiEnabled.Parallel;
using AiEnabled.Support;
using AiEnabled.Utilities;

using ParallelTasks;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using SpaceEngineers.Game.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots
{
  public static class BotFactory
  {
    public enum BotRoleNeutral { NOMAD, ENFORCER, PATRON };
    public enum BotRoleEnemy { ZOMBIE, SOLDIER, BRUISER, GRINDER, GHOST, CREATURE };
    public enum BotRoleFriendly { REPAIR, SCAVENGER, COMBAT, CREW };

    public static void ResetBotTargeting(BotBase bot)
    {
      if (bot != null)
      {
        bot.FollowMode = false;
        bot.PatrolMode = false;
        bot.UseAPITargets = false;
        bot.Target.RemoveTarget();
        bot.Target.RemoveOverride(false);
        bot.CleanPath();
      }
    }

    public static bool ThrowGrenade(BotBase bot)
    {
      var entity = bot.Target.Entity as IMyEntity;
      if (bot?.ToolDefinition != null && bot.ToolDefinition.WeaponType != MyItemWeaponType.None && entity != null && !bot.Target.IsFriendly())
      {
        var distanceSqd = Vector3D.DistanceSquared(bot.Target.CurrentActualPosition, bot.BotInfo.CurrentBotPositionActual);
        if (distanceSqd > 75 * 75 || distanceSqd < 100)
          return false;

        bool hasOwner = bot.Owner != null;
        if (!hasOwner)
        {
          var rand = MyUtils.GetRandomInt(1, 101);
          if (rand < 70 - bot._grenadeChanceOffset)
          {
            bot._grenadeChanceOffset += 5;
            return false;
          }

          bot._grenadeChanceOffset = 0;
        }
        else
        {
          if (bot._currentGraph.IsGridGraph)
          {
            var gridGraph = bot._currentGraph as CubeGridMap;
            var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(bot.Owner.IdentityId);
            var gridOwner = gridGraph.MainGrid?.BigOwners?.Count > 0 ? gridGraph.MainGrid.BigOwners[0] : 0L;
            var gridFaction = gridOwner > 0 ? MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner) : null;

            if (gridFaction != null && (int)MyAPIGateway.Session.Factions.GetRelationBetweenFactions(gridFaction.FactionId, ownerFaction.FactionId) >= 2)
              return false;
          }

          var inv = bot.Character.GetInventory() as MyInventory;
          if (inv == null || inv.RemoveItemsOfType(1, AiSession.Instance.HandGrenade_ConsumableId) <= 0)
            return false;
        }

        MyAPIGateway.Utilities.SendModMessage(2861675418, MyTuple.Create(bot.Character, entity));
        return true;
      }

      return false;
    }

    public static void GetInteriorNodes(WorkData workData)
    {
      try
      {
        var apiData = workData as ApiWorkData;
        if (AiSession.Instance == null || !AiSession.Instance.Registered || apiData == null)
          return;

        var nodeList = apiData.NodeList;
        if (nodeList == null)
          nodeList = new List<Vector3I>();
        else
          nodeList.Clear();

        var grid = apiData.Grid;
        if (grid == null || grid.IsPreview || grid.MarkedForClose || grid.Closed)
          return;

        MyCubeGrid biggestGrid = GridBase.GetLargestGridForMap(grid) as MyCubeGrid;

        if (biggestGrid == null || AiSession.Instance == null || !AiSession.Instance.Registered)
          return;

        var enclosureRating = Math.Max(1, Math.Min(apiData.EnclosureRating, 6));
        var airtightOnly = apiData.AirtightNodesOnly;

        List<IMyCubeGrid> gridList = AiSession.Instance.GridGroupListPool.Get();

        var group = grid.GetGridGroup(GridLinkTypeEnum.Mechanical);
        if (group == null)
          gridList.Add(grid);
        else
          group.GetGrids(gridList);

        var box = new BoundingBoxI(biggestGrid.Min, biggestGrid.Max);

        foreach (var g in gridList)
        {
          var cubeGrid = g as MyCubeGrid;
          if (cubeGrid?.Physics == null || cubeGrid.IsPreview || cubeGrid.MarkedForClose || cubeGrid.Closed || cubeGrid.GridSizeEnum == MyCubeSize.Small)
            continue;

          var worldMin = g.GridIntegerToWorld(g.Min);
          var worldMax = g.GridIntegerToWorld(g.Max);
          var localMin = biggestGrid.WorldToGridInteger(worldMin);
          var localMax = biggestGrid.WorldToGridInteger(worldMax);

          box = box.Include(localMin);
          box = box.Include(localMax);
        }

        Vector3D upVector = Vector3D.Zero;

        int numSeats;
        if (biggestGrid.HasMainCockpit())
        {
          upVector = biggestGrid.MainCockpit.WorldMatrix.Up;
        }
        else if (biggestGrid.HasMainRemoteControl())
        {
          upVector = biggestGrid.MainRemoteControl.WorldMatrix.Up;
        }
        else if (biggestGrid.BlocksCounters.TryGetValue(typeof(MyObjectBuilder_Cockpit), out numSeats) && numSeats > 0)
        {
          foreach (var b in biggestGrid.GetFatBlocks())
          {
            if (b is IMyShipController || b is IMyCockpit)
            {
              upVector = b.WorldMatrix.Up;
              break;
            }
          }
        }
        else
        {
          float _;
          var gridPos = biggestGrid.PositionComp.WorldAABB.Center;
          var nGrav = MyAPIGateway.Physics.CalculateNaturalGravityAt(gridPos, out _);
          var aGrav = MyAPIGateway.Physics.CalculateArtificialGravityAt(gridPos, 0);

          if (aGrav.LengthSquared() > 0)
          {
            upVector = Vector3D.Normalize(-aGrav);
          }
          else if (nGrav.LengthSquared() > 0)
          {
            upVector = Vector3D.Normalize(-nGrav);
          }
          else
          {
            upVector = biggestGrid.WorldMatrix.Up;
          }
        }

        var normalDir = biggestGrid.WorldMatrix.GetClosestDirection(upVector);
        var normal = Base6Directions.GetIntVector(normalDir);

        var maxDistance = box.HalfExtents.AbsMax() * 2 + 2;
        var iter = new Vector3I_RangeIterator(ref box.Min, ref box.Max);

        while (iter.IsValid())
        {
          if (AiSession.Instance == null || !AiSession.Instance.Registered)
            return;

          var current = iter.Current;
          iter.MoveNext();

          bool valid = true;
          var worldCurrent = grid.GridIntegerToWorld(current);

          foreach (var g in gridList)
          {
            if (g == null || g.MarkedForClose || g.Closed)
              continue;

            var localCurrent = g.WorldToGridInteger(worldCurrent);
            var localCube = g.GetCubeBlock(localCurrent);

            if (localCube != null && !localCube.IsDestroyed)
            {
              var cubeDef = localCube.BlockDefinition as MyCubeBlockDefinition;
              if (cubeDef?.IsAirTight == true || localCube.FatBlock == null || localCube.FatBlock is IMyDoor || localCube.FatBlock is IMyLargeTurretBase)
              {
                valid = false;
                break;
              }

              var id = localCube.BlockDefinition.Id;
              if (id.SubtypeName.IndexOf("locker", StringComparison.OrdinalIgnoreCase) >= 0
                || id.SubtypeName.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
                || id.SubtypeName.IndexOf("shower", StringComparison.OrdinalIgnoreCase) >= 0
                || id.SubtypeName.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0
                || (localCube.FatBlock is IMyCockpit
                && id.SubtypeName.IndexOf("passenger", StringComparison.OrdinalIgnoreCase) < 0
                && id.SubtypeName.IndexOf("couch", StringComparison.OrdinalIgnoreCase) < 0))
              {
                valid = false;
                break;
              }

              if (id.TypeId == typeof(MyObjectBuilder_Passage))
              {
                if (localCube.Orientation.Forward != Base6Directions.GetOppositeDirection(normalDir))
                {
                  valid = false;
                  break;
                }
              }
              else if (id.SubtypeName.IndexOf("passage", StringComparison.OrdinalIgnoreCase) >= 0)
              {
                if (localCube.Orientation.Up != normalDir)
                {
                  valid = false;
                  break;
                }
              }
            }
          }

          if (valid && IsInside(current, ref box, ref maxDistance, ref enclosureRating, ref airtightOnly, gridList, grid))
          {
            nodeList.Add(current);
          }
        }

        var hash = AiSession.Instance.LocalVectorHashPool.Get();
        hash.UnionWith(nodeList);

        for (int i = nodeList.Count - 1; i >= 0; i--)
        {
          var point = nodeList[i];
          bool neighborFound = false;

          foreach (var dir in AiSession.DirArray)
          {
            var checkPoint = point + dir;
            if (hash.Contains(checkPoint))
            {
              neighborFound = true;
              break;
            }
          }

          if (!neighborFound)
          {
            nodeList.RemoveAtFast(i);
          }
        }

        AiSession.Instance.LocalVectorHashPool?.Return(ref hash);

        if (!apiData.AllowAirNodes && nodeList.Count > 0)
        {
          var positionList = AiSession.Instance.LineListPool.Get();

          for (int i = nodeList.Count - 1; i >= 0; i--)
          {
            var nodePosition = nodeList[i];
            var positionBelow = nodePosition - normal;
            bool valid = false;

            foreach (var g in gridList)
            {
              var block = g.GetCubeBlock(positionBelow);
              if (block?.BlockDefinition != null)
              {
                var cubeDef = block.BlockDefinition as MyCubeBlockDefinition;

                if (cubeDef?.IsAirTight == true)
                {
                  valid = true;
                }
                else if (block.BlockDefinition.Id.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0 && block.Orientation.Up == Base6Directions.GetOppositeDirection(normalDir))
                {
                  valid = true;
                }
                else
                {
                  bool allowSolar = false;
                  if (block.FatBlock is IMySolarPanel)
                  {
                    if (block.BlockDefinition.Id.SubtypeName.IndexOf("colorable", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                      allowSolar = Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) < 0;
                    }
                    else
                    {
                      allowSolar = Base6Directions.GetIntVector(block.Orientation.Forward).Dot(ref normal) > 0;
                    }
                  }
                  bool allowConn = !allowSolar && block.FatBlock is IMyShipConnector && cubeDef.Id.SubtypeName == "Connector";
                  bool isCylinder = !allowConn && AiSession.Instance.PipeBlockDefinitions.ContainsItem(cubeDef.Id);

                  positionList.Clear();
                  AiUtils.FindAllPositionsForBlock(block, positionList);

                  foreach (var cell in positionList)
                  {
                    var positionAbove = cell + normal;

                    if (positionAbove == nodePosition
                      && (allowConn || allowSolar || isCylinder || AiUtils.IsSidePressurizedForBlock(block, cell, normal)))
                    {
                      valid = true;
                      break;
                    }
                  }
                }

                if (valid)
                  break;
              }

              block = g.GetCubeBlock(nodePosition);
              if (block?.BlockDefinition?.Id.SubtypeName.IndexOf("catwalk", StringComparison.OrdinalIgnoreCase) >= 0 && block.Orientation.Up == normalDir)
              {
                valid = true;
                break;
              }
            }

            if (!valid)
              nodeList.RemoveAtFast(i);
          }
        }

        AiSession.Instance.GridGroupListPool?.Return(ref gridList);
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.GetInterioNodes: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public static bool IsInside(Vector3I point, ref BoundingBoxI box, ref int maxDistance, ref int minEnclosure, ref bool airtightOnly, List<IMyCubeGrid> gridList, MyCubeGrid mainGrid)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
        return false;

      int numBlocksFound = 0;
      var positionList = AiSession.Instance.LineListPool.Get();

      for (int i = 0; i < AiSession.DirArray.Length; i++)
      {
        var dir = AiSession.DirArray[i];

        for (int j = 1; j < maxDistance; j++)
        {
          var checkPoint = point + dir * j;

          if (box.Contains(checkPoint) != ContainmentType.Contains)
            break;

          var worldPoint = mainGrid.GridIntegerToWorld(checkPoint);
          bool found = false;

          foreach (var grid in gridList)
          {
            if (grid == null || grid.Closed || grid.MarkedForClose || grid.GridSizeEnum == MyCubeSize.Small)
              continue;

            var localPoint = grid.WorldToGridInteger(worldPoint);

            if (airtightOnly && !grid.IsRoomAtPositionAirtight(localPoint))
              continue;

            var cube = grid.GetCubeBlock(localPoint);
            if (cube?.BlockDefinition != null)
            {
              var isDoor = cube.FatBlock is IMyDoor;
              var isWindow = !isDoor && cube.BlockDefinition.Id.SubtypeName.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0;
              var def = cube.BlockDefinition as MyCubeBlockDefinition;

              if (def?.IsAirTight == true || isDoor || isWindow)
              {
                found = true;
                break;
              }
              else
              {
                var normal = -dir;
                bool allowSolar = cube.FatBlock is IMySolarPanel && Base6Directions.GetIntVector(cube.Orientation.Forward).Dot(ref normal) != 0;
                bool allowConn = !allowSolar && cube.FatBlock is IMyShipConnector && def.Id.SubtypeName == "Connector";
                bool isCylinder = !allowConn && AiSession.Instance.PipeBlockDefinitions.ContainsItem(def.Id);

                positionList.Clear();
                AiUtils.FindAllPositionsForBlock(cube, positionList);

                foreach (var checkPosition in positionList)
                {
                  if (checkPosition == checkPoint && (allowConn || allowSolar || isCylinder
                    || AiUtils.IsSidePressurizedForBlock(cube, checkPosition, normal)
                    || AiUtils.IsSidePressurizedForBlock(cube, checkPosition, -normal)))
                  {
                    found = true;
                    break;
                  }
                }

                if (found)
                  break;
              }
            }
          }

          if (found)
          {
            numBlocksFound++;
            break;
          }
        }

        if (numBlocksFound >= minEnclosure)
          break;
      }

      AiSession.Instance.LineListPool?.Return(ref positionList);
      return numBlocksFound >= minEnclosure;
    }

    public static void GetInteriorNodesCallback(WorkData workData)
    {
      var apiData = workData as ApiWorkData;
      if (apiData != null)
      {
        apiData.CallBack?.Invoke(apiData.Grid, apiData.NodeList);
        AiSession.Instance?.ApiWorkDataPool?.Return(ref apiData);
      }
    }

    public static IMyCharacter SwitchBotCharacter(BotBase bot)
    {
      var oldChar = bot?.Character;
      if (oldChar == null)
      {
        return null;
      }

      var seat = oldChar.Parent as MyCockpit;
      if (seat == null)
      {
        return null;
      }

      RemoveBotFromSeat(bot, false);

      var displayName = oldChar.Name;
      var subType = oldChar.Definition.Id.SubtypeName;
      var botPlayerId = oldChar.ControllerInfo.ControllingIdentityId;
      var hsvOffset = ((MyObjectBuilder_Character)oldChar.GetObjectBuilder()).ColorMaskHSV;

      var matrix = oldChar.WorldMatrix;
      var positionAndOrientation = new MyPositionAndOrientation(ref matrix);
      var info = bot.BotControlInfo;
      var ownerId = bot.Owner?.IdentityId;
      var oldBotEntId = oldChar.EntityId;

      var ob = new MyObjectBuilder_Character()
      {
        Name = displayName,
        DisplayName = null,
        SubtypeName = subType,
        CharacterModel = subType,
        EntityId = 0,
        AIMode = false,
        JetpackEnabled = subType == "Drone_Bot" || MyAPIGateway.Session.SessionSettings.EnableJetpack,
        EnableBroadcasting = false,
        NeedsOxygenFromSuit = false,
        OxygenLevel = 1,
        MovementState = MyCharacterMovementEnum.Standing,
        PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.Enabled,
        PositionAndOrientation = positionAndOrientation,
        Health = 1000,
        OwningPlayerIdentityId = botPlayerId,
        ColorMaskHSV = hsvOffset,
        RelativeDampeningEntity = seat.CubeGrid.EntityId,
      };

      var newChar = MyEntities.CreateFromObjectBuilder(ob, true) as IMyCharacter;
      bot.ChangeCharacter(newChar);

      if (newChar != null)
      {
        newChar.Save = false;
        newChar.Synchronized = true;
        newChar.Flags &= ~VRage.ModAPI.EntityFlags.NeedsUpdate100;

        var charDef = newChar.Definition as MyCharacterDefinition;
        string open, close;
        if (AiSession.Instance.ModSaveData.AllowHelmetVisorChanges
          && charDef.AnimationNameToSubtypeName.TryGetValue("HelmetOpen", out open)
          && charDef.AnimationNameToSubtypeName.TryGetValue("HelmetClose", out close))
        {
          var oxyComp = newChar.Components.Get<MyCharacterOxygenComponent>();
          if (oxyComp == null)
          {
            oxyComp = new MyCharacterOxygenComponent();
            oxyComp.Init(ob);
            newChar.Components.Add(oxyComp);
          }
        }

        if (newChar.PositionComp.GetPosition() == Vector3D.Zero)
          newChar.SetPosition(positionAndOrientation.Position);

        var offset = Vector3D.Rotate(newChar.GetPosition() - seat.PositionComp.WorldAABB.Center, MatrixD.Transpose(seat.WorldMatrix));
        AiSession.Instance.BotToSeatRelativePosition[newChar.EntityId] = offset;

        if (seat.CubeGrid?.Physics.LinearVelocity.LengthSquared() > 0)
        {
          bot.Character.Physics.SetSpeeds(seat.CubeGrid.Physics.LinearVelocity, seat.CubeGrid.Physics.AngularVelocity);
        }

        if (info != null)
        {
          info.EntityId = newChar.EntityId;
          //var ident = MyAPIGateway.Players.CreateNewIdentity("", addToNpcs: true);
          //var botPlayer = MyAPIGateway.Players.CreateNewPlayer(ident, "", false, false, true, true);
          //info.Controller = MyAPIGateway.Players.CreateNewEntityController(botPlayer); // testing
          info.Controller.TakeControl(newChar);

          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new CharacterSwapPacket(oldBotEntId, newChar.EntityId);
            AiSession.Instance.Network.RelayToClients(packet);
          }
        }

        MyEntities.Add((MyEntity)newChar, true);
        AiSession.Instance.Scheduler.Schedule(() => TrySeatBot(bot, seat));
      }

      return newChar;
    }

    public static bool TrySeatBotOnGrid(BotBase bot, IMyCubeGrid grid)
    {
      try
      {
        var seats = AiSession.Instance.GridSeatsAPI;
        seats.Clear();

        grid.GetBlocks(seats, b => b.FatBlock is IMyCockpit);
        for (int i = seats.Count - 1; i >= 0; i--)
        {
          var seat = seats[i]?.FatBlock as IMyCockpit;

          if (seat == null || seat.Pilot != null || !seat.IsFunctional
            || !seat.HasPlayerAccess(bot.BotIdentityId)
            || seat.BlockDefinition.SubtypeId.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
            || seat.BlockDefinition.SubtypeId.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0
            || seat.BlockDefinition.SubtypeId.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0)
            continue;

          if (bot.HasWeaponOrTool)
          {
            var gun = bot.Character.EquippedTool as IMyHandheldGunObject<MyGunBase>;
            gun?.OnControlReleased();

            var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
          }

          var myCpit = seat as MyCockpit;
          var seatCube = seat as MyCubeBlock;
          var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
          bool changeBack = false;
          bool ignoreShareMode = false;

          if (shareMode != MyOwnershipShareModeEnum.All)
          {
            if (seatCube.IDModule.GetUserRelationToOwner(bot.BotIdentityId) == MyRelationsBetweenPlayerAndBlock.FactionShare)
            {
              ignoreShareMode = true;

              if (seatCube.IDModule.ShareMode != MyOwnershipShareModeEnum.Faction)
              {
                seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.Faction;
                changeBack = true;
              }
            }
            else
            {
              var owner = bot.Owner?.IdentityId ?? bot.BotIdentityId;
              var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

              var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
              if (relation != MyRelationsBetweenPlayers.Enemies)
              {
                changeBack = true;
                seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
              }
            }
          }

          if (ignoreShareMode || seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
          {
            //var freeSpace = MyEntities.FindFreePlaceCustom(seat.GetPosition(), 5);
            //if (freeSpace.HasValue)
            //{
            //  var offset = Vector3D.Rotate(freeSpace.Value - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
            //  AiSession.Instance.BotToSeatRelativePosition[bot.Character.EntityId] = offset;
            //  bot.Character.SetPosition(freeSpace.Value);
            //}

            var mapGrid = GridBase.GetLargestGridForMap(seat.CubeGrid) as MyCubeGrid;
            bot._currentGraph = AiSession.Instance.GetNewGraph(mapGrid, bot.BotInfo.CurrentBotPositionActual, bot.WorldMatrix);
            seat.AttachPilot(bot.Character);
          }

          if (changeBack)
            seatCube.IDModule.ShareMode = shareMode;

          bot.CleanPath();
          bot.Target.RemoveTarget();
          seats.Clear();
          return true;
        }

        seats.Clear();
        return false;
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.TrySeatBotOnGrid: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return false;
      }
    }

    public static bool TrySeatBot(BotBase bot, IMyCockpit seat)
    {
      try
      {
        var myCpit = seat as MyCockpit;
        if (myCpit != null)
        {
          var currentSeat = bot.Character.Parent as IMyCockpit;
          if (currentSeat != null)
            currentSeat.RemovePilot();

          var seatCube = seat as MyCubeBlock;
          var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
          bool changeBack = false;
          bool ignoreShareMode = false;

          if (shareMode != MyOwnershipShareModeEnum.All)
          {
            if (seatCube.IDModule.GetUserRelationToOwner(bot.BotIdentityId) == MyRelationsBetweenPlayerAndBlock.FactionShare)
            {
              ignoreShareMode = true;

              if (seatCube.IDModule.ShareMode != MyOwnershipShareModeEnum.Faction)
              {
                seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.Faction;
                changeBack = true;
              }
            }
            else
            {
              var owner = bot.Owner?.IdentityId ?? bot.BotIdentityId;
              var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

              var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
              if (relation != MyRelationsBetweenPlayers.Enemies)
              {
                changeBack = true;
                seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
              }
            }
          }

          if (ignoreShareMode || seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
          {
            //var freeSpace = MyEntities.FindFreePlaceCustom(seat.GetPosition(), 5);
            //if (freeSpace.HasValue)
            //{
            //  var offset = Vector3D.Rotate(freeSpace.Value - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
            //  AiSession.Instance.BotToSeatRelativePosition[bot.Character.EntityId] = offset;
            //  bot.Character.SetPosition(freeSpace.Value);
            //}

            var grid = GridBase.GetLargestGridForMap(seat.CubeGrid) as MyCubeGrid;

            if (grid?.Physics.LinearVelocity.LengthSquared() > 0)
            {
              bot.Character.Physics.SetSpeeds(grid.Physics.LinearVelocity, grid.Physics.AngularVelocity);
            }

            bot._currentGraph = AiSession.Instance.GetNewGraph(grid, bot.BotInfo.CurrentBotPositionActual, bot.WorldMatrix);
            AiSession.Instance.Scheduler.Schedule(() => SeatBotDeferred(bot.Character, myCpit));
          }
          
          if (changeBack)
            AiSession.Instance.BotToSeatShareMode[bot.Character.EntityId] = shareMode;

          bot.CleanPath();
          bot.Target.RemoveTarget();
          return true;
        }

        return false;
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.TrySeatBot: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return false;
      }
    }

    static void SeatBotDeferred(IMyCharacter botChar, MyCockpit seat)
    {
      try
      {
        if (seat?.CubeGrid != null && !seat.CubeGrid.MarkedForClose && botChar != null && !botChar.MarkedForClose)
        {
          var cpit = seat as IMyCockpit;
          cpit.AttachPilot(botChar);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotFactory.SeatBotDeferred: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }

    public static bool RemoveBotFromSeat(BotBase bot, bool checkTarget = true)
    {
      try
      {
        var seat = bot.Character.Parent as IMyCockpit;
        if (seat != null)
        {
          seat.RemovePilot();

          var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
          if (jetpack != null && !jetpack.TurnedOn)
          {
            if (bot.RequiresJetpack)
            {
              var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
              MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
              jetpack.TurnOnJetpack(true);
              MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
            }
            else if (bot.CanUseAirNodes && MyAPIGateway.Session.SessionSettings.EnableJetpack)
            {
              jetpack.TurnOnJetpack(true);
            }
          }

          var seatGrid = seat.CubeGrid;
          var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
          if (controlEnt != null)
            controlEnt.RelativeDampeningEntity = (MyEntity)seatGrid;

          MyOwnershipShareModeEnum shareMode;
          if (AiSession.Instance?.BotToSeatShareMode != null && AiSession.Instance.BotToSeatShareMode.TryRemove(bot.Character.EntityId, out shareMode))
          {
            var seatCube = seat as MyCubeBlock;
            if (seatCube?.IDModule != null)
              seatCube.IDModule.ShareMode = shareMode;
          }

          Vector3D position = seat.WorldAABB.Center;

          var map = bot._currentGraph;
          if (map == null || !map.IsPositionValid(position))
          {
            map = AiSession.Instance.GetNewGraph((MyCubeGrid)seatGrid, position, seat.WorldMatrix);
            bot._previousGraph = null;
            bot._currentGraph = map;
            bot._nextGraph = null;
            bot.CleanPath();
          }

          if (checkTarget)
          {
            bot.Target.RemoveTarget();

            if (!bot.UseAPITargets)
            {
              bot.SetTarget();

              var ent = bot.Target.Entity as IMyEntity;
              if (ent != null)
              {
                Vector3D actualPosition = ent.WorldAABB.Center;
                bot.StartCheckGraph(ref actualPosition, true);
              }
            }
          }

          var gridMap = map as CubeGridMap;
          var voxelGraph = map as VoxelGridMap;
          var localPoint = map.WorldToLocal(position);
          Node node;
          Vector3D offset;

          if (map.GetClosestValidNode(bot, localPoint, out localPoint, seat.WorldMatrix.Up, true, true, false))
          {
            position = map.LocalToWorld(localPoint);
          }
          else if (AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out offset))
          {
            position += Vector3D.Rotate(offset, seat.WorldMatrix);
          }
          else if (gridMap?.InteriorNodesReady == true && gridMap.InteriorNodeList.Count > 0)
          {
            var rand = MyUtils.GetRandomInt(0, gridMap.InteriorNodeList.Count);
            localPoint = gridMap.InteriorNodeList[rand];
            position = map.LocalToWorld(localPoint);

            if (gridMap.TryGetNodeForPosition(localPoint, out node))
              position += node.Offset;
          }
          else if (voxelGraph != null)
          {
            var quat = Quaternion.CreateFromRotationMatrix(seatGrid.WorldMatrix);
            var obb = new MyOrientedBoundingBoxD(seatGrid.WorldAABB.Center, seatGrid.LocalAABB.HalfExtents, quat);

            double distance;
            if (!map.GetEdgeDistanceInDirection(seat.WorldMatrix.Forward, obb, out distance))
              distance = seat.CubeGrid.LocalAABB.HalfExtents.AbsMax();

            position = seatGrid.WorldAABB.Center + seat.WorldMatrix.Forward * distance;

            if (!bot.CanUseAirNodes && voxelGraph.RootVoxel != null)
            {
              var voxelPosition = voxelGraph.GetClosestSurfacePointFast(bot, position, bot.WorldMatrix.Up);
              if (voxelPosition.HasValue)
                position = voxelPosition.Value;
            }
          }
          else
          {
            var freePlace = MyEntities.FindFreePlaceCustom(position, 5);
            if (freePlace.HasValue)
              position = freePlace.Value;
            else
              position += seat.WorldMatrix.Forward * 2;
          }

          if (gridMap != null)
          {
            localPoint = map.WorldToLocal(position);
            if (map.TryGetNodeForPosition(localPoint, out node) && node != null && node.IsAirNode)
            {
              if (gridMap.InteriorNodesReady == true && gridMap.InteriorNodeList.Count > 0)
              {
                var rand = MyUtils.GetRandomInt(0, gridMap.InteriorNodeList.Count);
                localPoint = gridMap.InteriorNodeList[rand];
                position = map.LocalToWorld(localPoint);

                if (gridMap.TryGetNodeForPosition(localPoint, out node))
                  position += node.Offset;
              }
              else if (gridMap.GetRandomOpenTile(bot, out node, false, false))
              {
                position = map.LocalToWorld(node.Position) + node.Offset;
              }
            }
          }

          if (!map.IsPositionValid(position))
          {
            var direction = Vector3D.Normalize(position - map.OBB.Center);
            double distance;
            if (!map.GetEdgeDistanceInDirection(direction, out distance))
              distance = (map.OBB.HalfExtent.AbsMax() + map.OBB.HalfExtent.AbsMin()) * 0.5;

            position = map.OBB.Center + seat.WorldMatrix.Forward * distance * 0.8;

            var freePlace = MyEntities.FindFreePlaceCustom(position, 5);
            if (freePlace.HasValue)
              position = freePlace.Value;
          }

          MyVoxelBase _;
          var up = bot.WorldMatrix.Up;
          GridBase.GetClosestPointAboveGround(ref position, ref up, out _, 100);

          var matrix = seat.WorldMatrix;
          matrix.Translation = position + matrix.Down;
          bot.Character.SetWorldMatrix(matrix);

          if (controlEnt != null && gridMap != null && controlEnt.RelativeDampeningEntity != gridMap.MainGrid)
          {
            var relativeGrid = gridMap.MainGrid;
            if (relativeGrid == null)
              relativeGrid = (GridBase.GetLargestGridForMap(seat.CubeGrid) ?? seat.CubeGrid) as MyCubeGrid;

            controlEnt.RelativeDampeningEntity = relativeGrid;
          }

          return true;
        }

        var gridGraph = bot._currentGraph as CubeGridMap;
        if (gridGraph != null)
        {
          var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
          var grid = gridGraph.MainGrid;

          if (controlEnt.RelativeDampeningEntity != grid)
            controlEnt.RelativeDampeningEntity = grid;
        }

        return false;
      }
      catch(Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.RemoveBotFromSeat: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return false;
      }
    }

    public static IMyCharacter SpawnBotFromAPI(MyPositionAndOrientation positionAndOrientation, RemoteBotAPI.SpawnData spawnData, MyCubeGrid grid = null, long? owner = null)
    {
      try
      {
        string toolType = null;
        if (spawnData.ToolSubtypeIdList?.Count > 0)
        {
          for (int i = spawnData.ToolSubtypeIdList.Count - 1; i >= 0; i--)
          {
            var subtype = spawnData.ToolSubtypeIdList[i];
            if (!AiSession.Instance.IsBotAllowedToUse(spawnData.BotRole, subtype))
              spawnData.ToolSubtypeIdList.RemoveAtFast(i);
          }

          if (spawnData.ToolSubtypeIdList.Count > 0)
          {
            var num = MyUtils.GetRandomInt(0, spawnData.ToolSubtypeIdList.Count);
            toolType = spawnData.ToolSubtypeIdList[num];
          }
        }
        else if (!string.IsNullOrWhiteSpace(spawnData.ToolSubtypeId) && AiSession.Instance.IsBotAllowedToUse(spawnData.BotRole, spawnData.ToolSubtypeId))
        {
          toolType = spawnData.ToolSubtypeId;
        }

        var syncRange = AiSession.Instance.SyncRange * 0.75;
        if (MyAPIGateway.Multiplayer.MultiplayerActive && grid?.PositionComp != null && Vector3D.DistanceSquared(grid.PositionComp.WorldAABB.Center, positionAndOrientation.Position) > syncRange * syncRange)
        {
          var gridAABB = grid.PositionComp.WorldAABB;
          var dirVector = grid.Physics.LinearVelocity.LengthSquared() > 0 ? Vector3D.Normalize(-grid.Physics.LinearVelocity) : grid.WorldMatrix.Backward;
          var closePosition = gridAABB.Center + dirVector * (gridAABB.HalfExtents.AbsMax() + 50);
          positionAndOrientation.Position = closePosition;
        }

        IMyCharacter botChar;
        if (owner > 0 && AiSession.Instance.Players.ContainsKey(owner.Value))
          botChar = SpawnHelper(spawnData.BotSubtype, spawnData.DisplayName, owner.Value, positionAndOrientation, grid, spawnData.BotRole, toolType, spawnData.Color);
        else
          botChar = SpawnNPC(spawnData.BotSubtype, spawnData.DisplayName, positionAndOrientation, grid, spawnData.BotRole, toolType, spawnData.Color, owner);

        BotBase bot;
        if (botChar != null && AiSession.Instance.Bots.TryGetValue(botChar.EntityId, out bot))
        {
          if (AiSession.Instance.ModSaveData.AllowEnemiesToFly)
          {
            bot.CanUseAirNodes = spawnData.CanUseAirNodes;
            bot.CanUseSpaceNodes = spawnData.CanUseSpaceNodes;
          }

          bot.AllowIdleMovement = spawnData.AllowIdleMovement;
          bot.CanTransitionMaps = spawnData.AllowMapTransitions;
          bot.ConfineToMap = spawnData.ConfineToMap;
          bot.CanDamageGrid = spawnData.CanDamageGrids;
          bot.CanUseWaterNodes = spawnData.CanUseWaterNodes;
          bot.WaterNodesOnly = spawnData.WaterNodesOnly;
          bot.GroundNodesFirst = spawnData.UseGroundNodesFirst;
          bot.CanUseLadders = spawnData.CanUseLadders;
          bot.CanUseSeats = spawnData.CanUseSeats;
          bot.ShouldLeadTargets = spawnData.LeadTargets;
          bot._lootContainerSubtype = spawnData.LootContainerSubtypeId;
          bot._shotAngleDeviationTan = (float)Math.Tan(MathHelper.ToRadians(spawnData.ShotDeviationAngle));

          if (spawnData.DespawnTicks == 0)
            bot.EnableDespawnTimer = false;
          else
            bot._despawnTicks = spawnData.DespawnTicks;

          if (!string.IsNullOrWhiteSpace(spawnData.DeathSound))
          {
            if (bot._deathSound != null)
              bot._deathSound.Init(spawnData.DeathSound);
            else
              bot._deathSound = new MySoundPair(spawnData.DeathSound);

            bot._deathSoundString = spawnData.DeathSound;
          }

          if (spawnData.AttackSounds?.Count > 0)
          {
            var botSounds = bot._attackSoundStrings;
            var botSoundPairs = bot._attackSounds;
            botSounds.Clear();

            for (int i = 0; i < spawnData.AttackSounds.Count; i++)
            {
              var sound = spawnData.AttackSounds[i];
              botSounds.Add(sound);

              if (botSoundPairs.Count > i)
                botSoundPairs[i].Init(sound);
              else
                botSoundPairs.Add(new MySoundPair(sound));
            }

            var remaining = botSoundPairs.Count - botSounds.Count;
            if (remaining > 0)
              botSoundPairs.RemoveRange(botSounds.Count, remaining);
          }

          if (spawnData.IdleSounds?.Count > 0)
          {
            var sounds = bot.Behavior.Phrases;
            sounds.Clear();
            sounds.AddList(spawnData.IdleSounds);
          }

          if (spawnData.TauntSounds?.Count > 0)
          {
            var sounds = bot.Behavior.Taunts;
            sounds.Clear();
            sounds.AddList(spawnData.TauntSounds);
          }

          if (spawnData.Actions?.Count > 0)
          {
            var actions = bot.Behavior.Actions;
            actions.Clear();
            actions.AddList(spawnData.Actions);
          }

          if (spawnData.PainSounds?.Count > 0)
          {
            var sounds = bot.Behavior.PainSounds;
            sounds.Clear();
            sounds.AddList(spawnData.PainSounds);
          }

          if (spawnData.RepairPriorities?.Count > 0)
          {
            bot.RepairPriorities = new RepairPriorities(spawnData.RepairPriorities);
          }
          else
          {
            bot.RepairPriorities = new RepairPriorities();
          }

          if (spawnData.TargetPriorities?.Count > 0)
          {
            bot.TargetPriorities = new TargetPriorities(spawnData.TargetPriorities);
          }
          else
          {
            bot.TargetPriorities = new TargetPriorities();
          }
        }

        return botChar;
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.SpawnBotAPI-1: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return null;
      }
    }

    public static IMyCharacter SpawnBotFromAPI(string subtype, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null, bool adminSpawn = false, long? factionId = null)
    {
      try
      {
        List<MyVoxelBase> vList = AiSession.Instance.VoxelMapListPool.Get();

        var syncRange = AiSession.Instance.SyncRange * 0.75;
        if (MyAPIGateway.Multiplayer.MultiplayerActive && grid != null && Vector3D.DistanceSquared(grid.PositionComp.WorldAABB.Center, positionAndOrientation.Position) > syncRange * syncRange)
        {
          var gridAABB = grid.PositionComp.WorldAABB;
          var dirVector = grid.Physics.LinearVelocity.LengthSquared() > 0 ? Vector3D.Normalize(-grid.Physics.LinearVelocity) : grid.WorldMatrix.Backward;
          var closePosition = gridAABB.Center + dirVector * (gridAABB.HalfExtents.AbsMax() + 50);
          positionAndOrientation.Position = closePosition;
        }

        var position = positionAndOrientation.Position;
        var sphere = new BoundingSphereD(position, 10);
        MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, vList);

        if (vList.Count > 0)
        {
          MyVoxelBase voxel = null;
          for (int i = 0; i < vList.Count; i++)
          {
            voxel = vList[i]?.RootVoxel;
            if (voxel != null)
              break;
          }

          if (GridBase.PointInsideVoxel(position, voxel))
          {
            float _;
            var gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out _);

            Vector3D upVector;
            if (gravity.LengthSquared() > 0)
            {
              upVector = Vector3D.Normalize(-gravity);
            }
            else
            {
              var matrix = MatrixD.CreateFromQuaternion(positionAndOrientation.Orientation);
              upVector = matrix.Up;
            }

            bool onGround;
            positionAndOrientation.Position = GridBase.GetClosestSurfacePointFast(position, -gravity, voxel, out onGround);
          }
        }

        vList.Clear();
        AiSession.Instance.VoxelMapListPool?.Return(ref vList);

        if (owner > 0 && AiSession.Instance.Players.ContainsKey(owner.Value))
          return SpawnHelper(subtype, displayName, owner.Value, positionAndOrientation, grid, role, null, color, adminSpawned: adminSpawn, factionId: factionId);

        return SpawnNPC(subtype, displayName, positionAndOrientation, grid, role, null, color, owner);
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.SpawnBotAPI-2: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
        return null;
      }
    }

    public static IMyCharacter SpawnHelper(string subType, string displayName, long ownerId, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, string toolType = null, Color? color = null, CrewBot.CrewType? crewFunction = null, bool adminSpawned = false, long? factionId = null)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
        return null;

      bool invalidRole = !AiSession.Instance.ModSaveData.AllowedBotRoles.Contains(role.ToUpperInvariant());
      if (!adminSpawned && !string.IsNullOrEmpty(role) && invalidRole)
      {
        if (invalidRole)
          AiSession.Instance.Logger.Log($"Something attempted to spawn a {role} helper which is not in AllowedBotRoles.", MessageType.WARNING);

        return null;
      }

      bool needsName = string.IsNullOrWhiteSpace(displayName);
      if (adminSpawned && !needsName)
        displayName = GetUniqueNameUser(displayName);

      var tuple = CreateBotObject(subType, displayName, positionAndOrientation, ownerId, color, factionToPairWith: factionId, adminSpawn: adminSpawned);
      var bot = tuple.Item1;
      if (bot != null)
      {
        var gridMap = AiSession.Instance.GetNewGraph(grid, bot.WorldAABB.Center, bot.WorldMatrix);

        if (grid?.Physics != null && !grid.IsStatic)
        {
          bot.Physics.LinearVelocity = grid.Physics.LinearVelocity;

          var controlEnt = bot as Sandbox.Game.Entities.IMyControllableEntity;
          if (controlEnt != null && controlEnt.RelativeDampeningEntity?.EntityId != grid.EntityId)
            controlEnt.RelativeDampeningEntity = grid;
        }

        BotRoleFriendly botRole;
        if (string.IsNullOrWhiteSpace(role))
        {
          switch (subType)
          {
            case "Drone_Bot":
              botRole = BotRoleFriendly.REPAIR;
              break;
            case "RoboDog":
              botRole = BotRoleFriendly.SCAVENGER;
              break;
            case "Default_Astronaut":
            case "Default_Astronaut_Female":
              botRole = BotRoleFriendly.CREW;
              break;
            //case "Police_Bot":
            //  botRole = BotRoleFriendly.MEDIC;
            //  break;
            default:
              botRole = BotRoleFriendly.COMBAT;
              break;
          }
        }
        else
          botRole = ParseFriendlyRole(role);

        if (!adminSpawned && !AiSession.Instance.ModSaveData.AllowedBotRoles.Contains(botRole.ToString()))
        {
          if (invalidRole)
            AiSession.Instance.Logger.Log($"Something attempted to spawn a {botRole} helper which is not in AllowedBotRoles.", MessageType.WARNING);

          bot.Delete();
          return null;
        }

        BotBase robot;
        switch (botRole)
        {
          case BotRoleFriendly.REPAIR:
            if (needsName)
            {
              bot.Name = GetUniqueName("RepairBot");
            }

            robot = new RepairBot(bot, gridMap, ownerId, tuple.Item2, toolType);
            break;
          case BotRoleFriendly.SCAVENGER:
            if (needsName)
            {
              bot.Name = GetUniqueName("ScavengerBot");
            }

            robot = new ScavengerBot(bot, gridMap, ownerId, tuple.Item2);
            break;
          case BotRoleFriendly.CREW:
            if (needsName)
            {
              bot.Name = GetUniqueName("CrewBot");
            }

            robot = new CrewBot(bot, gridMap, ownerId, tuple.Item2);

            if (crewFunction.HasValue)
            {
              var crewBot = robot as CrewBot;
              crewBot.CrewFunction = crewFunction.Value;

              string msg = null;
              switch (crewBot.CrewFunction)
              {
                case CrewBot.CrewType.ENGINEER:
                  msg = $"{bot.Name} will resume Engineer role.";
                  break;
                case CrewBot.CrewType.FABRICATOR:
                  msg = $"{bot.Name} will resume Fabricator role.";
                  break;
                case CrewBot.CrewType.WEAPONS:
                  msg = $"{bot.Name} will resume Weapons Specialist role.";
                  break;
                default:
                  break;
              }

              var ownerIdent = MyAPIGateway.Players.TryGetSteamId(ownerId);
              if (ownerIdent > 0 && !string.IsNullOrEmpty(msg))
              {
                var pkt = new MessagePacket(msg, "White", 3000);
                AiSession.Instance.Network.SendToPlayer(pkt, ownerIdent);
              }
            }

            break;
          default:
            if (needsName)
            {
              bot.Name = GetUniqueName("CombatBot");
            }

            robot = new CombatBot(bot, gridMap, ownerId, tuple.Item2, toolType);
            break;
        }

        AiSession.Instance.AddBot(robot, ownerId, adminSpawned);
      }

      return bot;
    }

    public static IMyCharacter SpawnNPC(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, string toolType = null, Color? color = null, long? ownerId = null, bool adminSpawned = false)
    {
      if (AiSession.Instance == null || !AiSession.Instance.Registered)
        return null; 

      bool nullRole = string.IsNullOrEmpty(role);
      bool invalidRole = !nullRole && !AiSession.Instance.ModSaveData.AllowedBotRoles.Contains(role.ToUpperInvariant());
      if (!adminSpawned && invalidRole)
      {
        if (invalidRole)
          AiSession.Instance.Logger.Log($"Something attempted to spawn a {role} helper which is not in AllowedBotRoles.", MessageType.WARNING);

        return null;
      }

      var tuple = CreateBotObject(subType, displayName, positionAndOrientation, null, color, adminSpawn: adminSpawned);
      var bot = tuple.Item1;
      if (bot != null)
      {
        var biggestGrid = grid;
        GridBase gridMap;

        if (grid != null && !grid.MarkedForClose && !AiSession.Instance.GridGraphDict.ContainsKey(grid.EntityId))
        {
          biggestGrid = GridBase.GetLargestGridForMap(grid) as MyCubeGrid;
        }

        if (biggestGrid?.Physics != null && !biggestGrid.IsStatic)
        {
          bot.Physics.LinearVelocity = biggestGrid.Physics.LinearVelocity;

          var controlEnt = bot as Sandbox.Game.Entities.IMyControllableEntity;
          if (controlEnt != null && controlEnt.RelativeDampeningEntity?.EntityId != biggestGrid.EntityId)
            controlEnt.RelativeDampeningEntity = biggestGrid;
        }

        gridMap = AiSession.Instance.GetNewGraph(biggestGrid, bot.WorldAABB.Center, bot.WorldMatrix);
        bool needsName = string.IsNullOrWhiteSpace(displayName);
        var botId = bot.ControllerInfo.ControllingIdentityId;

        bool isNomad = false;
        bool isEnforcer = false;
        bool isPatron = false;
        if (!string.IsNullOrWhiteSpace(role))
        {
          var upper = role.ToUpperInvariant();
          if (upper == "NOMAD")
            isNomad = true;
          else if (upper == "ENFORCER")
            isEnforcer = true;
          else if (upper == "PATRON")
            isPatron = true;
        }

        if (!isNomad && !isEnforcer && !isPatron)
        {
          if (!ownerId.HasValue && grid != null)
          {
            ownerId = 0;
            if (grid.BigOwners?.Count > 0)
              ownerId = grid.BigOwners[0];
            else if (grid.SmallOwners?.Count > 0)
              ownerId = grid.SmallOwners[0];
          }

          if (ownerId > 0)
          {
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId.Value);
            if (faction != null && !faction.AcceptHumans)
            {
              if (!faction.AutoAcceptMember)
                MyAPIGateway.Session.Factions.ChangeAutoAccept(faction.FactionId, botId, true, faction.AutoAcceptPeace);

              MyVisualScriptLogicProvider.SetPlayersFaction(botId, faction.Tag);
            }
            else if (MyAPIGateway.Session.Factions.TryGetPlayerFaction(botId)?.Tag != "SPRT")
            {
              faction = MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT");
              if (faction != null)
              {
                if (!faction.AutoAcceptMember)
                  MyAPIGateway.Session.Factions.ChangeAutoAccept(faction.FactionId, botId, true, faction.AutoAcceptPeace);

                MyVisualScriptLogicProvider.SetPlayersFaction(botId, "SPRT");
              }
            }
          }
        }

        BotBase robot;
        bool runNeutral = false;
        if (isNomad || isEnforcer || isPatron)
        {
          if (needsName)
          {
            bot.Name = GetUniqueName(isNomad ? "NomadBot" : isEnforcer ? "EnforcerBot" : "PatronBot");
          }

          var botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag("NOMAD");
          if (botFaction != null)
          {
            if (!botFaction.AutoAcceptMember)
              MyAPIGateway.Session.Factions.ChangeAutoAccept(botFaction.FactionId, botId, true, botFaction.AutoAcceptPeace);

            MyVisualScriptLogicProvider.SetPlayersFaction(botId, "NOMAD");
            
            botFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(botId);
            if (botFaction == null)
            {
              AiSession.Instance.Logger.Log($"BotFactory.SpawnNPC: Faction was null after assignment. Closing bot!", MessageType.WARNING);
              bot.Delete();
              return null;
            }
          }

          if (isNomad)
            robot = new NomadBot(bot, gridMap, tuple.Item2, toolType);
          else if (isEnforcer)
            robot = new EnforcerBot(bot, gridMap, tuple.Item2, toolType);
          else
            robot = new PatronBot(bot, gridMap, tuple.Item2, toolType);

          runNeutral = true;
        }
        else
        {
          var botFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(botId);
          if (botFaction == null)
          {
            botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT");
            if (botFaction != null)
            {
              if (!botFaction.AutoAcceptMember)
                MyAPIGateway.Session.Factions.ChangeAutoAccept(botFaction.FactionId, botId, true, botFaction.AutoAcceptPeace);

              MyVisualScriptLogicProvider.SetPlayersFaction(botId, "SPRT");
            }

            botFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(botId);
            if (botFaction == null)
            {
              AiSession.Instance.Logger.Log($"BotFactory.SpawnNPC: Faction was null after assignment. Closing bot!", MessageType.WARNING);
              bot.Delete();
              return null;
            }
          }
          else if (botFaction.Tag == "NOMAD")
            runNeutral = true;

          BotRoleEnemy botRole;
          if (string.IsNullOrWhiteSpace(role))
          {
            switch (subType)
            {
              case "Space_Skeleton":
                botRole = BotRoleEnemy.GRINDER;
                break;
              case "Space_Zombie":
                botRole = BotRoleEnemy.ZOMBIE;
                break;
              case "Boss_Bot":
                botRole = BotRoleEnemy.BRUISER;
                break;
              case "Ghost_Bot":
                botRole = BotRoleEnemy.GHOST;
                break;
              case "Police_Bot":
              default:
                botRole = BotRoleEnemy.SOLDIER;
                break;
            }
          }
          else
            botRole = ParseEnemyBotRole(role);

          if (!adminSpawned && !AiSession.Instance.ModSaveData.AllowedBotRoles.Contains(botRole.ToString()))
          {
            if (invalidRole)
              AiSession.Instance.Logger.Log($"Something attempted to spawn a {botRole} helper which is not in AllowedBotRoles.", MessageType.WARNING);

            bot.Delete();
            return null;
          }

          switch (botRole)
          {
            case BotRoleEnemy.ZOMBIE:
              if (needsName)
              {
                bot.Name = GetUniqueName("ZombieBot");
              }

              robot = new ZombieBot(bot, gridMap, tuple.Item2);
              break;
            case BotRoleEnemy.GRINDER:
              if (needsName)
              {
                bot.Name = GetUniqueName("GrinderBot");
              }

              robot = new GrinderBot(bot, gridMap, tuple.Item2, toolType);
              break;
            case BotRoleEnemy.BRUISER:
              robot = new BruiserBot(bot, gridMap, tuple.Item2);
              break;
            case BotRoleEnemy.GHOST:
              if (needsName)
              {
                bot.Name = GetUniqueName("GhostBot");
              }

              robot = new GhostBot(bot, gridMap, tuple.Item2);
              break;
            case BotRoleEnemy.CREATURE:
              if (needsName)
              {
                bot.Name = GetUniqueName("CreatureBot");
              }

              robot = new CreatureBot(bot, gridMap, tuple.Item2);
              break;
            case BotRoleEnemy.SOLDIER:
            default:
              if (needsName)
              {
                bot.Name = GetUniqueName("SoldierBot");
              }

              robot = new SoldierBot(bot, gridMap, tuple.Item2, toolType);
              break;
          }
        }

        AiSession.Instance.AddBot(robot);

        if (runNeutral)
          EnsureNuetrality();
      }

      return bot;
    }

    public static MyTuple<IMyCharacter, AiSession.ControlInfo> CreateBotObject(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, long? ownerId = null, Color? botColor = null, long? factionToPairWith = null, bool adminSpawn = false)
    {
      try
      {
        if (Environment.CurrentManagedThreadId != AiSession.MainThreadId)
          AiSession.Instance.Logger.Log($"CreateBotObject called from parallel thread! ThreadId = {Environment.CurrentManagedThreadId}", MessageType.WARNING);

        if (string.IsNullOrEmpty(subType))
          subType = "Default_Astronaut";

        bool invalidSubtype = (!adminSpawn && !AiSession.Instance.ModSaveData.AllowedBotSubtypes.Contains(subType));
        if (AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn || invalidSubtype)
        {
          if (invalidSubtype)
            AiSession.Instance.Logger.Log($"Something attempted to spawn '{subType}' which is not included in the AllowedBotSubtypes. Spawn aborted.", MessageType.WARNING);

          return MyTuple.Create<IMyCharacter, AiSession.ControlInfo>(null, null);
        }

        var info = AiSession.Instance.GetBotIdentity();
        if (info == null)
        {
          if (!AiSession.Instance.EemLoaded)
            AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: Attempted to create a bot, but ControlInfo returned null. Please try again in a few moments.", MessageType.WARNING);

          return MyTuple.Create<IMyCharacter, AiSession.ControlInfo>(null, null);
        }

        long botPlayerId = info.Identity.IdentityId;
        Vector3 hsvOffset;
        //_factionIdentities.Clear();

        IMyFaction ownerFaction = null;
        IMyFaction botFaction = null;

        if (ownerId.HasValue || factionToPairWith.HasValue)
        {
          if (factionToPairWith.HasValue)
          {
            ownerFaction = MyAPIGateway.Session.Factions.TryGetFactionById(factionToPairWith.Value);
            ownerId = ownerFaction.FounderId;
          }

          if (ownerFaction == null)
          {
            ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId.Value);
            if (ownerFaction == null)
            {
              AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: The bot owner is not in a faction!", MessageType.WARNING);
              return MyTuple.Create<IMyCharacter, AiSession.ControlInfo>(null, null);
            }
          }

          botFaction = AiSession.Instance.GetBotFactionAssignment(ownerFaction);
          if (botFaction == null)
          {
            AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: There was no bot faction associated with the owner!", MessageType.WARNING);
            return MyTuple.Create<IMyCharacter, AiSession.ControlInfo>(null, null);

            /*
              TODO: Switch to creating factions on the fly if Keen can default AcceptsHumans to false for NPC Factions

              var fTag = MyUtils.GetRandomInt(100, 999).ToString();
              var fName = MyUtils.GetRandomInt(1000, 9999).ToString();
              MyAPIGateway.Session.Factions.CreateNPCFaction(fTag, fName, "", "");
              botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag(fTag);
              if (botFaction == null)
              {
                AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: Unable to create bot faction!", MessageType.WARNING);
                return null;
              }

              AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: Created bot faction '{botFaction.Name}', AcceptsHumans = {botFaction.AcceptHumans}");
              AiSession.Instance.BotFactions[ownerFaction.FactionId] = botFaction;
            */
          }

          if (MyAPIGateway.Session.Factions.AreFactionsEnemies(botFaction.FactionId, ownerFaction.FactionId))
          {
            MyAPIGateway.Session.Factions.ChangeAutoAccept(botFaction.FactionId, botPlayerId, true, true);
            MyAPIGateway.Session.Factions.ChangeAutoAccept(ownerFaction.FactionId, ownerId.Value, ownerFaction.AutoAcceptMember, true);
            MyAPIGateway.Session.Factions.AcceptPeace(ownerFaction.FactionId, botFaction.FactionId);
          }

          MyVisualScriptLogicProvider.SetPlayersFaction(botPlayerId, botFaction.Tag);
          MyAPIGateway.Session.Factions.SetReputation(botFaction.FactionId, ownerFaction.FactionId, int.MaxValue);
          MyAPIGateway.Session.Factions.SetReputation(ownerFaction.FactionId, botFaction.FactionId, int.MaxValue);
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(botPlayerId, ownerFaction.FactionId, int.MaxValue);
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(ownerId.Value, botFaction.FactionId, int.MaxValue);

          foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
          {
            if (kvp.Key == ownerFaction.FactionId || kvp.Key == botFaction.FactionId)
              continue;

            var rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(ownerId.Value, kvp.Key);
            if (rep == 0)
              continue;

            MyAPIGateway.Session.Factions.SetReputation(botFaction.FactionId, kvp.Key, rep);
            MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(botPlayerId, kvp.Key, rep);
          }

          if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
          {
            var pkt = new RepChangePacket(botPlayerId, botFaction.FactionId, ownerId.Value, ownerFaction.FactionId, int.MaxValue);
            AiSession.Instance.Network.RelayToClients(pkt);
          }

          if (botColor.HasValue)
          {
            var clr = botColor.Value;
            var hsv = clr.ColorToHSV();
            hsvOffset = MyColorPickerConstants.HSVToHSVOffset(hsv);
          }
          else
          {
            IMyPlayer owner;
            if (AiSession.Instance.Players.TryGetValue(ownerId.Value, out owner) && owner?.Character != null)
            {
              var ownerOB = owner.Character.GetObjectBuilder() as MyObjectBuilder_Character;
              hsvOffset = ownerOB.ColorMaskHSV;
            }
            else
            {
              var hsv = Color.Crimson.ColorToHSV();
              hsvOffset = MyColorPickerConstants.HSVToHSVOffset(hsv);
            }
          }
        }
        else if (botColor.HasValue)
        {
          var clr = botColor.Value;
          var hsv = clr.ColorToHSV();
          hsvOffset = MyColorPickerConstants.HSVToHSVOffset(hsv);
        }
        else
        {
          var hsv = Color.DarkRed.ColorToHSV();
          hsvOffset = MyColorPickerConstants.HSVToHSVOffset(hsv);
        }

        if (string.IsNullOrWhiteSpace(displayName))
          displayName = GetBotName(subType, ownerId.HasValue);

        if ((Vector3)positionAndOrientation.Forward == Vector3.Zero || (Vector3)positionAndOrientation.Up == Vector3.Zero)
        {
          positionAndOrientation.Forward = Vector3.Forward;
          positionAndOrientation.Up = Vector3.Up;
        }

        var ob = new MyObjectBuilder_Character()
        {
          Name = displayName,
          DisplayName = null,
          SubtypeName = subType,
          CharacterModel = subType,
          EntityId = 0,
          AIMode = false,
          JetpackEnabled = subType == "Drone_Bot" || MyAPIGateway.Session.SessionSettings.EnableJetpack,
          EnableBroadcasting = false,
          NeedsOxygenFromSuit = false,
          OxygenLevel = 1,
          MovementState = MyCharacterMovementEnum.Standing,
          PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.Enabled,
          PositionAndOrientation = positionAndOrientation,
          Health = 1000,
          OwningPlayerIdentityId = botPlayerId,
          ColorMaskHSV = hsvOffset,
        };

        var bot = MyEntities.CreateFromObjectBuilder(ob, true) as IMyCharacter;
        if (bot != null)
        {
          bot.Save = false;
          bot.Synchronized = true;
          bot.Flags &= ~VRage.ModAPI.EntityFlags.NeedsUpdate100;

          var charDef = bot.Definition as MyCharacterDefinition;
          string open, close;
          if (AiSession.Instance.ModSaveData.AllowHelmetVisorChanges 
            && charDef.AnimationNameToSubtypeName.TryGetValue("HelmetOpen", out open)
            && charDef.AnimationNameToSubtypeName.TryGetValue("HelmetClose", out close))
          {
            var oxyComp = bot.Components.Get<MyCharacterOxygenComponent>();
            if (oxyComp == null)
            {
              oxyComp = new MyCharacterOxygenComponent();
              oxyComp.Init(ob);
              bot.Components.Add(oxyComp);
            }
          }

          if (bot.PositionComp.GetPosition() == Vector3D.Zero)
            bot.SetPosition(positionAndOrientation.Position);

          if (info != null)
          {
            info.EntityId = bot.EntityId;
            //var ident = MyAPIGateway.Players.CreateNewIdentity("", addToNpcs: true);
            //var botPlayer = MyAPIGateway.Players.CreateNewPlayer(ident, "", false, false, true, true);
            //info.Controller = MyAPIGateway.Players.CreateNewEntityController(botPlayer); // testing
            info.Controller.TakeControl(bot);

            if (MyAPIGateway.Multiplayer.MultiplayerActive)
            {
              var packet = new AdminPacket(botPlayerId, bot.EntityId, ownerId);
              AiSession.Instance.Network.RelayToClients(packet);
            }
          }

          var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(bot.ControllerInfo.ControllingIdentityId);
          if (faction == null && botFaction != null)
            AiSession.Instance.Logger.Log($"Bot was spawned with null faction! Was supposed to be {botFaction.Name}", MessageType.WARNING);

          MyEntities.Add((MyEntity)bot, true);
        }

        return MyTuple.Create(bot, info);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotFactory.CreateBot: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }

      return MyTuple.Create<IMyCharacter, AiSession.ControlInfo>(null, null);
    }

    public static BotRoleEnemy ParseEnemyBotRole(string role)
    {
      if (!string.IsNullOrEmpty(role))
      {
        role = role.ToUpperInvariant();
        if (role.EndsWith("BOT"))
        {
          role = role.Substring(0, role.Length - 3);
        }

        BotRoleEnemy br;
        if (Enum.TryParse(role, out br))
          return br;

      }

      return BotRoleEnemy.SOLDIER;
    }

    public static BotRoleFriendly ParseFriendlyRole(string role)
    {
      if (!string.IsNullOrEmpty(role))
      {
        role = role.ToUpperInvariant();
        if (role.EndsWith("BOT"))
        {
          role = role.Substring(0, role.Length - 3);
        }

        BotRoleFriendly br;
        if (Enum.TryParse(role, out br))
          return br;
      }

      return BotRoleFriendly.COMBAT;
    }

    public static string GetBotName(string subtype, bool isFriendly)
    {
      int random = MyUtils.GetRandomInt(1000, 9999);
      string name;
      switch (subtype)
      {
        case "Space_Zombie":
          name = "ZombieBot";
          break;
        case "Space_Skeleton":
          name = "GrinderBot";
          break;
        case "Boss_Bot":
          name = "BruiserBot";
          break;
        case "Drone_Bot":
          name = "RepairBot";
          break;
        case "RoboDog":
          name = "ScavengerBot";
          break;
        case "Police_Bot":
          name = isFriendly ? "MedicBot" : "SoldierBot";
          break;
        default:
          name = isFriendly ? "CombatBot" : "SoldierBot";
          break;
      }

      var displayName = $"{name}{random}";
      while (MyEntities.EntityExists(name))
      {
        random++;
        displayName = $"{name}{random}";
      }

      return displayName;
    }

    public static string GetUniqueNameUser(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        name = "AiEnabledBot";

      if (!MyEntities.EntityExists(name))
        return name;

      int num = 1;
      var displayName = $"{name}-{num}";

      while (MyEntities.EntityExists(displayName))
      {
        num++;
        displayName = $"{name}-{num}";
      }

      return displayName;
    }

    public static string GetUniqueName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        name = "AiEnabledBot";

      int random = MyUtils.GetRandomInt(1000, 9999);
      var displayName = $"{name}{random}";

      while (MyEntities.EntityExists(displayName))
      {
        random++;
        displayName = $"{name}{random}";
      }

      return displayName;
    }

    public static void EnsureNuetrality()
    {
      try
      {
        var botFaction = MyAPIGateway.Session.Factions.TryGetFactionByTag("NOMAD");
        if (botFaction == null)
          return;

        var players = AiSession.Instance.Players;
        var bots = AiSession.Instance.Bots;

        foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
        {
          if (kvp.Value.Tag == "SPRT" || kvp.Value.Tag == "SPID")
            continue;

          MyAPIGateway.Session.Factions.SetReputation(botFaction.FactionId, kvp.Key, 0);
        }

        foreach (var p in players.Values)
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(p.IdentityId, botFaction.FactionId, 0);

        foreach (var b in bots.Values)
        {
          if (b?.Character != null && b.Owner != null)
            MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(b.BotIdentityId, botFaction.FactionId, 0);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance?.Logger?.Log($"Exception in BotFactory.EnsureNuetrality: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }
    }
  }
}
