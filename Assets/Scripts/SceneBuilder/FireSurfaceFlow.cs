using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class FireSurfaceFlow : MonoBehaviour
{
    [Header("Surface Flow")]
    [SerializeField] private float _surfaceFlowStrength = 4f;

    [SerializeField] private float _upwardBias = 2f;

    [SerializeField] private float _surfaceStickiness = 0.5f;

    [SerializeField] private LayerMask _surfaceLayers;

    private ParticleSystem _ps;

    private List<ParticleCollisionEvent> _collisionEvents =
        new List<ParticleCollisionEvent>();

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    private void OnParticleCollision(GameObject other)
    {
        int count =
            _ps.GetCollisionEvents(other, _collisionEvents);

        for (int i = 0; i < count; i++)
        {
            ParticleCollisionEvent collision =
                _collisionEvents[i];

            Vector3 normal =
                collision.normal;

            Vector3 velocity =
                collision.velocity;

            // Remove velocity pushing INTO wall
            Vector3 tangentVelocity =
                Vector3.ProjectOnPlane(
                    velocity,
                    normal);

            // Add upward movement
            tangentVelocity +=
                Vector3.up * _upwardBias;

            // Normalize and scale
            tangentVelocity =
                tangentVelocity.normalized *
                _surfaceFlowStrength;

            // Blend with original velocity
            Vector3 finalVelocity =
                Vector3.Lerp(
                    velocity,
                    tangentVelocity,
                    _surfaceStickiness);

            // Apply force to nearby particles
            ApplyVelocityToNearbyParticles(
                collision.intersection,
                finalVelocity);
        }
    }

    private void ApplyVelocityToNearbyParticles(
        Vector3 point,
        Vector3 velocity)
    {
        ParticleSystem.Particle[] particles =
            new ParticleSystem.Particle[
                _ps.particleCount];

        int count =
            _ps.GetParticles(particles);

        for (int i = 0; i < count; i++)
        {
            float dist =
                Vector3.Distance(
                    particles[i].position,
                    point);

            if (dist < 0.5f)
            {
                particles[i].velocity =
                    Vector3.Lerp(
                        particles[i].velocity,
                        velocity,
                        0.5f);
            }
        }

        _ps.SetParticles(particles, count);
    }
}