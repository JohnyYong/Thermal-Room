using UnityEngine;

//Only available in Editor Mode
public class PlacementMode : MonoBehaviour
{
    public enum PlaceMode
    {
        NONE = 0,
        PLACE_VICTIMS,
        PLACE_FIRE
    }

    //Maybe have a ball in front of the camera (Not the drone when in placement mode) to show where the item will be placed?
    //Anyway to make it such that there is a preview of the object before it being actually placed down? Whereby the scripts of it won't run
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
