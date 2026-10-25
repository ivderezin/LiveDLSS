cbuffer MotionParams : register(b0) {
  float4x4 CurrCameraToWorld;
  float4x4 WorldToPrevCamera;
  float4   Intrinsics;
  float2   RenderSize;
  float    NearPlane;
  float    ConstantDepth;
  uint     UseConstantDepth;
  uint     HasPrevious;
  uint2    _pad;
};

Texture2D<float>    DepthMeters : register(t0);

RWTexture2D<float2> OutMotion   : register(u0);
RWTexture2D<float>  OutDepth    : register(u1);

[numthreads(8, 8, 1)]
void CSMotionVectors(uint3 id : SV_DispatchThreadID) {
  if (any(id.xy >= (uint2)RenderSize)) return;

  const float2 px = id.xy + 0.5;
  float d = ConstantDepth;
  if (!UseConstantDepth) {
    uint2 depthSize;
    DepthMeters.GetDimensions(depthSize.x, depthSize.y);
    const uint2 texel = min((uint2)(px / RenderSize * depthSize), depthSize - 1);
    d = DepthMeters[texel];
  }
  if (!(d > 0.05)) d = ConstantDepth;

  const float fx = Intrinsics.x, fy = Intrinsics.y, cx = Intrinsics.z, cy = Intrinsics.w;
  const float3 pCam = float3((px.x - cx) / fx * d, -(px.y - cy) / fy * d, -d);

  float2 motion = 0;
  if (HasPrevious) {
    const float4 world = mul(CurrCameraToWorld, float4(pCam, 1));
    const float3 pPrev = mul(WorldToPrevCamera, world).xyz;
    const float z = max(-pPrev.z, 1e-3);
    const float2 prevPx = float2(cx + fx * pPrev.x / z, cy - fy * pPrev.y / z);
    motion = prevPx - px;
  }

  OutMotion[id.xy] = motion;
  OutDepth[id.xy] = NearPlane / d;
}
