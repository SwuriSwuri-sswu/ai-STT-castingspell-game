// hun0fx demo floor / capsule shader. Pipeline-neutral: one SubShader for URP, one for the Built-in pipeline.
// Simple fixed-direction shading so the demo scenes look the same with or without scene lights.
Shader "HunFX/Demo Surface"
{
	Properties
	{
		_BaseMap ("Base Map", 2D) = "white" {}
		_BaseColor ("Base Color", Color) = (1, 1, 1, 1)
		_Shade ("Shading", Range(0, 1)) = 0.6
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	sampler2D _BaseMap; float4 _BaseMap_ST; fixed4 _BaseColor; half _Shade;
	struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
	struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nrm : TEXCOORD1; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
	v2f vert (appdata v)
	{
		v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
		o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
		o.nrm = UnityObjectToWorldNormal(v.normal); UNITY_TRANSFER_FOG(o, o.pos);
		return o;
	}
	fixed4 frag (v2f i) : SV_Target
	{
		fixed4 c = tex2D(_BaseMap, i.uv) * _BaseColor;
		half ndl = dot(normalize(i.nrm), normalize(float3(0.35, 0.85, -0.4))) * 0.5 + 0.5;
		c.rgb *= lerp(1.0, ndl * 1.15, _Shade);
		UNITY_APPLY_FOG(i.fogCoord, c);
		return c;
	}
	struct v2fd { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
	v2fd vertDepth (appdata v) { v2fd o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos = UnityObjectToClipPos(v.vertex); return o; }
	fixed4 fragDepth (v2fd i) : SV_Target { return 0; }
	ENDCG

	// Universal Render Pipeline
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
		Pass
		{
			Name "Forward"
			Tags { "LightMode" = "UniversalForward" }
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			ENDCG
		}
		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }
			ZWrite On ColorMask R
			CGPROGRAM
			#pragma vertex vertDepth
			#pragma fragment fragDepth
			#pragma multi_compile_instancing
			ENDCG
		}
	}

	// Built-in Render Pipeline
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
		Pass
		{
			Tags { "LightMode" = "ForwardBase" }
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			ENDCG
		}
	}
	Fallback "Legacy Shaders/VertexLit"
}
