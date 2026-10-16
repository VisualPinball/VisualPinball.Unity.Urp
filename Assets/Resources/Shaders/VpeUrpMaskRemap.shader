// Bakes HDRP's mask-map remaps into the texture itself, because URP/Lit has no remap
// parameters: out = lerp(_RemapMin, _RemapMax, mask), per channel. Used by UrpMaskRemap.
Shader "Hidden/VPE/URP/MaskRemap"
{
	Properties
	{
		_MainTex ("Mask", 2D) = "white" {}
	}
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			// x = metallic (R), y = AO (G), z = detail mask (B), w = smoothness (A)
			float4 _RemapMin;
			float4 _RemapMax;

			float4 frag(v2f_img i) : SV_Target
			{
				float4 mask = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
				return lerp(_RemapMin, _RemapMax, mask);
			}
			ENDCG
		}
	}
}
