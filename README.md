# Capstone Review Tool

Capstone Review Tool is a distributed .NET application for managing final-project review workflows. It supports user identity, project registration and submissions, review-round scheduling, council evaluations, rebuttals, reports, and notifications.

## System Architecture

The backend is split into independent ASP.NET Core services behind a YARP API gateway:

| Component | Responsibility | Protocol |
| --- | --- | --- |
| ApiGateway | Single HTTP entry point and JWT policy enforcement | REST proxy |
| IdentityService | Registration, login, refresh tokens, user administration | REST |
| ProjectService | Project CRUD, team members, submissions, upload history | REST + gRPC client + Kafka producer |
| UserProfileService | Active user/student lookup for other services | gRPC |
| SchedulingService | Review rounds, review slots, Hangfire background jobs | REST + Kafka producer |
| EvaluationService | Evaluations, rebuttals, reports and PDF report export | REST + Kafka producer |
| NotificationService | In-app notification APIs and async event consumer | REST + Kafka consumer |
| SQL Server | Relational storage for service databases | TCP |
| Kafka | Async integration-event broker | TCP |

`ProjectService` demonstrates the layered API -> service -> repository pattern. HTTP endpoints call `ProjectManagementService`; business rules call `IProjectRepository` for EF Core persistence and `UserProfileGateway` for gRPC user validation.

## Technology Stack

- ASP.NET Core Minimal APIs on .NET 9
- Entity Framework Core with SQL Server
- JWT bearer authentication
- Swagger/OpenAPI via Swashbuckle
- YARP reverse proxy API gateway
- Kafka with `Confluent.Kafka`
- gRPC with `.proto` contracts
- Hangfire with SQL Server storage for scheduled background jobs
- Docker Desktop and Docker Compose
- React, TypeScript, and Vite frontend

## Functional Coverage

- REST APIs: identity, projects, scheduling, evaluations, reports, notifications
- CRUD: users, projects, review rounds, schedule slots, evaluations, rebuttals, notifications
- Search/filter/sort/pagination: users, projects, rounds, evaluations, rebuttals, notification feeds, submission history
- JWT authentication and role policies: Admin, Lecturer, CouncilMember, Student
- Background jobs: deadline reminders every six hours and daily round-status updates
- Kafka producers: identity, project, scheduling, and evaluation events
- Kafka consumer: notification service subscribes to domain event topics and stores notifications
- gRPC: project creation/member updates call `UserProfileService` to validate active student profiles
- API documentation: Swagger UI is enabled for REST services

## Installation Guide

Prerequisites:

- Docker Desktop
- .NET SDK 9.0 or later
- Node.js 20 or later
- SQL Server Management Studio or Azure Data Studio for running the setup script

Restore and build the backend:

```powershell
cd BE
dotnet restore .\Capstone-tool.slnx
dotnet build .\Capstone-tool.slnx
```

Install and build the frontend:

```powershell
cd FE\CapstoneTools
npm install
npm run build
```

## Database Setup

Start SQL Server first:

```powershell
cd BE
docker compose up -d sqlserver
```

Connect to `localhost,11433` with:

- Login: `sa`
- Password: `YourStrong@Passw0rd!`

Run the SQL script in [BE/DATABASE_SETUP.md](BE/DATABASE_SETUP.md). It creates and seeds:

- `IdentityDb`
- `ProjectDb`
- `SchedulingDb`
- `EvaluationDb`
- `NotificationDb`

Seed accounts:

| Role | Email | Password |
| --- | --- | --- |
| Admin | `minh.backend@fpt.edu.vn` | `Admin123!` |
| Student | `nhan.frontend@fpt.edu.vn` | `Student123!` |
| Lecturer | `minh.jobs@fpt.edu.vn` | `Lecturer123!` |
| CouncilMember | `council@fpt.edu.vn` | `Council123!` |

## Deployment Instructions

Run the whole backend stack:

```powershell
cd BE
docker compose up --build
```

Main endpoints:

- API Gateway: `http://localhost:5000`
- IdentityService Swagger: `http://localhost:8081/swagger`
- ProjectService Swagger: `http://localhost:8082/swagger`
- SchedulingService Swagger: `http://localhost:8083/swagger`
- EvaluationService Swagger: `http://localhost:8084/swagger`
- NotificationService Swagger: `http://localhost:8085/swagger`
- UserProfileService health: `http://localhost:8086/health`
- Kafka: `localhost:9092`
- SQL Server: `localhost,11433`

Run the frontend in development:

```powershell
cd FE\CapstoneTools
npm run dev
```

## Demo Workflow

1. Sign in with an admin or lecturer account through `/auth/login`.
2. Create or update a project through `/projects`; `ProjectService` calls `UserProfileService` over gRPC to validate student IDs.
3. Submit a project file through `/projects/{id}/submit` or `/projects/{id}/submissions/upload`; `ProjectService` publishes `project.submitted` to Kafka.
4. Assign a schedule slot through `/schedule/assign`; `SchedulingService` publishes `schedule.created` to Kafka.
5. Create an evaluation through `/evaluations`; `EvaluationService` publishes `evaluation.completed` to Kafka.
6. Open `/notifications/{userId}` to show `NotificationService` consumed Kafka events and persisted notifications.
7. Trigger `/schedule/jobs/deadline-reminders` to demonstrate a manual background-job execution, or view Hangfire at `/hangfire`.
8. Open `/reports/{roundId}` or `/reports/{roundId}/pdf` to demonstrate reporting.

## Team Member Responsibilities

| Member | Responsibility |
| --- | --- |
| Luong Pham Binh Minh | Backend services, identity, Docker, Kafka/gRPC integration |
| Nguyen Chinh Nhan | Frontend, UI workflows, student-facing features |
| Tran Tuan Minh | Scheduling, background jobs, reporting, integration testing |



## Notes

- The API gateway validates JWTs and forwards user claims in headers.
- Kafka topics are named by event type, for example `project.submitted` and `evaluation.completed`.
- Hangfire creates its schema automatically in `SchedulingDb`.
- `DATABASE_SETUP.md` is a reset script; running it drops and recreates the service databases.
