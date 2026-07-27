using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum FlammableMaterial
{
    NonFlammable,
    Wood,
    Oil,
    Cloth
}

public class FlammableSurface : MonoBehaviour
{
    [Header("Material")]
    [SerializeField] private FlammableMaterial materialType = FlammableMaterial.Wood;

    [Header("Fire Prefab")]
    [SerializeField] private GameObject fireVFXPrefab;

    [Header("Spread Settings")]
    [SerializeField] private float spreadRadius = 1.5f;
    [SerializeField] private LayerMask flammableLayer;

    [Header("Burn Behaviour")]
    [SerializeField] private bool burnsOut = true;
    [SerializeField] private float minBurnDuration = 8f;
    [SerializeField] private float maxBurnDuration = 15f;

    [Header("Debug")]
    [SerializeField] private bool isBurning = false;
    [SerializeField] private bool hasBurned = false;

    private GameObject _activeFireInstance;

    private float SpreadDelay
    {
        get
        {
            switch (materialType)
            {
                case FlammableMaterial.Oil: return Random.Range(0.3f, 0.8f);
                case FlammableMaterial.Wood: return Random.Range(2.5f, 5f);
                case FlammableMaterial.Cloth: return Random.Range(1.5f, 4f);

                default: return Mathf.Infinity; // never spreads
            }
        }
    }

    public bool IsBurning => isBurning;
    public bool HasBurned => hasBurned;

    public void Ignite()
    {
        if (materialType == FlammableMaterial.NonFlammable) return;
        if (isBurning || hasBurned) return;

        isBurning = true;

        if (fireVFXPrefab)
        {
            _activeFireInstance = Instantiate(fireVFXPrefab, transform.position, Quaternion.identity, transform);

            if (_activeFireInstance.TryGetComponent<FireVFXFade>(out var fade))
            {
                fade.FadeIn();
            }
        }

        StartCoroutine(SpreadRoutine());

        if (burnsOut)
        {
            StartCoroutine(BurnOutRoutine());
        }
    }

    public void Extinguish()
    {
        StopAllCoroutines();
        isBurning = false;

        if (_activeFireInstance)
        {
            if (_activeFireInstance.TryGetComponent<FireVFXFade>(out var fade))
            {
                var instanceToDestroy = _activeFireInstance;
                fade.FadeOut(() => Destroy(instanceToDestroy));
                _activeFireInstance = null; // clear reference immediately so reignite works right away
            }
            else
            {
                Destroy(_activeFireInstance);
                _activeFireInstance = null;
            }
        }
    }

    private IEnumerator SpreadRoutine()
    {
        yield return new WaitForSeconds(SpreadDelay);

        Collider[] hits = Physics.OverlapSphere(transform.position, spreadRadius, flammableLayer);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<FlammableSurface>(out var neighbor))
            {
                if (neighbor != this && !neighbor.IsBurning && !neighbor.HasBurned)
                {
                    neighbor.Ignite();
                }
            }
        }
    }

    private IEnumerator BurnOutRoutine()
    {
        float duration = Random.Range(minBurnDuration, maxBurnDuration);
        yield return new WaitForSeconds(duration);

        Extinguish();
        hasBurned = true; 
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = materialType == FlammableMaterial.Oil ? Color.yellow :
                       materialType == FlammableMaterial.Wood ? new Color(0.6f, 0.3f, 0.1f) : Color.gray;
        Gizmos.DrawWireSphere(transform.position, spreadRadius);
    }
}