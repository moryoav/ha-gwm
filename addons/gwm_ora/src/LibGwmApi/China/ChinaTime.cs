#nullable enable

namespace libgwmapi.China;

/// <summary>
/// Mainland China has used UTC+08:00 without daylight-saving changes since 1991.
/// Keep protocol timestamps independent of the host operating system's optional
/// time-zone database so the client also works in minimal Alpine containers.
/// </summary>
internal static class ChinaTime
{
    internal static readonly TimeSpan UtcOffset = TimeSpan.FromHours(8);

    internal static DateTimeOffset Convert(DateTimeOffset instant) =>
        instant.ToOffset(UtcOffset);
}
