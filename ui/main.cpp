#include "DesktopBridge.h"
#include <QApplication>
#include <QDir>
#include <QIcon>
#include <QLocalServer>
#include <QLocalSocket>
#include <QPainter>
#include <QQmlApplicationEngine>
#include <QQuickStyle>
#include <QQuickWindow>
#include <QSGRendererInterface>
#include <QStandardPaths>
#include <QStyleHints>
#include <QTimer>
#include <QFileInfo>
#include <QSystemTrayIcon>
#include <QMenu>

int main(int argc, char** argv) {
    QApplication application(argc, argv);
    application.setApplicationName("Dis"); application.setApplicationVersion("0.4.4"); application.setOrganizationName("Dis");
    auto args = application.arguments();
    if (args.contains("--display-lifecycle-check") || args.contains("--display-capture-check") || args.contains("--display-smoke") || args.contains("--install-display-driver"))
        return QProcess::execute(QCoreApplication::applicationDirPath() + "/Dis.Engine.exe", args.mid(1));
    application.styleHints()->setColorScheme(Qt::ColorScheme::Dark);
    QQuickStyle::setStyle("Basic");
    QQuickWindow::setGraphicsApi(QSGRendererInterface::Direct3D11);
    QPixmap pixmap(128, 128); pixmap.fill(Qt::transparent);
    { QPainter p(&pixmap); p.setRenderHint(QPainter::Antialiasing); p.setBrush(QColor("#162b2b")); p.setPen(Qt::NoPen); p.drawRoundedRect(4, 4, 120, 120, 28, 28);
      p.setBrush(Qt::NoBrush); p.setPen(QPen(QColor("#62e0c2"), 6)); p.drawRoundedRect(23, 30, 68, 48, 7, 7); p.drawLine(55, 80, 55, 94); p.drawLine(40, 96, 70, 96);
      p.setBrush(QColor("#162b2b")); p.setPen(QPen(QColor("#e9f5f1"), 5)); p.drawRoundedRect(78, 55, 28, 45, 5, 5); }
    application.setWindowIcon(QIcon(pixmap));
    const auto socketName = QStringLiteral("Dis.Desktop.") + qEnvironmentVariable("USERNAME");
    QLocalSocket existing; existing.connectToServer(socketName);
    if (existing.waitForConnected(150)) {
        existing.write(args.contains("--quit") ? "quit" : args.contains("--background") ? "background" : "activate");
        existing.waitForBytesWritten(150); return 0;
    }
    if (args.contains("--quit")) return 0;
    QLocalServer singleInstance; singleInstance.setSocketOptions(QLocalServer::UserAccessOption);
    QLocalServer::removeServer(socketName);
    if (!singleInstance.listen(socketName)) return 1;
    DesktopBridge bridge;
    if (args.contains("--enable-startup")) bridge.setStartAtLogin(true);
    QQmlApplicationEngine engine;
    int page = 0;
    auto pageIndex = args.indexOf("--ui-page");
    if (pageIndex >= 0 && args.size() > pageIndex + 1) page = qBound(0, args[pageIndex + 1].toInt(), 3);
    engine.setInitialProperties({{"controller", QVariant::fromValue(&bridge)}, {"page", page}});
    engine.loadFromModule("Dis.Desktop", "Main");
    if (engine.rootObjects().isEmpty()) return 1;
    auto window = qobject_cast<QQuickWindow*>(engine.rootObjects().first());
    QObject::connect(&singleInstance, &QLocalServer::newConnection, window, [&] {
        while (auto socket = singleInstance.nextPendingConnection()) {
            auto handle = [&, socket] {
                const auto request = socket->readAll();
                if (request.isEmpty()) return;
                if (request == "quit") bridge.shutdown();
                else if (request == "activate") { window->showNormal(); window->raise(); window->requestActivate(); }
                socket->disconnectFromServer();
            };
            QObject::connect(socket, &QLocalSocket::readyRead, window, handle);
            QObject::connect(socket, &QLocalSocket::disconnected, socket, &QObject::deleteLater);
            if (socket->bytesAvailable()) handle();
        }
    });
    const bool trayAvailable = QSystemTrayIcon::isSystemTrayAvailable();
    application.setQuitOnLastWindowClosed(!trayAvailable);
    QSystemTrayIcon tray(application.windowIcon());
    QMenu trayMenu;
    auto showWindow = [&] { window->showNormal(); window->raise(); window->requestActivate(); };
    trayMenu.addAction("Open Dis", window, showWindow);
    trayMenu.addAction("Connect display", &bridge, [&] { bridge.command("connect"); });
    trayMenu.addAction("Disconnect", &bridge, [&] { bridge.command("disconnect"); });
    trayMenu.addSeparator();
    trayMenu.addAction("Quit Dis", &bridge, &DesktopBridge::shutdown);
    tray.setContextMenu(&trayMenu); tray.setToolTip("Dis - ready for USB");
    if (trayAvailable) tray.show();
    QObject::connect(&tray, &QSystemTrayIcon::activated, window, [&](QSystemTrayIcon::ActivationReason reason) {
        if (reason == QSystemTrayIcon::Trigger || reason == QSystemTrayIcon::DoubleClick) showWindow();
    });
    QObject::connect(&bridge, &DesktopBridge::hideRequested, window, &QWindow::hide);
    QObject::connect(&bridge, &DesktopBridge::stateChanged, &tray, [&] { tray.setToolTip("Dis - " + bridge.state().value("message").toString().left(110)); });
    QObject::connect(&bridge, &DesktopBridge::engineStopped, &application, [&] {
        if (bridge.closing()) application.quit(); else showWindow();
    });
    if (args.contains("--background") && trayAvailable) window->hide();
    QTimer::singleShot(0, &bridge, &DesktopBridge::start);
    auto smokeIndex = args.indexOf("--ui-smoke");
    if (smokeIndex >= 0 && args.size() > smokeIndex + 1) {
        auto path = QFileInfo(args[smokeIndex + 1]).absoluteFilePath();
        QTimer::singleShot(5000, window, [&, path] { window->grabWindow().save(path); bridge.shutdown(); });
    }
    return application.exec();
}
