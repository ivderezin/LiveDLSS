using System.Numerics;
using System.Runtime.InteropServices;
using LiveDLSS.Core.Gpu;
using LiveDLSS.Core.Math;
using LiveDLSS.Protocol;
using Vortice.Direct3D12;

namespace LiveDLSS.Features.MotionVectors;

public sealed record MotionInputs(
    Texture2D? DepthMeters,
    CameraIntrinsics Intrinsics,
    Matrix4x4 CameraToWorld,
    Matrix4x4? PreviousCameraToWorld,
    float ConstantDepthMeters);

public sealed class MotionVectorPass : IDisposable
{
    private const ResourceStates SrvState = ResourceStates.NonPixelShaderResource;

    private readonly ComputeKernel _motionKernel;
    private readonly ComputeKernel _vizKernel;

    public MotionVectorPass(GpuContext gpu)
    {
        var assembly = typeof(MotionVectorPass).Assembly;
        _motionKernel = new ComputeKernel(gpu, assembly, "MotionVectors.hlsl", "CSMotionVectors");
        _vizKernel = new ComputeKernel(gpu, assembly, "MotionViz.hlsl", "CSVisualize");
    }

    public void Execute(ID3D12GraphicsCommandList cmd, MotionInputs input, Texture2D outMotion, Texture2D outDepth)
    {
        var p = new MotionParams
        {
            CurrCameraToWorld = input.CameraToWorld,
            Intrinsics = new Vector4(input.Intrinsics.Fx, input.Intrinsics.Fy, input.Intrinsics.Cx, input.Intrinsics.Cy),
            RenderSize = new Vector2(outMotion.Width, outMotion.Height),
            NearPlane = CameraMath.NearPlane,
            ConstantDepth = input.ConstantDepthMeters,
            UseConstantDepth = input.DepthMeters is null ? 1u : 0u,
        };
        if (input.PreviousCameraToWorld is { } previous)
        {
            p.WorldToPrevCamera = CameraMath.Inverse(previous);
            p.HasPrevious = 1;
        }

        Span<CpuDescriptorHandle> srvs = stackalloc CpuDescriptorHandle[1];
        var srvCount = 0;
        if (input.DepthMeters is { } depth)
        {
            depth.Transition(cmd, SrvState);
            srvs[srvCount++] = depth.Srv;
        }
        outMotion.Transition(cmd, ResourceStates.UnorderedAccess);
        outDepth.Transition(cmd, ResourceStates.UnorderedAccess);

        _motionKernel.Dispatch(cmd, p, srvs[..srvCount], [outMotion.Uav, outDepth.Uav], outMotion.Width, outMotion.Height);
        outMotion.UavBarrier(cmd);
        outDepth.UavBarrier(cmd);
    }

    public void Visualize(ID3D12GraphicsCommandList cmd, Texture2D motion, Texture2D depth, Texture2D outMotionViz, Texture2D outDepthViz)
    {
        var p = new VizParams
        {
            Size = new Vector2(motion.Width, motion.Height),
            NearPlane = CameraMath.NearPlane,
            MaxDepth = 5f,
            MotionScale = 24f,
        };

        motion.Transition(cmd, SrvState);
        depth.Transition(cmd, SrvState);
        outMotionViz.Transition(cmd, ResourceStates.UnorderedAccess);
        outDepthViz.Transition(cmd, ResourceStates.UnorderedAccess);

        _vizKernel.Dispatch(cmd, p, [motion.Srv, depth.Srv], [outMotionViz.Uav, outDepthViz.Uav], motion.Width, motion.Height);
        outMotionViz.UavBarrier(cmd);
        outDepthViz.UavBarrier(cmd);
    }

    public void Dispose()
    {
        _motionKernel.Dispose();
        _vizKernel.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MotionParams
    {
        public Matrix4x4 CurrCameraToWorld;
        public Matrix4x4 WorldToPrevCamera;
        public Vector4 Intrinsics;
        public Vector2 RenderSize;
        public float NearPlane;
        public float ConstantDepth;
        public uint UseConstantDepth;
        public uint HasPrevious;
        private readonly uint _pad0;
        private readonly uint _pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VizParams
    {
        public Vector2 Size;
        public float NearPlane;
        public float MaxDepth;
        public float MotionScale;
        private readonly float _pad0;
        private readonly float _pad1;
        private readonly float _pad2;
    }
}
