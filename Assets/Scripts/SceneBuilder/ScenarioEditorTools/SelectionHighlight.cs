using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScenarioEditor
{
    public class SelectionHighlight : MonoBehaviour
    {
        [Header("Overlay Appearance")]
        [SerializeField] private Color _tintColor = new Color(1f, 0.55f, 0.05f, 0.4f);
        [SerializeField] private float _surfaceOffset = 0.004f;
        [SerializeField] private bool _scaleWithSize = true;

        private Material _overlayMat;
        private GameObject _shellRoot;
        private GameObject _currentTarget;

        private readonly List<SkinnedShell> _skinnedShells = new List<SkinnedShell>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private readonly List<GameObject> _shellPieces = new List<GameObject>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private struct SkinnedShell
        {
            public SkinnedMeshRenderer source;
            public MeshFilter shellFilter;
            public Mesh bakeMesh;
        }

        private void Awake()
        {
            _overlayMat = CreateOverlayMaterial();
        }

        private void OnEnable() => Subscribe();
        private void Start()    => Subscribe();

        private void Subscribe()
        {
            if (SelectionManager.Instance == null) return;
            SelectionManager.Instance.OnSelected   -= HandleSelected;
            SelectionManager.Instance.OnSelected   += HandleSelected;
            SelectionManager.Instance.OnDeselected -= HandleDeselected;
            SelectionManager.Instance.OnDeselected += HandleDeselected;
        }

        private void OnDisable()
        {
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.OnSelected   -= HandleSelected;
                SelectionManager.Instance.OnDeselected -= HandleDeselected;
            }
            ClearShell();
        }

        private void OnDestroy()
        {
            ClearShell();
            if (_overlayMat != null) Destroy(_overlayMat);
        }

        private void LateUpdate()
        {
            if (_skinnedShells.Count == 0) return;
            if (_currentTarget == null) { ClearShell(); return; }

            for (int i = 0; i < _skinnedShells.Count; i++)
            {
                var s = _skinnedShells[i];
                if (s.source == null || s.shellFilter == null || s.bakeMesh == null) continue;

                s.source.BakeMesh(s.bakeMesh, true);
                InflateMesh(s.bakeMesh, ResolveOffset(s.source.bounds.size));
                s.shellFilter.sharedMesh = s.bakeMesh;
            }
        }

        private void HandleSelected(GameObject target)
        {
            ClearShell();
            if (target == null) return;
            _currentTarget = target;
            BuildShell(target);
        }

        private void HandleDeselected() => ClearShell();

        private void BuildShell(GameObject target)
        {
            _shellRoot = new GameObject("~SelectionOverlay");
            _shellRoot.hideFlags = HideFlags.HideAndDontSave;
            _shellRoot.transform.SetParent(target.transform, false);

            var meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                CreateStaticShellPiece(mf, mr);
            }

            var smrs = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                CreateSkinnedShellPiece(smr);
            }
        }

        private void CreateStaticShellPiece(MeshFilter sourceFilter, MeshRenderer sourceRenderer)
        {
            Mesh src = sourceFilter.sharedMesh;

            var piece = new GameObject("OverlayPiece");
            piece.hideFlags = HideFlags.HideAndDontSave;
            piece.transform.SetParent(sourceFilter.transform, false);

            MeshFilter mf = piece.AddComponent<MeshFilter>();

            if (src.isReadable)
            {
                Mesh copy = Instantiate(src);
                copy.name = src.name + "_overlay";
                InflateMesh(copy, ResolveOffset(sourceRenderer.bounds.size));
                mf.sharedMesh = copy;
                _ownedMeshes.Add(copy);
            }
            else
            {
                mf.sharedMesh = src;
                float s = 1f + ResolveScaleOffset(sourceRenderer.bounds.size);
                piece.transform.localScale = Vector3.one * s;
            }

            ConfigureShellRenderer(piece.AddComponent<MeshRenderer>());
            _shellPieces.Add(piece);
        }

        private void CreateSkinnedShellPiece(SkinnedMeshRenderer smr)
        {
            var piece = new GameObject("SkinnedOverlayPiece");
            piece.hideFlags = HideFlags.HideAndDontSave;
            piece.transform.SetParent(smr.transform, false);

            var bakeMesh = new Mesh { name = smr.sharedMesh.name + "_skinnedOverlay" };
            smr.BakeMesh(bakeMesh, true);
            InflateMesh(bakeMesh, ResolveOffset(smr.bounds.size));

            var mf = piece.AddComponent<MeshFilter>();
            mf.sharedMesh = bakeMesh;
            ConfigureShellRenderer(piece.AddComponent<MeshRenderer>());

            _skinnedShells.Add(new SkinnedShell { source = smr, shellFilter = mf, bakeMesh = bakeMesh });
            _ownedMeshes.Add(bakeMesh);
            _shellPieces.Add(piece);
        }

        private void ConfigureShellRenderer(MeshRenderer mr)
        {
            mr.sharedMaterial = _overlayMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
        }

        private void ClearShell()
        {
            _skinnedShells.Clear();

            foreach (var piece in _shellPieces)
            {
                if (piece == null) continue;
                if (Application.isPlaying) Destroy(piece);
                else DestroyImmediate(piece);
            }
            _shellPieces.Clear();

            if (_shellRoot != null)
            {
                if (Application.isPlaying) Destroy(_shellRoot);
                else DestroyImmediate(_shellRoot);
                _shellRoot = null;
            }

            foreach (var m in _ownedMeshes)
            {
                if (m == null) continue;
                if (Application.isPlaying) Destroy(m);
                else DestroyImmediate(m);
            }
            _ownedMeshes.Clear();

            _currentTarget = null;
        }

        private void InflateMesh(Mesh mesh, float offset)
        {
            if (mesh == null || !mesh.isReadable) return;

            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;

            if (normals == null || normals.Length != verts.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            for (int i = 0; i < verts.Length; i++)
                verts[i] += normals[i] * offset;

            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        private float ResolveOffset(Vector3 worldSize)
        {
            if (!_scaleWithSize) return _surfaceOffset;
            float maxDim = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));
            float factor = Mathf.Clamp(maxDim, 0.1f, 10f);
            return _surfaceOffset * factor;
        }

        private float ResolveScaleOffset(Vector3 worldSize)
        {
            float maxDim = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));
            if (maxDim < 0.0001f) return 0.02f;
            return Mathf.Clamp(_surfaceOffset / maxDim, 0.005f, 0.1f);
        }

        private Material CreateOverlayMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "SelectionOverlay (runtime)" };

            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, _tintColor);
            if (mat.HasProperty(ColorId))     mat.SetColor(ColorId, _tintColor);

            if (mat.HasProperty(SurfaceId))  mat.SetFloat(SurfaceId, 1f);
            if (mat.HasProperty(SrcBlendId)) mat.SetInt(SrcBlendId, (int)BlendMode.SrcAlpha);
            if (mat.HasProperty(DstBlendId)) mat.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty(ZWriteId))   mat.SetInt(ZWriteId, 0);
            if (mat.HasProperty(CullId))     mat.SetInt(CullId, (int)CullMode.Back);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;

            return mat;
        }
    }
}
