using IronForge.Shared.Components.Gym;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace IronForge.Web.Services;

/// <summary>
/// Makes the circuit's <see cref="DisplayTimeZone"/> current for all of its
/// work (events, renders, the continuations of awaited calls), so dates
/// show in the gym's time zone rather than the server's.
/// </summary>
public sealed class DisplayTimeZoneCircuitHandler(DisplayTimeZone zone) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        context =>
        {
            DisplayTimeZone.Current = zone;
            return next(context);
        };
}
