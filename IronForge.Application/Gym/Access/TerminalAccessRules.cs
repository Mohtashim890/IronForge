using IronForge.Application.Entities;
using IronForge.Application.Gym.CheckIns;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.Access;

/// <summary>What a terminal should know about one member: may they enter, and until when without news from IronForge.</summary>
public sealed record TerminalAccess(bool Enabled, DateOnly ValidUntil);

/// <summary>
/// Turns a membership into the state a fingerprint terminal enforces on its
/// own. The terminal decides at the door, also while offline, so it is given
/// an end date: the last day the member may still enter if nothing changes,
/// which is the end of the grace period of the next (or oldest overdue)
/// invoice. Paying, freezing, expiring and so on change the state, and the
/// next sync sends it.
/// </summary>
public static class TerminalAccessRules
{
    public static TerminalAccess For(
        MemberProfile member,
        OverdueInvoice? overdue,
        DateOnly today,
        int gracePeriodDays)
    {
        if (member.AnonymizedAtUtc is not null)
        {
            return new TerminalAccess(false, today.AddDays(-1));
        }

        var decision = CheckInPolicy.Evaluate(member, overdue, today, gracePeriodDays);

        if (!decision.Allowed)
        {
            return new TerminalAccess(false, today.AddDays(-1));
        }

        // The next payment is due the day after the paid period (or now, for a
        // first invoice); entry continues for the grace period after that.
        var dueDate = overdue is not null && overdue.DueDate < today
            ? overdue.DueDate
            : member.PaidThrough is { } paid && paid >= today
                ? paid.AddDays(1)
                : today;

        return new TerminalAccess(true, dueDate.AddDays(gracePeriodDays));
    }
}
