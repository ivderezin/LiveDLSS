cbuffer Params : register(b0) {
  float2 OutputSize;
  float2 _pad;
};

Texture2D<float4> Source : register(t0);
SamplerState LinearClamp : register(s0);
RWTexture2D<unorm float4> Output : register(u0);

[numthreads(8, 8, 1)]
void CSBilinear(uint3 id : SV_DispatchThreadID) {
  if (any(id.xy >= (uint2)OutputSize)) return;
  Output[id.xy] = Source.SampleLevel(LinearClamp, (id.xy + 0.5) / OutputSize, 0);
}
