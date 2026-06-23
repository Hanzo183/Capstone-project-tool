// src/pages/StudentDashboard.tsx
import type { Project, Submission, ReviewSlot, Evaluation, ScheduleEvent } from '../types';
import './StudentDashboard.css'; // Make sure to import the CSS!

export default function StudentDashboard() {
    // --- MOCK DATA ---
    const currentProject: Project = {
        id: 'P101',
        title: 'Microservices E-Commerce Application',
        team: 'Group 6',
        lecturer: 'Dr. Nguyen Van A',
        status: 'In Review',
        round: 'Spring 2025 - Iteration 1',
        score: 8.5,
        updatedAt: '2026-06-23',
    };

    const scheduleTimeline: ScheduleEvent[] = [
        { id: 't1', title: 'Proposal Submission', date: 'May 15, 2026', status: 'Completed' },
        { id: 't2', title: 'Iteration 1 Review', date: 'June 25, 2026', status: 'Current' },
        { id: 't3', title: 'Final Defense', date: 'July 20, 2026', status: 'Upcoming' },
    ];

    const upcomingSlot: ReviewSlot = {
        id: 'R1',
        projectId: 'P101',
        projectTitle: 'Microservices E-Commerce App',
        room: 'Alpha 105',
        time: '2026-06-25 10:00 AM',
        council: ['Dr. Tran B', 'Prof. Le C'],
        type: 'Initial Review'
    };

    const recentEvaluations: Evaluation[] = [
        {
            id: 'E1',
            evaluator: 'Dr. Tran B',
            score: 8.5,
            feedback: 'Good microservice architecture. RabbitMQ integration is solid. Needs more unit tests.',
            submittedAt: '2026-06-20',
            canRebuttal: true
        }
    ];

    const submissions: Submission[] = [
        { id: 'S1', projectId: 'P101', fileName: 'architecture_diagram.pdf', version: 2, submittedAt: '2026-06-19', submittedBy: 'SE192706', status: 'Evaluated' },
        { id: 'S2', projectId: 'P101', fileName: 'requirements_doc.docx', version: 1, submittedAt: '2026-06-15', submittedBy: 'SE192706', status: 'Evaluated' },
    ];

    return (
        <div className="dashboard-container">
            {/* Page Header */}
            <div className="dashboard-header">
                <div>
                    <h2 className="dashboard-title">Student Workspace</h2>
                    <p className="dashboard-subtitle">Manage your capstone progress, schedules, and reviews.</p>
                </div>
                <div className="notification-badge">
                    <span className="pulse-dot"></span>
                    1 New Notification
                </div>
            </div>

            <div className="dashboard-grid">
                {/* LEFT COLUMN */}
                <div className="main-column">

                    {/* Active Project Card */}
                    <div className="glass-card">
                        <div className="project-header">
                            <div>
                                <h3>{currentProject.title}</h3>
                                <p className="project-round">{currentProject.round}</p>
                            </div>
                            <span className="status-badge">{currentProject.status}</span>
                        </div>

                        <div className="project-details">
                            <div className="detail-item">
                                <span>Team</span>
                                <p>{currentProject.team}</p>
                            </div>
                            <div className="detail-item">
                                <span>Supervisor</span>
                                <p>{currentProject.lecturer}</p>
                            </div>
                            <div className="detail-item">
                                <span>Current Score</span>
                                <p className="score-text">{currentProject.score ? `${currentProject.score} / 10` : 'Pending'}</p>
                            </div>
                            <div className="detail-item">
                                <span>Last Updated</span>
                                <p>{currentProject.updatedAt}</p>
                            </div>
                        </div>
                    </div>

                    {/* Schedule & Timeline Section */}
                    <div className="split-grid">
                        {/* Upcoming Review */}
                        <div className="glass-card upcoming-review">
                            <div className="blur-circle"></div>
                            <h3 className="section-title">Next Scheduled Event</h3>
                            <div className="review-time-box">
                                <p className="review-type">{upcomingSlot.type}</p>
                                <p className="review-time">{upcomingSlot.time.split(' ')[1]} {upcomingSlot.time.split(' ')[2]}</p>
                                <p className="review-date">{upcomingSlot.time.split(' ')[0]}</p>
                            </div>
                            <ul className="review-info">
                                <li><strong>Room:</strong> {upcomingSlot.room}</li>
                                <li><strong>Council:</strong>
                                    <span className="council-list">{upcomingSlot.council.map(c => <span key={c}>{c}</span>)}</span>
                                </li>
                            </ul>
                        </div>

                        {/* Timeline Progress */}
                        <div className="glass-card">
                            <h3 className="section-title">Project Timeline</h3>
                            <div className="timeline">
                                {scheduleTimeline.map((event) => (
                                    <div key={event.id} className="timeline-item">
                                        <span className={`timeline-dot ${event.status.toLowerCase()}`}></span>
                                        <div className="timeline-content">
                                            <p className={`timeline-title ${event.status === 'Current' ? 'current' : ''}`}>
                                                {event.title}
                                            </p>
                                            <p className="timeline-date">{event.date}</p>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    </div>

                    {/* Evaluations Section */}
                    <div className="glass-card">
                        <div className="evaluation-header">
                            <h3 className="section-title">Council Feedback & Scores</h3>
                            <button className="text-button">View Final Report PDF</button>
                        </div>
                        <div className="evaluation-list">
                            {recentEvaluations.map((evalRecord) => (
                                <div key={evalRecord.id} className="evaluation-card">
                                    <div className="evaluation-card-header">
                                        <div>
                                            <p className="evaluator-name">{evalRecord.evaluator}</p>
                                            <p className="evaluation-date">Evaluated on: {evalRecord.submittedAt}</p>
                                        </div>
                                        <span className="evaluation-score">{evalRecord.score} / 10</span>
                                    </div>
                                    <p className="evaluation-feedback">"{evalRecord.feedback}"</p>

                                    {evalRecord.canRebuttal && (
                                        <div className="rebuttal-section">
                                            <button className="btn-primary">Submit Rebuttal Request</button>
                                            <span className="rebuttal-hint">Available for 48 hours</span>
                                        </div>
                                    )}
                                </div>
                            ))}
                        </div>
                    </div>
                </div>

                {/* RIGHT COLUMN */}
                <div className="side-column">
                    <div className="glass-card sticky-card">
                        <h3 className="section-title">Artifact Submissions</h3>

                        {/* Upload Area */}
                        <div className="upload-area">
                            <p className="upload-title">Drag & drop files here</p>
                            <p className="upload-subtitle">PDF, DOCX, ZIP up to 50MB</p>
                            <button className="btn-secondary">Browse Files</button>
                        </div>

                        {/* Version History */}
                        <h4 className="history-title">Version History</h4>
                        <div className="submission-list">
                            {submissions.map((sub) => (
                                <div key={sub.id} className="submission-item">
                                    <div className="submission-header">
                                        <div className="submission-info">
                                            <p className="file-name" title={sub.fileName}>{sub.fileName}</p>
                                            <p className="file-meta">By {sub.submittedBy} • {sub.submittedAt}</p>
                                        </div>
                                        <span className="version-badge">v{sub.version}</span>
                                    </div>
                                    <div className="submission-footer">
                                        <span className="status-badge-small">{sub.status}</span>
                                        <button className="text-button download-btn">Download</button>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}