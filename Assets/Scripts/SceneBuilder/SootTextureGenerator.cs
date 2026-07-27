using UnityEngine;

public static class SootTextureGenerator
{
    private static Texture2D _cached;

    public static Texture2D Get()
    {
        if (_cached != null) return _cached;

        int size = 128;
        _cached = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;

                // Two perlin noise layers for irregular shape
                float noise1 = Mathf.PerlinNoise(u * 8f, v * 8f);
                float noise2 = Mathf.PerlinNoise(u * 3f + 100f, v * 3f + 100f);
                float noise = noise1 * noise2;

                // Radial falloff so edges fade out
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float radial = Mathf.SmoothStep(0.5f, 0.1f, dist);

                // Combine — step gives hard soot edges
                float value = noise * radial;
                float alpha = value > 0.25f ? radial : 0f;

                pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
            }
        }

        _cached.SetPixels(pixels);
        _cached.Apply();
        return _cached;
    }
}