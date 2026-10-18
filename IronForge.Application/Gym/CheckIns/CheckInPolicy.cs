using System.Globalization;
using IronForge.Application.Entities;
using IronForge.Shared.Models.Gym;

namespace IronForge.Application.Gym.CheckIns;

/// <summary>The oldest unpaid invoice past its due date, if any.</summary>
public sealed record OverdueInvoice(string Number, decimal Amount, string Currency, DateOnly DueDate);

public sealed record CheckInDecision(
    bool Allowed,
    CheckInDenialReason? Reason,
    string Message,
    string? Warning = null)
{
    public static CheckInDecision Deny(CheckInDenialReason reason, string message) =>
        new(false, reason, message);
}

/// <summary>
/// Who may enter, the same for every check-in method. Only Active members
/// get in. An overdue invoice does not block entry during the gym's grace
/// period (staff see a warning); past it, entry is refused even before the
/// billing job marks the membership Expired.
/// </summary>
public static class CheckInPolicy
{
    public static CheckInDecision Evaluate(
        MemberProfile member,
        OverdueInvoice? overdue,
        DateOnly today,
        int gracePeriodDays)
    {
        var name = member.FullName;

        switch (member.Status)
        {
            case MemberStatus.Pending:
                return CheckInDecision.Deny(
                    CheckInDenialReason.Pending,
                    $"{name}'s join request is not approved yet.");

            case MemberStatus.Frozen:
                return CheckInDecision.Deny(
                    CheckInDenialReason.Frozen,
                    member.FrozenUntil is { } until
                        ? $"{name}'s membership is frozen until {until.ToString("d MMM", CultureInfo.InvariantCulture)}."
                        : $"{name}'s membership is frozen.");

            case MemberStatus.Expired:
                return CheckInDecision.Deny(
                    CheckInDenialReason.Expired,
                    $"{name}'s membership has expired: an invoice is unpaid.");

            case MemberStatus.Cancelled:
                return CheckInDecision.Deny(
                    CheckInDenialReason.Cancelled,
                    $"{name}'s membership is cancelled.");
        }

        if (overdue is null || overdue.DueDate >= today)
        {
            return new CheckInDecision(true, null, $"Welcome, {FirstName(name)}!");
        }

        var daysOverdue = today.DayNumber - overdue.DueDate.DayNumber;
        var lastDay = overdue.DueDate.AddDays(gracePeriodDays);
        var invoice = $"{overdue.Number} ({overdue.Currency} {overdue.Amount.ToString("N2", CultureInfo.InvariantCulture)})";

        if (daysOverdue > gracePeriodDays)
        {
            return CheckInDecision.Deny(
                CheckInDenialReason.GracePeriodEnded,
                $"{name}'s membership is on hold: invoice {invoice} is {Days(daysOverdue)} overdue.");
        }

        return new CheckInDecision(
            true,
            null,
            $"Welcome, {FirstName(name)}!",
            $"Payment overdue: invoice {invoice} was due {Days(daysOverdue)} ago. " +
            $"Entry is allowed until {lastDay.ToString("d MMM", CultureInfo.InvariantCulture)}.");
    }

    private static string FirstName(string fullName) =>
        fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
}
