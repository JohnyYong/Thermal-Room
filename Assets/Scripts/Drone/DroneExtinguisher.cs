using UnityEngine;

//Extra notes: This will be optional amongst various simulations, maybe need to add a boolean or a safeguard to make this not crash the program in anypoint of time

//For attaching to the Extinguisher, mainly to have capacity
namespace DroneSystem
{
    public class DroneExtinguisher : MonoBehaviour
    {
        [Header("Capacity")]
        public float maxCapacity = 100f;
        public float drainRate = 1f;       //units per second while spraying
        public float currentCapacity;

        public bool IsEmpty => currentCapacity <= 0f;
        public float CapacityPercent => currentCapacity / maxCapacity;

        void Start()
        {
            currentCapacity = maxCapacity;
        }

        public bool DrainCapacity()
        {
            if (IsEmpty) return false;

            currentCapacity -= drainRate * Time.deltaTime;
            currentCapacity = Mathf.Max(currentCapacity, 0f);
            return true;
        }
    }
}