using ComputeSharp;
using ComputeSharp.D2D1;
using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;

namespace Moonrise.Shaders
{
    [D2DInputCount(0)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    public readonly partial struct BackgroundShader : ID2D1PixelShader
    {
        public readonly float Time;
        public readonly bool LightMode;

        public BackgroundShader(float time, bool lightMode)
        {
            Time = time;
            LightMode = lightMode;
        }

        private float Hash(float2 p)
        {
            p = Hlsl.Frac(p * new float2(127.1f, 311.7f));
            p += Hlsl.Dot(p, p + 19.19f);
            return Hlsl.Frac(p.X * p.Y);
        }

        private float Noise(float2 p)
        {
            float2 i = Hlsl.Floor(p);
            float2 f = Hlsl.Frac(p);
            float2 u = f * f * (3.0f - 2.0f * f);

            return Hlsl.Lerp(
                Hlsl.Lerp(Hash(i + new float2(0, 0)), Hash(i + new float2(1, 0)), u.X),
                Hlsl.Lerp(Hash(i + new float2(0, 1)), Hash(i + new float2(1, 1)), u.X),
                u.Y);
        }

        private float Fbm(float2 uv, int octaves)
        {
            float value = 0.0f;
            float amplitude = 0.5f;
            if (octaves > 1)
            {
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * Noise(uv);
                    uv *= 2.0f;
                    amplitude *= 0.5f;
                }
            }
            else
            {
                value += amplitude * Noise(uv);
                uv *= 2.0f;
                amplitude *= 0.5f;
            }
            return value;
        }

        public float4 Execute()
        {
            float2 uv = D2D.GetScenePosition().XY / 30f;
            float slowTime = Time * 0.1f;

            float2 q = new(
                Fbm(uv + slowTime, 3),
                Fbm(uv + new float2(1.7f, 9.2f) + slowTime, 3)
            );

            float cloud = Hlsl.SmoothStep(0.35f, 0.65f, Fbm(uv + q, 3));
            float cloudRemapped = Hlsl.SmoothStep(0.1f, 1f, cloud);

            float alpha = LightMode ? Hlsl.Max((1f - cloud) * 0.25f, 0.05f) : cloudRemapped * 0.8f;

            Float3 baseColor = LightMode ? new(0.08f, 0.08f, 0.08f) : new(0.034f, 0.034f, 0.034f);
            return new(baseColor, alpha);
        }

        //public float4 Execute()
        //{
        //    float2 uv = D2D.GetScenePosition().XY / 1000.0f;

        //    float slowTime = Time * 0.04f;

        //    float2 warpA = new float2(
        //        Hlsl.Sin(uv.Y * 2.5f + slowTime),
        //        Hlsl.Cos(uv.X * 2.5f + slowTime)
        //    );
        //    float2 warpB = new float2(
        //        Hlsl.Sin((uv.X + warpA.X) * 4.0f - slowTime * 0.5f),
        //        Hlsl.Cos((uv.Y + warpA.Y) * 4.0f + slowTime * 0.7f)
        //    );
        //    float2 warpC = new float2(
        //        Hlsl.Sin((uv.X + warpB.X) * 3.0f - slowTime * 0.3f),
        //        Hlsl.Cos((uv.Y + warpB.Y) * 1.5f + slowTime * 0.6f)
        //    );
        //    float2 warpD = new float2(
        //        Hlsl.Cos((uv.Y + warpC.Y) * 2.0f + slowTime * 0.5f),
        //        Hlsl.Sin((uv.X + warpC.X) * 2.0f - slowTime * 0.5f)
        //    );

        //    float noiseA = 0.5f + 0.5f * Hlsl.Sin(warpD.X + warpD.Y);

        //    float angle = Hlsl.Atan2(warpD.Y, warpD.X);
        //    float radius = Hlsl.Length(warpD);
        //    float noiseB = 0.5f + 0.5f * Hlsl.Sin(
        //        angle * 3.0f + slowTime * 0.4f +
        //        radius * 5.0f - slowTime * 0.25f
        //    );

        //    float blended = Hlsl.SmoothStep(0.0f, 1.0f, noiseA * noiseB * 1.6f);

        //    float r = Hlsl.Pow(blended, 2.8f) * 0.08f + 0.01f;
        //    float g = Hlsl.Pow(blended, 1.6f) * (0.07f + (LightMode ? 0.02f : 0f)) + 0.01f;
        //    float b = Hlsl.Pow(blended, 0.9f) * 0.08f + 0.01f;

        //    if (LightMode)
        //    {
        //        return new float4(1.0f - r * 5.0f, 1.0f - g * 5.0f, 1.0f - b * 5.0f, 0.6f);
        //    }
        //    return new float4(r, g, b, 0.6f);

        //}
    }
}
