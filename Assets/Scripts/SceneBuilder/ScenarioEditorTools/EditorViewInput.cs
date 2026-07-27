using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScenarioEditor
{
    [RequireComponent(typeof(RawImage))]
    public class EditorViewInput : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
    {
        public static EditorViewInput Instance { get; private set; }

        [Header("References")]
        [SerializeField] private Camera _renderCamera;
        [SerializeField] private Canvas _canvas;

        public event Action<ViewPointer> ViewPointerDown;
        public event Action<ViewPointer> ViewPointerUp;
        public event Action<ViewPointer> ViewBeginDrag;
        public event Action<ViewPointer> ViewDrag;
        public event Action<ViewPointer> ViewEndDrag;

        private RawImage _rawImage;
        private RectTransform _rect;


        private Vector2 _lastScreenPos;
        private bool _pointerInside;

        public bool IsPointerInside => _pointerInside;

        public readonly struct ViewPointer
        {
            public readonly Ray ray;
            public readonly Vector2 viewportPoint;
            public readonly Vector2 screenPosition;
            public readonly bool valid;

            public ViewPointer(Ray ray, Vector2 viewportPoint, Vector2 screenPosition, bool valid)
            {
                this.ray = ray;
                this.viewportPoint = viewportPoint;
                this.screenPosition = screenPosition;
                this.valid = valid;
            }
        }

        private void Awake()
        {
            Instance = this;
            _rawImage = GetComponent<RawImage>();
            _rect = (RectTransform)transform;

            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _lastScreenPos = e.position;
            ViewPointerDown?.Invoke(Build(e));
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _lastScreenPos = e.position;
            ViewPointerUp?.Invoke(Build(e));
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _lastScreenPos = e.position;
            ViewBeginDrag?.Invoke(Build(e));
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _lastScreenPos = e.position;
            ViewDrag?.Invoke(Build(e));
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _lastScreenPos = e.position;
            ViewEndDrag?.Invoke(Build(e));
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _pointerInside = true;
            _lastScreenPos = e.position;
        }

        public void OnPointerExit(PointerEventData e)
        {
            _pointerInside = false;
        }

        public void OnPointerMove(PointerEventData e)
        {
            _pointerInside = true;
            _lastScreenPos = e.position;
        }

        public bool TryGetRay(Vector2 screenPosition, out Ray ray, out Vector2 viewportPoint)
        {
            ray = default;
            viewportPoint = default;

            Camera cam = ResolveRenderCamera();
            if (cam == null)
                return false;

            Camera uiCam = ResolveUiCamera();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPosition, uiCam, out Vector2 local))
                return false;

            Rect r = _rect.rect;
            float u = (local.x - r.x) / r.width;
            float v = (local.y - r.y) / r.height;

            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            Rect uv = _rawImage.uvRect;
            viewportPoint = new Vector2(uv.x + u * uv.width, uv.y + v * uv.height);

            ray = cam.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            return true;
        }


        public bool TryGetHoverRay(out Ray ray, out Vector2 viewportPoint) //For fire placement point hovering
        {
            ray = default;
            viewportPoint = default;
            if (!_pointerInside) return false;
            return TryGetRay(_lastScreenPos, out ray, out viewportPoint);
        }


        private ViewPointer Build(PointerEventData e)
        {
            bool ok = TryGetRay(e.position, out Ray ray, out Vector2 vp);
            return new ViewPointer(ray, vp, e.position, ok);
        }

        private Camera ResolveRenderCamera()
        {
            if (_renderCamera != null)
                return _renderCamera;
            Debug.LogWarning("[EditorViewInput] No render camera assigned. Picking disabled.", this);
            return null;
        }

        private Camera ResolveUiCamera()
        {
            if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return _canvas.worldCamera;
        }
    }
}
