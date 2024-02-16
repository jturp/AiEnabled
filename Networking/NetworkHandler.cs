using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using System.Collections.Concurrent;
using AiEnabled.Support;
using AiEnabled.Utilities;

namespace AiEnabled.Networking
{
  public class NetworkHandler
  {
    public readonly ushort ChannelId;
    private List<IMyPlayer> _tempPlayers;
    internal AiSession SessionComp;

    /// <summary>
    /// <paramref name="channelId"/> must be unique from all other mods that also use network packets.
    /// </summary>
    public NetworkHandler(ushort channelId, AiSession sessionComp)
    {
      ChannelId = channelId;
      SessionComp = sessionComp;
    }
    /// <summary>
    /// Register packet monitoring, not necessary if you don't want the local machine to handle incomming packets.
    /// </summary>
    public void Register()
    {
      MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, ReceivedPacket);
    }

    /// <summary>
    /// This must be called on world unload if you called <see cref="Register"/>.
    /// </summary>
    public void Unregister()
    {
      MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, ReceivedPacket);
    }

    private void ReceivedPacket(ushort handlerId, byte[] rawData, ulong senderId, bool fromServer) // executed when a packet is received on this machine
    {
      try
      {
        var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(rawData);
        if (packet == null)
        {
          SessionComp?.Logger?.Log($"Networking.ReceivedPacket: Packet was null. IsServer = {MyAPIGateway.Multiplayer.IsServer}, IsDedi = {MyAPIGateway.Utilities.IsDedicated}", MessageType.WARNING);
          return;
        }

        HandlePacket(packet, rawData);
      }
      catch (Exception e)
      {
        SessionComp?.Logger?.Error($"Error in Networking.ReceivedPacket:\n{e}");

        if (MyAPIGateway.Session?.Player != null)
          MyAPIGateway.Utilities.ShowNotification($"[ERROR: {GetType().FullName}: {e.Message} | Send log to mod author]", 10000, MyFontEnum.Red);
      }
    }

    private void HandlePacket(PacketBase packet, byte[] rawData = null)
    {
      var relay = packet.Received(this);

      if (relay)
        RelayToClients(packet, rawData);
    }

    /// <summary>
    /// Send a packet to the server.
    /// Works from clients and server.
    /// </summary>
    public void SendToServer(PacketBase packet)
    {
      if (MyAPIGateway.Multiplayer.IsServer)
      {
        HandlePacket(packet);
        return;
      }

      var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);
      MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, bytes);
    }

    /// <summary>
    /// Send a packet to a specific player.
    /// Only works server side.
    /// </summary>
    public void SendToPlayer(PacketBase packet, ulong steamId)
    {
      if (packet.SenderId == steamId)
      {
        HandlePacket(packet);
        return;
      }

      if (!MyAPIGateway.Multiplayer.IsServer)
        return;

      var bytes = MyAPIGateway.Utilities.SerializeToBinary(packet);
      MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, bytes, steamId);
    }

    /// <summary>
    /// Sends packet (or supplied bytes) to all players except server player and supplied packet's sender.
    /// Only works server side.
    /// </summary>
    public void RelayToClients(PacketBase packet, byte[] rawData = null)
    {
      if (!MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive)
        return;

      try
      {
        if (rawData == null)
          rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);
      }
      catch(Exception ex)
      {
        AiSession.Instance.Logger.Error($"Exception in RelayToClients: Packet type = {packet?.GetType().FullName ?? "NULL"}\n{ex}");
        return;
      }

      if (_tempPlayers == null)
        _tempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
      else
        _tempPlayers.Clear();

      MyAPIGateway.Players.GetPlayers(_tempPlayers);

      foreach (var p in _tempPlayers)
      {
        if (p.IsBot)
          continue;

        if (p.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
          continue;

        if (p.SteamUserId == packet.SenderId)
          continue;

        if (string.IsNullOrWhiteSpace(p.DisplayName))
          continue;

        MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, rawData, p.SteamUserId);
      }

      _tempPlayers.Clear();
    }
  }
}
