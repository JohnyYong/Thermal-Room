using UnityEditor.ShaderKeywordFilter;
using UnityEngine;

public class FireSmokeEmissionController : MonoBehaviour
{
    [SerializeField] private SmokeSim simulationManager;

    private SmokeSim.Emitter[] existingEmitters;

    [SerializeField] private float maxFireEmission = 1f;
    [SerializeField] private float maxSmokeEmission = 3f;

    //Both begin at 0
    [SerializeField] private float startingFireEmission = 0f;
    [SerializeField] private float startingSmokeEmission = 0f;

    [Tooltip("Shared emission rate multiplier that will multiply both smoke and fire's emission")]
    [SerializeField] private float sharedMultiplier = 0.5f;

    [Tooltip("The multiplier which controls smoke accumulation")]
    [SerializeField] private float smokeDecayMultiplier = 0.05f;
    [SerializeField] private float maxSmokeDecay = -1.5f;

    [SerializeField] private bool undergoingSimulation = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        existingEmitters = simulationManager.emitters;
        if (existingEmitters == null) {
            Debug.Log("[FireSmokeEmissionController]Doesn't seem to detect any existing emitters.");
        }
        simulationManager.decayRate = startingSmokeEmission;
    }

    // Update is called once per frame
    void Update()
    {
        if (undergoingSimulation)
        {
            for (int i = 0; i < existingEmitters.Length; i++)
            {

                if (existingEmitters[i].smokeRate < maxSmokeEmission)
                {
                    existingEmitters[i].smokeRate += 1 * sharedMultiplier;
                }

                if (existingEmitters[i].fireRate < maxFireEmission)
                {
                    existingEmitters[i].fireRate += 1 * sharedMultiplier;
                }
            }

            if (simulationManager.decayRate > maxSmokeDecay)
            {
                simulationManager.decayRate -= 1 * smokeDecayMultiplier;
            }
        }

        if (Input.GetKeyDown(KeyCode.F1))
        {
            StartSimulation();
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            StopAndReset();
        }
    }

    public void StopAndReset()
    {
        undergoingSimulation = false;
        simulationManager.decayRate = 0;

        for (int i = 0; i < existingEmitters.Length; i++)
        {
            existingEmitters[i].smokeRate = startingSmokeEmission; // was startingFireEmission
            existingEmitters[i].fireRate = startingFireEmission;
        }

        if (ThermalSimulation.Instance != null)
            ThermalSimulation.Instance.ResetSimulation();
    }

    public void StartSimulation()
    {
        undergoingSimulation = true;
    }
}
