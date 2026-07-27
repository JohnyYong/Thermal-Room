using UnityEngine;

namespace ScenarioEditor
{
    /// <summary>
    /// Marker component placed on the ROOT of any prop that can be selected,
    /// moved, rotated, deleted, or duplicated.
    ///
    /// Why a marker instead of relying purely on layers/tags:
    ///  - It unambiguously defines the "root" when the user clicks a child mesh.
    ///    (A chair imported from FBX may have 5 nested mesh children; clicking any
    ///     of them should select the chair, not the leg.)
    ///  - It gives you one place to store per-prop metadata later (e.g. prop type,
    ///    whether it can be deleted, snap settings) without touching the managers.
    ///
    /// Add this to: furniture, fires, victims, placed props.
    /// Do NOT add this to: walls, floors, ceilings.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectableProp : MonoBehaviour
    {
        public enum PropKind
        {
            Furniture,
            Fire,
            Victim,
            Other
        }

        [Tooltip("Category of this prop. Useful for filtering and for tools like Delete/Duplicate.")]
        public PropKind kind = PropKind.Furniture;

        [Tooltip("If false, Move/Rotate tools will ignore this object even though it can be selected.")]
        public bool canTransform = true;

        [Tooltip("If false, the Delete tool will refuse to remove this object.")]
        public bool canDelete = true;
    }
}
