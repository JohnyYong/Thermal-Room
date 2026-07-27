using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ExtinguisherFoam : MonoBehaviour
{
    private ParticleSystem _ps;
    private readonly HashSet<FireVFX> _hitThisFrame = new();

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();

        var trigger = _ps.trigger;
        while (trigger.colliderCount > 0)
            trigger.RemoveCollider(0);

        foreach (FireVFX fire in FindObjectsByType<FireVFX>(FindObjectsSortMode.None))
        {
            Collider col = fire.GetComponent<Collider>();
            if (col != null)
            {
                trigger.AddCollider(col);
                Debug.Log($"[Foam] Auto-registered: {fire.gameObject.name}");
            }
        }
    }

    void OnParticleTrigger()
    {
        List<ParticleSystem.Particle> particles = new();

        _ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, particles);
        NotifyHitFires(particles);

        _ps.GetTriggerParticles(ParticleSystemTriggerEventType.Inside, particles);
        NotifyHitFires(particles);
    }

    private void NotifyHitFires(List<ParticleSystem.Particle> particles)
    {
        if (particles.Count == 0) return;

        _hitThisFrame.Clear();
        var triggers = _ps.trigger;

        for (int t = 0; t < triggers.colliderCount; t++)
        {
            Collider col = triggers.GetCollider(t) as Collider;
            if (col == null) continue;

            for (int p = 0; p < particles.Count; p++)
            {
                //Now that simulation space is World, positions are already world space
                if (col.bounds.Contains(particles[p].position))
                {
                    FireVFX fire = col.GetComponentInParent<FireVFX>();
                    if (fire != null && _hitThisFrame.Add(fire))
                    {
                        Debug.Log($"[Foam] HIT: {fire.gameObject.name}");

                        // 1) Visual fire: shrink / destroy (unchanged).
                        fire.OnFoamContact();

                        //if (ThermalSimulation.Instance != null)
                        //    ThermalSimulation.Instance.ExtinguishAtWorldBounds(col);
                    }
                    break;
                }
            }
        }
    }
}