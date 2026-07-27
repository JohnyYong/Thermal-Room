using UnityEngine;
using UnityEngine.Rendering;

namespace ScenarioEditor
{
    public class DuplicateTool : MonoBehaviour
    {
        [SerializeField] private Vector3 _offset = new Vector3(0.5f, 0f, 0.5f);
        [SerializeField]
        private EditorToolManager.ToolMode _modeAfter =
            EditorToolManager.ToolMode.Select;

        private void OnEnable() => Subscribe();
        private void Start() => Subscribe();

        private void OnDisable()
        {
            if (EditorToolManager.Instance != null)
                EditorToolManager.Instance.OnModeChanged -= HandleModeChanged;
        }

        private void Subscribe()
        {
            var mgr = EditorToolManager.Instance;
            if (mgr == null) return;
            mgr.OnModeChanged -= HandleModeChanged;
            mgr.OnModeChanged += HandleModeChanged;
        }

        private void HandleModeChanged(EditorToolManager.ToolMode mode)
        {
            if (mode != EditorToolManager.ToolMode.Duplicate) return;
            DuplicateSelection();
            EditorToolManager.Instance.SetMode(_modeAfter); // not Duplicate -> no loop
        }

        private void DuplicateSelection()
        {
            var sel = SelectionManager.Instance != null
                ? SelectionManager.Instance.CurrentSelection : null;
            if (sel == null) return;


            var clone = Instantiate(sel, sel.transform.parent);

            // Nudge in world space so it doesn't sit exactly on the original.
            clone.transform.position += _offset;

            clone.name = sel.name; 
            clone.SetActive(true);

            EditHistory.Instance?.Push(new SpawnAction(clone));
            SelectionManager.Instance.Select(clone);
        }
    }
}