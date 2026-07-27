using UnityEngine;

[RequireComponent(typeof(FluidSimulator))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FireRenderer : MonoBehaviour
{
    [Header("Material")]
    public Material fireMaterial;

    FluidSimulator _sim;
    MeshRenderer   _meshRenderer;

    void OnEnable()
    {
        _sim          = GetComponent<FluidSimulator>();
        _meshRenderer = GetComponent<MeshRenderer>();

        // Assign a unit cube mesh
        GetComponent<MeshFilter>().mesh = CreateCube();

        if (fireMaterial == null)
        {
            Debug.LogError("[FireRenderer] Assign the FireRaymarch material!");
            return;
        }

        _meshRenderer.material        = fireMaterial;
        _meshRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        if (fireMaterial == null || _sim == null) return;

        // Push live simulation textures to the material every frame
        fireMaterial.SetTexture("_DensityTex",     _sim.DensityTexture);
        fireMaterial.SetTexture("_TemperatureTex", _sim.TemperatureTexture);

        // Scale the cube to match the sim volume (10 world units)
        transform.localScale = Vector3.one * 10f;
    }

    // Simple unit cube centered at origin
    Mesh CreateCube()
    {
        var mesh = new Mesh();

        Vector3[] verts =
        {
            new(-0.5f,-0.5f,-0.5f), new( 0.5f,-0.5f,-0.5f),
            new( 0.5f, 0.5f,-0.5f), new(-0.5f, 0.5f,-0.5f),
            new(-0.5f,-0.5f, 0.5f), new( 0.5f,-0.5f, 0.5f),
            new( 0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
        };

        int[] tris =
        {
            0,2,1, 0,3,2,   // back
            4,5,6, 4,6,7,   // front
            0,1,5, 0,5,4,   // bottom
            2,3,7, 2,7,6,   // top
            0,4,7, 0,7,3,   // left
            1,2,6, 1,6,5    // right
        };

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }
}