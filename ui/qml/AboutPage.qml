import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
    id: page
    required property var app
    contentWidth: availableWidth; clip: true; ScrollBar.horizontal.policy: ScrollBar.AlwaysOff
    ColumnLayout { width: page.availableWidth; spacing: 18
        SectionCard { Layout.fillWidth: true; implicitHeight: brand.implicitHeight+64; padding: 32
            ColumnLayout { id: brand; anchors.fill: parent; spacing: 17
                Rectangle { width: 70; height: 70; radius: 20; color: "#1b3d34"; Icon { anchors.centerIn: parent; name: "monitor"; color: Theme.accent; width: 43; height: 43 } }
                Text { text: "Room for your next idea."; color: Theme.text; font.pixelSize: 25; font.weight: Font.DemiBold }
                Text { text: "Dis turns an Android phone or tablet into an extra Windows display.\nBuilt for a direct connection, a focused workspace, and less friction."; color: Theme.muted; font.pixelSize: 13; lineHeight: 1.6; wrapMode: Text.Wrap; Layout.fillWidth: true }
                RowLayout { spacing: 9; Rectangle { width: 6; height: 6; radius: 3; color: Theme.accent }
                    Text { text: "VERSION 0.4.4 PREVIEW"; color: Theme.accent; font.pixelSize: 10; font.letterSpacing: 1.3 } }
            }
        }
        SectionCard { Layout.fillWidth: true; implicitHeight: technology.implicitHeight+44
            ColumnLayout { id: technology; anchors.fill: parent; spacing: 16
                Text { text: "Built on dependable foundations"; color: Theme.text; font.pixelSize: 15; font.weight: Font.DemiBold }
                Text { text: "Qt 6.8.3 for the desktop interface. Windows Graphics Capture and\nMedia Foundation for video. Android MediaCodec for playback."; color: Theme.muted; font.pixelSize: 12; lineHeight: 1.5; wrapMode: Text.Wrap; Layout.fillWidth: true }
                Text { text: "This application uses dynamically linked Qt libraries under LGPLv3.\nQt is unmodified and its libraries can be replaced. License texts and\nsource-code links are included in the licenses folder beside Dis.exe."; color: Theme.subtle; font.pixelSize: 11; lineHeight: 1.5; wrapMode: Text.Wrap; Layout.fillWidth: true }
                RowLayout { spacing: 10
                    AppButton { text: "Qt licenses"; iconName: "info"; onClicked: page.app.openLink("https://www.qt.io/development/open-source-lgpl-obligations") }
                    AppButton { text: "Qt source"; quiet: true; onClicked: page.app.openLink("https://download.qt.io/archive/qt/6.8/6.8.3/submodules/") }
                }
            }
        }
        Text { text: "An independent project. No affiliation with SuperDisplay or Coffee & Work."; color: Theme.subtle; font.pixelSize: 11; Layout.bottomMargin: 15 }
    }
}
