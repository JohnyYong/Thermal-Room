using UnityEngine;
using UnityEngine.UI;

namespace ScenarioEditor
{
    [RequireComponent(typeof(Button))]
    public class ToolButton : MonoBehaviour
    {
        public enum Action
        {
            Select, Move, Rotate, PlaceFire, PlaceVictim, Delete, Duplicate,
            Undo, Redo, Save, Load
        }

        [SerializeField] private Action _action;

        private Button _button;

        private void Awake() => _button = GetComponent<Button>();

        private void OnEnable()
        {
            _button.onClick.RemoveListener(Invoke); // guard against double-add
            _button.onClick.AddListener(Invoke);
        }

        private void OnDisable() => _button.onClick.RemoveListener(Invoke);

        private void Invoke()
        {
            var mgr = EditorToolManager.Instance;
            switch (_action)
            {
                case Action.Select: mgr?.SelectMode(); break;
                case Action.Move: mgr?.MoveMode(); break;
                case Action.Rotate: mgr?.RotateMode(); break;
                case Action.PlaceFire: mgr?.PlaceFireMode(); break;
                case Action.PlaceVictim: mgr?.PlaceVictimMode(); break;
                case Action.Delete: mgr?.DeleteMode(); break;
                case Action.Duplicate: mgr?.DuplicateMode(); break;
                case Action.Undo: EditHistory.Instance?.Undo(); break;
                case Action.Redo: EditHistory.Instance?.Redo(); break;
                case Action.Save:        /* SaveManager.Instance?.Save(); */ break;
                case Action.Load:        /* SaveManager.Instance?.Load(); */ break;
            }
        }
    }
}