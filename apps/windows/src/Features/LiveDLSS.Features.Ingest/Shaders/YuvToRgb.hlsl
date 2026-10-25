cbuffer Params : register(b0) {
  float2 Size;
  uint   FullRange;
  uint   Bt709;
};

Texture2D<float> PlaneY : register(t0);
Texture2D<float> PlaneU : register(t1);
Texture2D<float> PlaneV : register(t2);
SamplerState LinearClamp : register(s0);
RWTexture2D<unorm float4> Output : register(u0);

[numthreads(8, 8, 1)]
void CSYuvToRgb(uint3 id : SV_DispatchThreadID) {
  if (any(id.xy >= (uint2)Size)) return;

  const float2 uv = (id.xy + 0.5) / Size;
  float y = PlaneY[id.xy];
  float u = PlaneU.SampleLevel(LinearClamp, uv, 0) - 128.0 / 255.0;
  float v = PlaneV.SampleLevel(LinearClamp, uv, 0) - 128.0 / 255.0;
  if (!FullRange) {
    y = (y - 16.0 / 255.0) * (255.0 / 219.0);
    u *= 255.0 / 224.0;
    v *= 255.0 / 224.0;
  }

  const float3 rgb = Bt709
    ? float3(y + 1.5748 * v, y - 0.1873 * u - 0.4681 * v, y + 1.8556 * u)
    : float3(y + 1.402 * v, y - 0.344136 * u - 0.714136 * v, y + 1.772 * u);
  Output[id.xy] = float4(saturate(rgb), 1);
}
