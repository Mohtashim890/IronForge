using System.Diagnostics.Metrics;

namespace IronForge.Application.Observablity.Metrics
{
    public class IronForgeBusinessMetrics
    {
        public static readonly Counter<long> InvoicesIssued =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.invoices.issued",
                description: "Number of member invoices issued.");

        public static readonly Counter<long> PaymentsReceived =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.payments.received",
                description: "Number of invoice payments received (card and manual).");

        public static readonly Counter<long> PaymentRemindersSent =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.payment_reminders.sent",
                description: "Number of payment reminders queued.");

        public static readonly Counter<long> MembersJoined =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.members.joined",
                description: "Number of join requests and staff-added members.");

        public static readonly Counter<long> EmailsSent =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.emails.sent",
                description: "Number of emails delivered by the outbox.");

        public static readonly Counter<long> EmailsFailed =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.emails.failed",
                description: "Number of emails that failed permanently.");

        public static readonly Counter<long> BillingRuns =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.billing.runs",
                description: "Number of per-gym billing runs completed.");
    }
}
