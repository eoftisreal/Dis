import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
    id: page
    required property var app
    readonly property var data: app.state
    readonly property var p: data.preferences || ({})
    readonly property bool editable: app.ready && !data.busy && !data.active
    contentWidth: availableWidth; clip: true; ScrollBar.horizontal.policy: ScrollBar.AlwaysOff
    component Field: TextField {
        implicitHeight: 43; font.family: Theme.font; font.pixelSize: 12; color: Theme.text
        placeholderTextColor: Theme.subtle; selectionColor: "#32594e"; selectedTextColor: Theme.text; padding: 12
        background: Rectangle { radius: 8; color: Theme.inset; border.color: parent.activeFocus ? Theme.accent : Theme.border }
    }
    component LabelText: Text { color: Theme.muted; font.pixelSize: 11 }
    ColumnLayout { width: page.availableWidth; spacing: 18
        SectionCard { Layout.fillWidth: true; implicitHeight: behavior.implicitHeight+44
            ColumnLayout { id: behavior; anchors.fill: parent; spacing: 17
                Text { text: "Connection behavior"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold }
                RowLayout { Layout.fillWidth: true
                    ColumnLayout { spacing: 5; Layout.fillWidth: true
                        Text { text: "Virtual display turns off when idle"; color: Theme.text; font.pixelSize: 13 }
                        LabelText { text: "Remove the extra screen from Windows when you disconnect.\nWindows asks for administrator approval when connecting."; lineHeight: 1.4 }
                    }
                }
                Rectangle { Layout.fillWidth: true; height: 1; color: Theme.border }
                RowLayout { Layout.fillWidth: true
                    ColumnLayout { spacing: 5; Layout.fillWidth: true
                        Text { text: "USB autoconnect"; color: Theme.text; font.pixelSize: 13 }
                        LabelText { text: "Connect when USB is plugged in. Enable USB start in the Android app too." }
                    }
                    AppSwitch { checked: page.p.autoConnect || false; enabled: page.editable; onToggled: page.app.configure({autoConnect:checked}); Accessible.name: "USB autoconnect" }
                }
                RowLayout { Layout.fillWidth: true
                    ColumnLayout { Layout.fillWidth: true; spacing: 5
                        Text { text: "Keep Dis running in the tray"; color: Theme.text; font.pixelSize: 13 }
                        LabelText { text: "Closing the window keeps USB ready. Use the tray menu to quit." }
                    }
                    AppSwitch { checked: page.app.backgroundEnabled; onToggled: page.app.setBackgroundEnabled(checked); Accessible.name: "Keep Dis running in tray" }
                }
                RowLayout { Layout.fillWidth: true
                    ColumnLayout { Layout.fillWidth: true; spacing: 5
                        Text { text: "Start with Windows"; color: Theme.text; font.pixelSize: 13 }
                        LabelText { text: "Start quietly in your tray when you sign in. No administrator prompt." }
                    }
                    AppSwitch { checked: page.app.startAtLogin; onToggled: page.app.setStartAtLogin(checked); Accessible.name: "Start with Windows" }
                }
            }
        }
        SectionCard { Layout.fillWidth: true; implicitHeight: transport.implicitHeight+44
            ColumnLayout { id: transport; anchors.fill: parent; spacing: 16
                Text { text: "Transport & video"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold }
                RowLayout { Layout.fillWidth: true; spacing: 18
                    ColumnLayout { Layout.fillWidth: true; spacing: 8
                        LabelText { text: "Connection" }
                        AppCombo { Layout.fillWidth: true; model: page.data.connections || []; textRole: "label"; enabled: page.editable
                            currentIndex: Math.max(0,(page.data.connections || []).findIndex(c => c.address === page.p.connectionAddress))
                            onActivated: index => page.app.configure({connectionAddress:page.data.connections[index].address}) }
                    }
                    ColumnLayout { spacing: 8
                        LabelText { text: "Frame-rate target" }
                        SpinBox { from: 10; to: 60; value: page.p.fps || 60; stepSize: 5; editable: true; enabled: page.editable; implicitWidth: 130; implicitHeight: 43; font.pixelSize: 12; onValueModified: page.app.configure({fps:value}); Accessible.name: "Target frame rate" }
                    }
                }
                LabelText { text: "A target is a preference. Your actual frame rate appears on the Display page."; wrapMode: Text.Wrap; Layout.fillWidth: true }
                Field { visible: page.p.connectionAddress && page.p.connectionAddress !== "127.0.0.1"; Layout.fillWidth: true; readOnly: true; text: page.data.pairingUri || "Connect your display to create a network pairing link."; Accessible.name: "Network pairing link" }
                AppButton { visible: page.p.connectionAddress && page.p.connectionAddress !== "127.0.0.1"; text: "Copy pairing link"; iconName: "copy"; enabled: !!page.data.pairingUri; onClicked: page.app.copyPairingLink() }
            }
        }
        SectionCard { Layout.fillWidth: true; implicitHeight: advanced.implicitHeight+44
            ColumnLayout { id: advanced; anchors.fill: parent; spacing: 15
                Text { text: "Advanced"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold }
                LabelText { text: "USB tools are built in and prepared automatically. No separate ADB download or file selection."; wrapMode: Text.Wrap; Layout.fillWidth: true }
                RowLayout { Layout.fillWidth: true; spacing: 18
                    ColumnLayout { Layout.fillWidth: true; spacing: 8
                        LabelText { text: "Device serial (optional)" }
                        Field { Layout.fillWidth: true; enabled: page.editable; text: page.p.serial || ""; placeholderText: "Automatic for one authorized device"
                            onEditingFinished: { if(text !== page.p.serial) page.app.configure({serial:text}) }
                            Accessible.name: "Device serial" }
                    }
                    ColumnLayout { spacing: 8
                        LabelText { text: "Connection port" }
                        SpinBox { from: 1024; to: 65535; value: page.p.port || 49830; editable: true; enabled: page.editable; implicitWidth: 130; implicitHeight: 43; onValueModified: page.app.configure({port:value}); Accessible.name: "Connection port" }
                    }
                }
                AppButton { text: "Windows display settings"; iconName: "monitor"; quiet: true; onClicked: page.app.displaySettings() }
            }
        }
        LabelText { text: "Changes are saved automatically. Disconnect before changing session preferences."; Layout.bottomMargin: 15 }
    }
}
