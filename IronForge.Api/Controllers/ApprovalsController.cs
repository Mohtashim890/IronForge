using IronForge.Api.Helpers;
using IronForge.Application.Agents.Approvals;
using IronForge.Shared.Models.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronForge.Api.Controllers
{
    /// <summary>
    /// The approvals queue for High/Critical-risk agent actions. Running an
    /// approved action stays on api/agent/{agentId}/approvals/{id}/decision.
    /// </summary>
    [ApiController]
    [Route("api/approvals")]
    [Authorize]
    public sealed class ApprovalsController : ControllerBase
    {
        private readonly IApprovalQueueService _queue;

        public ApprovalsController(IApprovalQueueService queue)
        {
            _queue = queue;
        }

        [HttpGet]
        public async Task<IActionResult> GetQueue(CancellationToken cancellationToken)
        {
            return this.ToActionResult(await _queue.GetAsync(cancellationToken));
        }

        [HttpPost("{approvalId}/decision")]
        public async Task<IActionResult> Decide(
            string approvalId,
            ApprovalQueueDecisionRequest request,
            CancellationToken cancellationToken)
        {
            return this.ToActionResult(
                await _queue.DecideAsync(
                    approvalId,
                    request.Approved,
                    request.Reason,
                    cancellationToken));
        }
    }
}
