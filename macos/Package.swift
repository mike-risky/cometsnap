// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Cometsnap",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "Cometsnap", targets: ["Cometsnap"]),
    ],
    targets: [
        // Pure logic (geometry, naming, settings, annotation model). No AppKit, unit tested.
        .target(name: "CometsnapCore", path: "Sources/CometsnapCore"),
        // The menu-bar app: AppKit, ScreenCaptureKit, AVFoundation.
        .executableTarget(
            name: "Cometsnap",
            dependencies: ["CometsnapCore"],
            path: "Sources/Cometsnap",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("ScreenCaptureKit"),
                .linkedFramework("AVFoundation"),
                .linkedFramework("Carbon"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
        .testTarget(name: "CometsnapCoreTests", dependencies: ["CometsnapCore"], path: "Tests/CometsnapCoreTests"),
    ]
)
