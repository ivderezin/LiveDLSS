cbuffer Params : register(b0) { float2 Size; float2 _pad; };
Texture2D<float4> Original : register(t0);
Texture2D<float4> Model : register(t1);
RWTexture2D<float4> Target : register(u0);

[numthreads(8, 8, 1)]
void CSPrepare(uint3 id : SV_DispatchThreadID) {
  if (any(id.xy >= (uint2)Size)) return;
  Target[id.xy] = float4(saturate(Original[id.xy].rgb), 1.0);
}

[numthreads(8, 8, 1)]
void CSResolve(uint3 id : SV_DispatchThreadID) {
  if (any(id.xy >= (uint2)Size)) return;
  Target[id.xy] = float4(saturate(Model[id.xy].rgb), Original[id.xy].a);
}
