#ifndef CHARANIM_INCLUDED
#define CHARANIM_INCLUDED

UNITY_INSTANCING_BUFFER_START(Props)
UNITY_DEFINE_INSTANCED_PROP(float4, _FrameRange)
UNITY_INSTANCING_BUFFER_END(Props)

void GetVertexPosition_float(float _speed, float _totalFrame, UnityTexture2D _posTex, float _boundingMax, float _boundingMin, float2 uv, out float3 vertexOffset){

	float4 localFrameRange = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameRange);
	float _numOfFrames = localFrameRange.y - localFrameRange.x;
	float timeInFrames = ((ceil(frac(_Time.y * _speed / _numOfFrames) * _numOfFrames)) / _totalFrame) + (1.0 / _totalFrame);
	timeInFrames += localFrameRange.x / _totalFrame;

	//get position and normal from textures
	float4 texturePos = tex2Dlod(_posTex, float4(uv.x, 1 - (timeInFrames + uv.y), 0, 0));

	//expand normalised position texture values to world space
	float expand = _boundingMax - _boundingMin;
	texturePos.xyz *= expand;
	texturePos.xyz += _boundingMin;
	texturePos.x *= -1;  //flipped to account for right-handedness of unity
	vertexOffset = texturePos.xzy;  //swizzle y and z because textures are exported with z-up
}

#endif