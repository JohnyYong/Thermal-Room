using UnityEngine;
using UnityEngine.InputSystem;

public class FireExtinguisher : MonoBehaviour
{
    public Collider sprayCollider;

    public float extinguishStrength = 5.0f;

    //public ParticleSystem powderParticles;

    public void Spray()
    {
        //if (powderParticles != null)
        //{
        //    if (spraying)
        //    {
        //        if (!powderParticles.isPlaying)
        //            powderParticles.Play();
        //    }
        //    else
        //    {
        //        if (powderParticles.isPlaying)
        //            powderParticles.Stop();
        //    }
        //}

        ThermalSimulation.Instance.Extinguish(
            sprayCollider,
            extinguishStrength);
    }
}