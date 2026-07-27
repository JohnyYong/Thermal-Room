using UnityEngine;
using UnityEditor;
using Unity.VisualScripting;

public enum RoomObjectType
{
    //For people
    Human,


    Chair,
    Bed,
    Table,
    Cushion,
    Closets,
    Kitchen,

}

public enum RoomObjectCategory
{
    People,
    Seatings,
    Storage,
    Tables,
    Kitchen
}

[CreateAssetMenu(fileName = "NewRoomObject", menuName = "Room Editor/Room Object")]
public class RoomObject : ScriptableObject
{
    [Header("Identity")]
    public string DisplayName;
    public RoomObjectType objectType;
    public RoomObjectCategory objectCategory;

    [TextArea(2, 4)]
    public string description;

    [Header("Visuals")] //Kinda need an auto picture snipping tool for this as well
    public Sprite icon;
    public GameObject prefab;
    public Color tintColor = Color.white;

    [Header("Grid Size (in tiles)")] //For now we plan to make it be placed by tile system
    public int widthTiles = 1;
    public int heightTiles = 1;

    [Header("Placement Rules")]
    public bool canRotate = true;
    public bool requireWallPlacement = false;

    public bool isWalkable = false;
    public bool isStackable = false; //This one getting 

    [Header("Properties")]
    public int maxOccupants = 30;
    public float weight = 10.0f;
}
