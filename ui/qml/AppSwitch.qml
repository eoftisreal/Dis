import QtQuick
import QtQuick.Controls
Switch {
    id: toggle
    implicitWidth: 43; implicitHeight: 27; padding: 0
    indicator: Rectangle {
        x: (toggle.width-width)/2; y: (toggle.height-height)/2
        width: 40; height: 23; radius: 12
        color: toggle.checked ? Theme.accent : "#354255"
        opacity: toggle.enabled ? 1 : 0.45
        border.color: toggle.activeFocus ? Theme.text : "transparent"
        Rectangle { width: 17; height: 17; radius: 9; y: 3; x: toggle.checked ? 20 : 3; color: toggle.checked ? "#123027" : "#c0ccdc"; Behavior on x { NumberAnimation { duration: 130 } } }
        Behavior on color { ColorAnimation { duration: 130 } }
    }
    contentItem: Item { }
}
