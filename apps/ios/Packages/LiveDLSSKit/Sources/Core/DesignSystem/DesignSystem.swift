import SwiftUI

public enum DS {
    public enum Spacing {
        public static let xs: CGFloat = 4
        public static let s: CGFloat = 8
        public static let m: CGFloat = 16
        public static let l: CGFloat = 24
    }

    public enum Colors {
        public static let accent = Color(red: 0.46, green: 0.73, blue: 0.0)
        public static let overlay = Color.black.opacity(0.55)
        public static let warning = Color.orange
        public static let danger = Color.red
    }
}

public struct StatBadge: View {
    let title: String
    let value: String

    public init(_ title: String, _ value: String) {
        self.title = title
        self.value = value
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title).font(.caption2).foregroundStyle(.secondary)
            Text(value).font(.caption.monospacedDigit().weight(.semibold))
        }
        .padding(.horizontal, DS.Spacing.s)
        .padding(.vertical, DS.Spacing.xs)
        .background(DS.Colors.overlay, in: RoundedRectangle(cornerRadius: 8))
        .foregroundStyle(.white)
    }
}

public struct StatusDot: View {
    let color: Color

    public init(_ color: Color) { self.color = color }

    public var body: some View {
        Circle().fill(color).frame(width: 8, height: 8)
    }
}
