using System.Collections.Generic;
using UnityEngine;

public class ScorchDecalSpawner : MonoBehaviour
{
    [Header("Scorch Prefab")]
    [SerializeField] private GameObject _scorchPrefab;

    [Header("Materials")]
    [SerializeField] private Material _baseScorchMaterial;

    [Tooltip("Optional random burn textures.")]
    [SerializeField] private Texture2D[] _burnTextures;

    [Header("Scorch Settings")]
    [SerializeField] private float _decalSize = 0.15f;

    [Tooltip("Push decal slightly off the wall.")]
    [SerializeField] private float _surfaceOffset = 0.002f;

    [Header("Performance")]
    [SerializeField] private float _spawnCooldown = 0.05f;

    [Tooltip("Maximum scorch marks alive at once.")]
    [SerializeField] private int _maxDecals = 30;

    [Tooltip("Minimum spacing between scorch marks.")]
    [SerializeField] private float _minSpawnDistance = 0.05f;

    [Header("Surface Detection")]
    [SerializeField] private LayerMask _surfaceLayers = Physics.DefaultRaycastLayers;

    [SerializeField] private float _raycastDistance = 1f;

    [Header("Randomization")]
    [SerializeField] private Vector2 _sizeVariation = new(0.8f, 1.3f);

    private ParticleSystem _particleSystem;

    private readonly List<ParticleCollisionEvent> _collisionEvents = new();

    private readonly Queue<GameObject> _activeDecals = new();

    private float _lastSpawnTime = -999f;

    // Prevent z-fighting between overlapping decals
    private int _spawnIndex = 0;

    private void Awake()
    {
        _particleSystem = GetComponent<ParticleSystem>();

        if (_particleSystem == null)
        {
            Debug.LogError("[ScorchDecalSpawner] Missing ParticleSystem.");
            enabled = false;
            return;
        }

        // Auto-create fallback prefab
        if (_scorchPrefab == null)
        {
            _scorchPrefab = CreateRuntimeQuadPrefab();
        }
    }

    private void OnParticleCollision(GameObject other)
    {
        if (Time.time - _lastSpawnTime < _spawnCooldown)
            return;

        if (other.GetComponent<ParticleSystem>() != null)
            return;

        int collisionCount =
            _particleSystem.GetCollisionEvents(other, _collisionEvents);

        for (int i = 0; i < collisionCount; i++)
        {
            ParticleCollisionEvent collision =
                _collisionEvents[i];

            Vector3 hitPosition =
                collision.intersection;

            Vector3 velocityDirection =
                collision.velocity.normalized;

            Vector3 rayOrigin =
                hitPosition - velocityDirection * 0.1f;

            if (Physics.Raycast(
                    rayOrigin,
                    velocityDirection,
                    out RaycastHit hit,
                    _raycastDistance,
                    _surfaceLayers))
            {
                if (IsTooClose(hit.point))
                    continue;

                SpawnScorch(
                    hit.point,
                    hit.normal,
                    hit.transform);
            }

            _lastSpawnTime = Time.time;
            break;
        }
    }

    private void SpawnScorch(
        Vector3 position,
        Vector3 normal,
        Transform parent)
    {
        // Tiny offset stacking to avoid z-fighting
        float layeredOffset =
            _spawnIndex * 0.0001f;

        Vector3 spawnPosition =
            position + normal * (_surfaceOffset + layeredOffset);

        // Align flush to wall
        Quaternion rotation =
            Quaternion.FromToRotation(
                Vector3.forward,
                normal);

        // Quad correction
        rotation *= Quaternion.Euler(90f, 0f, 0f);

        GameObject scorch =
            Instantiate(
                _scorchPrefab,
                spawnPosition,
                rotation);

        scorch.name = "ScorchMark";

        scorch.transform.SetParent(parent, true);

        // Random rotation around surface normal
        scorch.transform.Rotate(
            Vector3.forward,
            Random.Range(0f, 360f),
            Space.Self);

        // Random scale
        float scale =
            _decalSize *
            Random.Range(
                _sizeVariation.x,
                _sizeVariation.y);

        //scorch.theDrone.transform.localScale =
        //    Vector3.one * scale;

        MeshRenderer renderer =
            scorch.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            renderer.receiveShadows = false;

            // Create UNIQUE material instance
            Material instanceMat =
                new Material(_baseScorchMaterial);

            // Random burn texture
            if (_burnTextures != null &&
                _burnTextures.Length > 0)
            {
                Texture2D tex =
                    _burnTextures[
                        Random.Range(0, _burnTextures.Length)];

                instanceMat.mainTexture = tex;
            }

            renderer.material = instanceMat;
        }

        _activeDecals.Enqueue(scorch);

        CleanupOldDecals();

        _spawnIndex++;
    }

    private void CleanupOldDecals()
    {
        while (_activeDecals.Count > _maxDecals)
        {
            GameObject oldest =
                _activeDecals.Dequeue();

            if (oldest != null)
            {
                Destroy(oldest);
            }
        }
    }

    private bool IsTooClose(Vector3 position)
    {
        foreach (GameObject decal in _activeDecals)
        {
            if (decal == null)
                continue;

            float distance =
                Vector3.Distance(
                    decal.transform.position,
                    position);

            if (distance < _minSpawnDistance)
            {
                return true;
            }
        }

        return false;
    }

    private GameObject CreateRuntimeQuadPrefab()
    {
        GameObject quad =
            GameObject.CreatePrimitive(
                PrimitiveType.Quad);

        quad.name = "RuntimeScorchQuad";

        Collider col =
            quad.GetComponent<Collider>();

        if (col != null)
        {
            Destroy(col);
        }

        MeshRenderer renderer =
            quad.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            renderer.receiveShadows = false;

            if (_baseScorchMaterial != null)
            {
                renderer.material =
                    _baseScorchMaterial;
            }
        }

        quad.SetActive(false);

        return quad;
    }
}