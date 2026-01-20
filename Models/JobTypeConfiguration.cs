namespace AsyncWorker.Models;

public class JobTypeConfiguration
{
    public string JobType { get; set; } = string.Empty;
    public int MaxConcurrency { get; set; }                 // 최대 동시 실행 수
    public int DelayMilliseconds { get; set; }              // Task.Delay 시간
}
