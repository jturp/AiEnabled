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
    public enum BotRoleEnemy { ZOMBIE, SOLDIER, BRUISER, GRINDER, GHOST };
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
          var useObj = useObjs[0];
          useObj.Use(UseActionEnum.Manipulate, bot.Character);
          bot._pathCollection?.CleanUp(true);
          bot.Target.RemoveTarget();
          return true;
        }
      }

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
        var useObj = list[0];

        var currentSeat = bot.Character.Parent as IMyCockpit;
        if (currentSeat != null)
          currentSeat.RemovePilot();

        useObj.Use(UseActionEnum.Manipulate, bot.Character);
        bot._pathCollection?.CleanUp(true);
        bot.Target.RemoveTarget();
        return true;
      }

      return false;
    }

    public static bool RemoveBotFromSeat(BotBase bot)
    {
      var seat = bot.Character.Parent as IMyCockpit;
      if (seat != null)
      {
        seat.RemovePilot();

        var jetpack = bot.Character.Components?.Get<MyCharacterJetpackComponent>();
        if (jetpack != null)
        {
          if (bot._requiresJetpack)
          {
            if (!jetpack.TurnedOn)
            {
              var jetpacksAllowed = MyAPIGateway.Session.SessionSettings.EnableJetpack;
              MyAPIGateway.Session.SessionSettings.EnableJetpack = true;
              jetpack.TurnOnJetpack(true);
              MyAPIGateway.Session.SessionSettings.EnableJetpack = jetpacksAllowed;
            }
          }
          else if (jetpack.TurnedOn)
            jetpack.SwitchThrusts();
        }

        bot.Target.RemoveTarget();

        if (!bot.UseAPITargets)
          bot.SetTarget();

        Vector3D gotoPosition, actualPosition;
        bot.Target.GetTargetPosition(out gotoPosition, out actualPosition);
        bot.StartCheckGraph(ref actualPosition, true);
        Vector3D position = seat.WorldAABB.Center + seat.WorldMatrix.Forward * 2;

        var voxelGraph = bot._currentGraph as VoxelGridMap;
        if (voxelGraph != null)
        {
          var grid = seat.CubeGrid;
          var vec = position - grid.WorldAABB.Center;
          var dot = seat.WorldMatrix.Forward.Dot(vec);

          position += seat.WorldMatrix.Forward * seat.CubeGrid.LocalAABB.HalfExtents.AbsMax();
          if (dot <= 0)
            position += seat.WorldMatrix.Forward * Math.Max(1, vec.Length());

          position = voxelGraph.GetClosestSurfacePointFast(bot, position, bot.WorldMatrix.Up);
        }

        bot.Character.SetPosition(position);
        return true;
      }

      return false;
    }

    public static IMyCharacter SpawnBotFromAPI(string displayName, MyPositionAndOrientation positionAndOrientation, RemoteBotAPI.SpawnData spawnData, MyCubeGrid grid = null, long? owner = null)
    {
      IMyCharacter botChar;
      if (owner > 0 && AiSession.Instance.Players.ContainsKey(owner.Value))
        botChar = SpawnHelper(spawnData.BotSubtype, displayName, owner.Value, positionAndOrientation, grid, spawnData.BotRole, spawnData.Color);
      else
        botChar = SpawnNPC(spawnData.BotSubtype, displayName, positionAndOrientation, grid, spawnData.BotRole, spawnData.Color, owner);

      BotBase bot;
      if (botChar != null && AiSession.Instance.Bots.TryGetValue(botChar.EntityId, out bot))
      {
        bot._canUseAirNodes = spawnData.CanUseAirNodes;
        bot._canUseSpaceNodes = spawnData.CanUseSpaceNodes;
        bot._canUseWaterNodes = spawnData.CanUseWaterNodes;
        bot._waterNodesOnly = spawnData.WaterNodesOnly;
        bot._groundNodesFirst = spawnData.UseGroundNodesFirst;
        bot._enableDespawnTimer = spawnData.EnableDespawnTimer;
        bot._canUseLadders = spawnData.CanUseLadders;
        bot._canUseSeats = spawnData.CanUseSeats;

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
      if (owner > 0 && AiSession.Instance.Players.ContainsKey(owner.Value))
        return SpawnHelper(subtype, displayName, owner.Value, positionAndOrientation, grid, role, color);

      return SpawnNPC(subtype, displayName, positionAndOrientation, grid, role, color, owner);
    }

    public static IMyCharacter SpawnHelper(string subType, string displayName, long ownerId, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, Color? color = null)
    {
      var bot = CreateBotObject(subType, displayName, positionAndOrientation, ownerId, color);
      if (bot != null)
      {
        grid = grid?.GetBiggestGridInGroup();
        var gridMap = AiSession.Instance.GetNewGraph(grid, bot.WorldAABB.Center, bot.WorldMatrix);
        bool needsName = string.IsNullOrWhiteSpace(displayName);

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

            robot = new RepairBot(bot, gridMap, ownerId);
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

            robot = new CombatBot(bot, gridMap, ownerId);
            break;
        }

        AiSession.Instance.AddBot(robot, ownerId);
      }

      return bot;
    }

    static List<IMyCubeGrid> _gridGroups = new List<IMyCubeGrid>();

    public static IMyCharacter SpawnNPC(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, MyCubeGrid grid = null, string role = null, Color? color = null, long? ownerId = null)
    {
      var bot = CreateBotObject(subType, displayName, positionAndOrientation, null, color);
      if (bot != null)
      {
        grid = grid?.GetBiggestGridInGroup(); // TODO: This causes errors if a grid gets closed. Change to loop through grid groups
        var gridMap = AiSession.Instance.GetNewGraph(grid, bot.WorldAABB.Center, bot.WorldMatrix);
        bool needsName = string.IsNullOrWhiteSpace(displayName);

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
          if (faction != null)
          {
            if (!faction.AcceptHumans)
            {
              MyVisualScriptLogicProvider.SetPlayersFaction(bot.ControllerInfo.ControllingIdentityId, faction.Tag);
            }
            else if (AiSession.Instance.BotFactions.TryGetValue(faction.FactionId, out faction))
            {
              MyVisualScriptLogicProvider.SetPlayersFaction(bot.ControllerInfo.ControllingIdentityId, faction.Tag);
            }
          }
        }

        BotBase robot;
        bool runNeutral = false;
        if (!string.IsNullOrWhiteSpace(role) && role.ToUpperInvariant() == "NOMAD")
        {
          if (needsName)
          {
            bot.Name = GetUniqueName("NomadBot");
          }

          MyVisualScriptLogicProvider.SetPlayersFaction(bot.ControllerInfo.ControllingIdentityId, "NOMAD");
          robot = new NomadBot(bot, gridMap);
          runNeutral = true;
        }
        else
        {
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

              robot = new GrinderBot(bot, gridMap);
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
            case BotRoleEnemy.SOLDIER:
            default:
              if (needsName)
              {
                bot.Name = GetUniqueName("SoldierBot");
              }

              robot = new SoldierBot(bot, gridMap);
              break;
          }
        }

        AiSession.Instance.AddBot(robot);

        if (runNeutral)
          EnsureNuetrality();
      }

      return bot;
    }

    //static HashSet<long> _factionIdentities = new HashSet<long>();

    public static IMyCharacter CreateBotObject(string subType, string displayName, MyPositionAndOrientation positionAndOrientation, long? ownerId = null, Color? botColor = null)
    {
      try
      {
        if (AiSession.Instance?.Registered != true || !AiSession.Instance.CanSpawn)
          return null;

        var info = AiSession.Instance.GetBotIdentity();
        if (info == null)
        {
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
      BotRoleEnemy br;
      if (Enum.TryParse(role.ToUpperInvariant(), out br))
        return br;

      return BotRoleEnemy.SOLDIER;
    }

    public static BotRoleFriendly ParseFriendlyRole(string role)
    {
      BotRoleFriendly br;
      if (Enum.TryParse(role.ToUpperInvariant(), out br))
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
