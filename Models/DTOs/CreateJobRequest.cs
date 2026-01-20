namespace AsyncWorker.Models.DTOs;

public class CreateJobRequest
{
    public string Type { get; set; } = string.Empty;
    public string? Payload { get; set; }
}
