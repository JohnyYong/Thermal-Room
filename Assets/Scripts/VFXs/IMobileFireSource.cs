using UnityEngine;

public interface IMobileFireSource
{
    Vector3 WorldPosition { get; }

    float HeatRadius { get; }
    float HeatPower { get; }

    float RadiationRange { get; }
    float RadiationPower { get; }
}
