using UnityEngine;

namespace ScenarioEditor
{

    public class DeleteTool : MonoBehaviour
    {
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
            if (mode != EditorToolManager.ToolMode.Delete) return;
            DeleteSelection();
            EditorToolManager.Instance.SetMode(_modeAfter); // not Delete -> no loop
        }

        private void DeleteSelection()
        {
            var sel = SelectionManager.Instance != null
                ? SelectionManager.Instance.CurrentSelection : null;
            if (sel == null) return;

            //For cannot be deleted in the future
            // var prop = sel.GetComponent<SelectableProp>();
            // if (prop != null && !prop.canDelete) return;

            if (EditHistory.Instance != null)
                EditHistory.Instance.Push(new DeleteAction(sel)); // ctor hides + deselects
            else
                Destroy(sel); // no history available: just remove it (not undoable)
        }
    }
}