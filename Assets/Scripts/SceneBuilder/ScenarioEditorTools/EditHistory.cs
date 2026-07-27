using NUnit.Framework;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

//Command level Undo / Redo

//Can study on Memento State

namespace ScenarioEditor
{

    public interface IEditAction
    {
        void Undo();
        void Redo();
        void Discard();
    }

    public class EditHistory : MonoBehaviour
    {

        public static EditHistory Instance {  get; private set; }

        [SerializeField] private int _maxDepth;

        private readonly List<IEditAction> _undo = new();
        private readonly List<IEditAction> _redo = new();

        public event System.Action OnHistoryChanged;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;


        //For singletone
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }


        public void Push(IEditAction Action)
        {
            _undo.Add(Action);
            
            while (_undo.Count > _maxDepth) { _undo[0].Discard(); _undo.RemoveAt(0); }
            
            foreach(var a in _redo) a.Discard(); //New branch invalidates redo
            _redo.Clear();
            OnHistoryChanged?.Invoke();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var a = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            a.Undo();
            _redo.Add(a); //Add action to be redoable if undo action have been done
            OnHistoryChanged?.Invoke();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var a = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            a.Redo();
            _undo.Add(a); //Add action to be undoable
            OnHistoryChanged?.Invoke();
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }


    //Object was created (Place or duplicate). Undo hides it
    public class SpawnAction : IEditAction
    {
        private readonly GameObject _go;
        public SpawnAction(GameObject go) { _go = go; }

        public void Undo()
        {
            if (_go == null) { return; }
            if (SelectionManager.Instance != null &&
                    SelectionManager.Instance.CurrentSelection == _go) { SelectionManager.Instance.Deselect(); }
            _go.SetActive(false);
        }

        public void Redo() { if (_go != null) _go.SetActive(true); }
        public void Discard() { if (_go != null && !_go.activeSelf) Object.Destroy(_go); }
    };


    //Object was deleted. Constructed already-hidden; undo restores it.
    public class DeleteAction : IEditAction
    {
        private readonly GameObject _go;
        public DeleteAction(GameObject go)
        {
            _go = go;
            if (SelectionManager.Instance != null &&
                SelectionManager.Instance.CurrentSelection == go)
                SelectionManager.Instance.Deselect();
            _go.SetActive(false);
        }
        public void Undo() { if (_go != null) _go.SetActive(true); }
        public void Redo()
        {
            if (_go == null) return;
            if (SelectionManager.Instance != null &&
                SelectionManager.Instance.CurrentSelection == _go)
                SelectionManager.Instance.Deselect();
            _go.SetActive(false);
        }
        public void Discard() { if (_go != null && !_go.activeSelf) Object.Destroy(_go); }
    }

    //Transforms edits
    public class TransformAction : IEditAction
    {
        private readonly Transform _t;
        private readonly Vector3 _fromPos, _toPos;
        private readonly Quaternion _fromRot, _toRot;

        public TransformAction(Transform t, Vector3 fromPos, Quaternion fromRot,
                                            Vector3 toPos, Quaternion toRot)
        { _t = t; _fromPos = fromPos; _fromRot = fromRot; _toPos = toPos; _toRot = toRot; }

        public void Undo() { if (_t) _t.SetPositionAndRotation(_fromPos, _fromRot); }
        public void Redo() { if (_t) _t.SetPositionAndRotation(_toPos, _toRot); }
        public void Discard() { }

    }
}