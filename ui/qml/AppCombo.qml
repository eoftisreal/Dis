import QtQuick
import QtQuick.Controls
ComboBox {
    id: combo
    property string accessibleLabel: ""
    implicitHeight: 43
    implicitWidth: 190
    leftPadding: 13; rightPadding: 34
    font.family: Theme.font; font.pixelSize: 13
    hoverEnabled: true
    Accessible.name: accessibleLabel.length ? accessibleLabel + ": " + displayText : displayText
    contentItem: Text { text: combo.displayText; font: combo.font; color: combo.enabled ? Theme.text : Theme.subtle; verticalAlignment: Text.AlignVCenter; elide: Text.ElideRight }
    background: Rectangle { radius: 8; color: combo.down ? "#111b29" : "#111824"; border.color: combo.activeFocus ? Theme.accent : combo.hovered ? "#42536b" : Theme.border }
    indicator: Icon { name: "chevron"; color: combo.enabled ? Theme.muted : Theme.subtle; width: 16; height: 16; x: combo.width - width - 12; y: (combo.height-height)/2 }
    delegate: ItemDelegate {
        id: entry
        required property int index
        width: combo.width - 12; height: 39
        text: combo.textAt(entry.index)
        Accessible.name: text
        highlighted: combo.highlightedIndex === index
        contentItem: Text { text: entry.text; font: combo.font; color: entry.highlighted ? Theme.accent : Theme.text; elide: Text.ElideRight; verticalAlignment: Text.AlignVCenter }
        background: Rectangle { color: entry.highlighted ? "#223d3b" : "transparent"; radius: 6 }
    }
    popup: Popup {
        y: combo.height + 6; width: combo.width; padding: 6
        implicitHeight: Math.min(284, list.contentHeight + 12)
        background: Rectangle { radius: 10; color: "#1a2432"; border.color: "#3a485d" }
        contentItem: ListView { id: list; clip: true; model: combo.popup.visible ? combo.delegateModel : null; currentIndex: combo.highlightedIndex; ScrollIndicator.vertical: ScrollIndicator {} }
    }
}
