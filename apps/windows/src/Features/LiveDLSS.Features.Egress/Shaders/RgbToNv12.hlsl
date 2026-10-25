cbuffer Params : register(b0) {
  uint2 Size;
  uint2 _pad;
};

Texture2D<float4> Source : register(t0);
RWTexture2D<unorm float>  OutY  : register(u0);
RWTexture2D<unorm float2> OutUV : register(u1);

float LumaOf(float3 rgb) {
  return dot(rgb, float3(0.2126, 0.7152, 0.0722));
}

[numthreads(8, 8, 1)]
void CSRgbToNv12(uint3 id : SV_DispatchThreadID) {
  const uint2 base = id.xy * 2;
  if (any(base >= Size)) return;

  float3 sum = 0;
  [unroll]
  for (uint i = 0; i < 4; i++) {
    const uint2 p = min(base + uint2(i & 1, i >> 1), Size - 1);
    const float3 rgb = saturate(Source[p].rgb);
    OutY[p] = (16.0 + 219.0 * LumaOf(rgb)) / 255.0;
    sum += rgb;
  }

  const float3 rgb = sum * 0.25;
  const float luma = LumaOf(rgb);
  const float cb = (rgb.b - luma) / 1.8556;
  const float cr = (rgb.r - luma) / 1.5748;
  OutUV[id.xy] = (128.0 + 224.0 * float2(cb, cr)) / 255.0;
}
