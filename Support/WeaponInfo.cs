using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

using System;
using System.Collections.Generic;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

using VRageMath;

namespace AiEnabled.Support
{
  public class WeaponInfo
  {
    public IMyCharacter Bot;
    public IMyEntity Target;
    public List<float> Randoms;
    public float Damage;
    public float ShotDeviationAngleTan;
    public int Ticks;
    public bool Finished;
    public bool IsGrinder;
    public bool IsWelder;
    public bool LeadTargets;
    internal int _maxTicks;
    internal int _ticksBetweenProjectiles;
    internal int _ammoRemaining;

    public void Set(IMyCharacter bot, IMyEntity tgt, float damage, float angleDeviationTan, List<float> rand, int ticksBetween, int ammoLeft, bool isGrinder, bool isWelder, bool leadTargets)
    {
      Bot = bot;
      Target = tgt;
      Damage = damage;
      ShotDeviationAngleTan = angleDeviationTan;
      Randoms = rand;
      Ticks = 0;
      Finished = false;
      IsGrinder = isGrinder;
      IsWelder = isWelder;
      LeadTargets = leadTargets;
      _ticksBetweenProjectiles = ticksBetween;
      _ammoRemaining = ammoLeft;
      _maxTicks = isGrinder || isWelder ? 60 : ticksBetween * 10;
    }

    public float GetRandom()
    {
      float rand = 0;

      if (Randoms?.Count > 0)
      {
        var last = Randoms.Count - 1;
        rand = Randoms[last];
        Randoms.RemoveAtFast(last);
      }

      return rand;
    }

    public bool Update()
    {
      if (Bot == null || (!IsWelder && Target == null))
      {
        Finished = true;
        return false;
      }

      var ch = Target as IMyCharacter;
      if (ch != null && ch.IsDead)
      {
        Finished = true;
        return false;
      }

      ++Ticks;
      if (Ticks > _maxTicks)
        Finished = true;

      if (IsGrinder || IsWelder)
        return true;

      var isServer = MyAPIGateway.Multiplayer.IsServer;
      var gun = Bot.EquippedTool as IMyHandheldGunObject<MyGunBase>;

      if (gun == null || (isServer ? gun.GunBase.CurrentAmmo <= 0 : _ammoRemaining <= 0))
      {
        Finished = true;
      }
      else if (Ticks % _ticksBetweenProjectiles == 0)
      {
        if (isServer)
          gun.GunBase.ConsumeAmmo();

        _ammoRemaining--;
        return true;
      }

      return false;
    }

    public void Clear()
    {
      Bot = null;
      Target = null;
    }
  }
}
