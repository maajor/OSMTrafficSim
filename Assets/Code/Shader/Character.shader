Shader "Custom/Character" {
	Properties {
		[HDR]_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_boundingMax("Bounding Max", Float) = 1.0
		_boundingMin("Bounding Min", Float) = 1.0
		_totalFrame("Total Frames", int) = 240
		_speed("Speed", Float) = 0.33
		_posTex ("Position Map (RGB)", 2D) = "white" {}
		_FrameRange("FrameRange", Vector) = (0,0,0,0)
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		
		CGPROGRAM
		#pragma surface surf Lambert addshadow vertex:vert

		#pragma target 3.0

		sampler2D _MainTex;
		sampler2D _posTex;
		uniform float _boundingMax;
		uniform float _boundingMin;
		uniform float _speed;
		uniform int _numOfFrames;
		uniform float _totalFrame;
		uniform float4 _FrameRange;
		struct Input {
			float2 uv_MainTex;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		//vertex function
		void vert(inout appdata_full v){
			float _numOfFrames = _FrameRange.y - _FrameRange.x;
			float timeInFrames = ((ceil(frac(_Time.y * _speed / _numOfFrames) * _numOfFrames))/ _totalFrame) + (1.0/ _totalFrame);
			timeInFrames += _FrameRange.x / _totalFrame;

			//get position and normal from textures
			float4 texturePos = tex2Dlod(_posTex,float4(v.texcoord1.x, 1 - (timeInFrames + v.texcoord1.y), 0, 0));

			//expand normalised position texture values to world space
			float expand = _boundingMax - _boundingMin;
			texturePos.xyz *= expand;
			texturePos.xyz += _boundingMin;
			texturePos.x *= -1;  //flipped to account for right-handedness of unity
			v.vertex.xyz += texturePos.xzy;  //swizzle y and z because textures are exported with z-up

		}

		void surf (Input IN, inout SurfaceOutput o) {
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			// Metallic and smoothness come from slider variables
			o.Alpha = c.a;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
