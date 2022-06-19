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
using AiEnabled.Support;
using AiEnabled.Utilities;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

namespace AiEnabled.Bots
{
  public static class BotFactory
  {
    public enum BotRoleNeutral { NOMAD };
    public enum BotRoleEnemy { ZOMBIE, SOLDIER, BRUISER, GRINDER, GHOST, CREATURE };
    public enum BotRoleFriendly { REPAIR, COMBAT, SCAVENGER }; //, MEDIC };

    public static bool TrySeatBotOnGrid(BotBase bot, IMyCubeGrid grid)
    {
      var seats = AiSession.Instance.GridSeatsAPI;
      var useObjs = AiSession.Instance.UseObjectsAPI;
      seats.Clear();
      useObjs.Clear();

      grid.GetBlocks(seats, b => b.FatBlock is IMyCockpit);
      for (int i = seats.Count - 1; i >= 0; i--)
      {
        var seat = seats[i]?.FatBlock as IMyCockpit;

        if (seat == null || seat.Pilot != null || !seat.IsFunctional 
          || !seat.HasPlayerAccess(bot.Character.ControllerInfo.ControllingIdentityId)
          || seat.BlockDefinition.SubtypeId.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0
          || seat.BlockDefinition.SubtypeId.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0
          || seat.BlockDefinition.SubtypeId.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0)
          continue;

        var useComp = seat.Components.Get<MyUseObjectsComponentBase>();
        useComp?.GetInteractiveObjects(useObjs);
        if (useObjs.Count > 0)
        {
          if (bot.HasWeaponOrTool)
          {
            var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
            controlEnt?.SwitchToWeapon(null);
          }

          var seatCube = seat as MyCubeBlock;
          var useObj = useObjs[0];

          if (useObj != null)
          {
            var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
            bool changeBack = false;

            if (shareMode != MyOwnershipShareModeEnum.All)
            {
              var owner = bot.Owner?.IdentityId ?? bot.Character.ControllerInfo.ControllingIdentityId;
              var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

              var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
              if (relation != MyRelationsBetweenPlayers.Enemies)
              {
                changeBack = true;
                seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
              }
            }

            if (seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
            {
              var freeSpace = MyEntities.FindFreePlaceCustom(seat.GetPosition(), 5);
              if (freeSpace.HasValue)
              {
                var offset = Vector3D.Rotate(freeSpace.Value - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
                AiSession.Instance.BotToSeatRelativePosition[bot.Character.EntityId] = offset;
                bot.Character.SetPosition(freeSpace.Value);
              }

              var mapGrid = GetLargestGridForMap(seat.CubeGrid) as MyCubeGrid;
              bot._currentGraph = AiSession.Instance.GetNewGraph(mapGrid, bot.GetPosition(), bot.WorldMatrix);
              useObj.Use(UseActionEnum.Manipulate, bot.Character);
            }

            if (changeBack)
              seatCube.IDModule.ShareMode = shareMode;

            bot._pathCollection?.CleanUp(true);
            bot.Target.RemoveTarget();
            seats.Clear();
            useObjs.Clear();
            return true;
          }
        }
      }

      seats.Clear();
      useObjs.Clear();
      return false;
    }

    public static bool TrySeatBot(BotBase bot, IMyCockpit seat)
    {
      var list = AiSession.Instance.UseObjectsAPI;
      list.Clear();

      var useComp = seat.Components?.Get<MyUseObjectsComponentBase>();
      useComp?.GetInteractiveObjects(list);

      if (list.Count > 0)
      {
        var currentSeat = bot.Character.Parent as IMyCockpit;
        if (currentSeat != null)
          currentSeat.RemovePilot();

        if (bot.HasWeaponOrTool)
        {
          var controlEnt = bot.Character as Sandbox.Game.Entities.IMyControllableEntity;
          controlEnt?.SwitchToWeapon(null);
        }

        var seatCube = seat as MyCubeBlock;
        var useObj = list[0];

        if (useObj != null)
        {
          var shareMode = seatCube.IDModule?.ShareMode ?? MyOwnershipShareModeEnum.All;
          bool changeBack = false;

          if (shareMode != MyOwnershipShareModeEnum.All)
          {
            var owner = bot.Owner?.IdentityId ?? bot.Character.ControllerInfo.ControllingIdentityId;
            var gridOwner = seat.CubeGrid.BigOwners?.Count > 0 ? seat.CubeGrid.BigOwners[0] : seat.CubeGrid.SmallOwners?.Count > 0 ? seat.CubeGrid.SmallOwners[0] : seat.SlimBlock.BuiltBy;

            var relation = MyIDModule.GetRelationPlayerPlayer(owner, gridOwner, MyRelationsBetweenFactions.Neutral, MyRelationsBetweenPlayers.Neutral);
            if (relation != MyRelationsBetweenPlayers.Enemies)
            {
              changeBack = true;
              seatCube.IDModule.ShareMode = MyOwnershipShareModeEnum.All;
            }
          }

          if (seatCube.IDModule == null || seatCube.IDModule.ShareMode == MyOwnershipShareModeEnum.All)
          {
            var freeSpace = MyEntities.FindFreePlaceCustom(seat.GetPosition(), 5);
            if (freeSpace.HasValue)
            {
              var offset = Vector3D.Rotate(freeSpace.Value - seat.GetPosition(), MatrixD.Transpose(seat.WorldMatrix));
              AiSession.Instance.BotToSeatRelativePosition[bot.Character.EntityId] = offset;
              bot.Character.SetPosition(freeSpace.Value);
            }

            var grid = GetLargestGridForMap(seat.CubeGrid) as MyCubeGrid;
            bot._currentGraph = AiSession.Instance.GetNewGraph(grid, bot.GetPosition(), bot.WorldMatrix);
            useObj.Use(UseActionEnum.Manipulate, bot.Character);
          }

          if (changeBack)
            seatCube.IDModule.ShareMode = shareMode;

          bot._pathCollection?.CleanUp(true);
          bot.Target.RemoveTarget();
          list.Clear();
          return true;
        }
      }

      list.Clear();
      return false;
    }

    public static IMyCubeGrid GetLargestGridForMap(IMyCubeGrid initialGrid)
    {
      try
      {
        if (initialGrid == null)
          return null;

        List<IMyCubeGrid> gridList;
        if (!AiSession.Instance.GridGroupListStack.TryPop(out gridList))
          gridList = new List<IMyCubeGrid>();
        else
          gridList.Clear();

        var biggest = initialGrid;
        initialGrid?.GetGridGroup(GridLinkTypeEnum.Logical)?.GetGrids(gridList);

        for (int i = 0; i < gridList.Count; i++)
        {
          var g = gridList[i];
          if (g == null || g.MarkedForClose || g.Closed || g.GridSizeEnum == MyCubeSize.Small)
            continue;

          if (biggest.GridSizeEnum == MyCubeSize.Small || g.WorldAABB.Volume > biggest.WorldAABB.Volume)
            biggest = g;
        }

        gridList.Clear();
        AiSession.Instance.GridGroupListStack.Push(gridList);

        return (biggest.GridSize > 1) ? biggest : null;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotFactory.GetLargestGridForMap: {ex.Message}\n{ex.StackTrace}");
        return null;
      }
    }

    public static bool RemoveBotFromSeat(BotBase bot, bool checkTarget = true)
    {
      var seat = bot.Character.Parent as IMyCockpit;
      if (seat != null)
      {
        seat.RemovePilot();

        var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
        if (jetpack != null)
        {
          if (bot.RequiresJetpack || bot.CanUseAirNodes)
          {
            if (!jetpack.TurnedOn)
            {
              var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
              MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
              jetpack.TurnOnJetpack(true);
              MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
            }
          }
        }

        if (checkTarget)
        {
          bot.Target.RemoveTarget();

          if (!bot.UseAPITargets)
            bot.SetTarget();

          Vector3D actualPosition = bot.Target.CurrentActualPosition;
          bot.StartCheckGraph(ref actualPosition, true);
        }

        Vector3D position = seat.WorldAABB.Center;
        Vector3D offset;

        var map = bot._currentGraph;
        if (map == null)
        {
          var grid = GetLargestGridForMap(seat.CubeGrid) as MyCubeGrid;
          map = AiSession.Instance.GetNewGraph(grid, position, seat.WorldMatrix);
        }

        var localPoint = map.WorldToLocal(position);
        Node node;

        if (map.GetClosestValidNode(bot, localPoint, out localPoint, seat.WorldMatrix.Up, true, true, false))
        {
          position = map.LocalToWorld(localPoint);
        }
        else if (AiSession.Instance.BotToSeatRelativePosition.TryGetValue(bot.Character.EntityId, out offset))
        {
          position += Vector3D.Rotate(offset, seat.WorldMatrix);
        }
        else
        {
          offset = MyEntities.FindFreePlaceCustom(seat.GetPosition(), 5) ?? seat.WorldMatrix.Forward * 2;
          position += offset;
        }

        localPoint = map.WorldToLocal(position);
        if (map.TryGetNodeForPosition(localPoint, out node) && node != null && node.IsAirNode)
        {
          var gridMap = map as CubeGridMap;
          if (gridMap != null && gridMap.GetRandomOpenTile(bot, out node, false, false))
          {
            position = map.LocalToWorld(node.Position) + node.Offset;
          }
        }

        var voxelGraph = map as VoxelGridMap;
        if (voxelGraph != null)
        {
          var grid = seat.CubeGrid;
          var vec = position - grid.WorldAABB.Center;
          var dot = seat.WorldMatrix.Forward.Dot(vec);

          position += seat.WorldMatrix.Forward * seat.CubeGrid.LocalAABB.HalfExtents.AbsMax();
          if (dot <= 0)
            position += seat.WorldMatrix.Forward * Math.Max(1, vec.Length());

          if (!bot.CanUseAirNodes)
          {
            var voxelPosition = voxelGraph.GetClosestSurfacePointFast(bot, position, bot.WorldMatrix.Up);
            if (voxelPosition.HasValue)
              position = voxelPosition.Value;
          }
        }

        var matrix = seat.WorldMatrix;
        matrix.Translation = position;
        bot.Character.SetWorldMatrix(matrix);
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

    public static IMyCharacter SpawnBotFromAPI(MyPositionAndOrientation positionAndOrientation, RemoteBotAPI.SpawnData spawnData, MyCubeGrid grid = null, long? owner = null)
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

          for (int i = 0; i < spawnData.IdleSounds.Count; i++)
          {
            sounds.Add(spawnData.IdleSounds[i]);
          }
        }

        if (spawnData.Actions?.Count > 0)
        {
          var actions = bot.Behavior.Actions;
          actions.Clear();

          for (int i = 0; i < spawnData.Actions.Count; i++)
          {
            actions.Add(spawnData.Actions[i]);
          }
        }

        if (spawnData.PainSounds?.Count > 0)
        {
          var sounds = bot.Behavior.PainSounds;
          sounds.Clear();

          for (int i = 0; i < spawnData.PainSounds.Count; i++)
          {
            sounds.Add(spawnData.PainSounds[i]);
          }
        }
      }

      return botChar;
    }

    public static IMyCharacter SpawnBotFromAPI(string subtype, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, long? owner = null, Color? color = null)
    {
      List<MyVoxelBase> vList;
      if (!AiSession.Instance.VoxelMapListStack.TryPop(out vList) || vList == null)
        vList = new List<MyVoxelBase>();
      else
        vList.Clear();

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
      AiSession.Instance.VoxelMapListStack.Push(vList);

      if (owner > 0 && AiSession.Instance.Players.ContainsKey(owner.Value))
        return SpawnHelper(subtype, displayName, owner.Value, positionAndOrientation, grid, role, null, color);

      return SpawnNPC(subtype, displayName, positionAndOrientation, grid, role, null, color, owner);
    }

    public static IMyCharacter SpawnHelper(string subType, string displayName, long ownerId, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, string toolType = null, Color? color = null)
    {
      var bot = CreateBotObject(subType, displayName, positionAndOrientation, ownerId, color);
      if (bot != null)
      {
        var gridMap = AiSession.Instance.GetNewGraph(grid, bot.WorldAABB.Center, bot.WorldMatrix);
        bool needsName = string.IsNullOrWhiteSpace(displayName);

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

        BotBase robot;
        switch (botRole)
        {
          case BotRoleFriendly.REPAIR:
            if (needsName)
            {
              bot.Name = GetUniqueName("RepairBot");
            }

            robot = new RepairBot(bot, gridMap, ownerId, toolType);
            break;
          case BotRoleFriendly.SCAVENGER:
            if (needsName)
            {
              bot.Name = GetUniqueName("ScavengerBot");
            }

            robot = new ScavengerBot(bot, gridMap, ownerId);
            break;
          default:
            if (needsName)
            {
              bot.Name = GetUniqueName("CombatBot");
            }

            robot = new CombatBot(bot, gridMap, ownerId, toolType);
            break;
        }

        AiSession.Instance.AddBot(robot, ownerId);
      }

      return bot;
    }

    public static IMyCharacter SpawnNPC(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, string toolType = null, Color? color = null, long? ownerId = null)
    {
      var bot = CreateBotObject(subType, displayName, positionAndOrientation, null, color);
      if (bot != null)
      {
        var biggestGrid = grid;
        GridBase gridMap;

        if (grid != null && !grid.MarkedForClose && !AiSession.Instance.GridGraphDict.ContainsKey(grid.EntityId))
        {
          List<IMyCubeGrid> gridGroup;
          if (!AiSession.Instance.GridGroupListStack.TryPop(out gridGroup))
            gridGroup = new List<IMyCubeGrid>();
          else
            gridGroup.Clear();

          grid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(gridGroup);

          for (int i = 0; i < gridGroup.Count; i++)
          {
            var otherGrid = gridGroup[i] as MyCubeGrid;
            if (otherGrid != null && otherGrid.EntityId != grid.EntityId && otherGrid.GridSizeEnum == MyCubeSize.Large && otherGrid.BlocksCount > grid.BlocksCount)
            {
              biggestGrid = otherGrid;
            }
          }

          gridGroup.Clear();
          AiSession.Instance.GridGroupListStack.Push(gridGroup);
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
        bool isNomad = !string.IsNullOrWhiteSpace(role) && role.ToUpperInvariant() == "NOMAD";
        var botId = bot.ControllerInfo.ControllingIdentityId;

        if (!isNomad)
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
        if (isNomad)
        {
          if (needsName)
          {
            bot.Name = GetUniqueName("NomadBot");
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
              bot.Close();
              return null;
            }
          }

          robot = new NomadBot(bot, gridMap);
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
              bot.Close();
              return null;
            }
          }

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

          switch (botRole)
          {
            case BotRoleEnemy.ZOMBIE:
              if (needsName)
              {
                bot.Name = GetUniqueName("ZombieBot");
              }

              robot = new ZombieBot(bot, gridMap);
              break;
            case BotRoleEnemy.GRINDER:
              if (needsName)
              {
                bot.Name = GetUniqueName("GrinderBot");
              }

              robot = new GrinderBot(bot, gridMap, toolType);
              break;
            case BotRoleEnemy.BRUISER:
              robot = new BruiserBot(bot, gridMap);
              break;
            case BotRoleEnemy.GHOST:
              if (needsName)
              {
                bot.Name = GetUniqueName("GhostBot");
              }

              robot = new GhostBot(bot, gridMap);
              break;
            case BotRoleEnemy.CREATURE:
              if (needsName)
              {
                bot.Name = GetUniqueName("CreatureBot");
              }

              robot = new CreatureBot(bot, gridMap);
              break;
            case BotRoleEnemy.SOLDIER:
            default:
              if (needsName)
              {
                bot.Name = GetUniqueName("SoldierBot");
              }

              robot = new SoldierBot(bot, gridMap, toolType);
              break;
          }
        }

        AiSession.Instance.AddBot(robot);

        if (runNeutral)
          EnsureNuetrality();
      }

