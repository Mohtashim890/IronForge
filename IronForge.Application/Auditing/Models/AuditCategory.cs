namespace IronForge.Application.Auditing.Models
{
    public enum AuditCategory
    {
        Authentication = 0,
        Authorization = 1,
        // 2 was Product (removed sample domain).
        Agent = 3,
        Approval = 4,
        Tool = 5,
        Workflow = 6,
        Delegation = 7,
        Tenant = 8,
        Membership = 9,
        Billing = 10,
        Coaching = 11,
        Notification = 12
    }
}
