using UnityEngine;

// Anything that wants char/temperature readings from ThermalSampler
// implements this. Lets the sampler treat CharController, CharOverlayController,
// or any future consumer uniformly.
public interface IThermalSampleReceiver
{
    // World-space region to search for char in. Should be padded out a
    // couple of voxels beyond the object's own bounds -- char only ever
    // accumulates in the AIR touching a burning solid, on whichever face
    // happens to be exposed, so a single guessed point (e.g. "just below
    // the object") can easily miss it. Scanning a small padded region
    // around the object finds it regardless of which side is burning.
    Bounds SampleBounds { get; }

    // Called when a readback completes with the MAX char value found
    // anywhere inside SampleBounds.
    void ReceiveTemperature(float temp);
}