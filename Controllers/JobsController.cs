using AsyncWorker.Data;
using AsyncWorker.Models;
using AsyncWorker.Models.DTOs;
using AsyncWorker.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsyncWorker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobConcurrencyManager _concurrencyManager;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        ApplicationDbContext context,
        IServiceScopeFactory scopeFactory,
        JobConcurrencyManager concurrencyManager,
        ILogger<JobsController> logger)
    {
        _context = context;
        _scopeFactory = scopeFactory;
        _concurrencyManager = concurrencyManager;
        _logger = logger;
    }

    // POST /api/jobs - 작업 생성
    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest(new { Message = "Job type is required" });
        }

        // 작업 타입이 설정에 존재하는지 확인
        var config = _concurrencyManager.GetConfiguration(request.Type);
        if (config == null)
        {
            return BadRequest(new { Message = $"Unknown job type: {request.Type}" });
        }

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Payload = request.Payload,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created job {JobId} ({JobType})", job.Id, job.Type);

        // Fire-and-forget: 백그라운드 Task 실행
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<JobExecutionService>();
            await executionService.ExecuteJobAsync(job.Id, CancellationToken.None);
        });

        return AcceptedAtAction(nameof(GetJob), new { id = job.Id }, new JobResponse(job));
    }

    // GET /api/jobs/{id} - 작업 조회
    [HttpGet("{id}")]
    public async Task<IActionResult> GetJob(Guid id)
    {
        var job = await _context.Jobs.FindAsync(id);
        if (job == null)
        {
            return NotFound(new { Message = "Job not found" });
        }

        return Ok(new JobResponse(job));
    }

    // GET /api/jobs - 작업 목록
    [HttpGet]
    public async Task<IActionResult> GetJobs(
        [FromQuery] JobStatus? status = null,
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _context.Jobs.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(j => j.Type == type);
        }

        var totalCount = await query.CountAsync();
        
        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Jobs = jobs.Select(j => new JobResponse(j))
        });
    }

    // DELETE /api/jobs/{id}/cancel - 작업 취소
    [HttpDelete("{id}/cancel")]
    public async Task<IActionResult> CancelJob(Guid id)
    {
        var job = await _context.Jobs.FindAsync(id);
        if (job == null)
        {
            return NotFound(new { Message = "Job not found" });
        }

        if (job.Status != JobStatus.Pending && job.Status != JobStatus.InProgress)
        {
            return BadRequest(new { Message = $"Cannot cancel job in {job.Status} state" });
        }

        // CancellationToken 트리거
        var cancelled = _concurrencyManager.CancelJob(id);

        if (!cancelled && job.Status == JobStatus.Pending)
        {
            // 아직 실행 전이면 DB에서 직접 취소
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = "Cancelled by user before execution";
            await _context.SaveChangesAsync();
            _logger.LogInformation("Job {JobId} cancelled before execution", id);
        }
        else
        {
            _logger.LogInformation("Job {JobId} cancellation requested", id);
        }

        return Ok(new { Message = "Cancellation requested", JobId = id });
    }
}
