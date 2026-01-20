namespace AsyncWorker.Services;

public class ProcessInstanceManager
{
    public string CurrentInstanceId { get; } = Guid.NewGuid().ToString();
}
