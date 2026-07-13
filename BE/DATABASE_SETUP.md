# Capstone Review Tool Database Setup

This script is for the Docker SQL Server container.

Connect from SSMS with:

```text
Server: localhost,11433
Login: sa
Password: YourStrong@Passw0rd!
```

The schema below uses short string IDs such as `PRJ-1001`, `RND-2025A`, and `NOT-1001` instead of `UNIQUEIDENTIFIER` values.

Hangfire creates its own tables automatically in `SchedulingDb` under the `Hangfire` schema when `SchedulingService` starts.

Warning: this is a clean reset script. It drops and recreates the five service databases.

```sql
USE master;
GO

IF DB_ID('NotificationDb') IS NOT NULL
BEGIN
    ALTER DATABASE NotificationDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE NotificationDb;
END
GO

IF DB_ID('EvaluationDb') IS NOT NULL
BEGIN
    ALTER DATABASE EvaluationDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE EvaluationDb;
END
GO

IF DB_ID('SchedulingDb') IS NOT NULL
BEGIN
    ALTER DATABASE SchedulingDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SchedulingDb;
END
GO

IF DB_ID('ProjectDb') IS NOT NULL
BEGIN
    ALTER DATABASE ProjectDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ProjectDb;
END
GO

IF DB_ID('IdentityDb') IS NOT NULL
BEGIN
    ALTER DATABASE IdentityDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE IdentityDb;
END
GO

CREATE DATABASE IdentityDb;
CREATE DATABASE ProjectDb;
CREATE DATABASE SchedulingDb;
CREATE DATABASE EvaluationDb;
CREATE DATABASE NotificationDb;
GO

USE IdentityDb;
GO

CREATE TABLE Users (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    StudentId NVARCHAR(50) NULL,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(255) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(MAX) NOT NULL,
    Role NVARCHAR(50) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE UNIQUE INDEX UX_Users_StudentId
ON Users(StudentId)
WHERE StudentId IS NOT NULL;

CREATE TABLE RefreshTokens (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    UserId NVARCHAR(32) NOT NULL,
    Token NVARCHAR(500) NOT NULL,
    ExpiresAt DATETIME2 NOT NULL,
    IsRevoked BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE TABLE PasswordResetTokens (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    UserId NVARCHAR(32) NOT NULL,
    Token NVARCHAR(500) NOT NULL UNIQUE,
    ExpiresAt DATETIME2 NOT NULL,
    IsUsed BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_PasswordResetTokens_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
);

INSERT INTO Users (Id, StudentId, FullName, Email, PasswordHash, Role, IsActive)
VALUES
('SE192737', 'SE192737', N'Luong Pham Binh Minh', 'minh.backend@fpt.edu.vn', '6fOzTH/JdRybQCvayoPwjw==.1DJnPbNhZCob2W0Fjmpp7F6cvUdRA3KTdAcg31ZjaGk=', 'Admin', 1),
('SE192706', 'SE192706', N'Nguyen Chinh Nhan', 'nhan.frontend@fpt.edu.vn', 'opVaOz9VdNRKbplLt/JRvg==.7AKrsWOt0ZUWTr6hyi7EMUilGrNlgSK5KLaAWFpZQ/g=', 'Student', 1),
('SE192879', 'SE192879', N'Tran Tuan Minh', 'minh.jobs@fpt.edu.vn', 'FvMTjyK4QwRf9FVuetx+BA==.O6TlmPOZWWUmDuJEjYPt8oF/nb9mh7nDHvM0KWtFFSc=', 'Lecturer', 1),
('CM001', 'CM001', N'Council Reviewer', 'council@fpt.edu.vn', 'i0lhpg6S9xhIlFl7c2owsw==.siTO5llB/Ev1ncx/eIG4OpZscW06RRqmprxYP+r7guU=', 'CouncilMember', 1);
GO

USE ProjectDb;
GO

CREATE TABLE Projects (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    TeamId NVARCHAR(100) NOT NULL,
    TeamLeaderId NVARCHAR(100) NULL,
    LecturerId NVARCHAR(100) NOT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Draft',
    RoundId NVARCHAR(32) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE UNIQUE INDEX UX_Projects_TeamId
ON Projects(TeamId);

CREATE TABLE Submissions (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    ProjectId NVARCHAR(32) NOT NULL,
    FileUrl NVARCHAR(500) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    Version INT NOT NULL,
    SubmittedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    SubmittedBy NVARCHAR(100) NOT NULL,
    CONSTRAINT FK_Submissions_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
);

CREATE TABLE ProjectMembers (
    ProjectId NVARCHAR(32) NOT NULL,
    StudentId NVARCHAR(50) NOT NULL,
    IsLeader BIT NOT NULL DEFAULT 0,
    AddedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ProjectMembers PRIMARY KEY (ProjectId, StudentId),
    CONSTRAINT FK_ProjectMembers_Projects FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
);

INSERT INTO Projects (Id, Title, Description, TeamId, TeamLeaderId, LecturerId, Status, RoundId)
VALUES
('PRJ-1001', N'AI Review Scheduler', N'A system to automatically schedule capstone reviews using AI.', 'Team 6', 'SE192706', 'SE192879', 'In Review', 'RND-2025A'),
('PRJ-1002', N'Submission Quality Tracker', N'Track submission guidelines adherence.', 'Team 2', 'SE192706', 'SE192879', 'Submitted', 'RND-2025A'),
('PRJ-1003', N'Council Scoring Portal', N'A web portal for council scoring.', 'Team 4', 'SE192706', 'SE192737', 'Needs Revision', 'RND-2025A');

INSERT INTO Submissions (Id, ProjectId, FileUrl, FileName, Version, SubmittedBy)
VALUES
('SUB-1001', 'PRJ-1001', 'https://files.local/proposal-v1.pdf', 'proposal-v1.pdf', 1, 'SE192706'),
('SUB-1002', 'PRJ-1001', 'https://files.local/architecture-v2.pdf', 'architecture-v2.pdf', 2, 'SE192706'),
('SUB-1003', 'PRJ-1002', 'https://files.local/submission-quality-v1.pdf', 'submission-quality-v1.pdf', 1, 'SE192706');

INSERT INTO ProjectMembers (ProjectId, StudentId, IsLeader)
VALUES
('PRJ-1001', 'SE192706', 1),
('PRJ-1002', 'SE192706', 1),
('PRJ-1003', 'SE192706', 1);
GO

USE SchedulingDb;
GO

CREATE TABLE ReviewRounds (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Upcoming',
    CreatedBy NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE ScheduleSlots (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    RoundId NVARCHAR(32) NOT NULL,
    ProjectId NVARCHAR(32) NOT NULL,
    ReviewDate DATETIME2 NOT NULL,
    Room NVARCHAR(100) NOT NULL,
    DurationMinutes INT NOT NULL DEFAULT 60,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ScheduleSlots_ReviewRounds FOREIGN KEY (RoundId) REFERENCES ReviewRounds(Id)
);

CREATE TABLE SlotReviewers (
    SlotId NVARCHAR(32) NOT NULL,
    UserId NVARCHAR(100) NOT NULL,
    CONSTRAINT PK_SlotReviewers PRIMARY KEY (SlotId, UserId),
    CONSTRAINT FK_SlotReviewers_ScheduleSlots FOREIGN KEY (SlotId) REFERENCES ScheduleSlots(Id) ON DELETE CASCADE
);

INSERT INTO ReviewRounds (Id, Name, StartDate, EndDate, Status, CreatedBy)
VALUES
('RND-2025A', 'Spring 2025 Round 1', '2025-06-25', '2025-07-05', 'Upcoming', 'SE192737'),
('RND-2026A', 'Demo Review Round', '2026-07-01', '2026-07-10', 'Upcoming', 'SE192737');

INSERT INTO ScheduleSlots (Id, RoundId, ProjectId, ReviewDate, Room, DurationMinutes)
VALUES
('SLT-1001', 'RND-2025A', 'PRJ-1001', '2025-06-25 09:00:00', 'B3-201', 60),
('SLT-1002', 'RND-2025A', 'PRJ-1002', '2025-06-25 10:00:00', 'B3-202', 60);

INSERT INTO SlotReviewers (SlotId, UserId)
VALUES
('SLT-1001', 'CM001'),
('SLT-1001', 'SE192879'),
('SLT-1002', 'CM001'),
('SLT-1002', 'SE192737');
GO

USE EvaluationDb;
GO

CREATE TABLE Evaluations (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    ProjectId NVARCHAR(32) NOT NULL,
    RoundId NVARCHAR(32) NOT NULL,
    EvaluatorId NVARCHAR(100) NOT NULL,
    Score DECIMAL(5,2) NOT NULL,
    Feedback NVARCHAR(MAX) NULL,
    SubmittedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Rebuttals (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    EvaluationId NVARCHAR(32) NOT NULL,
    StudentId NVARCHAR(100) NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
    Response NVARCHAR(MAX) NULL,
    SubmittedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ReviewedAt DATETIME2 NULL,
    CONSTRAINT FK_Rebuttals_Evaluations FOREIGN KEY (EvaluationId) REFERENCES Evaluations(Id)
);

INSERT INTO Evaluations (Id, ProjectId, RoundId, EvaluatorId, Score, Feedback)
VALUES
('EVA-1001', 'PRJ-1001', 'RND-2025A', 'CM001', 8.50, N'Architecture is clear; add stronger testing evidence.'),
('EVA-1002', 'PRJ-1001', 'RND-2025A', 'SE192879', 8.20, N'Good workflow coverage; improve integration testing.');

INSERT INTO Rebuttals (Id, EvaluationId, StudentId, Content, Status)
VALUES
('REB-1001', 'EVA-1001', 'SE192706', N'We added more API and UI test cases after the review.', 'Pending');
GO

USE NotificationDb;
GO

CREATE TABLE Notifications (
    Id NVARCHAR(32) NOT NULL PRIMARY KEY,
    UserId NVARCHAR(100) NOT NULL,
    Title NVARCHAR(255) NOT NULL,
    Body NVARCHAR(MAX) NOT NULL,
    IsRead BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Type NVARCHAR(100) NOT NULL
);

CREATE TABLE NotificationPreferences (
    UserId NVARCHAR(100) NOT NULL PRIMARY KEY,
    EmailEnabled BIT NOT NULL DEFAULT 1,
    InAppEnabled BIT NOT NULL DEFAULT 1,
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

INSERT INTO Notifications (Id, UserId, Title, Body, IsRead, Type)
VALUES
('NOT-1001', 'SE192706', N'Review slot assigned', N'Your review is scheduled for B3-201.', 0, 'schedule.created'),
('NOT-1002', 'SE192879', N'Submission ready', N'Team 6 uploaded architecture-v2.pdf.', 0, 'project.submitted'),
('NOT-1003', 'SE192706', N'Feedback released', N'Council feedback is ready for your project.', 1, 'evaluation.completed'),
('NOT-1004', 'CM001', N'Rebuttal submitted', N'A rebuttal is pending council review.', 0, 'rebuttal.submitted');

INSERT INTO NotificationPreferences (UserId, EmailEnabled, InAppEnabled)
VALUES
('SE192706', 1, 1),
('SE192879', 1, 1),
('CM001', 1, 1);
GO
```

Seed login accounts:

| Role | Email | Password |
| --- | --- | --- |
| Admin | `minh.backend@fpt.edu.vn` | `Admin123!` |
| Student | `nhan.frontend@fpt.edu.vn` | `Student123!` |
| Lecturer | `minh.jobs@fpt.edu.vn` | `Lecturer123!` |
| CouncilMember | `council@fpt.edu.vn` | `Council123!` |
