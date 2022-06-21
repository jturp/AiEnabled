using AiEnabled.Networking;

using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace AiEnabled.Bots.Behaviors
{
  public abstract class BotBehavior
  {
    public List<string> Phrases = new List<string>();
    public List<string> Taunts = new List<string>();
    public List<string> Songs = new List<string>();
    public List<string> Actions = new List<string>();
    public List<string> PainSounds = new List<string>();
    public BotBase Bot;

    public string LastAction, LastPhrase, LastSong;
    int _painTimer;

    public BotBehavior(BotBase bot)
    {
      Bot = bot;
    }

    public void Update()
    {
      ++_painTimer;
    }

    public virtual void Speak(string words = null)
    {
      try
      {
        if (Bot?.Character == null || Bot.Character.IsDead || Bot.Character.MarkedForClose)
          return;

        if (string.IsNullOrWhiteSpace(words))
        {
          bool taunt = Bot.Target.Entity is IMyEntity && !Bot.Target.IsFriendly();

          if (taunt)
          {
            if (Taunts.Count == 0)
              return;

            var rand = MyUtils.GetRandomInt(0, Taunts.Count);
            words = Taunts[rand];
          }
          else if (Phrases.Count > 0 || Songs.Count > 0)
          {
            var max = Phrases.Count + Songs.Count;
            var rand = MyUtils.GetRandomInt(0, max);

            if (rand > Phrases.Count - 1)
            {
              if (AiSession.Instance.AllowMusic)
                Sing();

              return;
            }

            words = Phrases[rand];
          }
          else
            return;
        }

        LastPhrase = words;
        var botEntityId = Bot.Character.EntityId;

        if (MyAPIGateway.Utilities.IsDedicated)
        {
          var packet = new SoundPacket(words, botEntityId, includeIcon: true);
          AiSession.Instance.Network.RelayToClients(packet);
        }
        else
        {
          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new SoundPacket(words, botEntityId, includeIcon: true);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.PlaySoundForEntity(botEntityId, words, false, true);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotBehavior.Speak: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }
    }

    public virtual void Sing(string song = null)
    {
      try
      {
        if (Bot?.Character == null || Bot.Character.IsDead || Bot.Character.MarkedForClose)
          return;

        if (string.IsNullOrWhiteSpace(song))
        {
          if (Songs.Count == 0)
            return;

          var rand = MyUtils.GetRandomInt(0, Songs.Count);
          song = Songs[rand];
        }

        LastSong = song;
        var botEntityId = Bot.Character.EntityId;

        if (MyAPIGateway.Utilities.IsDedicated)
        {
          var packet = new SoundPacket(song, botEntityId);
          AiSession.Instance.Network.RelayToClients(packet);
        }
        else
        {
          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new SoundPacket(song, botEntityId);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          MySoundPair sp;
          if (!AiSession.Instance.SoundPairDict.TryGetValue(song, out sp))
          {
            sp = new MySoundPair(song);
            AiSession.Instance.SoundPairDict[song] = sp;
          }

          var soundComp = Bot.Character.Components?.Get<MyCharacterSoundComponent>();
          soundComp?.PlayActionSound(sp);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotBehavior.Sing: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }
    }

    public virtual void Perform(string action = null)
    {
      try
      {
        if (Bot?.Character == null || Bot.Character.IsDead || Bot.Character.MarkedForClose)
          return;

        if (string.IsNullOrWhiteSpace(action))
        {
          if (Actions.Count == 0)
            return;

          var rand = MyUtils.GetRandomInt(0, Actions.Count);
          action = Actions[rand];
        }

        LastAction = action;
        var character = Bot.Character;

        var weapon = character.EquippedTool as IMyGunObject<MyDeviceBase>;
        var controlEnt = Bot as Sandbox.Game.Entities.IMyControllableEntity;
        if (weapon != null)
          controlEnt.SwitchToWeapon(null);

        character.TriggerCharacterAnimationEvent("emote", true);
        character.TriggerCharacterAnimationEvent(action, true);

        if (weapon != null)
          controlEnt.SwitchToWeapon(weapon.DefinitionId);
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotBehavior.Perform: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }
    }

    public virtual void ApplyPain(string sound = null)
    {
      try
      {
        if (Bot?.Character == null || Bot.Character.IsDead || Bot.Character.MarkedForClose)
          return;

        if (_painTimer < 100)
          return;

        _painTimer = 0;

        if (string.IsNullOrWhiteSpace(sound))
        {
          if (PainSounds.Count == 0)
            return;

          var rand = MyUtils.GetRandomInt(0, PainSounds.Count);
          sound = PainSounds[rand];
        }

        var botEntityId = Bot.Character.EntityId;

        if (MyAPIGateway.Utilities.IsDedicated)
        {
          var packet = new SoundPacket(sound, botEntityId, includeIcon: true);
          AiSession.Instance.Network.RelayToClients(packet);
        }
        else
        {
          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new SoundPacket(sound, botEntityId, includeIcon: true);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.PlaySoundForEntity(botEntityId, sound, false, true);
        }
      }
      catch (Exception ex)
      {
        AiSession.Instance.Logger.Log($"Exception in BotBehavior.ApplyPain: {ex.Message}\n{ex.StackTrace}", Utilities.MessageType.ERROR);
      }
    }

    public virtual void Close()
    {
      Phrases?.Clear();
      Taunts?.Clear();
      Actions?.Clear();
      Songs?.Clear();
      PainSounds?.Clear();

      Phrases = null;
      Taunts = null;
      Actions = null;
      Songs = null;
      PainSounds = null;
    }
  }
}
