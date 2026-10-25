using System.Numerics;
using LiveDLSS.Protocol;

namespace LiveDLSS.Core.Math;

public static class CameraMath
{
    public const float NearPlane = 0.1f;

    public static Matrix4x4 FromArKitColumnMajor(ReadOnlySpan<float> m) =>
        m.Length != 16
            ? throw new ArgumentException("Expected 16 floats", nameof(m))
            : new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);

    public static Matrix4x4 ProjectionFromIntrinsics(CameraIntrinsics k, float width, float height, float near = NearPlane)
    {
        var p = new Matrix4x4
        {
            M11 = 2f * k.Fx / width,
            M31 = -(2f * k.Cx / width - 1f),
            M22 = 2f * k.Fy / height,
            M32 = -(1f - 2f * k.Cy / height),
            M43 = near,
            M34 = -1f,
        };
        return p;
    }

    public static Matrix4x4 Inverse(Matrix4x4 m) =>
        Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity;

    public static Vector3 Position(Matrix4x4 cameraToWorld) => cameraToWorld.Translation;
    public static Vector3 Right(Matrix4x4 cameraToWorld) => new(cameraToWorld.M11, cameraToWorld.M12, cameraToWorld.M13);
    public static Vector3 Up(Matrix4x4 cameraToWorld) => new(cameraToWorld.M21, cameraToWorld.M22, cameraToWorld.M23);
    public static Vector3 Forward(Matrix4x4 cameraToWorld) => -new Vector3(cameraToWorld.M31, cameraToWorld.M32, cameraToWorld.M33);

    public static float VerticalFov(CameraIntrinsics k, float height) => 2f * MathF.Atan(height / (2f * k.Fy));

    public static CameraIntrinsics Scaled(this CameraIntrinsics k, float s) => new(k.Fx * s, k.Fy * s, k.Cx * s, k.Cy * s);
}
