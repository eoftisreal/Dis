#include "DesktopBridge.h"
#include <QClipboard>
#include <QCoreApplication>
#include <QDesktopServices>
#include <QFileDialog>
#include <QFileInfo>
#include <QGuiApplication>
#include <QJsonDocument>
#include <QJsonObject>
#include <QTimer>
#include <QUrl>
#include <QDir>
#include <QSettings>
#include <QSystemTrayIcon>

DesktopBridge::DesktopBridge(QObject* parent) : QObject(parent) {
    m_state = {{"status", "starting"}, {"message", "Getting your workspace ready…"}, {"busy", true}, {"active", false}, {"preferences", QVariantMap{}}};
    connect(&m_process, &QProcess::readyReadStandardOutput, this, [this] {
        m_buffer += m_process.readAllStandardOutput();
        if (m_buffer.size() > 1024 * 1024) { fail("The display engine returned an invalid response."); m_process.kill(); return; }
        for (;;) {
            auto end = m_buffer.indexOf('\n'); if (end < 0) break;
            auto line = m_buffer.left(end); m_buffer.remove(0, end + 1);
            auto document = QJsonDocument::fromJson(line);
            if (!document.isObject() || document.object().value("type").toString() != "state") continue;
            auto state = document.object().toVariantMap();
            if (!m_ready || state != m_state) {
                bool finishedAction = m_state.value("status").toString() == "working" && state.value("status").toString() == "ready";
                m_state = state; m_ready = true; emit stateChanged();
                if (finishedAction) emit toast(state.value("message").toString());
            }
        }
    });
    connect(&m_process, &QProcess::errorOccurred, this, [this](QProcess::ProcessError error) {
        if (!m_closing && error == QProcess::FailedToStart) fail("The display engine could not start. Keep Dis.Engine.exe beside Dis.exe.");
    });
    connect(&m_process, qOverload<int, QProcess::ExitStatus>(&QProcess::finished), this, [this](int code, QProcess::ExitStatus exit) {
        if (!m_closing) fail(code == 0 && exit == QProcess::NormalExit ? "The display engine has stopped." : "The display engine exited unexpectedly. Close Dis and open it again.");
        emit stateChanged(); emit engineStopped();
    });
    // Bound diagnostics and drain stderr so a child cannot block on a full pipe.
    connect(&m_process, &QProcess::readyReadStandardError, this, [this] { m_process.readAllStandardError(); });
}

DesktopBridge::~DesktopBridge() {
    if (m_process.state() != QProcess::NotRunning) {
        send({{"command", "quit"}});
        if (!m_process.waitForFinished(1500)) m_process.kill();
    }
}

void DesktopBridge::start() {
    m_process.setWorkingDirectory(QCoreApplication::applicationDirPath());
    QStringList arguments{"--qt-engine"};
    if (QCoreApplication::arguments().contains("--ui-smoke") || QCoreApplication::arguments().contains("--no-auto-connect")) arguments << "--no-auto-connect";
    m_process.start(QCoreApplication::applicationDirPath() + "/Dis.Engine.exe", arguments);
}
void DesktopBridge::send(QVariantMap message) {
    if (m_closing && message.value("command").toString() != "quit") return;
    if (m_process.state() != QProcess::Running) return;
    m_process.write(QJsonDocument(QJsonObject::fromVariantMap(message)).toJson(QJsonDocument::Compact) + '\n');
}
void DesktopBridge::configure(const QVariantMap& settings) { send({{"command", "configure"}, {"settings", settings}}); }
bool DesktopBridge::startAtLogin() const {
    QSettings startup("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", QSettings::NativeFormat);
    return startup.value("Dis").toString() == "\"" + QDir::toNativeSeparators(QCoreApplication::applicationFilePath()) + "\" --background";
}
bool DesktopBridge::backgroundEnabled() const { return QSettings().value("backgroundEnabled", true).toBool(); }
void DesktopBridge::setStartAtLogin(bool enabled) {
    QSettings startup("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", QSettings::NativeFormat);
    if (enabled) startup.setValue("Dis", "\"" + QDir::toNativeSeparators(QCoreApplication::applicationFilePath()) + "\" --background");
    else startup.remove("Dis");
    startup.sync();
    if (startup.status() != QSettings::NoError) emit toast("Windows could not save the startup preference.");
    emit desktopSettingsChanged();
}
void DesktopBridge::setBackgroundEnabled(bool enabled) { QSettings().setValue("backgroundEnabled", enabled); emit desktopSettingsChanged(); }
bool DesktopBridge::hideToTray() {
    if (m_closing || !backgroundEnabled() || !QSystemTrayIcon::isSystemTrayAvailable()) return false;
    emit hideRequested(); return true;
}
void DesktopBridge::command(const QString& action) { send({{"command", action}}); }
void DesktopBridge::rotate(int orientation) { send({{"command", "rotate"}, {"orientation", orientation}}); }
void DesktopBridge::browseAdb() {
    auto path = QFileDialog::getOpenFileName(nullptr, "Choose Android Debug Bridge", {}, "Android Debug Bridge (adb.exe)");
    if (!path.isEmpty()) configure({{"adb", path}});
}
void DesktopBridge::installApk(bool chooseFile) {
    auto path = QCoreApplication::applicationDirPath() + "/Dis.apk";
    if (chooseFile || !QFileInfo::exists(path)) path = QFileDialog::getOpenFileName(nullptr, "Choose the Dis Android receiver", {}, "Android application (*.apk)");
    if (!path.isEmpty()) send({{"command", "install"}, {"path", path}});
}
void DesktopBridge::copyPairingLink() {
    auto uri = m_state.value("pairingUri").toString();
    if (!uri.isEmpty()) { QGuiApplication::clipboard()->setText(uri); emit toast("Pairing link copied"); }
}
void DesktopBridge::displaySettings() { QDesktopServices::openUrl(QUrl("ms-settings:display")); }
void DesktopBridge::openLink(const QString& url) {
    auto target = QUrl(url);
    if (target.scheme() == "https") QDesktopServices::openUrl(target);
}
void DesktopBridge::shutdown() {
    if (m_closing) return;
    m_closing = true; m_state["busy"] = true; m_state["message"] = "Disconnecting your display…"; emit stateChanged();
    if (canClose()) { emit engineStopped(); return; }
    send({{"command", "quit"}});
    QTimer::singleShot(20000, this, [this] { if (!canClose()) m_process.kill(); });
}
void DesktopBridge::fail(const QString& message) {
    m_ready = false; m_state["status"] = "error"; m_state["message"] = message; m_state["busy"] = false; m_state["active"] = false; emit stateChanged();
}
