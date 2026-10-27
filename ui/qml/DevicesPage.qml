import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
    id: page
    required property var app
    readonly property var data: app.state
    readonly property bool editable: app.ready && !data.busy && !data.active
    contentWidth: availableWidth; clip: true; ScrollBar.horizontal.policy: ScrollBar.AlwaysOff
    ColumnLayout { width: page.availableWidth; spacing: 18
        Rectangle { visible: !!page.data.probeError; Layout.fillWidth: true; implicitHeight: probeText.implicitHeight+28; radius: 10; color: "#2e2831"; border.color: "#534151"
            Text { id: probeText; anchors.fill: parent; anchors.margins: 14; text: page.data.probeError || ""; color: Theme.amber; font.pixelSize: 11; wrapMode: Text.Wrap }
        }
        SectionCard { Layout.fillWidth: true; implicitHeight: deviceList.implicitHeight+44
            ColumnLayout { id: deviceList; anchors.fill: parent; spacing: 18
                RowLayout { Text { text: "USB devices"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold; Layout.fillWidth: true }
                    AppButton { text: "Refresh"; iconName: "refresh"; quiet: true; enabled: !page.data.busy; onClicked: page.app.command("refresh") } }
                Repeater { model: page.data.devices || []
                    delegate: Rectangle { id: device; required property var modelData; Layout.fillWidth: true; implicitHeight: 92; radius: 10; color: Theme.inset; border.color: Theme.border
                        RowLayout { anchors.fill: parent; anchors.margins: 16; spacing: 14
                            Rectangle { width: 44; height: 48; radius: 10; color: "#1e3735"; Icon { anchors.centerIn: parent; name: "phone"; width: 27; height: 27; color: Theme.accent } }
                            ColumnLayout { spacing: 5; Layout.fillWidth: true
                                Text { text: device.modelData.model; color: Theme.text; font.pixelSize: 14; font.weight: Font.DemiBold }
                                Text { text: device.modelData.state === "device" ? "Authorized · USB debugging ready" : device.modelData.state === "unauthorized" ? "Allow USB debugging on your Android device" : "Device offline · reconnect your USB cable"; color: device.modelData.state === "device" ? Theme.muted : Theme.amber; font.pixelSize: 11 }
                                Text { text: "Device ···" + device.modelData.serial.slice(-6); color: Theme.subtle; font.pixelSize: 10 }
                            }
                            AppButton { text: "Use device"; enabled: page.editable && device.modelData.state === "device"; onClicked: page.app.configure({serial:device.modelData.serial}) }
                        }
                    }
                }
                ColumnLayout { visible: !(page.data.devices || []).length; Layout.fillWidth: true; spacing: 13
                    Icon { name: "usb"; color: Theme.subtle; implicitWidth: 48; implicitHeight: 48; Layout.alignment: Qt.AlignHCenter; Layout.topMargin: 12 }
                    Text { text: "Connect your Android device"; color: Theme.text; font.pixelSize: 17; font.weight: Font.DemiBold; Layout.alignment: Qt.AlignHCenter }
                    Text { text: "Use a data-capable USB cable, enable USB debugging,\nand accept this PC on your phone or tablet."; color: Theme.muted; font.pixelSize: 12; horizontalAlignment: Text.AlignHCenter; lineHeight: 1.4; Layout.alignment: Qt.AlignHCenter; Layout.bottomMargin: 20 }
                }
            }
        }
        SectionCard { Layout.fillWidth: true; implicitHeight: receiver.implicitHeight+44
            RowLayout { id: receiver; anchors.fill: parent; spacing: 18
                Icon { name: "download"; color: Theme.blue; implicitWidth: 28; implicitHeight: 28 }
                ColumnLayout { spacing: 7; Layout.fillWidth: true
                    Text { text: "Android receiver"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold }
                    Text { text: "Install or update Dis on your connected device.\nThe APK is included with your desktop app."; color: Theme.muted; font.pixelSize: 12; lineHeight: 1.4 }
                }
                AppButton { text: "Install receiver"; iconName: "download"; enabled: page.editable && page.data.phoneOnline; onClicked: page.app.installApk() }
            }
        }
        SectionCard { Layout.fillWidth: true; implicitHeight: setup.implicitHeight+44
            RowLayout { id: setup; anchors.fill: parent; spacing: 18
                Icon { name: "monitor"; color: Theme.accent; implicitWidth: 28; implicitHeight: 28 }
                ColumnLayout { spacing: 7; Layout.fillWidth: true
                    Text { text: "Virtual display"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold }
                    Text { text: page.data.driverInstalled ? "Installed. The adapter turns off when you disconnect." : "One-time setup enables a real extended desktop.\nWindows will ask for administrator approval."; color: Theme.muted; font.pixelSize: 12; lineHeight: 1.4 }
                }
                AppButton { text: page.data.driverInstalled ? "Repair setup" : "Set up display"; enabled: page.editable; primary: !page.data.driverInstalled; onClicked: page.app.command("setup") }
            }
        }
    }
}
