#pragma once
#include <QObject>
#include <QProcess>
#include <QVariantMap>

class DesktopBridge final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QVariantMap state READ state NOTIFY stateChanged)
    Q_PROPERTY(bool ready READ ready NOTIFY stateChanged)
    Q_PROPERTY(bool closing READ closing NOTIFY stateChanged)
    Q_PROPERTY(bool canClose READ canClose NOTIFY stateChanged)
    Q_PROPERTY(bool startAtLogin READ startAtLogin NOTIFY desktopSettingsChanged)
    Q_PROPERTY(bool backgroundEnabled READ backgroundEnabled NOTIFY desktopSettingsChanged)
public:
    explicit DesktopBridge(QObject* parent = nullptr);
    ~DesktopBridge() override;
    QVariantMap state() const { return m_state; }
    bool ready() const { return m_ready; }
    bool closing() const { return m_closing; }
    bool canClose() const { return m_process.state() == QProcess::NotRunning; }
    void start();
    bool startAtLogin() const;
    bool backgroundEnabled() const;
    Q_INVOKABLE void setStartAtLogin(bool enabled);
    Q_INVOKABLE void setBackgroundEnabled(bool enabled);
    Q_INVOKABLE bool hideToTray();
    Q_INVOKABLE void configure(const QVariantMap& settings);
    Q_INVOKABLE void command(const QString& action);
    Q_INVOKABLE void rotate(int orientation);
    Q_INVOKABLE void browseAdb();
    Q_INVOKABLE void installApk(bool chooseFile = false);
    Q_INVOKABLE void copyPairingLink();
    Q_INVOKABLE void displaySettings();
    Q_INVOKABLE void openLink(const QString& url);
    Q_INVOKABLE void shutdown();
signals:
    void desktopSettingsChanged();
    void hideRequested();
    void stateChanged();
    void engineStopped();
    void toast(const QString& message);
private:
    QProcess m_process;
    QByteArray m_buffer;
    QVariantMap m_state;
    bool m_ready = false, m_closing = false;
    void send(QVariantMap message);
    void fail(const QString& message);
};
