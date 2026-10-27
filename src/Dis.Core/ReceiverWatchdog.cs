namespace Dis.Core;

/// <summary>Decoded-frame acknowledgements, rather than an open socket, keep a display alive.</summary>
public static class ReceiverWatchdog
{
    public static bool Expired(TimeSpan sessionAge, TimeSpan? acknowledgementAge, bool receiverPresent, bool usb)
    {
        if (acknowledgementAge is null) return sessionAge >= TimeSpan.FromSeconds(usb ? 30 : 60);
        return acknowledgementAge >= TimeSpan.FromSeconds(receiverPresent ? 10 : 3);
    }
}
