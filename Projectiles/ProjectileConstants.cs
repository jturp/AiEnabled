using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Utils;

namespace AiEnabled.Projectiles
{
  public static class ProjectileConstants
  {
    public static Dictionary<MyStringHash, string> HitMaterialToEffect = new Dictionary<MyStringHash, string>(MyStringHash.Comparer)
    {
      { MyMaterialType.CHARACTER, MyParticleEffectsNameEnum.MaterialHit_Character },
      { MyMaterialType.METAL, MyParticleEffectsNameEnum.MaterialHit_Metal },
      { MyMaterialType.ROCK, MyParticleEffectsNameEnum.MaterialHit_Rock },
      { MyMaterialType.MISSILE, MyParticleEffectsNameEnum.MaterialHit_Metal },
      { MyMaterialType.THRUSTER_LARGE, MyParticleEffectsNameEnum.MaterialHit_Metal },
      { MyMaterialType.THRUSTER_SMALL, MyParticleEffectsNameEnum.MaterialHit_Metal },
      { MyMaterialType.WOOD, MyParticleEffectsNameEnum.MaterialHit_Wood },
      { MyMaterialType.AMMO, MyParticleEffectsNameEnum.MaterialHit_Metal },
      { MyStringHash.GetOrCompute("CharacterFemale"), MyParticleEffectsNameEnum.MaterialHit_Character },
      { MyStringHash.GetOrCompute("Wheel"), MyParticleEffectsNameEnum.MaterialHit_Metal },
      { MyStringHash.GetOrCompute("Wolf"), MyParticleEffectsNameEnum.MaterialHit_Character },
      { MyStringHash.GetOrCompute("Spider"), MyParticleEffectsNameEnum.Blood_Spider },
      { MyStringHash.GetOrCompute("GlassOpaque"), MyParticleEffectsNameEnum.MaterialHit_Glass },
      { MyStringHash.GetOrCompute("Glass"), MyParticleEffectsNameEnum.MaterialHit_Glass },
      { MyStringHash.GetOrCompute("Snow"), MyParticleEffectsNameEnum.MaterialHit_Snow },
      { MyStringHash.GetOrCompute("Ice"), MyParticleEffectsNameEnum.MaterialHit_Ice },
      { MyStringHash.GetOrCompute("MoonSoil"), MyParticleEffectsNameEnum.MaterialHit_Soil },
      { MyStringHash.GetOrCompute("Sand"), MyParticleEffectsNameEnum.MaterialHit_Sand },
      { MyStringHash.GetOrCompute("MarsSoil"), MyParticleEffectsNameEnum.MaterialHit_Soil },
      { MyStringHash.GetOrCompute("Grass"), MyParticleEffectsNameEnum.MaterialHit_GrassGreen },
      { MyStringHash.GetOrCompute("Grass bare"), MyParticleEffectsNameEnum.MaterialHit_GrassYellow },
      { MyStringHash.GetOrCompute("GrassDry"), MyParticleEffectsNameEnum.MaterialHit_GrassYellow },
      { MyStringHash.GetOrCompute("Soil"), MyParticleEffectsNameEnum.MaterialHit_Soil },
      { MyStringHash.GetOrCompute("Soildry"), MyParticleEffectsNameEnum.MaterialHit_Soil },
      { MyStringHash.GetOrCompute("Stone"), MyParticleEffectsNameEnum.MaterialHit_Rock },
      { MyStringHash.GetOrCompute("AlienGreenGrass"), MyParticleEffectsNameEnum.MaterialHit_GrassGreen },
      { MyStringHash.GetOrCompute("OrangeAlienGrass"), MyParticleEffectsNameEnum.MaterialHit_GrassOrange },
      { MyStringHash.GetOrCompute("AlienYellowGrass"), MyParticleEffectsNameEnum.MaterialHit_GrassYellow },
    };

    public static Dictionary<MyStringHash, string> HitMaterialToSound = new Dictionary<MyStringHash, string>(MyStringHash.Comparer)
    {
      { MyMaterialType.CHARACTER, "WepPlayRifleImpPlay" },
      { MyMaterialType.METAL, "WepPlayRifleImpMetal" },
      { MyMaterialType.ROCK, "WepPlayRifleImpRock" },
      { MyMaterialType.MISSILE, "WepPlayRifleImpMetal" },
      { MyMaterialType.THRUSTER_LARGE, "WepPlayRifleImpMetal" },
      { MyMaterialType.THRUSTER_SMALL, "WepPlayRifleImpMetal" },
      { MyMaterialType.WOOD, "WepPlayRifleImpWood" },
      { MyMaterialType.AMMO, "WepPlayRifleImpMetal" },
      { MyStringHash.GetOrCompute("CharacterFemale"), "WepPlayRifleImpPlay" },
      { MyStringHash.GetOrCompute("Wheel"), "WepPlayRifleImpMetal" },
      { MyStringHash.GetOrCompute("Wolf"), "WepPlayRifleImpPlay" },
      { MyStringHash.GetOrCompute("Spider"), "WepPlayRifleImpPlay" },
      { MyStringHash.GetOrCompute("Glass"), "WepPlayRifleImpGlass" },
      { MyStringHash.GetOrCompute("Snow"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Ice"), "WepPlayRifleImpRock" },
      { MyStringHash.GetOrCompute("MoonSoil"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Sand"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("MarsSoil"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Grass"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Grass bare"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("GrassDry"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Soil"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Soildry"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("Stone"), "WepPlayRifleImpRock" },
      { MyStringHash.GetOrCompute("AlienGreenGrass"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("OrangeAlienGrass"), "WepPlayRifleImpSand" },
      { MyStringHash.GetOrCompute("AlienYellowGrass"), "WepPlayRifleImpSand" },
    };

    public static MyStringId ProjectileTrailLine = MyStringId.GetOrCompute("ProjectileTrailLine");
    public static MyStringHash BlockSkin_Wood = MyStringHash.GetOrCompute("Wood_Armor");
    public static MyStringHash BlockSkin_Concrete = MyStringHash.GetOrCompute("Concrete_Armor");
    public static MyStringHash Material_Wood = MyStringHash.GetOrCompute("MaterialHit_Wood");
    public static MyStringHash Material_Rock = MyStringHash.GetOrCompute("MaterialHit_Rock");
    public static MyStringHash DecalSource_Rifle = MyStringHash.GetOrCompute("RifleBullet");
    public static MyStringHash ShieldHitSound_Projectile = MyStringHash.GetOrCompute("WepPlayRifleImpGlass");
    public static MyStringHash ShieldHitSound_Missile = MyStringHash.GetOrCompute("WepSmallMissileExpl");
    public static MyStringHash ShieldHash = MyStringHash.GetOrCompute("DefenseShield");

    public static void Close()
    {
      HitMaterialToEffect?.Clear();
      HitMaterialToSound?.Clear();

      HitMaterialToEffect = null;
      HitMaterialToSound = null;
    }
  }
}
