using System.Security.Claims;
using Finbuckle.MultiTenant;
using FSH.WebApi.Infrastructure.Common;
using FSH.WebApi.Shared.Multitenancy;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FSH.WebApi.Infrastructure.BackgroundJobs;

public class FSHJobFilter : IClientFilter
{
    private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

    private readonly IServiceProvider _services;

    public FSHJobFilter(IServiceProvider services) => _services = services;

    public void OnCreating(CreatingContext context)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));

        string recurringJobId = context.GetJobParameter<string>("RecurringJobId");
        if (!string.IsNullOrEmpty(recurringJobId))
        {
            string tenantIdName = recurringJobId.Split('-')[0];

            context.SetJobParameter(MultitenancyConstants.TenantIdName, tenantIdName);

        }
        else
        {
            using var scope = _services.CreateScope();

            var httpContext = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>()?.HttpContext;
            _ = httpContext ?? throw new InvalidOperationException("Can't create a TenantJob without HttpContext.");

            var tenantInfo = scope.ServiceProvider.GetRequiredService<ITenantInfo>();
            context.SetJobParameter(MultitenancyConstants.TenantIdName, tenantInfo.Identifier);

            string? userId = httpContext.User.GetUserId();
            context.SetJobParameter(QueryStringKeys.UserId, userId);
        }

        Logger.InfoFormat("Set TenantId and UserId parameters to job {0}.{1}...", context.Job.Method.ReflectedType?.FullName, context.Job.Method.Name);
    }

    public void OnCreated(CreatedContext context) =>
        Logger.InfoFormat(
            "Job created with parameters {0}",
            context.Parameters.Select(x => x.Key + "=" + x.Value).Aggregate((s1, s2) => s1 + ";" + s2));
}

public class LogEverythingAttribute : JobFilterAttribute,
    IClientFilter, IServerFilter, IElectStateFilter, IApplyStateFilter
{
    private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

    public void OnCreating(CreatingContext context)
    {
        Logger.InfoFormat("Creating a job based on method `{0}`...", context.Job.Method.Name);
    }

    public void OnCreated(CreatedContext context)
    {
        Logger.InfoFormat(
            "Job that is based on method `{0}` has been created with id `{1}`",
            context.Job.Method.Name,
            context.BackgroundJob?.Id);
    }

    public void OnPerforming(PerformingContext context)
    {
        Logger.InfoFormat("Starting to perform job `{0}`", context.BackgroundJob.Id);
    }

    public void OnPerformed(PerformedContext context)
    {
        Logger.InfoFormat("Job `{0}` has been performed", context.BackgroundJob.Id);
    }

    public void OnStateElection(ElectStateContext context)
    {
        var failedState = context.CandidateState as FailedState;
        if (failedState != null)
        {
            Logger.WarnFormat(
                "Job `{0}` has been failed due to an exception `{1}`",
                context.BackgroundJob.Id,
                failedState.Exception);
        }
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        Logger.InfoFormat(
            "Job `{0}` state was changed from `{1}` to `{2}`",
            context.BackgroundJob.Id,
            context.OldStateName,
            context.NewState.Name);
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        Logger.InfoFormat(
            "Job `{0}` state `{1}` was unapplied.",
            context.BackgroundJob.Id,
            context.OldStateName);
    }
}