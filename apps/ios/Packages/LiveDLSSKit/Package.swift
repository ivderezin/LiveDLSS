// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "LiveDLSSKit",
    defaultLocalization: "en",
    platforms: [.iOS(.v17)],
    products: [
        .library(
            name: "LiveDLSSKit",
            targets: [
                "CaptureFeature", "ConnectionFeature", "LiveFeature", "SettingsFeature",
            ]),
        .library(name: "LiveSession", targets: ["LiveSession"]),
    ],
    targets: [
        .target(name: "WireProtocol", path: "Sources/Core/WireProtocol"),
        .target(
            name: "SharedDomain",
            dependencies: ["WireProtocol"],
            path: "Sources/Core/SharedDomain",
            resources: [.process("Resources")]
        ),
        .target(
            name: "Transport",
            dependencies: ["WireProtocol"],
            path: "Sources/Core/Transport",
            resources: [.process("Resources")]
        ),
        .target(name: "MediaCodec", path: "Sources/Core/MediaCodec"),
        .target(name: "DesignSystem", path: "Sources/Core/DesignSystem"),
        .target(name: "LiveSession", path: "Sources/Core/LiveSession"),

        .target(
            name: "CaptureFeature",
            dependencies: ["SharedDomain", "MediaCodec"],
            path: "Sources/Features/Capture"
        ),
        .target(
            name: "ConnectionFeature",
            dependencies: ["SharedDomain", "Transport", "DesignSystem"],
            path: "Sources/Features/Connection",
            resources: [.process("Resources")]
        ),
        .target(
            name: "SettingsFeature",
            dependencies: ["SharedDomain", "DesignSystem"],
            path: "Sources/Features/Settings",
            resources: [.process("Resources")]
        ),
        .target(
            name: "LiveFeature",
            dependencies: ["SharedDomain", "Transport", "MediaCodec", "WireProtocol", "DesignSystem", "LiveSession"],
            path: "Sources/Features/Live",
            resources: [.process("Resources")]
        ),
    ]
)
