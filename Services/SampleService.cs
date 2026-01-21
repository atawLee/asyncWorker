namespace AsyncWorker.Services;

/// <summary>
/// 샘플 서비스 - Scoped 생명주기 예제
/// HTTP 요청당 하나의 인스턴스가 생성됨
/// </summary>
public interface ISampleService
{
    Task ProcessAsync(int delayMilliseconds);
}

public class SampleService : ISampleService
{
    private readonly ILogger<SampleService> _logger;

    public SampleService(ILogger<SampleService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 지정된 시간만큼 지연 처리를 시뮬레이션
    /// </summary>
    public async Task ProcessAsync(int delayMilliseconds)
    {
        _logger.LogInformation("SampleService 작업 시작 - {Delay}ms 대기", delayMilliseconds);
        
        await Task.Delay(delayMilliseconds);
        
        _logger.LogInformation("SampleService 작업 완료");
    }
}
