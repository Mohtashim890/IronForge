using Docker.DotNet.Models;
using IronForge.Application.Agents.Models;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace IronForge.Application.IntegrationTests.Infrastructure
{
    public sealed class TestExecutionContextAccessor
    : IExecutionContextAccessor
    {
        public Execution.ExecutionContext? Current { get; private set; }

        public void Set(Execution.ExecutionContext context)
        {
            Current = context;
        }

        public void SetAgent(
            IronForge.Application.Agents.Models.AgentIdentity agent)
        {
            if (Current == null)
            {
                throw new InvalidOperationException(
                    "Execution context has not been established.");
            }

            Current.Actor.Agent = agent;
        }
    }
}
