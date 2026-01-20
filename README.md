# AsyncWorker - 카프카 없이 비동기 작업 관리 시스템

ASP.NET Core 10과 SQLite를 사용하여 구현한 비동기 작업 관리 시스템입니다. 
외부 메시지 큐(Kafka, RabbitMQ 등) 없이 간단하고 효율적인 비동기 작업 처리를 제공합니다.

## 주요 기능

### 1. Fire-and-Forget 패턴
- API 요청 시 즉시 202 Accepted 응답
- 백그라운드에서 Task.Run으로 독립적인 작업 실행
- 각 작업은 DB에 상태 저장

### 2. 타입별 동시성 제어
- 작업 타입별로 최대 동시 실행 수 제한
- `SemaphoreSlim`을 사용한 경량 동시성 관리
- 설정 파일(appsettings.json)로 타입별 동시성 제어

### 3. 작업 취소
- CancellationToken 기반 작업 취소
- 실행 전/실행 중 작업 모두 취소 가능
- CancellationTokenSource를 Dictionary로 관리

### 4. 프로세스 장애 복구
- 앱 시작 시 고유 ProcessInstanceId (UUID) 생성
- InProgress 상태의 orphaned 작업 자동 감지
- 이전 프로세스의 작업을 Failed로 자동 처리

## 기술 스택

- **.NET 10** - 최신 .NET 버전
- **ASP.NET Core Web API** - Controller 기반 RESTful API
- **Entity Framework Core 10** - ORM
- **SQLite** - 경량 데이터베이스
- **System.Threading** - SemaphoreSlim, CancellationToken

## 프로젝트 구조

```
AsyncWorker/
├── Controllers/
│   ├── JobsController.cs          # 작업 CRUD API
│   └── StatusController.cs        # 시스템 상태 API
├── Data/
│   └── ApplicationDbContext.cs    # EF Core DbContext
├── Models/
│   ├── Job.cs                     # Job 엔티티
│   ├── JobStatus.cs               # 상태 Enum
│   ├── JobTypeConfiguration.cs    # 작업 타입 설정
│   └── DTOs/
│       ├── CreateJobRequest.cs
│       └── JobResponse.cs
├── Services/
│   ├── ProcessInstanceManager.cs  # 프로세스 UUID 관리 (Singleton)
│   ├── JobConcurrencyManager.cs   # 동시성 제어 (Singleton)
│   ├── JobExecutionService.cs     # 작업 실행 로직 (Scoped)
│   └── JobRecoveryService.cs      # 시작 시 복구 (IHostedService)
├── Migrations/                    # EF Core 마이그레이션
├── appsettings.json               # 설정 파일
└── Program.cs                     # DI 및 앱 설정
```

## 시작하기

### 1. 프로젝트 복원 및 빌드

```bash
dotnet restore
dotnet build
```

### 2. 데이터베이스 마이그레이션

```bash
dotnet ef database update
```

### 3. 앱 실행

```bash
dotnet run
```

앱은 기본적으로 `https://localhost:5001` 또는 `http://localhost:5000`에서 실행됩니다.

## API 사용 예제

### 1. 작업 생성

```bash
curl -X POST https://localhost:5001/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "type": "EmailJob",
    "payload": "{\"to\":\"user@example.com\",\"subject\":\"Hello\"}"
  }'
```

**응답:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "EmailJob",
  "status": "Pending",
  "createdAt": "2026-01-20T12:00:00Z"
}
```

### 2. 작업 상태 조회

```bash
curl https://localhost:5001/api/jobs/{jobId}
```

**응답:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "EmailJob",
  "payload": "{\"to\":\"user@example.com\",\"subject\":\"Hello\"}",
  "status": "Completed",
  "processInstanceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "createdAt": "2026-01-20T12:00:00Z",
  "startedAt": "2026-01-20T12:00:01Z",
  "completedAt": "2026-01-20T12:00:06Z"
}
```

### 3. 작업 목록 조회 (필터링 & 페이징)

```bash
# 모든 작업
curl https://localhost:5001/api/jobs

# 상태별 필터링
curl "https://localhost:5001/api/jobs?status=InProgress"

# 타입별 필터링
curl "https://localhost:5001/api/jobs?type=EmailJob"

# 페이징
curl "https://localhost:5001/api/jobs?page=1&pageSize=20"
```

### 4. 작업 취소

```bash
curl -X DELETE https://localhost:5001/api/jobs/{jobId}/cancel
```

