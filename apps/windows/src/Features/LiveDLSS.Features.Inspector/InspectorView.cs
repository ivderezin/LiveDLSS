using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using LiveDLSS.Protocol;

namespace LiveDLSS.Features.Inspector;

public static class InspectorView
{
    private static readonly Vector4 Green = new(0.46f, 0.73f, 0f, 1f);
    private static readonly Vector4 Orange = new(1f, 0.6f, 0.1f, 1f);
    private static readonly string[] Modes = [Strings.ModeDlaa, Strings.ModeQuality, Strings.ModeBalanced, Strings.ModePerformance, Strings.ModeUltraPerformance];
    private static readonly string[] Views = [Strings.ViewFinal, Strings.ViewInput, Strings.ViewDepth, Strings.ViewMotionVectors];
    private static readonly string[] DepthSources = [Strings.DepthSensor, Strings.DepthPlane];

    public static void Draw(InspectorState state)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.Begin("LiveDLSS Server",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus);

        if (ImGui.BeginTable("layout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("side", ImGuiTableColumnFlags.WidthFixed, 380f);
            ImGui.TableSetupColumn("main", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.BeginChild("side");
            DrawStatus(state);
            DrawSettings(state);
            DrawNeuralRendering(state);
            ImGui.EndChild();

            ImGui.TableNextColumn();
            ImGui.BeginChild("main");
            DrawPreviews(state);
            ImGui.EndChild();

            ImGui.EndTable();
        }
        ImGui.End();
    }

    private static void DrawStatus(InspectorState s)
    {
        ImGui.SeparatorText(Strings.SectionServer);
        ImGui.Text($"GPU: {s.GpuName}");
        ImGui.TextColored(s.UpscalerName.Contains("DLSS", StringComparison.Ordinal) ? Green : Orange, string.Format(CultureInfo.CurrentCulture, Strings.Upscaler, s.UpscalerName));
        ImGui.TextWrapped(s.UpscalerStatus);
        ImGui.Text(string.Format(CultureInfo.CurrentCulture, Strings.Encoder, s.EncoderName.Length == 0 ? "—" : s.EncoderName));

        ImGui.SeparatorText(Strings.SectionClient);
        if (s.ClientConnected)
        {
            ImGui.TextColored(Green, $"● {(s.ClientName.Length == 0 ? s.ClientPeer : s.ClientName)}");
            ImGui.Text(string.Format(CultureInfo.CurrentCulture, Strings.ClientDepth, s.ClientPeer, s.ClientHasDepth ? Strings.Yes : Strings.No));
        }
        else
        {
            ImGui.TextColored(Orange, Strings.WaitingForIphone);
        }

        ImGui.SeparatorText(Strings.SectionPipeline);
        ImGui.Text(string.Format(CultureInfo.CurrentCulture, Strings.RenderToOutput, s.RenderSize.W, s.RenderSize.H, s.OutputSize.W, s.OutputSize.H));
        ImGui.Text($"FPS: {s.Stats.Fps:F1}");
        ImGui.Text($"Decode (CPU)    {s.Stats.DecodeMs,6:F2} ms");
        ImGui.Text($"Motion (GPU)    {s.Stats.MotionMs,6:F2} ms");
        ImGui.Text($"Upscale (GPU)   {s.Stats.UpscaleMs,6:F2} ms");
        if (s.NeuralRenderingEnabled) ImGui.Text($"NR (GPU)       {s.Stats.NeuralMs,6:F2} ms");
        ImGui.Text($"Encode (CPU)    {s.Stats.EncodeMs,6:F2} ms");
        ImGui.Text(string.Format(CultureInfo.CurrentCulture, Strings.Frames, s.Stats.FramesProcessed, s.Stats.DroppedFrames));
    }

    private static void DrawSettings(InspectorState s)
    {
        ImGui.SeparatorText(Strings.SectionDlss);
        var overrideSettings = s.OverrideClientSettings;
        if (ImGui.Checkbox(Strings.OverrideIphone, ref overrideSettings)) s.OverrideClientSettings = overrideSettings;
        if (!s.OverrideClientSettings) s.LocalSettings = s.ClientSettings;

        ImGui.BeginDisabled(!s.OverrideClientSettings);
        var st = s.LocalSettings;

        var enabled = st.Enabled;
        if (ImGui.Checkbox(Strings.DlssEnabled, ref enabled)) st = st with { Enabled = enabled };

        var mode = (int)st.QualityMode;
        if (ImGui.Combo(Strings.Mode, ref mode, Modes, Modes.Length)) st = st with { QualityMode = (QualityMode)mode };

        int preset = st.Preset;
        if (ImGui.SliderInt(Strings.Preset, ref preset, 0, 15)) st = st with { Preset = (byte)preset };

        var sharpness = st.Sharpness;
        if (ImGui.SliderFloat(Strings.Sharpness, ref sharpness, 0f, 1f)) st = st with { Sharpness = sharpness };

        var view = (int)st.DebugView;
        if (ImGui.Combo(Strings.ReturnToIphone, ref view, Views, Views.Length)) st = st with { DebugView = (DebugView)view };

        var depth = (int)st.DepthSource;
        if (ImGui.Combo(Strings.Depth, ref depth, DepthSources, DepthSources.Length)) st = st with { DepthSource = (DepthSource)depth };

        var plane = st.ConstantDepthMeters;
        if (ImGui.SliderFloat(Strings.PlaneMeters, ref plane, 0.3f, 10f)) st = st with { ConstantDepthMeters = plane };

        var bitrate = (int)(st.DownlinkBitrateKbps / 1000);
        if (ImGui.SliderInt(Strings.DownlinkMbps, ref bitrate, 5, 100)) st = st with { DownlinkBitrateKbps = (uint)bitrate * 1000 };

        s.LocalSettings = st;
        ImGui.EndDisabled();
    }

    private static void DrawNeuralRendering(InspectorState s)
    {
        ImGui.SeparatorText(Strings.SectionNeural);
        ImGui.TextWrapped(s.NeuralRenderingStatus);
        ImGui.BeginDisabled(!s.NeuralRenderingAvailable);
        var enabled = s.NeuralRenderingEnabled;
        if (ImGui.Checkbox(Strings.NeuralOnIphone, ref enabled)) s.NeuralRenderingEnabled = enabled;
        ImGui.BeginDisabled(s.NeuralRenderingEnabled);
        var intensity = s.NeuralIntensity;
        if (ImGui.SliderFloat(Strings.NeuralIntensity, ref intensity, 0f, 2f)) s.NeuralIntensity = intensity;
        var style = s.NeuralStyle;
        string[] styles = [Strings.StyleDefault, Strings.StyleNatural, Strings.StyleCinematic];
        if (ImGui.Combo(Strings.NeuralStyle, ref style, styles, styles.Length)) s.NeuralStyle = style;
        var preset = s.NeuralPreset;
        string[] presets = [Strings.ModelDllDefault, "A", "B"];
        if (ImGui.Combo(Strings.NeuralModel, ref preset, presets, presets.Length)) s.NeuralPreset = preset;
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.TextWrapped(Strings.NeuralHint);
    }

    private static void DrawPreviews(InspectorState s)
    {
        if (s.Previews.Count == 0)
        {
            ImGui.TextDisabled(Strings.NoFrames);
            return;
        }

        var avail = ImGui.GetContentRegionAvail().X;
        var columns = avail > 900 ? 2 : 1;
        var cellWidth = avail / columns - ImGui.GetStyle().ItemSpacing.X;

        if (ImGui.BeginTable("previews", columns))
        {
            foreach (var p in s.Previews)
            {
                ImGui.TableNextColumn();
                ImGui.Text($"{p.Label}  ({p.Width}x{p.Height})");
                var aspect = p.Width > 0 ? (float)p.Height / p.Width : 0.75f;
                unsafe
                {
                    ImGui.Image(new ImTextureRef(null, new ImTextureID(p.GpuHandle)), new Vector2(cellWidth, cellWidth * aspect));
                }
            }
            ImGui.EndTable();
        }
    }
}
