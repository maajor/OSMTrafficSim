Shader "Unlit/CharacterURP"
{
    Properties
    {
        [HDR]_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_boundingMax("Bounding Max", Float) = 1.0
		_boundingMin("Bounding Min", Float) = 1.0
		_totalFrame("Total Frames", int) = 240
		_speed("Speed", Float) = 0.33
		_posTex ("Position Map (RGB)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog 
            #pragma multi_compile_instancing 
            #pragma instancing_options

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            float4 _MainTex_ST;
            sampler2D _MainTex;
		    sampler2D _posTex;
		    uniform float _boundingMax;
		    uniform float _boundingMin;
		    uniform float _speed;
		    uniform int _numOfFrames;
		    uniform float _totalFrame;

            UNITY_INSTANCING_BUFFER_START(Props)
			    UNITY_DEFINE_INSTANCED_PROP(float4, _FrameRange)
		    UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;

                float4 localFrameRange = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameRange);
			    float _numOfFrames = localFrameRange.y - localFrameRange.x;
			    float timeInFrames = ((ceil(frac(_Time.y * _speed / _numOfFrames) * _numOfFrames))/ _totalFrame) + (1.0/ _totalFrame);
			    timeInFrames += localFrameRange.x / _totalFrame;

			    //get position and normal from textures
			    float4 texturePos = tex2Dlod(_posTex,float4(v.uv1.x, 1 - (timeInFrames + v.uv1.y), 0, 0));

			    //expand normalised position texture values to world space
			    float expand = _boundingMax - _boundingMin;
			    texturePos.xyz *= expand;
			    texturePos.xyz += _boundingMin;
			    texturePos.x *= -1;  //flipped to account for right-handedness of unity
			    v.vertex.xyz += texturePos.xzy;  //swizzle y and z because textures are exported with z-up

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
