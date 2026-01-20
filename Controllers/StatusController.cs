using AsyncWorker.Data;
using AsyncWorker.Models;
using AsyncWorker.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsyncWorker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ProcessInstanceManager _processManager;
    private readonly JobConcurrencyManager _concurrencyManager;

    public StatusController(
        ApplicationDbContext context,
        ProcessInstanceManager processManager,
        JobConcurrencyManager concurrencyManager)
    {
        _context = context;
        _processManager = processManager;
        _concurrencyManager = concurrencyManager;
    }

    // GET /api/status/process - 현재 프로세스 정보
    [HttpGet("process")]
    public IActionResult GetProcessInfo()
    {
        return Ok(new
        {
            ProcessInstanceId = _processManager.CurrentInstanceId,
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            StartTime = DateTime.UtcNow // 실제로는 앱 시작 시간을 저장해야 하지만 데모용
        });
    }

    // GET /api/status/concurrency - 타입별 슬롯 사용 현황
    [HttpGet("concurrency")]
    public IActionResult GetConcurrencyStatus()
    {
        var jobTypes = _concurrencyManager.GetJobTypes();

        var status = jobTypes.Select(jobType =>
        {
            var config = _concurrencyManager.GetConfiguration(jobType);
            var available = _concurrencyManager.GetAvailableSlots(jobType);

            return new
            {
                JobType = jobType,
                MaxConcurrency = config?.MaxConcurrency ?? 0,
                AvailableSlots = available,
                InUse = (config?.MaxConcurrency ?? 0) - available,
                DelayMilliseconds = config?.DelayMilliseconds ?? 0
            };
        });

        return Ok(status);
    }

    // GET /api/status/summary - 전체 작업 통계
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _context.Jobs
            .GroupBy(j => j.Status)
            .Select(g => new
            {
                Status = g.Key.ToString(),
                Count = g.Count()
            })
            .ToListAsync();

        var totalJobs = await _context.Jobs.CountAsync();
        
        var jobsByType = await _context.Jobs
            .GroupBy(j => j.Type)
            .Select(g => new
            {
                JobType = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        return Ok(new
        {
            TotalJobs = totalJobs,
            StatusSummary = summary,
            JobsByType = jobsByType,
            ProcessInstanceId = _processManager.CurrentInstanceId
        });
    }
}
