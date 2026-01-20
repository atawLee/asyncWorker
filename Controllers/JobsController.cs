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
    private readonly JobExecutionService _executionService;

    public JobsController(
        ApplicationDbContext context,
        JobExecutionService executionService)
    {
        _context = context;
        _executionService = executionService;
    }

    // POST /api/jobs - 작업 생성
    [HttpPost]
    public async Task CreateJob([FromBody] CreateJobRequest request)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new ArgumentException("Job type is required");
        }

        _ = _executionService.CreateAndExecuteJobAsync(request.Type, request.Payload);
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

        // 실행 중인 작업 취소 시도
        var cancelled = _executionService.CancelJob(id);

        // 취소가 안되었고 Pending 상태라면 직접 취소
        if (!cancelled && job.Status == JobStatus.Pending)
        {
            await _executionService.CancelPendingJobAsync(id);
        }

        return Ok(new { Message = "Cancellation requested", JobId = id });
    }
}
