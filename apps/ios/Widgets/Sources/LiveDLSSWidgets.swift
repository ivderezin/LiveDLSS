import ActivityKit
import LiveSession
import SwiftUI
import WidgetKit

@main
struct LiveDLSSWidgets: WidgetBundle {
    var body: some Widget {
        LiveSessionWidget()
    }
}

private let accent = Color(red: 0.46, green: 0.73, blue: 0.0)

struct LiveSessionWidget: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: LiveSessionAttributes.self) { context in
            LockScreenView(attributes: context.attributes, state: context.state)
                .activityBackgroundTint(.black.opacity(0.7))
                .activitySystemActionForegroundColor(.white)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Label {
                        Text(verbatim: "LiveDLSS")
                    } icon: {
                        Image(systemName: "camera.aperture").foregroundStyle(accent)
                    }
                    .font(.headline)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    StatusView(state: context.state)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    ServerView(attributes: context.attributes, state: context.state)
                }
            } compactLeading: {
                Image(systemName: "camera.aperture").foregroundStyle(accent)
            } compactTrailing: {
                if context.state.isPaused {
                    Image(systemName: "pause.fill")
                } else {
                    Text(context.state.startedAt, style: .timer)
                        .monospacedDigit()
                        .frame(maxWidth: 48)
                }
            } minimal: {
                Image(systemName: context.state.isPaused ? "pause.fill" : "camera.aperture")
                    .foregroundStyle(accent)
            }
        }
    }
}

private struct LockScreenView: View {
    let attributes: LiveSessionAttributes
    let state: LiveSessionAttributes.ContentState

    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            Image(systemName: "camera.aperture")
                .font(.title2)
                .foregroundStyle(accent)
            VStack(alignment: .leading, spacing: 2) {
                Text(verbatim: "LiveDLSS").font(.headline)
                ServerView(attributes: attributes, state: state)
            }
            Spacer()
            StatusView(state: state)
        }
        .foregroundStyle(.white)
        .padding()
    }
}

private struct ServerView: View {
    let attributes: LiveSessionAttributes
    let state: LiveSessionAttributes.ContentState

    var body: some View {
        HStack(spacing: 6) {
            Text(verbatim: "\(attributes.serverName) · \(attributes.gpuName)")
                .lineLimit(1)
            if state.dlssEnabled {
                Text(verbatim: "DLSS").fontWeight(.bold).foregroundStyle(accent)
            }
        }
        .font(.caption)
        .foregroundStyle(.secondary)
    }
}

private struct StatusView: View {
    let state: LiveSessionAttributes.ContentState

    var body: some View {
        VStack(alignment: .trailing, spacing: 2) {
            if state.isPaused {
                Text("Paused").font(.subheadline.weight(.semibold))
                Text("Open the app to resume").font(.caption2).foregroundStyle(.secondary)
            } else {
                Text("Streaming").font(.subheadline.weight(.semibold)).foregroundStyle(accent)
                Text(state.startedAt, style: .timer).font(.caption.monospacedDigit())
            }
        }
        .multilineTextAlignment(.trailing)
    }
}
