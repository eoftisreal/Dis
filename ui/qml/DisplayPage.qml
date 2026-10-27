import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
    id: page
    required property var app
    readonly property var data: app.state
    readonly property var p: data.preferences || ({})
    readonly property var metrics: data.metrics || ({})
    readonly property bool connected: data.status === "connected"
    readonly property bool editable: app.ready && !data.busy && !data.active
    clip: true; contentWidth: availableWidth
    ScrollBar.horizontal.policy: ScrollBar.AlwaysOff
    ColumnLayout {
        width: page.availableWidth; spacing: 16
        SectionCard {
            visible: page.app.ready && !page.data.driverInstalled
            Layout.fillWidth: true; implicitHeight: 84
            RowLayout { anchors.fill: parent; spacing: 14
                Icon { name: "monitor"; color: Theme.accent }
                ColumnLayout { spacing: 5; Layout.fillWidth: true
                    Text { text: "Set up your second display"; color: Theme.text; font.pixelSize: 14; font.weight: Font.DemiBold }
                    Text { text: "One-time setup enables a real extended Windows desktop."; color: Theme.muted; font.pixelSize: 11 }
                }
                AppButton { text: "Set up display"; enabled: !page.data.busy; primary: true; onClicked: page.app.command("setup") }
            }
        }
        SectionCard {
            Layout.fillWidth: true; implicitHeight: 82
            RowLayout { anchors.fill: parent; spacing: 14
                Rectangle { width: 40; height: 40; radius: 11; color: "#1c3337"; Icon { anchors.centerIn: parent; name: "phone"; color: Theme.accent; width: 24; height: 24 } }
                ColumnLayout { spacing: 4; Layout.fillWidth: true
                    Text { text: page.data.phoneOnline ? ((page.data.devices || []).filter(d => d.state === "device")[0]?.model || "Android device") : "An extra screen is one cable away"; color: Theme.text; font.pixelSize: 14; font.weight: Font.DemiBold; Layout.fillWidth: true }
                    Text { text: page.data.phoneOnline ? "Authorized and ready over USB" : "Connect your Android device and allow USB debugging."; color: Theme.muted; font.pixelSize: 11; Layout.fillWidth: true }
                }
                RowLayout { visible: page.connected; spacing: 22
                    ColumnLayout { spacing: 1; Text { text: page.metrics.fps ? Number(page.metrics.fps).toFixed(1) : "—"; color: Theme.accent; font.pixelSize: 23; font.weight: Font.DemiBold; Accessible.role: Accessible.StaticText; Accessible.name: "Frame rate " + text + " fps" }
                        Text { text: "FPS"; color: Theme.subtle; font.pixelSize: 8; font.letterSpacing: 1 } }
                    ColumnLayout { spacing: 1; Text { text: page.metrics.ackMs ? Number(page.metrics.ackMs).toFixed(0)+" ms" : "—"; color: Theme.text; font.pixelSize: 23; font.weight: Font.DemiBold; Accessible.role: Accessible.StaticText; Accessible.name: "Frame acknowledgement " + text }
                        Text { text: "FRAME ACK"; color: Theme.subtle; font.pixelSize: 8; font.letterSpacing: 1 } }
                }
                Rectangle { visible: page.data.phoneOnline && !page.connected; width: 8; height: 8; radius: 4; color: Theme.accent }
                Text { visible: page.data.phoneOnline && !page.connected; text: "USB"; color: Theme.accent; font.pixelSize: 11; font.weight: Font.DemiBold }
            }
        }
        SectionCard {
            id: preview
            Layout.fillWidth: true; implicitHeight: 230
            padding: 0
            Canvas { anchors.fill: parent; onPaint: {let c=getContext("2d");c.fillStyle="#2b3748";for(let x=18;x<width;x+=20)for(let y=18;y<height;y+=20){c.beginPath();c.arc(x,y,0.7,0,Math.PI*2);c.fill()}} }
            Row { anchors.left: parent.left; anchors.top: parent.top; anchors.margins: 20; spacing: 7
                Icon { width: 14; height: 14; name: "monitor"; color: Theme.subtle }
                Text { text: "YOUR WORKSPACE"; color: Theme.subtle; font.pixelSize: 9; font.letterSpacing: 1.5 }
            }
            RowLayout {
                anchors.centerIn: parent; anchors.verticalCenterOffset: 8; spacing: 27
                ColumnLayout { spacing: 0
                    Rectangle { width: 206; height: 124; radius: 8; color: "#222f42"; border.color: "#4a5d77"; border.width: 2
                        Rectangle { anchors.fill: parent; anchors.margins: 7; radius: 3; gradient: Gradient { GradientStop { position:0; color:"#294455" } GradientStop { position:1; color:"#172734" } }
                            Row { anchors.left: parent.left; anchors.top: parent.top; anchors.margins: 11; spacing: 3; Repeater { model:3; Rectangle { width:4; height:4; radius:2; color: "#658494" } } }
                            Rectangle { x:12; y:28; width:48; height:66; radius:3; color:"#355264" }
                            Column { x:68; y:28; spacing:6; Repeater { model:3; Rectangle { width:107; height:18; radius:3; color: index===0 ? "#466a7b" : "#2e4a5c" } } }
                        }
                    }
                    Rectangle { width: 20; height: 10; color: "#3c4c61"; Layout.alignment: Qt.AlignHCenter }
                    Rectangle { width: 70; height: 4; radius: 2; color: "#4a5d77"; Layout.alignment: Qt.AlignHCenter }
                    Text { text: "Windows PC"; color: Theme.muted; font.pixelSize: 11; Layout.alignment: Qt.AlignHCenter; Layout.topMargin: 9 }
                }
                ColumnLayout { spacing: 6; Layout.alignment: Qt.AlignVCenter; Layout.bottomMargin: 16
                    Row { spacing: 5; Repeater { model: 4; Rectangle { required property int index; width: 4; height: 4; radius: 2; color: page.connected ? Theme.accent : "#48586c"; opacity: page.connected ? 0.4 + index * 0.15 : 0.6 } } }
                    Text { text: "EXTEND"; color: page.connected ? Theme.accent : Theme.subtle; font.pixelSize: 8; font.letterSpacing: 1.2; Layout.alignment: Qt.AlignHCenter }
                }
                ColumnLayout { spacing: 9
                    Rectangle { width: (page.p.orientation || 0) % 2 ? 77 : 150; height: (page.p.orientation || 0) % 2 ? 128 : 98; radius: 11; color: "#172d2c"; border.width: 2; border.color: page.connected ? Theme.accent : "#437366"
                        Rectangle { anchors.fill: parent; anchors.margins: 7; radius: 4; gradient: Gradient { GradientStop { position:0; color: "#205447" } GradientStop { position:1; color: "#17332e" } }
                            Icon { anchors.centerIn: parent; name: "monitor"; width: 29; height: 29; color: page.connected ? Theme.accent : "#5b8a7d" }
                            Rectangle { anchors.bottom: parent.bottom; anchors.bottomMargin: 6; anchors.horizontalCenter: parent.horizontalCenter; width: 26; height: 3; radius: 2; color: "#498b77" }
                        }
                    }
                    Text { text: "Android display"; color: page.connected ? Theme.accent : Theme.muted; font.pixelSize: 11; Layout.alignment: Qt.AlignHCenter }
                }
            }
        }
        SectionCard {
            Layout.fillWidth: true; implicitHeight: options.implicitHeight + 44
            ColumnLayout {
                id: options; anchors.fill: parent; spacing: 18
                RowLayout { Layout.fillWidth: true; spacing: 14
                    ColumnLayout { Layout.fillWidth: true; spacing: 8
                        Text { text: "Desktop size"; color: Theme.muted; font.pixelSize: 11 }
                        AppCombo { accessibleLabel: "Desktop size"; Layout.fillWidth: true; enabled: page.editable; model: ["Match phone shape", "Full HD · 1920 × 1080", "Large text · compact"]; currentIndex: page.p.desktop || 0; onActivated: index => page.app.configure({desktop:index}) }
                    }
                    ColumnLayout { Layout.fillWidth: true; spacing: 8
                        Text { text: "Performance"; color: Theme.muted; font.pixelSize: 11 }
                        AppCombo { accessibleLabel: "Performance"; Layout.fillWidth: true; enabled: page.editable; model: ["Fast · 720p / 60 fps", "Balanced · 1080p / 30 fps", "Sharp · 1080p / 60 fps"]; currentIndex: page.p.quality || 0; onActivated: index => page.app.configure({quality:index,fps:index===1?30:60}) }
                    }
                    ColumnLayout { Layout.fillWidth: true; spacing: 8
                        Text { text: "Orientation"; color: Theme.muted; font.pixelSize: 11 }
                        AppCombo { accessibleLabel: "Orientation"; Layout.fillWidth: true; enabled: page.app.ready && !page.data.busy; model: ["Landscape", "Portrait", "Landscape flipped", "Portrait flipped"]; currentIndex: page.p.orientation || 0; onActivated: index => page.app.rotate(index) }
                    }
                }
                Rectangle { Layout.fillWidth: true; height: 1; color: Theme.border }
                RowLayout { Layout.fillWidth: true
                    Icon { name: "shield"; color: Theme.subtle; width: 17; height: 17 }
                    Text { text: "Touch and pen input"; color: Theme.muted; font.pixelSize: 11; Layout.fillWidth: true }
                    AppSwitch { checked: page.p.input !== false; enabled: page.editable; onToggled: page.app.configure({input:checked}); Accessible.name: "Touch and pen" }
                }
            }
        }
        RowLayout { Layout.fillWidth: true; spacing: 12
            Repeater { model: [
                    {title:"FRAME RATE",value:page.connected && page.metrics.fps ? Number(page.metrics.fps).toFixed(1) : "—",unit:"fps"},
                    {title:"FRAME ACK",value:page.connected && page.metrics.ackMs ? Number(page.metrics.ackMs).toFixed(0) : "—",unit:"ms"},
                    {title:"VIDEO STREAM",value:page.connected && page.metrics.width ? page.metrics.width+" × "+page.metrics.height : "—",unit:""}
                ]
                delegate: SectionCard { required property var modelData; Layout.fillWidth: true; implicitHeight: 88; padding: 15
                    ColumnLayout { anchors.fill: parent; spacing: 8
                        Text { text: modelData.title; color: Theme.subtle; font.pixelSize: 9; font.letterSpacing: 1.3 }
                        RowLayout { spacing: 6; Text { text: modelData.value; color: Theme.text; font.pixelSize: 22; font.weight: Font.DemiBold }
                            Text { text: modelData.unit; color: Theme.muted; font.pixelSize: 11; Layout.alignment: Qt.AlignBottom; Layout.bottomMargin: 3 } }
                    }
                }
            }
        }
        Text {
            text: page.connected ? ((page.metrics.encoder || "H.264") + " · " + Number(page.metrics.mbps || 0).toFixed(1) + " Mbps"
                + (page.metrics.captureBackend ? " · " + page.metrics.captureBackend + "\nCapture + encode " + Number(page.metrics.captureEncodeMs).toFixed(1)
                    + " ms · Frame wait " + Number(page.metrics.frameWaitMs).toFixed(1) + " ms · Send " + Number(page.metrics.writeMs).toFixed(1) + " ms" : ""))
                : "Live performance appears here when your receiver connects."
            color: Theme.subtle; font.pixelSize: 10; Layout.fillWidth: true; wrapMode: Text.Wrap; Layout.bottomMargin: 12
        }
    }
}
