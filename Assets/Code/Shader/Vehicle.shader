Shader "Custom/Vehicle" {
	Properties {
		_ColorBody ("Body Color", Color) = (1,1,1,1)
		[HDR] _ColorFrontLight("Front Light", Color) = (1,1,1,1)
		[HDR] _ColorRearLight("Rear Light", Color) = (1,1,1,1)
		_ColorWindows("Windows Color", Color) = (1,1,1,1)
		_MainTex ("Mask(body, window, front, rear)", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Lambert fullforwardshadows

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D _MainTex;

		struct Input {
			float2 uv_MainTex;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _ColorBody;
		fixed4 _ColorFrontLight;
		fixed4 _ColorRearLight;
		fixed4 _ColorWindows;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			UNITY_DEFINE_INSTANCED_PROP(fixed4, _ColorVehicle)
		UNITY_INSTANCING_BUFFER_END(Props)

		void surf (Input IN, inout SurfaceOutput o) {
			// Albedo comes from a texture tinted by color
			fixed4 mask = tex2D (_MainTex, IN.uv_MainTex);
			o.Albedo = mask.r * _ColorBody + mask.g * _ColorWindows
				+ mask.a * _ColorFrontLight + mask.b * _ColorRearLight;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
