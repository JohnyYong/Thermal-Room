using UnityEngine;

// Anything that acts as a moving/dynamic heat source implements this --
// a thrown fireball, a torch, anything spawned at runtime. Reported to
// ThermalSimulation every FixedUpdate so heat genuinely tracks the
// source's CURRENT position, unlike RegisterRuntimeBurningObject (which
// ignites a fixed set of voxels once, at whatever position the object
// was at that instant, and never moves).
public interface IMobileFireSource
{
    // Sampled fresh every tick -- just return transform.position.
    Vector3 WorldPosition { get; }

    // HeatStep: falloff radius/strength for heating the surrounding air.
    float HeatRadius { get; }
    float HeatPower { get; }

    // RadiationStep: falloff range/strength for radiantly heating nearby
    // solids (this is what can eventually ignite something flammable
    // this source lingers near, via CombustionStep's own temperature
    // check -- no special-case ignition code needed here).
    float RadiationRange { get; }
    float RadiationPower { get; }
}
