using AsyncWorker.Data;
using AsyncWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace AsyncWorker.Services;

public class JobExecutionService
{
    private readonly ApplicationDbContext _context;
    private readonly JobConcurrencyManager _concurrencyManager;
    private readonly ProcessInstanceManager _processManager;
    private readonly ILogger<JobExecutionService> _logger;

    public JobExecutionService(
        ApplicationDbContext context,
        JobConcurrencyManager concurrencyManager,
        ProcessInstanceManager processManager,
        ILogger<JobExecutionService> logger)
    {
        _context = context;
        _concurrencyManager = concurrencyManager;
        _processManager = processManager;
        _logger = logger;
    }

    // 메인 실행 메서드
    public async Task ExecuteJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs.FindAsync(new object[] { jobId }, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found", jobId);
            return;
        }

        var config = _concurrencyManager.GetConfiguration(job.Type);
        if (config == null)
        {
            _logger.LogError("Unknown job type: {JobType}", job.Type);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = $"Unknown job type: {job.Type}";
            job.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
            return;
        }

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
            await _context.SaveChangesAsync(linkedCts.Token);

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
            await _context.SaveChangesAsync(CancellationToken.None);
            _concurrencyManager.Release(job.Type);
            _concurrencyManager.UnregisterJob(jobId);
            linkedCts.Dispose();
            
            _logger.LogInformation("Job {JobId} ({JobType}) released semaphore", jobId, job.Type);
        }
    }
}
