using AsyncWorker.Data;
using AsyncWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace AsyncWorker.Services;

public class JobExecutionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobConcurrencyManager _concurrencyManager;
    private readonly ProcessInstanceManager _processManager;
    private readonly ILogger<JobExecutionService> _logger;

    public JobExecutionService(
        IServiceScopeFactory scopeFactory,
        JobConcurrencyManager concurrencyManager,
        ProcessInstanceManager processManager,
        ILogger<JobExecutionService> logger)
    {
        _scopeFactory = scopeFactory;
        _concurrencyManager = concurrencyManager;
        _processManager = processManager;
        _logger = logger;
    }

    // 작업 생성 및 백그라운드 실행 시작
    public async Task CreateAndExecuteJobAsync(string jobType, string? payload)
    {
        // 작업 타입 유효성 검사
        var config = _concurrencyManager.GetConfiguration(jobType);
        if (config == null)
        {
            throw new InvalidOperationException($"Unknown job type: {jobType}");
        }

        await ExecuteJobAsync(jobType, payload, CancellationToken.None);
    }

    private static async Task<Job> InsertNewJobAsync(string jobType, string? payload, IServiceScope scope)
    {
        Job job;
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        job = new Job
        {
            Id = Guid.NewGuid(),
            Type = jobType,
            Payload = payload,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        return job;
    }

    // 작업 취소

    public bool CancelJob(Guid jobId)
    {
        var cancelled = _concurrencyManager.CancelJob(jobId);

        if (cancelled)
        {
            _logger.LogInformation("Job {JobId} cancellation requested", jobId);
        }

        return cancelled;
    }

    // 작업 취소 (실행 전 작업)
    public async Task<bool> CancelPendingJobAsync(Guid jobId)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var job = await context.Jobs.FindAsync(jobId);
        if (job == null || job.Status != JobStatus.Pending)
        {
            return false;
        }

        job.Status = JobStatus.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        job.ErrorMessage = "Cancelled by user before execution";
        await context.SaveChangesAsync();

        _logger.LogInformation("Job {JobId} cancelled before execution", jobId);
        return true;
    }

    // 메인 실행 메서드 (내부용)
    private async Task ExecuteJobAsync(string jobType, string payload, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await InsertNewJobAsync(jobType, payload, scope);
        var config = _concurrencyManager.GetConfiguration(job.Type);
        if (config == null)
        {
            _logger.LogError("Unknown job type: {JobType}", job.Type);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = $"Unknown job type: {job.Type}";
            job.CompletedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            return;
        }
        var jobId = job.Id;
        // CancellationTokenSource 등록
        var cts = _concurrencyManager.RegisterJob(jobId);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

        try
        {
            _logger.LogInformation("Job {JobId} ({JobType}) waiting for semaphore...", jobId, job.Type);

            // 세마포어 대기 (블로킹 - 슬롯 확보 시까지)
            await _concurrencyManager.AcquireAsync(job.Type, linkedCts.Token);

            _logger.LogInformation("Job {JobId} ({JobType}) acquired semaphore, starting execution", jobId, job.Type);

            // 상태 업데이트: InProgress
            job.Status = JobStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            job.ProcessInstanceId = _processManager.CurrentInstanceId;
            await context.SaveChangesAsync(linkedCts.Token);

            // 실제 작업 실행 (Task.Delay로 시뮬레이션)
            await Task.Delay(config.DelayMilliseconds, linkedCts.Token);

            // 완료 처리
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("Job {JobId} ({JobType}) completed successfully", jobId, job.Type);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = "Job was cancelled";
            _logger.LogWarning("Job {JobId} ({JobType}) was cancelled", jobId, job.Type);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Job {JobId} ({JobType}) failed", jobId, job.Type);
        }
        finally
        {
            await context.SaveChangesAsync(CancellationToken.None);
            _concurrencyManager.Release(job.Type);
            _concurrencyManager.UnregisterJob(jobId);
            linkedCts.Dispose();

            _logger.LogInformation("Job {JobId} ({JobType}) released semaphore", jobId, job.Type);
        }
    }
}
