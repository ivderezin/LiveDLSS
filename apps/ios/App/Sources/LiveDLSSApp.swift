import SwiftUI

@main
struct LiveDLSSApp: App {
    @State private var container = AppContainer()

    var body: some Scene {
        WindowGroup {
            RootView(container: container)
                .preferredColorScheme(.dark)
        }
    }
}
