import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
Button {
    id: button
    property bool primary: false
    property bool quiet: false
    property string iconName: ""
    implicitHeight: 44
    implicitWidth: Math.max(88, row.implicitWidth + 32)
    padding: 14
    hoverEnabled: true
    Accessible.name: text
    background: Rectangle {
        radius: 9
        color: !button.enabled ? "#1d2632" : button.primary ? (button.down ? "#44bfa2" : button.hovered ? "#83e9d0" : Theme.accent)
            : button.quiet ? (button.hovered ? "#202b3b" : "transparent") : (button.down ? "#182231" : button.hovered ? "#2a3749" : "#222d3e")
        border.width: button.activeFocus ? 2 : button.primary || button.quiet ? 0 : 1
        border.color: button.activeFocus ? Theme.text : Theme.border
        Behavior on color { ColorAnimation { duration: 110 } }
    }
    contentItem: RowLayout {
        id: row; spacing: 9
        Icon { visible: button.iconName.length > 0; name: button.iconName; color: button.primary && button.enabled ? "#102b26" : button.enabled ? Theme.text : Theme.subtle; implicitWidth: 18; implicitHeight: 18 }
        Text { text: button.text; font.family: Theme.font; font.pixelSize: 13; font.weight: Font.DemiBold; color: !button.enabled ? Theme.subtle : button.primary ? "#102b26" : Theme.text; horizontalAlignment: Text.AlignHCenter; Layout.fillWidth: true }
    }
}
