namespace AsyncWorker.Models;

public enum JobStatus
{
    Pending,      // 생성됨, 세마포어 대기 중
    InProgress,   // 실행 중
    Completed,    // 완료
    Failed,       // 실패 (재시도 없음)
    Cancelled     // 취소됨
}