### 5. 시스템 상태 확인

```bash
# 프로세스 정보
curl https://localhost:5001/api/status/process

# 동시성 슬롯 현황
curl https://localhost:5001/api/status/concurrency

# 작업 통계
curl https://localhost:5001/api/status/summary
```

## 설정

`appsettings.json`에서 작업 타입별 설정을 관리합니다:

```json
{
  "JobTypes": [
    {
      "JobType": "EmailJob",
      "MaxConcurrency": 3,
      "DelayMilliseconds": 5000
    },
    {
      "JobType": "DataProcessJob",
      "MaxConcurrency": 5,
      "DelayMilliseconds": 10000
    },
    {
      "JobType": "ReportJob",
      "MaxConcurrency": 2,
      "DelayMilliseconds": 15000
    }
  ]
}
```

- **JobType**: 작업 타입 이름
- **MaxConcurrency**: 해당 타입의 최대 동시 실행 수
- **DelayMilliseconds**: Task.Delay 시뮬레이션 시간 (실제로는 비즈니스 로직으로 대체)

## 아키텍처 상세

### 작업 실행 흐름

```
[Client Request] 
    ↓
[JobsController.CreateJob]
    ↓ 1. Job 엔티티 생성 (Pending)
    ↓ 2. DB 저장
    ↓ 3. Task.Run으로 백그라운드 실행
    ↓ 4. 202 Accepted 응답
    ↓
[JobExecutionService.ExecuteJobAsync] (별도 Task)
    ↓ 1. 타입별 SemaphoreSlim 획득 대기
    ↓ 2. 획득 성공 → InProgress 업데이트
    ↓ 3. ProcessInstanceId 기록
    ↓ 4. Task.Delay 실행
    ↓ 5. Completed 처리
    ↓ 6. SemaphoreSlim 해제
```

### 프로세스 장애 복구

```
[App Startup]
    ↓
[JobRecoveryService.StartAsync]
    ↓ 1. 현재 프로세스 UUID 생성
    ↓ 2. InProgress 작업 중 다른 UUID 검색
    ↓ 3. 해당 작업들을 Failed로 변경
    ↓ 4. ErrorMessage에 이전 프로세스 정보 기록
```

## 테스트 시나리오

### 1. 정상 작업 흐름 테스트
1. EmailJob 작업 생성
2. 5초 대기
3. 작업 상태 확인 → Completed

### 2. 동시성 제한 테스트
1. EmailJob 10개 동시 생성
2. 동시성 상태 확인 → InUse: 3, Available: 0
3. 작업 완료 대기 후 확인

### 3. 작업 취소 테스트
1. ReportJob 생성 (15초 소요)
2. 즉시 취소 요청
3. 작업 상태 확인 → Cancelled

### 4. 프로세스 장애 테스트
1. DataProcessJob 생성
2. 실행 중 앱 강제 종료 (Ctrl+C)
3. 앱 재시작
4. 해당 작업 상태 확인 → Failed

## 장점

- ✅ **간단한 설정**: 외부 인프라 불필요
- ✅ **디버깅 용이**: 모든 로직이 코드베이스 내부에 존재
- ✅ **낮은 복잡도**: 학습 곡선이 낮음
- ✅ **빠른 개발**: 프로토타입 및 MVP에 적합

## 한계

- ❌ **단일 서버만 지원**: 수평 확장 불가
- ❌ **메모리 기반 세마포어**: 재시작 시 초기화
- ❌ **Pending 작업 복구 불가**: 자동 재시작 미지원

## 언제 사용해야 하나?

### 적합한 경우
- 중소규모 애플리케이션
- 단일 서버 환경
- 간단한 비동기 작업 처리 필요
- 외부 인프라 비용 절감 필요

### 부적합한 경우
- 대규모 트래픽
- 다중 서버 환경 (로드 밸런싱)
- 작업 영속성 및 보장된 전달 필요
- 복잡한 워크플로우 관리

## 확장 가능성

### Redis 분산 락으로 업그레이드
- SemaphoreSlim → RedLock
- 다중 서버 환경 지원

### Hangfire/Quartz.NET으로 마이그레이션
- 대시보드 UI 제공
- 스케줄링 기능 추가

### 메시지 큐 도입
- RabbitMQ, Azure Service Bus, Kafka 등
- 완전한 분산 환경 지원

## 라이선스

MIT License

## 기여

이슈 및 PR 환영합니다!
