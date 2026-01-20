namespace AsyncWorker.Models.DTOs;

public class JobResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProcessInstanceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public JobResponse(Job job)
    {
        Id = job.Id;
        Type = job.Type;
        Payload = job.Payload;
        Status = job.Status.ToString();
        ProcessInstanceId = job.ProcessInstanceId;
        CreatedAt = job.CreatedAt;
        StartedAt = job.StartedAt;
        CompletedAt = job.CompletedAt;
        ErrorMessage = job.ErrorMessage;
    }
}