      return bot;
    }

    public static IMyCharacter CreateBotObject(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, long? ownerId = null, Color? botColor = null)
    {
      try
      {
        if (AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn)
          return null;

        var info = AiSession.Instance.GetBotIdentity();
        if (info == null)
        {
          if (!AiSession.Instance.EemLoaded)
            AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: Attempted to create a bot, but ControlInfo returned null. Please try again in a few moments.", MessageType.WARNING);
  
          return null;
        }

        long playerId = info.Identity.IdentityId;
        Vector3 hsvOffset;
        //_factionIdentities.Clear();

        if (ownerId.HasValue)
        {
          var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId.Value);
          if (ownerFaction == null)
          {
            AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: The bot owner is not in a faction!", MessageType.WARNING);
            return null;
          }

          IMyFaction botFaction = null;
          if (!AiSession.Instance.BotFactions.TryGetValue(ownerFaction.FactionId, out botFaction))
          {
            AiSession.Instance.Logger.Log($"BotFactory.CreateBotObject: There was no bot faction associated with the owner!", MessageType.WARNING);
            return null;

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
            MyAPIGateway.Session.Factions.ChangeAutoAccept(botFaction.FactionId, playerId, true, true);
            MyAPIGateway.Session.Factions.ChangeAutoAccept(ownerFaction.FactionId, ownerId.Value, ownerFaction.AutoAcceptMember, true);
            MyAPIGateway.Session.Factions.AcceptPeace(ownerFaction.FactionId, botFaction.FactionId);
          }

          MyVisualScriptLogicProvider.SetPlayersFaction(playerId, botFaction.Tag);
          MyAPIGateway.Session.Factions.SetReputation(botFaction.FactionId, ownerFaction.FactionId, int.MaxValue);
          MyAPIGateway.Session.Factions.SetReputation(ownerFaction.FactionId, botFaction.FactionId, int.MaxValue);
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(playerId, ownerFaction.FactionId, int.MaxValue);
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(ownerId.Value, botFaction.FactionId, int.MaxValue);

          foreach (var kvp in MyAPIGateway.Session.Factions.Factions)
          {
            if (kvp.Key == ownerFaction.FactionId || kvp.Key == botFaction.FactionId)
              continue;

            var rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(ownerId.Value, kvp.Key);
            if (rep == 0)
              continue;

            MyAPIGateway.Session.Factions.SetReputation(botFaction.FactionId, kvp.Key, rep);
            MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(playerId, kvp.Key, rep);
          }

          if (MyAPIGateway.Multiplayer.MultiplayerActive && AiSession.Instance.IsServer)
          {
            var pkt = new RepChangePacket(playerId, botFaction.FactionId, ownerId.Value, ownerFaction.FactionId, int.MaxValue);
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

        var ob = new MyObjectBuilder_Character()
        {
          Name = displayName,
          DisplayName = null,
          SubtypeName = subType,
          CharacterModel = subType,
          EntityId = 0,
          AIMode = false,
          JetpackEnabled = subType == "Drone_Bot",
          EnableBroadcasting = false,
          NeedsOxygenFromSuit = false,
          OxygenLevel = 1,
          MovementState = MyCharacterMovementEnum.Standing,
          PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.Enabled,
          PositionAndOrientation = positionAndOrientation,
          Health = 1000,
          OwningPlayerIdentityId = playerId,
          ColorMaskHSV = hsvOffset,
          //IsPersistenceCharacter = true,
          //IsStartingCharacterForLobby = true,
        };

        var bot = MyEntities.CreateFromObjectBuilder(ob, true) as IMyCharacter;
        if (bot != null)
        {
          bot.Save = false;
          bot.Synchronized = true;
          bot.Flags &= ~VRage.ModAPI.EntityFlags.NeedsUpdate100;

          if (info != null)
          {
            info.EntityId = bot.EntityId;
            //var ident = MyAPIGateway.Players.CreateNewIdentity("", addToNpcs: true);
            //var botPlayer = MyAPIGateway.Players.CreateNewPlayer(ident, "", false, false, true, true);
            //info.Controller = MyAPIGateway.Players.CreateNewEntityController(botPlayer); // testing
            info.Controller.TakeControl(bot);

            if (MyAPIGateway.Multiplayer.MultiplayerActive)
            {
              var packet = new AdminPacket(playerId, bot.EntityId, ownerId);
              AiSession.Instance.Network.RelayToClients(packet);
            }
          }

          MyEntities.Add((MyEntity)bot, true);
        }

        return bot;
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotFactory.CreateBot: {ex.Message}\n{ex.StackTrace}", MessageType.ERROR);
      }

      return null;
    }

    public static BotRoleEnemy ParseEnemyBotRole(string role)
    {
      role = role.ToUpperInvariant();
      if (role.EndsWith("BOT"))
      {
        role = role.Substring(0, role.Length - 3);
      }

      BotRoleEnemy br;
      if (Enum.TryParse(role, out br))
        return br;

      return BotRoleEnemy.SOLDIER;
    }

    public static BotRoleFriendly ParseFriendlyRole(string role)
    {
      role = role.ToUpperInvariant();
      if (role.EndsWith("BOT"))
      {
        role = role.Substring(0, role.Length - 3);
      }

      BotRoleFriendly br;
      if (Enum.TryParse(role, out br))
      {
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

    public static string GetUniqueName(string name)
    {
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
          MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(b.Character.ControllerInfo.ControllingIdentityId, botFaction.FactionId, 0);
      }
    }
  }
}
