using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ExtinguisherFoam : MonoBehaviour
{
    [SerializeField] private int _particlesForFullCoverage = 20;

    private ParticleSystem _ps;
    private readonly List<ParticleSystem.Particle> _particles = new();
    private int _registeredCount = -1;

    private void Awake() => _ps = GetComponent<ParticleSystem>();

    // Registering in Awake could only ever see fires that existed at load.
    // Fires the player places at runtime appear later, so the collider list
    // is rebuilt whenever the live registry changes size.
    private void RefreshColliders()
    {
        if (_registeredCount == FireExtinguishable.Active.Count) return;

        var trigger = _ps.trigger;
        while (trigger.colliderCount > 0) trigger.RemoveCollider(0);

        for (int i = 0; i < FireExtinguishable.Active.Count; i++)
        {
            var fire = FireExtinguishable.Active[i];
            if (fire == null) continue;

            Collider col = fire.GetComponent<Collider>();
            if (col != null) trigger.AddCollider(col);
        }

        _registeredCount = FireExtinguishable.Active.Count;
    }

    private void Update() => RefreshColliders();

    private void OnParticleTrigger()
    {
        _ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, _particles);
        Suppress(_particles);

        _ps.GetTriggerParticles(ParticleSystemTriggerEventType.Inside, _particles);
        Suppress(_particles);
    }

    private void Suppress(List<ParticleSystem.Particle> particles)
    {
        if (particles.Count == 0) return;

        var triggers = _ps.trigger;

        for (int t = 0; t < triggers.colliderCount; t++)
        {
            Collider col = triggers.GetCollider(t) as Collider;
            if (col == null) continue;

            var fire = col.GetComponentInParent<FireExtinguishable>();
            if (fire == null || fire.IsOut) continue;

            // Count every particle inside rather than breaking on the first.
            // The count IS the aim feedback -- a direct hit lands far more
            // particles than a glancing one and so puts the fire out faster.
            int inside = 0;
            for (int p = 0; p < particles.Count; p++)
            {
                if (col.bounds.Contains(particles[p].position)) inside++;
            }

            if (inside == 0) continue;

            float coverage = Mathf.Clamp01(
                inside / (float)Mathf.Max(_particlesForFullCoverage, 1));

            fire.ApplyFoam(coverage);
        }
    }
}