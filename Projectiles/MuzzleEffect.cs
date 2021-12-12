using Sandbox.Game.Weapons;

using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace AiEnabled.Projectiles
{
  internal class MuzzleEffect
  {
    public int Duration;
    MyGunBase _gun;
    IMyEntity _tool;

    public void Start(MyGunBase gun, IMyCharacter bot)
    {
      Duration = gun.MuzzleFlashLifeSpan;
      gun.CreateEffects(Sandbox.Definitions.MyWeaponDefinition.WeaponEffectAction.Shoot);

      _gun = gun;
      _tool = bot.EquippedTool;
      _tool.OnMarkForClose += EquippedTool_OnMarkForClose;
    }

    private void EquippedTool_OnMarkForClose(IMyEntity obj)
    {
      obj.OnMarkForClose -= EquippedTool_OnMarkForClose;
      Stop();
    }

    public bool Update()
    {
      if (_gun == null)
        return false;

      _gun.UpdateEffects();

      Duration--;
      return Duration > 0;
    }

    public void Stop()
    {
      if (_tool != null)
        _tool.OnMarkForClose -= EquippedTool_OnMarkForClose;

      if (_gun != null)
        _gun.RemoveOldEffects();
    }
  }
}
