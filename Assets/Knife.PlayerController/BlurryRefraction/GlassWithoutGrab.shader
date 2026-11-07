// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Similar to regular FX/Glass/Stained BumpDistort shader
// from standard Effects package, just without grab pass,
// and samples a texture with a different name.

Shader "FX/Glass/Stained BumpDistort (no grab)" {
Properties {
	_BumpAmt  ("Distortion", range (0,640)) = 10
	_TintAmt ("Tint Amount", Range(0,1)) = 0.1
	_MainTex ("Tint Color (RGB)", 2D) = "white" {}
	_Mask("Blur Mask", 2D) = "white" {}
	_NoiseMask("Noise Mask", 2D) = "white" {}
	_WorldScale("World Scale", float) = 1.0
	_MaskPower("Blur Mask Power", float) = 1
	_Tint ("Tint", Color) = (1, 1, 1, 1)
	_BumpMap ("Normalmap", 2D) = "bump" {}
}

Category {

	// We must be transparent, so other objects are drawn before this one.
	Tags { "Queue"="Transparent" "RenderType"="Opaque" }

	SubShader {

		Pass {
			Name "BASE"
			Tags { "LightMode" = "Always" }
			
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fog
#include "UnityCG.cginc"

struct appdata_t {
	float4 vertex : POSITION;
	float2 texcoord: TEXCOORD0;
};

struct v2f {
	float4 vertex : POSITION;
	float4 uvgrab : TEXCOORD0;
	float2 uvbump : TEXCOORD1;
	float2 uvmain : TEXCOORD2;
	float2 uvnoise : TEXCOORD3;
	UNITY_FOG_COORDS(3)
};

float _BumpAmt;
half _TintAmt;
float4 _BumpMap_ST;
float4 _MainTex_ST;
float4 _Tint;
float _WorldScale;

v2f vert (appdata_t v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	#if UNITY_UV_STARTS_AT_TOP
	float scale = -1.0;
	#else
	float scale = 1.0;
	#endif

	float2 worldXZ = mul(unity_ObjectToWorld, v.vertex).xz;

	o.uvgrab.xy = (float2(o.vertex.x, o.vertex.y*scale) + o.vertex.w) * 0.5;
	o.uvgrab.zw = o.vertex.zw;
	o.uvbump = TRANSFORM_TEX( v.texcoord, _BumpMap );
	o.uvmain = TRANSFORM_TEX( v.texcoord, _MainTex );
	o.uvnoise = v.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw + worldXZ * _WorldScale;
	UNITY_TRANSFER_FOG(o,o.vertex);
	return o;
}

sampler2D _GrabBlurTexture;
sampler2D _GrabNotBlurTexture;
float4 _GrabBlurTexture_TexelSize;
sampler2D _BumpMap;
sampler2D _MainTex;
sampler2D _Mask;
sampler2D _NoiseMask;
float _MaskPower;

half4 frag (v2f i) : SV_Target
{
	// calculate perturbed coordinates
	// we could optimize this by just reading the x & y without reconstructing the Z
	half2 bump = UnpackNormal(tex2D( _BumpMap, i.uvbump )).rg;
	float2 offset = bump * _BumpAmt * _GrabBlurTexture_TexelSize.xy;
	i.uvgrab.xy = offset * i.uvgrab.z + i.uvgrab.xy;
	
	half4 col = tex2Dproj (_GrabBlurTexture, UNITY_PROJ_COORD(i.uvgrab));
	half4 originalCol = tex2Dproj(_GrabNotBlurTexture, UNITY_PROJ_COORD(i.uvgrab));
	half4 tint = tex2D(_MainTex, i.uvmain);
	half4 noiseMask = tex2D(_NoiseMask, i.uvnoise);
	half4 maskAmount = tex2D(_Mask, i.uvmain);
	col = col * lerp (float4(1,1,1,1), tint, _TintAmt) * _Tint;

	col = lerp(originalCol, col, pow(maskAmount.r * noiseMask.r, _MaskPower));

	UNITY_APPLY_FOG(i.fogCoord, col);
	return col;
}
ENDCG
		}
	}

}

}
