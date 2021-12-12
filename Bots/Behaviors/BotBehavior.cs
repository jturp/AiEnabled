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
using VRage.Utils;

namespace AiEnabled.Bots.Behaviors
{
  public abstract class BotBehavior
  {
    public List<string> Phrases = new List<string>();
    public List<string> Songs = new List<string>();
    public List<string> Actions = new List<string>();
    public List<string> PainSounds = new List<string>();
    public IMyCharacter Bot;

    public string LastAction, LastPhrase, LastSong;
    int _painTimer;

    public BotBehavior(IMyCharacter bot)
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
        if (string.IsNullOrWhiteSpace(words))
        {
          if (Phrases.Count == 0 && Songs.Count == 0)
            return;

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

        LastPhrase = words;

        if (MyAPIGateway.Utilities.IsDedicated)
        {
          var packet = new SoundPacket(words, Bot.EntityId, includeIcon: true);
          AiSession.Instance.Network.RelayToClients(packet);
        }
        else
        {
          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new SoundPacket(words, Bot.EntityId, includeIcon: true);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.PlaySoundForEntity(Bot.EntityId, words, false, true);
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
        if (string.IsNullOrWhiteSpace(song))
        {
          if (Songs.Count == 0)
            return;

          var rand = MyUtils.GetRandomInt(0, Songs.Count);
          song = Songs[rand];
        }

        LastSong = song;

        if (MyAPIGateway.Utilities.IsDedicated)
        {
          var packet = new SoundPacket(song, Bot.EntityId);
          AiSession.Instance.Network.RelayToClients(packet);
        }
        else
        {
          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new SoundPacket(song, Bot.EntityId);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          MySoundPair sp;
          if (!AiSession.Instance.SoundPairDict.TryGetValue(song, out sp))
          {
            sp = new MySoundPair(song);
            AiSession.Instance.SoundPairDict[song] = sp;
          }

          var soundComp = Bot.Components?.Get<MyCharacterSoundComponent>();
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
        if (string.IsNullOrWhiteSpace(action))
        {
          if (Actions.Count == 0)
            return;

          var rand = MyUtils.GetRandomInt(0, Actions.Count);
          action = Actions[rand];
        }

        LastAction = action;

        var weapon = Bot.EquippedTool as IMyGunObject<MyDeviceBase>;
        var controlEnt = Bot as Sandbox.Game.Entities.IMyControllableEntity;
        if (weapon != null)
          controlEnt.SwitchToWeapon(null);

        Bot.TriggerCharacterAnimationEvent("emote", true);
        Bot.TriggerCharacterAnimationEvent(action, true);

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

        if (MyAPIGateway.Utilities.IsDedicated)
        {
          var packet = new SoundPacket(sound, Bot.EntityId, includeIcon: true);
          AiSession.Instance.Network.RelayToClients(packet);
        }
        else
        {
          if (MyAPIGateway.Multiplayer.MultiplayerActive)
          {
            var packet = new SoundPacket(sound, Bot.EntityId, includeIcon: true);
            AiSession.Instance.Network.RelayToClients(packet);
          }

          AiSession.Instance.PlaySoundForEntity(Bot.EntityId, sound, false, true);
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
      Actions?.Clear();
      Songs?.Clear();
      PainSounds?.Clear();

      Phrases = null;
      Actions = null;
      Songs = null;
      PainSounds = null;
    }
  }
}
