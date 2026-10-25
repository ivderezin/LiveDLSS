DOTNET ?= dotnet
IOS_PKG := apps/ios/Packages/LiveDLSSKit
IOS_DEVICE ?=
IOS_APP := apps/ios/build/DerivedData/Build/Products/Debug-iphoneos/LiveDLSS.app
SWIFT_SOURCES := App Widgets/Sources Packages/LiveDLSSKit/Sources
WIN_DIR := apps/windows

.PHONY: help ios-project ios-build ios-run win-build win-run win-package lint format

help:
	@grep -E '^[a-z-]+:.*## ' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  %-14s %s\n", $$1, $$2}'

ios-project: ## Generate LiveDLSS.xcodeproj (requires xcodegen and Configs/Local.xcconfig)
	@test -f apps/ios/Configs/Local.xcconfig || { echo "Create apps/ios/Configs/Local.xcconfig from Local.xcconfig.example"; exit 1; }
	cd apps/ios && xcodegen generate

ios-run: ios-project ## Build, install and launch on iPhone: make ios-run IOS_DEVICE=<UDID>
	@test -n "$(IOS_DEVICE)" || { echo "Set IOS_DEVICE=<UDID> (xcrun devicectl list devices)"; exit 1; }
	cd apps/ios && xcodebuild -project LiveDLSS.xcodeproj -scheme LiveDLSS -configuration Debug \
		-destination 'id=$(IOS_DEVICE)' -derivedDataPath build/DerivedData -allowProvisioningUpdates build
	xcrun devicectl device install app --device $(IOS_DEVICE) $(IOS_APP)
	xcrun devicectl device process launch --device $(IOS_DEVICE) \
		$$(/usr/libexec/PlistBuddy -c "Print CFBundleIdentifier" $(IOS_APP)/Info.plist)

ios-build: ## Build the iOS feature package
	cd $(IOS_PKG) && xcodebuild -scheme LiveDLSSKit -destination 'generic/platform=iOS' build

win-build: ## Build the C# server (compile check only on macOS/Linux)
	cd $(WIN_DIR) && $(DOTNET) build LiveDLSS.Server.slnx -c Release

win-package: ## Build a versioned Windows ZIP in dist/
	DOTNET=$(DOTNET) tools/windows-package/package.sh

win-run: ## Run the server (Windows)
	cd $(WIN_DIR) && $(DOTNET) run --project src/App/LiveDLSS.Server -c Release

lint: ## Check Swift and C# formatting
	cd apps/ios && xcrun swift-format lint --strict -r $(SWIFT_SOURCES)
	cd $(WIN_DIR) && $(DOTNET) format LiveDLSS.Server.slnx --verify-no-changes

format: ## Format Swift and C#
	cd apps/ios && xcrun swift-format format -i -r $(SWIFT_SOURCES)
	cd $(WIN_DIR) && $(DOTNET) format LiveDLSS.Server.slnx
