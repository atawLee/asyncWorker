namespace AsyncWorker.Models;

public class Job
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;       // EmailJob, DataProcessJob, ReportJob
    public string? Payload { get; set; }                    // JSON 직렬화된 작업 데이터
    public JobStatus Status { get; set; }
    public string? ProcessInstanceId { get; set; }          // 작업을 실행한 프로세스 UUID
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
