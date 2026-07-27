using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "RoomObjectDatabase", menuName = "Room Editor/Room Object Database")]
public class RoomObjectDatabase : ScriptableObject
{
    [SerializeField] private List<RoomObject> entries = new();

    public IReadOnlyList<RoomObject> All => entries;

    public RoomObject GetByType(RoomObjectType type) =>
        entries.FirstOrDefault(e => e.objectType == type);

    public List<RoomObject> GetByCategory(RoomObjectCategory category) =>
        entries.Where(e => e.objectCategory == category).ToList();

    public List<RoomObject> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<RoomObject>(entries);
        string q = query.ToLower();
        return entries
            .Where(e => e.DisplayName.ToLower().Contains(q) || e.description.ToLower().Contains(q))
            .ToList();
    }

    public bool TryGet(RoomObjectType type, out RoomObject result)
    {
        result = GetByType(type);
        return result != null;
    }

#if UNITY_EDITOR
    public void AddEntry(RoomObject obj)
    {
        if (obj != null && !entries.Contains(obj))
            entries.Add(obj);
    }

    public void RemoveEntry(RoomObject obj) => entries.Remove(obj);

    public void SortByCategory() =>
        entries = entries.OrderBy(e => e.objectCategory).ThenBy(e => e.DisplayName).ToList();
#endif
}