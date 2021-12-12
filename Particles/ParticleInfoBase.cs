
using AiEnabled.Networking;

using Sandbox.ModAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage.Game;
using VRage.Game.ModAPI;

namespace AiEnabled.Particles
{
  public abstract class ParticleInfoBase
  {
    public enum ParticleType { Zombie, Ghost, Factory, Builder }

    public IMyCharacter Bot;
    public ParticleType Type;
    public List<MyParticleEffect> Effects = new List<MyParticleEffect>();

    public abstract void Set(IMyCharacter bot);
    public abstract void Update();

    public virtual void Stop()
    {
      if (Effects == null || Effects.Count == 0)
        return;

      for (int i = 0; i < Effects.Count; i++)
      {
        var particle = Effects[i];
        if (particle != null)
        {
          particle.Autodelete = true;
          particle.StopEmitting(1f);
          MyParticlesManager.RemoveParticleEffect(particle);
        }
      }

      Effects.Clear();
    }

    public virtual void Close()
    {
      Stop();
      Effects = null;
    }
  }
}
