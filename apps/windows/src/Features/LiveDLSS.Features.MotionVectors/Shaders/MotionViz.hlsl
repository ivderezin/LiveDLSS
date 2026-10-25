cbuffer VizParams : register(b0) {
  float2 VizSize;
  float  VizNearPlane;
  float  VizMaxDepth;
  float  VizMotionScale;
  float3 _vizPad;
};

Texture2D<float2>  InMotion : register(t0);
Texture2D<float>   InDepth  : register(t1);
RWTexture2D<unorm float4> OutMotionViz : register(u0);
RWTexture2D<unorm float4> OutDepthViz  : register(u1);

float3 Turbo(float t) {
  t = saturate(t);
  const float4 kR = float4(0.13572138, 4.61539260, -42.66032258, 132.13108234);
  const float4 kG = float4(0.09140261, 2.19418839, 4.84296658, -14.18503333);
  const float4 kB = float4(0.10667330, 12.64194608, -60.58204836, 110.36276771);
  const float2 kR2 = float2(-152.94239396, 59.28637943);
  const float2 kG2 = float2(4.27729857, 2.82956604);
  const float2 kB2 = float2(-89.90310912, 27.34824973);
  const float4 v4 = float4(1, t, t * t, t * t * t);
  const float2 v2 = v4.zw * v4.z;
  return float3(dot(v4, kR) + dot(v2, kR2), dot(v4, kG) + dot(v2, kG2), dot(v4, kB) + dot(v2, kB2));
}

[numthreads(8, 8, 1)]
void CSVisualize(uint3 id : SV_DispatchThreadID) {
  if (any(id.xy >= (uint2)VizSize)) return;

  const float2 mv = InMotion[id.xy] / VizMotionScale;
  const float magnitude = saturate(length(mv));
  const float angle = atan2(mv.y, mv.x) / (2 * 3.14159265) + 0.5;
  const float3 hue = saturate(abs(frac(angle + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1);
  OutMotionViz[id.xy] = float4(lerp(float3(1, 1, 1), hue, magnitude), 1);

  const float ndc = InDepth[id.xy];
  const float meters = ndc > 0 ? VizNearPlane / ndc : VizMaxDepth;
  OutDepthViz[id.xy] = float4(Turbo(1 - meters / VizMaxDepth), 1);
}
