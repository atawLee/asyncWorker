using AsyncWorker.Data;
using AsyncWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace AsyncWorker.Services;

public class JobRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProcessInstanceManager _processManager;
    private readonly ILogger<JobRecoveryService> _logger;

    public JobRecoveryService(
        IServiceScopeFactory scopeFactory,
        ProcessInstanceManager processManager,
        ILogger<JobRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JobRecoveryService starting. Current Process Instance ID: {InstanceId}", 
            _processManager.CurrentInstanceId);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 다른 프로세스의 InProgress 작업 → Failed 처리
        var orphanedJobs = await context.Jobs
            .Where(j => j.Status == JobStatus.InProgress && 
                        j.ProcessInstanceId != _processManager.CurrentInstanceId)
            .ToListAsync(cancellationToken);

        if (orphanedJobs.Any())
        {
            _logger.LogWarning("Found {Count} orphaned jobs from previous process instances", orphanedJobs.Count);

            foreach (var job in orphanedJobs)
            {
                _logger.LogWarning("Marking job {JobId} ({JobType}) as Failed. Previous process: {ProcessId}", 
                    job.Id, job.Type, job.ProcessInstanceId);
                
                job.Status = JobStatus.Failed;
                job.ErrorMessage = $"Process instance terminated unexpectedly. Previous process: {job.ProcessInstanceId}";
                job.CompletedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Successfully marked {Count} orphaned jobs as Failed", orphanedJobs.Count);
        }
        else
        {
            _logger.LogInformation("No orphaned jobs found");
        }

        // Pending 상태 작업은 그대로 유지 (자동 재시작 안 함)
        var pendingJobsCount = await context.Jobs
            .Where(j => j.Status == JobStatus.Pending)
            .CountAsync(cancellationToken);

        _logger.LogInformation("Found {Count} pending jobs (not auto-restarted)", pendingJobsCount);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JobRecoveryService stopping");
        return Task.CompletedTask;
    }
}
