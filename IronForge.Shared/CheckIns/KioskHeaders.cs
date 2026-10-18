namespace IronForge.Shared.CheckIns;

public static class KioskHeaders
{
    /// <summary>A paired check-in device sends its key in this header on every request.</summary>
    public const string DeviceKey = "X-Device-Key";
}
