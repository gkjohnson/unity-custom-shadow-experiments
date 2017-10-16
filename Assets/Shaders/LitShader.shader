Shader "CustomShadows/Shadowed" {
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
	}
    SubShader {
        Tags { "RenderType"="Opaque" }

        Pass {
            Lighting On
        
            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ VARIANCE_SHADOWS PCF_SHADOWS HARD_SHADOWS
        
            float4 _Color;

            // Shadow Map info
            sampler2D _ShadowTex;
            float4x4 _LightMatrix;
            float4 _ShadowTexScale;

            // Shadow Variables
            float _MaxShadowIntensity;

            float3 CTIllum(float4 wVertex, float3 normal)
            {
                // Reference
                // http://ruh.li/GraphicsCookTorrance.html

                float fresnel_val = 0.2;
                float roughness_val = .06;
                float k = 0.01;

                wVertex = mul(unity_WorldToObject, wVertex);
                float3 viewpos = -mul(UNITY_MATRIX_MV, wVertex).xyz;

                float3 col = float3(0, 0, 0);
                for (int i = 0; i < 2; i++)
                {
                    // View vector, light direction, and Normal in model-view space
                    float3 toLight = unity_LightPosition[i].xyz;
                    float3 L = normalize(toLight);
                    float3 V = normalize(viewpos);//float3(0, 0, 1);
                    float3 N = mul(UNITY_MATRIX_MV, float4(normal,0));
                    N = normalize(N);

                    // Half vector from view to light vector
                    float3 H = normalize(V + L);

                    // Dot products
                    float NdotL = max(dot(N, L), 0);
                    float NdotV = max(dot(N, V), 0);
                    float NdotH = max(dot(N, H), 1.0e-7);
                    float VdotH = max(dot(V, H), 0);

                    // model the geometric attenuation of the surface
                    float geo_numerator = 2 * NdotH;
                    float geo_b = (geo_numerator * NdotV) / VdotH;
                    float geo_c = (geo_numerator * NdotL) / VdotH;
                    float geometric = 2 * NdotH / VdotH;
                    geometric = min(1, max(0,min(geo_b, geo_c)));

                    // calculate the roughness of the model
                    float r2 = roughness_val * roughness_val;
                    float NdotH2 = NdotH * NdotH;
                    float NdotH2_r = 1 / (NdotH2 * r2);
                    float roughness_exp = (NdotH2 - 1) * NdotH2_r;
                    float roughness = exp(roughness_exp) * NdotH2_r / (4 * NdotH2);

                    // Calculate the fresnel value
                    float fresnel = pow(1.0 - VdotH, 5.0);
                    fresnel *= 1 - fresnel_val;
                    fresnel += fresnel_val;

                    // Calculate the final specular value
                    float s = (1-k)*(fresnel * geometric * roughness) / (NdotV * NdotL * 3.14 + 1.0e-7) + k;
                    float3 spec = float3(1,1,1)*s;

                    // apply to the model
                    float lengthSq = dot(toLight, toLight);
                    float atten = 1.0 / (1.0 + lengthSq * unity_LightAtten[i].z);
                    col += NdotL * (unity_LightColor[i].xyz * spec + unity_LightColor[i].xyz *_Color) * atten;
                }
                return col;
            }



            struct v2f
            {
                fixed4 color : COLOR0;
                float4 pos : SV_POSITION;
				float4 wPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float depth : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = float4(1, 1, 1, 1);
                o.wPos = mul(unity_ObjectToWorld, v.vertex);
                o.normal = v.normal;

                COMPUTE_EYEDEPTH(o.depth);
                return o; 
            }            

            fixed4 frag (v2f i) : COLOR
			{
                // COLOR
                // modulate with lighting
                float4 color = i.color;
                color = float4(CTIllum(i.wPos, i.normal), 1);

                // SHADOWS
                // get distance to lightPos
                float4 lightSpacePos = mul(_LightMatrix, i.wPos);
                float3 lightSpaceNorm = normalize(mul(_LightMatrix, mul(unity_ObjectToWorld, i.normal)));
                float depth = lightSpacePos.z / _ShadowTexScale.z;

                float2 uv = lightSpacePos.xy;
                uv += _ShadowTexScale.xy / 2;
                uv /= _ShadowTexScale.xy;

                float shadowIntensity = 0;

#ifdef HARD_SHADOWS
                float2 offset = lightSpaceNorm * _ShadowTexScale.w * 1;
                float sDepth = tex2D(_ShadowTex, uv + offset).r;
                if (sDepth < depth - _ShadowTexScale.w) shadowIntensity = 1;
#endif
#ifdef VARIANCE_SHADOWS

                // https://www.gdcvault.com/play/1023808/Rendering-Antialiased-Shadows-with-Moment
                // https://developer.nvidia.com/gpugems/GPUGems3/gpugems3_ch08.html
                // The moments of the fragment live in "_shadowTex"
                float2 s = tex2D(_ShadowTex, uv.xy).rg;

                // average / expected depth and depth^2 across the texels
                // E(x) and E(x^2)
                float x = s.r; 
                float x2 = s.g;
                
                // calculate the variance of the texel based on
                // the formula var = E(x^2) - E(x)^2
                // https://en.wikipedia.org/wiki/Algebraic_formula_for_the_variance#Proof
                float var = x2 - x*x; 

                // calculate our initial probability based on the basic depths
                // if our depth is closer than x, then the fragment has a 100%
                // probability of being lit (p=1)
                float p = depth <= x;
                
                // calculate the upper bound of the probability using Chebyshev's inequality
                // https://en.wikipedia.org/wiki/Chebyshev%27s_inequality
                float delta = depth - x;
                float p_max = var / (var + delta*delta);

                // To alleviate the light bleeding, expand the shadows to fill in the gaps
                float amount = 0.0;
                p_max = clamp( (p_max - amount) / (1 - amount), 0, 1);

                shadowIntensity = 1 - max(p, p_max);
#endif
                color.xyz *= 1 - shadowIntensity * _MaxShadowIntensity;
                color.xyz += UNITY_LIGHTMODEL_AMBIENT.xyz;

                return color;

            }
            ENDCG
        }
    }

    Fallback "VertexLit"
}