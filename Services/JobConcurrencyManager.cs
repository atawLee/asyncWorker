using AsyncWorker.Models;
using System.Collections.Concurrent;

namespace AsyncWorker.Services;

public class JobConcurrencyManager
{
    // 작업 타입별 SemaphoreSlim 저장
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores;
    
    // 작업 타입별 설정
    private readonly Dictionary<string, JobTypeConfiguration> _configurations;
    
    // 현재 실행 중인 작업 추적 (CancellationTokenSource 관리)
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningJobs;
    
    public JobConcurrencyManager(IConfiguration configuration)
    {
        _semaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        _runningJobs = new ConcurrentDictionary<Guid, CancellationTokenSource>();
        
        // appsettings.json에서 작업 타입별 설정 로드
        var jobTypes = configuration.GetSection("JobTypes").Get<List<JobTypeConfiguration>>() 
            ?? new List<JobTypeConfiguration>();
        
        _configurations = jobTypes.ToDictionary(x => x.JobType);
        
        // 각 작업 타입별 SemaphoreSlim 초기화
        foreach (var config in _configurations.Values)
        {
            _semaphores[config.JobType] = new SemaphoreSlim(
                config.MaxConcurrency, 
                config.MaxConcurrency
            );
        }
    }
    
    // 작업 실행 전 세마포어 획득 (비동기 대기)
    public async Task AcquireAsync(string jobType, CancellationToken cancellationToken)
    {
        if (_semaphores.TryGetValue(jobType, out var semaphore))
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Unknown job type: {jobType}");
        }
    }
    
    // 작업 완료 후 세마포어 해제
    public void Release(string jobType)
    {
        if (_semaphores.TryGetValue(jobType, out var semaphore))
        {
            semaphore.Release();
        }
    }
    
    // 작업 시작 시 CancellationTokenSource 등록
    public CancellationTokenSource RegisterJob(Guid jobId)
    {
        var cts = new CancellationTokenSource();
        _runningJobs[jobId] = cts;
        return cts;
    }
    
    // 작업 취소 요청
    public bool CancelJob(Guid jobId)
    {
        if (_runningJobs.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return true;
        }
        return false;
    }
    
    // 작업 완료 시 CancellationTokenSource 정리
    public void UnregisterJob(Guid jobId)
    {
        if (_runningJobs.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }
    
    // 작업 타입별 설정 조회
    public JobTypeConfiguration? GetConfiguration(string jobType)
    {
        _configurations.TryGetValue(jobType, out var config);
        return config;
    }
    
    // 현재 대기 중인 작업 수 (디버깅/모니터링용)
    public int GetAvailableSlots(string jobType)
    {
        if (_semaphores.TryGetValue(jobType, out var semaphore))
        {
            return semaphore.CurrentCount;
        }
        return 0;
    }
    
    // 모든 작업 타입 목록 반환
    public IEnumerable<string> GetJobTypes()
    {
        return _configurations.Keys;
    }
}
