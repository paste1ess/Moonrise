using ComputeSharp;
using ComputeSharp.D2D1;
using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Shaders
{
    [D2DInputCount(0)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    public readonly partial struct BackgroundShader : ID2D1PixelShader
    {
        public readonly float Time;

        public BackgroundShader(float time)
        {
            Time = time;
        }

        public float4 Execute()
        {
            float2 localPos = D2D.GetScenePosition().XY;

            float2 newUV = localPos / 100.0f;

            float r = 0.2f * Hlsl.Cos(Time + newUV.X);
            float g = 0.2f * Hlsl.Cos(Time + newUV.Y + 1.0f);
            float b = 0.3f;

            return new float4(r, g, b, 1.0f);
        }
    }
}
