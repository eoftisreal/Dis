import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Window

ApplicationWindow {
    id: window
    required property var controller
    width: Math.max(980, Math.min(1180, Screen.width - 80)); height: Math.max(500, Math.min(760, Screen.height - 96)); minimumWidth: 980; minimumHeight: 500
    visible: true; title: "Dis · Virtual Monitor"; color: Theme.background
    font.family: Theme.font
    palette.window: Theme.card
    palette.windowText: Theme.text
    palette.text: Theme.text
    palette.button: "#263447"
    palette.buttonText: Theme.text
    palette.base: Theme.inset
    palette.highlight: Theme.accent
    palette.highlightedText: "#102b26"
    property int page: 0
    readonly property var data: controller.state
    readonly property var preferences: data.preferences || ({})
    readonly property bool active: data.active || false
    readonly property bool busy: data.busy || controller.closing
    readonly property bool connected: data.status === "connected"
    readonly property bool usb: !preferences.connectionAddress || preferences.connectionAddress === "127.0.0.1"
    readonly property string pageTitle: ["Your workspace, extended.", "Your devices", "Make it yours", "Made for a second screen"][page]
    onClosing: close => { if (controller.hideToTray()) close.accepted = false; else if (!controller.canClose) { close.accepted = false; controller.shutdown() } }

    Rectangle {
        id: sidebar; width: 220; anchors.top: parent.top; anchors.bottom: parent.bottom; color: Theme.sidebar
        Rectangle { anchors.right: parent.right; height: parent.height; width: 1; color: Theme.border }
        ColumnLayout {
            anchors.fill: parent; anchors.margins: 20; spacing: 9
            RowLayout {
                Layout.topMargin: 16; Layout.leftMargin: 9; spacing: 12
                Rectangle { width: 42; height: 42; radius: 12; color: "#19352f"; Icon { anchors.centerIn: parent; name: "monitor"; color: Theme.accent; width: 27; height: 27 } }
                ColumnLayout { spacing: 0; Text { text: "Dis"; font.family: Theme.font; font.pixelSize: 28; font.weight: Font.Bold; color: Theme.text }
                    Text { text: "VIRTUAL MONITOR"; font.family: Theme.font; font.pixelSize: 9; font.letterSpacing: 1.7; color: Theme.subtle } }
            }
            Text { text: "WORKSPACE"; color: Theme.subtle; font.pixelSize: 10; font.letterSpacing: 1.8; Layout.topMargin: 40; Layout.bottomMargin: 8; Layout.leftMargin: 14 }
            Repeater {
                model: [{label:"Display",icon:"monitor"},{label:"Devices",icon:"devices"},{label:"Settings",icon:"settings"}]
                delegate: ItemDelegate {
                    id: navigation
                    required property int index
                    required property var modelData
                    Layout.fillWidth: true; implicitHeight: 48
                    Accessible.name: modelData.label
                    highlighted: window.page === index
                    onClicked: window.page = index
                    background: Rectangle { radius: 9; color: window.page === navigation.index ? "#1a302f" : navigation.hovered ? "#171f2c" : "transparent" }
                    contentItem: RowLayout { spacing: 12; Icon { name: navigation.modelData.icon; color: window.page === navigation.index ? Theme.accent : Theme.muted; Layout.leftMargin: 7 }
                        Text { text: navigation.modelData.label; color: window.page === navigation.index ? Theme.accent : Theme.muted; font.pixelSize: 14; font.weight: window.page === navigation.index ? Font.DemiBold : Font.Normal; Layout.fillWidth: true } }
                }
            }
            Item { Layout.fillHeight: true }
            Rectangle { visible: window.height >= 660; Layout.fillWidth: true; implicitHeight: 92; radius: 10; color: "#121d28"; border.color: "#243142"
                ColumnLayout { anchors.fill: parent; anchors.margins: 14; spacing: 6
                    RowLayout { Rectangle { width: 7; height: 7; radius: 4; color: window.connected ? Theme.accent : window.data.phoneOnline ? Theme.blue : Theme.subtle }
                        Text { text: window.connected ? "Display connected" : window.data.phoneOnline ? "USB device ready" : "Waiting for a device"; color: Theme.text; font.pixelSize: 11; Layout.fillWidth: true } }
                    Text { text: window.connected ? "Your extra room to create." : window.data.phoneOnline ? "Your device is ready.\nChoose Connect display." : "Plug in your Android device\nto get started."; color: Theme.muted; font.pixelSize: 10; lineHeight: 1.3; Layout.fillWidth: true }
                }
            }
            AppButton { text: "About Dis"; iconName: "info"; quiet: true; Layout.fillWidth: true; Layout.topMargin: 8; onClicked: window.page = 3 }
            Text { text: "0.4 PREVIEW  /  WINDOWS"; font.pixelSize: 9; font.letterSpacing: 1.2; color: Theme.subtle; Layout.leftMargin: 13; Layout.bottomMargin: 5 }
        }
    }

    Item {
        id: main; anchors.left: sidebar.right; anchors.right: parent.right; anchors.top: parent.top; anchors.bottom: footer.top
        ColumnLayout {
            anchors.fill: parent; anchors.leftMargin: 32; anchors.rightMargin: 32; anchors.topMargin: 32; spacing: 20
            RowLayout {
                Layout.fillWidth: true
                ColumnLayout { spacing: 7; Layout.fillWidth: true
                    Text { text: window.pageTitle; color: Theme.text; font.family: Theme.font; font.pixelSize: 28; font.weight: Font.DemiBold }
                    Text { text: ["A little more space. A lot more possibility.","Everything you need for a seamless connection.","Your display. Your preferences.","An independent virtual monitor for Windows and Android."][window.page]; color: Theme.muted; font.pixelSize: 12 }
                }
                Rectangle { implicitWidth: statusText.implicitWidth+28; implicitHeight: 30; radius: 15; color: window.connected ? "#18382f" : "#202a39"; border.color: window.connected ? "#2a6051" : Theme.border
                    Text { id: statusText; anchors.centerIn: parent; text: window.connected ? "●  Connected" : window.busy ? "●  Working" : "●  Standby"; color: window.connected ? Theme.accent : Theme.muted; font.pixelSize: 11; Accessible.role: Accessible.StaticText; Accessible.name: text } }
            }
            Rectangle {
                visible: window.data.status === "error"
                Layout.fillWidth: true; implicitHeight: errorText.implicitHeight+28; radius: 10; color: "#34212a"; border.color: "#63404b"
                Text { id: errorText; anchors.fill: parent; anchors.margins: 14; text: window.data.message || ""; color: Theme.error; font.pixelSize: 12; wrapMode: Text.Wrap }
            }
            StackLayout {
                currentIndex: window.page; Layout.fillWidth: true; Layout.fillHeight: true
                DisplayPage { app: window.controller }
                DevicesPage { app: window.controller }
                SettingsPage { app: window.controller }
                AboutPage { app: window.controller }
            }
        }
    }
    Rectangle {
        id: footer; anchors.left: sidebar.right; anchors.right: parent.right; anchors.bottom: parent.bottom; height: 88; color: "#121a25"
        Rectangle { anchors.top: parent.top; width: parent.width; height: 1; color: Theme.border }
        RowLayout {
            anchors.fill: parent; anchors.leftMargin: 32; anchors.rightMargin: 32; spacing: 14
            Rectangle { width: 36; height: 36; radius: 10; color: window.connected ? "#19392f" : "#202b3b"; Icon { anchors.centerIn: parent; width: 19; height: 19; name: window.connected ? "check" : window.usb ? "usb" : "link"; color: window.connected ? Theme.accent : Theme.muted } }
            ColumnLayout { spacing: 4; Layout.fillWidth: true
                Text { text: window.controller.closing ? "Disconnecting…" : window.connected ? "Your Android display is connected" : window.busy ? "Preparing your workspace…" : window.active ? "Waiting for your Android receiver" : window.data.phoneOnline ? "Ready to connect" : "Connect an Android device"; color: Theme.text; font.pixelSize: 13; font.weight: Font.DemiBold; Accessible.role: Accessible.StaticText; Accessible.name: text }
                Text { text: window.controller.closing ? "Restoring your Windows display layout." : window.busy ? (window.data.message || "") : window.connected ? (window.preferences.input !== false ? "Encrypted connection · Remote input enabled" : "Encrypted connection · Viewing only") : window.usb ? "USB pairs automatically. No connection link needed." : "Pair using the link in Settings."; color: Theme.muted; font.pixelSize: 10; elide: Text.ElideRight; Layout.fillWidth: true }
            }
            BusyIndicator { running: window.busy; visible: running; implicitWidth: 26; implicitHeight: 26; palette.dark: Theme.accent }
            AppButton { text: window.active ? "Disconnect" : "Connect display"; iconName: window.active ? "" : "arrow"; primary: !window.active; implicitWidth: 166; implicitHeight: 46
                enabled: window.controller.ready && !window.busy && (window.active || ((!window.usb || window.data.phoneOnline) && window.data.driverInstalled))
                onClicked: window.controller.command(window.active ? "disconnect" : "connect") }
        }
    }
    Rectangle {
        id: notification; anchors.horizontalCenter: parent.horizontalCenter; anchors.bottom: footer.top; anchors.bottomMargin: 14; z: 100
        width: toastText.implicitWidth + 40; height: 42; radius: 10; color: "#29483f"; border.color: "#427263"; visible: false
        Text { id: toastText; anchors.centerIn: parent; color: Theme.text; font.pixelSize: 12 }
        Timer { id: toastTimer; interval: 2400; onTriggered: notification.visible = false }
    }
    Connections { target: window.controller; function onToast(message) { toastText.text=message; notification.visible=true; toastTimer.restart() } }
}
