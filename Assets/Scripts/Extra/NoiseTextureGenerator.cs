using UnityEngine;

public static class NoiseTextureGenerator
{
    public static Texture2D GenerateStaticNoise(int width = 256, int height = 256, float greenChance = 0.1f)
    {
        Texture2D tex = new Texture2D(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float v = Random.value;
                Color c;
                if (Random.value < greenChance)
                    c = new Color(v * 0.3f, v, v * 0.3f, 1f); 
                else
                    c = new Color(v, v, v, 1f); 

                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }
}