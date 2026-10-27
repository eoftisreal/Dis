import QtQuick
Canvas {
    id: icon
    property string name: "monitor"
    property color color: Theme.muted
    implicitWidth: 22; implicitHeight: 22
    onNameChanged: requestPaint()
    onColorChanged: requestPaint()
    onPaint: {
        let c = getContext("2d"); c.reset(); c.scale(width / 24, height / 24)
        c.strokeStyle = color; c.lineWidth = 1.7; c.lineCap = "round"; c.lineJoin = "round"
        function line(x,y,a,b) { c.moveTo(x,y); c.lineTo(a,b) }
        function box(x,y,w,h,r) { c.moveTo(x+r,y); c.arcTo(x+w,y,x+w,y+h,r); c.arcTo(x+w,y+h,x,y+h,r); c.arcTo(x,y+h,x,y,r); c.arcTo(x,y,x+w,y,r) }
        c.beginPath()
        if (name === "monitor") { box(3,4,18,13,2); line(12,17,12,21); line(8,21,16,21) }
        else if (name === "devices" || name === "phone") { box(7,2,11,20,2); line(11,18,14,18); line(10,5,15,5) }
        else if (name === "settings") { c.arc(12,12,4,0,Math.PI*2); for(let i=0;i<8;i++){let a=i*Math.PI/4;line(12+8*Math.cos(a),12+8*Math.sin(a),12+10*Math.cos(a),12+10*Math.sin(a))} }
        else if (name === "info") { c.arc(12,12,9,0,Math.PI*2); line(12,11,12,16); line(12,7,12,7.2) }
        else if (name === "arrow") { line(4,12,20,12); line(14,6,20,12); line(20,12,14,18) }
        else if (name === "check") { line(5,12,10,17); line(10,17,20,6) }
        else if (name === "rotate") { c.arc(12,12,8,0.4,5.3); line(17,3,17,8); line(17,8,22,8) }
        else if (name === "download") { line(12,3,12,16); line(7,11,12,16); line(12,16,17,11); line(4,18,4,21); line(4,21,20,21); line(20,21,20,18) }
        else if (name === "shield") { c.moveTo(12,2); c.lineTo(21,6); c.lineTo(20,14); c.quadraticCurveTo(18,20,12,22); c.quadraticCurveTo(6,20,4,14); c.lineTo(3,6); c.closePath(); line(8,12,11,15); line(11,15,16,9) }
        else if (name === "usb") { line(12,21,12,3); line(9,6,12,3); line(12,3,15,6); line(12,16,6,12); line(6,12,6,8); line(12,13,18,9); line(18,9,18,6) }
        else if (name === "link") { c.moveTo(10,7); c.bezierCurveTo(14,0,24,5,19,11); line(19,11,16,14); c.moveTo(14,17); c.bezierCurveTo(10,24,0,19,5,13); line(5,13,8,10); line(8,16,16,8) }
        else if (name === "refresh") { c.arc(12,12,8,0.5,5.5); line(18,3,18,8); line(18,8,23,8) }
        else if (name === "chevron") { line(7,10,12,15); line(12,15,17,10) }
        else if (name === "copy") { box(8,7,12,14,2); c.moveTo(15,4); c.lineTo(15,3); c.lineTo(4,3); c.lineTo(4,16); c.lineTo(5,16) }
        c.stroke()
    }
}
