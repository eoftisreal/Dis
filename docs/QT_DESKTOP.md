# Qt desktop

The default Windows app is a native C++/Qt Quick frontend built with the supplied Qt 6.8.3 MSVC kit. It has a persistent navigation rail, a workspace illustration, connection/device status, compact display controls, live metrics, separate device/setup and settings pages, and an About page with Qt attribution. The main connection action stays visible while content scrolls on smaller displays. The interface supports high DPI, keyboard focus, and named accessibility controls.

`Dis.exe` owns presentation. `Dis.Engine.exe` owns streaming, pairing, USB, display modes, and virtual-adapter cleanup. The processes exchange bounded JSON-line messages through inherited pipes. There is no new HTTP control server or network control port. Pairing secrets are not saved in preferences. Closing the window requests an orderly engine shutdown; the existing elevated adapter helper remains responsible for cleanup if the engine exits unexpectedly.

Source layout:

- `ui/qml/Main.qml`: window, navigation, status, and persistent connection action.
- `DisplayPage.qml`, `DevicesPage.qml`, `SettingsPage.qml`, `AboutPage.qml`: working application pages.
- `Theme.qml`, `AppButton.qml`, `AppCombo.qml`, `AppSwitch.qml`, `Icon.qml`, `SectionCard.qml`: shared design components.
- `ui/DesktopBridge.cpp`: typed Qt properties, commands, file selection, and the private engine connection.
- `src/Dis.Host/HostController.cs`: GUI-independent Windows session ownership.
- `QtEngineContext.cs`: process-pipe adapter running on the Windows message loop.

Build with `BUILD_ALL.cmd -Offline` once dependencies are cached. The kit in the sibling `EngineTest/deps/qt` directory is discovered automatically. `-QtRoot` or `QT_ROOT` supports another installation. All generated Qt build files are under `artifacts/qt-desktop`; the source Qt installation is not modified. The package deploys Qt runtime libraries, QML plugins, MSVC runtime DLLs, notices, and licenses, and does not require Qt installed on the target PC.

To capture a UI preview without connecting a display:

```bat
artifacts\Dis-Complete\Dis.exe --ui-smoke artifacts\qt-preview.png
```

Add `--ui-page 1`, `2`, or `3` to inspect Devices, Settings, or About. These checks briefly open the UI and close its engine afterward. The existing `--display-capture-check` and `--display-lifecycle-check` are forwarded to the engine and retain their real-display behavior. The optional `-Classic` build is a diagnostic fallback.
